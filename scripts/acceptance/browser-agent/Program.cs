using System.Diagnostics;
using System.Text.Json;
using Asura.Application;
using Asura.Browser;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Asura.BrowserAgentAcceptance;

internal static class Program
{
    internal static string Fixture { get; private set; } = string.Empty;
    internal static string Profile { get; private set; } = string.Empty;
    internal static Uri? Proxy { get; private set; }

    [STAThread]
    public static int Main(string[] arguments)
    {
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            Console.Error.WriteLine("Browser acceptance must use the release application's reflection-disabled JSON configuration.");
            return 2;
        }
        Console.WriteLine(System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported
            ? "Browser acceptance: reflection-disabled managed build"
            : "Browser acceptance: Native AOT build");
        if (arguments.Length is < 2 or > 3
            || !Uri.TryCreate(arguments[0], UriKind.Absolute, out var uri)
            || !uri.IsLoopback || !string.Equals(uri.Scheme, "http", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Expected a disposable loopback HTTP fixture URL, profile directory and optional fixture proxy URL.");
            return 2;
        }
        Fixture = arguments[0];
        Profile = Path.GetFullPath(arguments[1]);
        if (arguments.Length == 3)
        {
            Proxy = new Uri(arguments[2]);
            if (!Proxy.IsLoopback || !string.Equals(Proxy.Scheme, "http", StringComparison.Ordinal))
            {
                return 2;
            }
        }
        try
        {
            // Desktop startup runs tools before starting CEF. Initialize the
            // managed child-exit handler in that same order for this regression.
            // The proxy run also covers installing .NET's handler after CEF.
            if (Proxy is null)
            {
                using var child = Process.Start(new ProcessStartInfo("/bin/sh")
                {
                    ArgumentList = { "-c", "exit 23" }, UseShellExecute = false,
                })!;
                if (!child.WaitForExit(5000) || child.ExitCode != 23)
                {
                    throw new InvalidOperationException("Child process failed before browser startup.");
                }
            }
            return BrowserEngineRuntime.Configure(AppBuilder.Configure<ProbeApp>().UsePlatformDetect())
                .StartWithClassicDesktopLifetime(arguments, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception exception)
        {
            // Report startup failures to the gate instead of raising a macOS
            // crash dialog for this disposable acceptance process.
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}

internal sealed class ProbeApp : Avalonia.Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        BrowserEngineRuntime.Initialize(new BrowserEngineRuntimeOptions(
            Program.Profile, Path.Combine(Program.Profile, "cef.log"), "0.1.0"));
        var surface = CreateSurface();
        surface.Width = 900;
        surface.Height = 650;
        surface.IsVisible = false;
        // The probe injects events at the Avalonia boundary. Do not steal the
        // user's physical keyboard while these assertions run in the background.
        var window = new Window
        {
            Title = "Asura browser acceptance", Width = 940, Height = 700,
            Content = surface, ShowActivated = false,
        };
        lifetime.MainWindow = window;
        window.Show();
        Dispatcher.UIThread.Post(() => _ = RunAsync(surface, window, lifetime));
    }

    private static BrowserSurface CreateSurface()
    {
        if (Program.Proxy is not { } proxy)
        {
            return new BrowserSurface();
        }
        var network = CefBrowserNetworkContext.Create(proxy);
        var authentication = new WorkspaceProxyAuthenticationResolver(proxy,
            new WorkspaceNetworkProxyCredentials("fixture", "fixture"));
        return new BrowserSurface(network.CreateView(authentication), BrowserDestinationPolicy.SshRouted,
            nativeViewReplacementFactory: () => network.CreateView(authentication), networkLifetime: network);
    }

    private static async Task RunAsync(
        BrowserSurface surface, Window window, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var exitCode = 1;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stage = "cold background navigation";
        using var fixtureClient = new HttpClient { BaseAddress = new Uri(Program.Fixture) };
        void BeginStage(string name)
        {
            // Each independent scenario keeps the same bound. A slow preceding
            // scenario must not consume the renderer recovery scenario's budget.
            // Check before resetting so an expired stage can never be revived.
            deadline.Token.ThrowIfCancellationRequested();
            stage = name;
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            Console.WriteLine("BEGIN " + stage);
        }
        try
        {
            BeginStage("child process exit after browser startup");
            using (var child = Process.Start(new ProcessStartInfo("/bin/sh")
            {
                ArgumentList = { "-c", "printf 'codex-cli 0.160.0\\n'; exit 23" },
                UseShellExecute = false, RedirectStandardOutput = true,
            })!)
            {
                using var childDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var output = await child.StandardOutput.ReadToEndAsync(childDeadline.Token);
                await child.WaitForExitAsync(childDeadline.Token);
                Require(child.ExitCode == 23 && string.Equals(output.Trim(), "codex-cli 0.160.0", StringComparison.Ordinal), "child process exit and output", null);
            }
            Console.WriteLine("PASS managed child process exit and output after CEF startup");
            // Cold, backgrounded surface: no human visit or manual navigation.
            var address = new BrowserAddress(new Uri(Program.Fixture + "/agent-form"));
            var navigation = await surface.NavigateWithinOriginAsync(
                new BrowserOriginConstrainedNavigationRequest.Navigate(address),
                BrowserNavigationOrigin.WorkspaceNetwork,
                BrowserNavigationStartBinding.FromState(surface.State), deadline.Token);
            Require(navigation.IsSuccess, "background navigation", navigation.Error);
            Console.WriteLine("PASS cold background navigation through the real CEF renderer");
            surface.IsVisible = true;
            await Task.Delay(200, deadline.Token);

            BeginStage("keyboard translation");
            await BrowserKeyboardAcceptance.VerifyAsync(surface, window, deadline.Token);

            BeginStage("custom controls and screenshot");
            var custom = await surface.ClickWithinOriginAsync(
                await ReferenceAsync(surface, "clickable", "Rescue fixture", deadline.Token),
                BrowserNavigationOrigin.WorkspaceNetwork, deadline.Token);
            Require(custom.IsSuccess, "custom click target", custom.Error);
            var customSnapshot = await surface.CaptureSnapshotAsync(
                BrowserDocumentBinding.FromState(surface.State), deadline.Token);
            Require(customSnapshot.IsSuccess && customSnapshot.Value!.Nodes.Any(node =>
                node.Name.Contains("Custom control activated", StringComparison.Ordinal)),
                "custom click delivered", customSnapshot.Error);
            Console.WriteLine("PASS custom onclick control has a reference and receives native click");
            var tableClick = await surface.ClickWithinOriginAsync(
                await ReferenceAsync(surface, "clickable", "Table control", deadline.Token),
                BrowserNavigationOrigin.WorkspaceNetwork, deadline.Token);
            Require(tableClick.IsSuccess, "clickable table cell", tableClick.Error);
            Console.WriteLine("PASS custom table cell receives native click");
            var screenshot = await surface.CaptureSnapshotAsync(
                BrowserDocumentBinding.FromState(surface.State), deadline.Token, BrowserSnapshotQuery.Screenshot);
            Require(screenshot.IsSuccess && screenshot.Value!.Screenshot is { PixelWidth: > 0, PixelHeight: > 0 },
                "viewport screenshot", screenshot.Error);
            await File.WriteAllBytesAsync(Path.Combine(Program.Profile, "viewport.png"),
                screenshot.Value!.Screenshot!.Image.Content.ToArray(), deadline.Token);
            Console.WriteLine("PASS viewport PNG captured with coordinate bindings");

            BeginStage("form input and session persistence");
            var input = await ReferenceAsync(surface, "textbox", "Fixture text", deadline.Token);
            var fill = await surface.FillWithinOriginAsync(input, "native-canary",
                BrowserNavigationOrigin.WorkspaceNetwork, deadline.Token);
            Require(fill.IsSuccess, "fill", fill.Error);
            var check = await surface.CheckWithinOriginAsync(
                await ReferenceAsync(surface, "checkbox", "Fixture check", deadline.Token),
                BrowserNavigationOrigin.WorkspaceNetwork, deadline.Token);
            Require(check.IsSuccess, "check", check.Error);
            var click = await surface.ClickWithinOriginAsync(
                await ReferenceAsync(surface, "button", "Submit fixture", deadline.Token),
                BrowserNavigationOrigin.WorkspaceNetwork, deadline.Token);
            Require(click.IsSuccess, "click", click.Error);
            while (surface.State.LoadState != BrowserLoadState.Ready
                || !surface.State.Address.Value.AbsolutePath.Equals("/agent-result", StringComparison.Ordinal))
            {
                await Task.Delay(20, deadline.Token);
            }
            var expected = "?value=native-canary&checked=true";
            if (!string.Equals(surface.State.Address.Value.Query, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Native form input did not reach the fixture.");
            }
            Console.WriteLine("PASS native snapshot, fill, check and click reached the fixture");
            var resultSnapshot = await surface.CaptureSnapshotAsync(
                BrowserDocumentBinding.FromState(surface.State), deadline.Token);
            Require(resultSnapshot.IsSuccess && resultSnapshot.Value!.Nodes.Any(node =>
                string.Equals(node.Name, "Fixture session retained", StringComparison.Ordinal)),
                "same browser session", resultSnapshot.Error);
            Console.WriteLine("PASS browser session cookie survives agent interaction");

            BeginStage("slow coordinate navigation");
            await VerifySlowMouseNavigationAsync(surface, fixtureClient, deadline.Token);

            BeginStage("input cancellation and authority");
            var restricted = await surface.NavigateWithinOriginAsync(
                new BrowserOriginConstrainedNavigationRequest.Navigate(address),
                BrowserNavigationOrigin.Unrestricted,
                BrowserNavigationStartBinding.FromState(surface.State), deadline.Token);
            Require(restricted.Error?.Code == BrowserErrorCode.TransportUnavailable,
                "restricted authority retained", restricted.Error);
            Console.WriteLine("PASS restricted actions retain the transport requirement");

            Require((await surface.NavigateWithinOriginAsync(
                new BrowserOriginConstrainedNavigationRequest.Navigate(address),
                BrowserNavigationOrigin.WorkspaceNetwork,
                BrowserNavigationStartBinding.FromState(surface.State), deadline.Token)).IsSuccess,
                "return to form", null);
            var initialInputs = await CounterAsync(fixtureClient, "agentInputEvents", deadline.Token);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var typing = surface.FillWithinOriginAsync(
                await ReferenceAsync(surface, "textbox", "Fixture text", deadline.Token),
                new string('x', 160), BrowserNavigationOrigin.WorkspaceNetwork, stop.Token).AsTask();
            while (await CounterAsync(fixtureClient, "agentInputEvents", deadline.Token) == initialInputs)
            {
                await Task.Delay(20, deadline.Token);
            }
            stop.Cancel();
            Require(!(await typing.WaitAsync(deadline.Token)).IsSuccess, "stopped typing", null);
            await Task.Delay(150, deadline.Token);
            var stoppedInputs = await CounterAsync(fixtureClient, "agentInputEvents", deadline.Token);
            await Task.Delay(400, deadline.Token);
            Require(stoppedInputs == await CounterAsync(fixtureClient, "agentInputEvents", deadline.Token),
                "no input after cancellation", null);
            Require(surface.State.Address == address, "cancelled typing preserves the page", null);
            Require((await surface.CheckWithinOriginAsync(
                await ReferenceAsync(surface, "checkbox", "Fixture check", deadline.Token),
                BrowserNavigationOrigin.WorkspaceNetwork, deadline.Token)).IsSuccess,
                "same page remains usable after cancellation", null);
            Console.WriteLine("PASS cancelling real native typing stops input and preserves the usable page");
            if (Program.Proxy is not null)
            {
                BeginStage("proxy authentication and isolation");
                Require(await CounterAsync(fixtureClient, "proxyAuthenticated", deadline.Token) > 0,
                    "workspace proxy used", null);
                while (surface.State.LoadState != BrowserLoadState.Ready)
                {
                    await Task.Delay(20, deadline.Token);
                }
                // The fixture proxy denies localhost while the origin is reachable there directly.
                // A direct-network fallback would therefore hit this endpoint and fail this check.
                var priorDenials = await CounterAsync(fixtureClient, "proxyDenied", deadline.Token);
                var routeDenied = new BrowserAddress(new UriBuilder(address.Value)
                { Host = "localhost", Path = "/agent-route-denied" }.Uri);
                _ = await surface.NavigateWithinOriginAsync(
                    new BrowserOriginConstrainedNavigationRequest.Navigate(routeDenied),
                    BrowserNavigationOrigin.WorkspaceNetwork,
                    BrowserNavigationStartBinding.FromState(surface.State), deadline.Token);
                Require(await CounterAsync(fixtureClient, "proxyDenied", deadline.Token) > priorDenials,
                    "proxy rejected the attempted destination", null);
                Require(await CounterAsync(fixtureClient, "deniedProbeRequests", deadline.Token) == 0,
                    "no direct fallback around workspace proxy", null);
                Console.WriteLine("PASS workspace proxy authentication and no direct-network fallback");
            }
            BeginStage("renderer crash and recovery");
            await BrowserRendererRecoveryAcceptance.VerifyAsync(surface, address, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            exitCode = 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Browser acceptance failed during " + stage
                + (deadline.IsCancellationRequested ? " (45-second stage deadline expired)." : "."));
            Console.Error.WriteLine(exception);
        }
        finally
        {
            surface.Dispose();
            window.Close();
            if (!BrowserEngineRuntime.Shutdown())
            {
                exitCode = 1;
            }
            lifetime.Shutdown(exitCode);
        }
    }

    private static async Task<int> CounterAsync(HttpClient client, string name, CancellationToken cancellationToken)
    {
        using var status = JsonDocument.Parse(await client.GetStringAsync(new Uri("/fixture-status", UriKind.Relative), cancellationToken));
        return status.RootElement.GetProperty(name).GetInt32();
    }

    private static async Task VerifySlowMouseNavigationAsync(
        BrowserSurface surface, HttpClient client, CancellationToken cancellationToken)
    {
        var source = new BrowserAddress(new Uri(Program.Fixture + "/agent-slow-form"));
        Require((await surface.NavigateWithinOriginAsync(
            new BrowserOriginConstrainedNavigationRequest.Navigate(source),
            BrowserNavigationOrigin.WorkspaceNetwork,
            BrowserNavigationStartBinding.FromState(surface.State), cancellationToken)).IsSuccess,
            "slow mouse fixture navigation", null);
        var initial = await CounterAsync(client, "agentSlowNavigations", cancellationToken);
        var click = await surface.DispatchMouseWithinOriginAsync(
            new BrowserMouseRequest(new Asura.Core.SessionId("fixture"),
                BrowserAutomationBinding.FromState(surface.State), BrowserMouseAction.Click,
                100, 65, BrowserMouseButton.Left, clickCount: 1),
            BrowserNavigationOrigin.WorkspaceNetwork, cancellationToken);
        Require(click.IsSuccess || click.Error?.Code == BrowserErrorCode.InteractionOutcomeUnknown,
            "slow mouse dispatch result", click.Error);
        Require(surface.State.Address != BrowserAddress.Blank,
            "slow mouse navigation preserves live renderer", click.Error);
        while (surface.State.LoadState != BrowserLoadState.Ready)
        {
            await Task.Delay(20, cancellationToken);
        }
        Require(string.Equals(surface.State.Address.Value.AbsolutePath, "/agent-slow-target", StringComparison.Ordinal),
            "slow mouse navigation completes on the same page", null);
        Require(await CounterAsync(client, "agentSlowNavigations", cancellationToken) == initial + 1,
            "slow click is not replayed", null);
        Require((await surface.CheckWithinOriginAsync(
            await ReferenceAsync(surface, "checkbox", "Fixture check", cancellationToken),
            BrowserNavigationOrigin.WorkspaceNetwork, cancellationToken)).IsSuccess,
            "page remains interactive after slow mouse navigation", null);
        Console.WriteLine("PASS slow coordinate click preserves the page and does not replay input");
    }

    private static async Task<BrowserElementReference> ReferenceAsync(
        BrowserSurface surface, string role, string name, CancellationToken cancellationToken)
    {
        var snapshot = await surface.CaptureSnapshotAsync(
            BrowserDocumentBinding.FromState(surface.State), cancellationToken);
        Require(snapshot.IsSuccess, "snapshot", snapshot.Error);
        var match = snapshot.Value!.Nodes.SingleOrDefault(node =>
            string.Equals(node.Role, role, StringComparison.Ordinal)
            && string.Equals(node.Name.Trim(), name, StringComparison.Ordinal));
        return match?.Reference ?? throw new InvalidOperationException(
            $"The fixture element {role}/{name} has no reference: "
            + string.Join("; ", snapshot.Value.Nodes.Select(node => $"{node.Role}/{node.Name}/{node.Reference?.Value}")));
    }

    private static void Require(bool condition, string step, BrowserError? error)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Failed {step}: {error?.StableCode}: {error?.Message}");
        }
    }
}
