using Asura.Application;
using Asura.Browser;
using Avalonia.VisualTree;
using Exclr8Cef.WebView;

namespace Asura.BrowserAgentAcceptance;

internal static class BrowserRendererRecoveryAcceptance
{
    public static async Task VerifyAsync(BrowserSurface surface, BrowserAddress address, CancellationToken cancellationToken)
    {
        // Repeated navigation must not be mistaken for a renderer failure.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await NavigateAsync(surface, address, cancellationToken);
        }

        var logPath = Path.Combine(Program.Profile, "renderer-exits.log");
        if (File.Exists(logPath))
        {
            throw new InvalidOperationException("A renderer exited during repeated fixture navigation.");
        }

        var recovered = new TaskCompletionSource<BrowserProductEvent.RendererRecovered>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnProductEvent(object? sender, BrowserProductEvent productEvent)
        {
            if (productEvent is BrowserProductEvent.RendererRecovered recovery)
            {
                recovered.TrySetResult(recovery);
            }
        }

        surface.ProductEvent += OnProductEvent;
        try
        {
            var browser = surface.GetVisualDescendants().OfType<WebView>().Single().Browser
                ?? throw new InvalidOperationException("The fixture browser is not ready.");
            // Crash only this disposable fixture renderer, never an installed app.
            if (!browser.SendDevToolsMessageRaw("{\"id\":900001,\"method\":\"Page.crash\"}"))
            {
                throw new InvalidOperationException("The fixture crash request was not dispatched.");
            }
            var result = await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (result.LostAddress != address
                || !File.ReadAllText(logPath).Contains("status=2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Renderer recovery lost its address or crash evidence.");
            }

            await NavigateAsync(surface, address, cancellationToken);
            Console.WriteLine("PASS repeated navigation, real renderer crash evidence, replacement and reload");
        }
        finally
        {
            surface.ProductEvent -= OnProductEvent;
        }
    }

    private static async Task NavigateAsync(BrowserSurface surface, BrowserAddress address, CancellationToken cancellationToken)
    {
        var result = await surface.NavigateWithinOriginAsync(
            new BrowserOriginConstrainedNavigationRequest.Navigate(address),
            BrowserNavigationOrigin.WorkspaceNetwork,
            BrowserNavigationStartBinding.FromState(surface.State), cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Fixture navigation failed: " + result.Error?.Code);
        }
    }
}
