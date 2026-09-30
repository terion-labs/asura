using Asura.Application;
using Asura.Core;
using Avalonia;

namespace Asura.Browser.Tests;

public sealed class BrowserInputRecoveryTests
{
    private static readonly BrowserAddress Page = new(new Uri("https://example.test/form"));

    [Fact]
    public async Task DefaultInputDeadlineAllowsAcknowledgementAfterFiveSeconds()
    {
        var native = new RecordingEmbeddedBrowserView
        {
            PendingAutomation = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var surface = new BrowserSurface(native, BrowserTestDestinationPolicy.Public,
            InlineBrowserUiDispatcher.Instance,
            capabilityProfile: BrowserCapabilityProfile.FullAutomationCandidate);
        surface.Measure(new Size(800, 600));
        surface.Arrange(new Rect(0, 0, 800, 600));
        var action = DispatchAsync(surface, "mouse",
            BrowserAutomationBinding.FromState(surface.State), CancellationToken.None).AsTask();

        await Task.Delay(TimeSpan.FromSeconds(6));
        Assert.False(action.IsCompleted);
        Assert.False(native.LastAutomationCancellation.IsCancellationRequested);
        native.PendingAutomation.SetResult(NativeBrowserAutomationResult.Acknowledged());
        Assert.True((await action.WaitAsync(TimeSpan.FromSeconds(2))).IsSuccess);
        Assert.Equal(1, native.AutomationDispatchCount);
    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("check")]
    public async Task SemanticInputTimeoutPreservesPageUntilNativeCompletion(string kind)
    {
        var native = new RecordingEmbeddedBrowserView
        {
            SnapshotResult = NativeBrowserSnapshotResult.Success(new NativeBrowserSnapshot(
                [new NativeBrowserSnapshotNode(0, "document", "Fixture", BrowserSnapshotNodeState.None, Handle: null),
                 new NativeBrowserSnapshotNode(1, "button", "Fixture", BrowserSnapshotNodeState.None,
                    new NativeBrowserElementHandle("snapshot_test", "fixture", 0))], IsTruncated: false)),
            PendingClick = new(TaskCreationOptions.RunContinuationsAsynchronously),
            PendingFill = new(TaskCreationOptions.RunContinuationsAsynchronously),
            PendingCheck = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var surface = Surface(native);
        var document = BrowserDocumentBinding.FromState(surface.State);
        var snapshot = await surface.CaptureSnapshotAsync(document, CancellationToken.None);
        var reference = snapshot.Value!.Nodes[1].Reference!;
        var error = kind switch
        {
            "click" => (await surface.ClickWithinOriginAsync(reference,
                BrowserNavigationOrigin.WorkspaceNetwork, CancellationToken.None)).Error,
            "fill" => (await surface.FillWithinOriginAsync(reference, "synthetic input",
                BrowserNavigationOrigin.WorkspaceNetwork, CancellationToken.None)).Error,
            "check" => (await surface.CheckWithinOriginAsync(reference,
                BrowserNavigationOrigin.WorkspaceNetwork, CancellationToken.None)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        Assert.Equal(BrowserErrorCode.InteractionOutcomeUnknown, error?.Code);
        Assert.False(native.IsDisposed);
        Assert.Equal(Page, surface.State.Address);
        Assert.False(surface.TryResolveElementReference(reference, out _));
        Assert.False((await surface.CaptureSnapshotAsync(document, CancellationToken.None)).IsSuccess);

        native.PendingClick.SetResult(NativeBrowserClickResult.Activated());
        native.PendingFill.SetResult(NativeBrowserFillResult.Filled());
        native.PendingCheck.SetResult(NativeBrowserCheckResult.Checked());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!(await surface.CaptureSnapshotAsync(document, timeout.Token)).IsSuccess)
        {
            await Task.Delay(1, timeout.Token);
        }

        Assert.Equal(1, native.ClickCount + native.FillCount + native.CheckCount);
        Assert.Equal(Page, surface.State.Address);
        Assert.Equal(document.DocumentRevision, surface.State.DocumentRevision);
    }

    [Theory]
    [InlineData("mouse")]
    [InlineData("key")]
    [InlineData("scroll")]
    public async Task UnknownWorkspaceInputPreservesPageAndInvalidatesOldCoordinates(string kind)
    {
        var native = new RecordingEmbeddedBrowserView
        {
            AutomationResult = NativeBrowserAutomationResult.OutcomeUnknown(),
        };
        var surface = Surface(native);
        var binding = BrowserAutomationBinding.FromState(surface.State);

        var result = await DispatchAsync(surface, kind, binding, CancellationToken.None);

        Assert.Equal(BrowserErrorCode.InteractionOutcomeUnknown, result.Error?.Code);
        Assert.False(native.IsDisposed);
        Assert.Equal(Page, surface.State.Address);
        Assert.Equal(binding.InputEpoch + 1, surface.State.InputEpoch);
        Assert.Equal(1, native.AutomationDispatchCount);
        var stale = await DispatchAsync(surface, kind, binding, CancellationToken.None);
        Assert.Equal(BrowserErrorCode.NavigationStateChanged, stale.Error?.Code);
        Assert.Equal(1, native.AutomationDispatchCount);

        native.AutomationResult = NativeBrowserAutomationResult.Acknowledged();
        Assert.True((await DispatchAsync(surface, kind,
            BrowserAutomationBinding.FromState(surface.State), CancellationToken.None)).IsSuccess);
        Assert.Equal(2, native.AutomationDispatchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrDeadlinePreservesPageAndGatesInputUntilNativeDispatchSettles(bool deadline)
    {
        var pending = new TaskCompletionSource<NativeBrowserAutomationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var native = new RecordingEmbeddedBrowserView { PendingAutomation = pending };
        var surface = Surface(native);
        using var cancellation = new CancellationTokenSource();
        var binding = BrowserAutomationBinding.FromState(surface.State);
        var action = DispatchAsync(surface, "mouse", binding, cancellation.Token).AsTask();
        Assert.Equal(1, native.AutomationDispatchCount);
        if (!deadline)
        {
            cancellation.Cancel();
        }

        var result = await action.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(BrowserErrorCode.InteractionOutcomeUnknown, result.Error?.Code);
        Assert.True(native.LastAutomationCancellation.IsCancellationRequested);
        Assert.False(native.IsDisposed);
        Assert.Equal(Page, surface.State.Address);
        Assert.Equal(binding.InputEpoch + 1, surface.State.InputEpoch);
        var overlap = await DispatchAsync(surface, "key",
            BrowserAutomationBinding.FromState(surface.State), CancellationToken.None);
        Assert.Equal(BrowserErrorCode.NavigationInProgress, overlap.Error?.Code);
        Assert.Equal(1, native.AutomationDispatchCount);

        pending.SetResult(NativeBrowserAutomationResult.Acknowledged());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!(await surface.CaptureSnapshotAsync(
            BrowserDocumentBinding.FromState(surface.State), timeout.Token)).IsSuccess)
        {
            await Task.Delay(1, timeout.Token);
        }

        Assert.Equal(binding.InputEpoch + 1, surface.State.InputEpoch);
        Assert.Equal(1, native.AutomationDispatchCount);
        native.PendingAutomation = null;
        Assert.True((await DispatchAsync(surface, "key",
            BrowserAutomationBinding.FromState(surface.State), timeout.Token)).IsSuccess);
        Assert.Equal(2, native.AutomationDispatchCount);
    }

    [Fact]
    public async Task AcknowledgedWorkspaceClickDoesNotWaitForDestinationPageToLoad()
    {
        var pending = new TaskCompletionSource<NativeBrowserAutomationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var native = new RecordingEmbeddedBrowserView { PendingAutomation = pending };
        var surface = Surface(native);
        var binding = BrowserAutomationBinding.FromState(surface.State);
        var action = DispatchAsync(surface, "mouse", binding, CancellationToken.None).AsTask();
        var destination = new BrowserAddress(new Uri("https://example.test/slow"));
        Assert.False(native.RaiseNavigationStarted(destination));
        pending.SetResult(NativeBrowserAutomationResult.Acknowledged());

        var result = await action.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result.IsSuccess);
        Assert.Equal(BrowserLoadState.Loading, result.Value!.FreshState.LoadState);
        Assert.False(native.IsDisposed);
        native.RaiseNavigationCompleted(destination, true);
        Assert.Equal(destination, surface.State.Address);
        Assert.Equal(BrowserLoadState.Ready, surface.State.LoadState);
        Assert.Equal(1, native.AutomationDispatchCount);
    }

    private static BrowserSurface Surface(RecordingEmbeddedBrowserView native)
    {
        var surface = new BrowserSurface(native, BrowserTestDestinationPolicy.Public,
            InlineBrowserUiDispatcher.Instance,
            () => new RecordingEmbeddedBrowserView(), static _ => { },
            capabilityProfile: BrowserCapabilityProfile.FullAutomationCandidate,
            nativeInputDeadline: TimeSpan.FromMilliseconds(100));
        surface.Measure(new Size(800, 600));
        surface.Arrange(new Rect(0, 0, 800, 600));
        native.RaiseNavigationStarted(Page);
        native.RaiseNavigationCompleted(Page, true);
        return surface;
    }

    private static ValueTask<BrowserResult<BrowserAutomationReceipt>> DispatchAsync(
        BrowserSurface surface, string kind, BrowserAutomationBinding binding, CancellationToken token) =>
        kind switch
        {
            "mouse" => surface.DispatchMouseWithinOriginAsync(new BrowserMouseRequest(
                new SessionId("browser"), binding, BrowserMouseAction.Click, 20, 30,
                BrowserMouseButton.Left, clickCount: 1), BrowserNavigationOrigin.WorkspaceNetwork, token),
            "key" => surface.DispatchKeyWithinOriginAsync(new BrowserKeyRequest(
                new SessionId("browser"), binding, BrowserKeyAction.Press, BrowserKey.Enter),
                BrowserNavigationOrigin.WorkspaceNetwork, token),
            "scroll" => surface.ScrollWithinOriginAsync(new BrowserScrollRequest(
                new SessionId("browser"), binding, 20, 30, 0, 100),
                BrowserNavigationOrigin.WorkspaceNetwork, token),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
