using System.Reflection;
using Asura.App;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class BrowserHistoryRecordingTests
{
    [Fact]
    public async Task Title_bursts_serialize_history_and_remember_the_final_title()
    {
        var history = new BlockedHistory("Final title");
        using var panel = HistoryPanel(history);
        var address = Address("https://example.test/history");
        panel.ApplyBrowserState(new(address, "Loaded", BrowserLoadState.Ready, false, false, 1));
        for (var index = 0; index < 500; index++)
        {
            panel.ApplyBrowserState(new(address, index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                BrowserLoadState.Ready, false, false, 1));
        }
        panel.ApplyBrowserState(new(address, "Final title", BrowserLoadState.Ready, false, false, 1));

        Assert.Equal(address, panel.CurrentAddress);
        Assert.Equal(1, history.StartedWrites);
        history.ReleaseFirstWrite();
        await history.FinalTitleSaved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, history.MaximumConcurrentWrites);
        Assert.Equal(["Loaded", "Final title"], history.Records.Select(record => record.Title), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Navigation_during_a_title_burst_keeps_each_address_with_its_title()
    {
        var history = new BlockedHistory("Second final");
        using var panel = HistoryPanel(history);
        var first = Address("https://example.test/first");
        var second = Address("https://example.test/second");
        panel.ApplyBrowserState(new(first, "First loaded", BrowserLoadState.Ready, false, false, 1));
        panel.ApplyBrowserState(new(first, "First final", BrowserLoadState.Ready, false, false, 1));
        panel.ApplyBrowserState(new(second, "Second loaded", BrowserLoadState.Ready, false, false, 2));
        panel.ApplyBrowserState(new(second, "Second final", BrowserLoadState.Ready, false, false, 2));
        history.ReleaseFirstWrite();
        await history.FinalTitleSaved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, history.MaximumConcurrentWrites);
        Assert.Equal([(first, "First loaded"), (first, "First final"), (second, "Second final")], history.Records);
    }

    [Fact]
    public async Task Deferred_navigation_history_retains_the_latest_thousand_pending_addresses()
    {
        var history = new BlockedHistory("Final title");
        using var panel = HistoryPanel(history);
        panel.ApplyBrowserState(new(Address("https://example.test/initial"), "Initial", BrowserLoadState.Ready, false, false, 1));
        for (var index = 0; index < 1_500; index++)
        {
            var address = Address("https://example.test/history/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            panel.ApplyBrowserState(new(address, "Navigation", BrowserLoadState.Ready, false, false, index + 2));
        }
        var finalAddress = Address("https://example.test/history/1499");
        panel.ApplyBrowserState(new(finalAddress, "Final title", BrowserLoadState.Ready, false, false, 1_501));
        Assert.Equal(1, history.StartedWrites);
        history.ReleaseFirstWrite();
        await history.FinalTitleSaved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1_001, history.Records.Count);
        Assert.Equal(Address("https://example.test/history/500"), history.Records[1].Address);
        Assert.Equal((finalAddress, "Final title"), history.Records[^1]);
        Assert.Equal(1, history.MaximumConcurrentWrites);
    }

    [Fact]
    public async Task Clearing_history_waits_for_the_current_write_and_discards_pending_titles()
    {
        var history = new BlockedHistory("Pending title");
        using var panel = HistoryPanel(history);
        var address = Address("https://example.test/history");
        panel.ApplyBrowserState(new(address, "Loaded", BrowserLoadState.Ready, false, false, 1));
        panel.ApplyBrowserState(new(address, "Pending title", BrowserLoadState.Ready, false, false, 1));

        var clearing = panel.ClearHistoryAsync(CancellationToken.None);
        Assert.False(clearing.IsCompleted);
        history.ReleaseFirstWrite();
        await clearing.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, history.StartedWrites);
        Assert.Empty(history.Records);
        Assert.False(history.FinalTitleSaved.Task.IsCompleted);
    }

    [Fact]
    public async Task Disposing_a_panel_cancels_the_current_history_write_and_drops_pending_titles()
    {
        var history = new BlockedHistory("Pending title");
        using var panel = HistoryPanel(history);
        var address = Address("https://example.test/history");
        panel.ApplyBrowserState(new(address, "Loaded", BrowserLoadState.Ready, false, false, 1));
        panel.ApplyBrowserState(new(address, "Pending title", BrowserLoadState.Ready, false, false, 1));

        panel.Dispose();
        await history.FirstWriteCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, history.StartedWrites);
        Assert.Empty(history.Records);
        Assert.False(history.FinalTitleSaved.Task.IsCompleted);
    }

    private static BrowserRuntimePanelViewModel HistoryPanel(IBrowserHistory history)
    {
        var profile = new BrowserProfileDefinition(new BrowserProfileId("browser.history"), 1,
            "History", BrowserProfilePersistence.DurableMetadata, BrowserProfilePrivacyPolicy.Strict);
        var panelId = new PanelInstanceId("browser-history");
        return new BrowserRuntimePanelViewModel(panelId, "Browser",
            new SessionOwner(HostMode.Desktop, new WindowInstanceId("window"),
                new WorkspaceInstanceId("workspace"), new TabInstanceId("tab"), panelId),
            BrowserAddress.Blank, DispatchProxy.Create<ISessionHostClient, NoopHistoryProxy>(),
            new ClientId("client"), BuiltInConnections.Local,
            new BrowserProfileBinding(new BrowserProfileSelection(profile.Id, BrowserProfileKey.Global), profile, 1),
            DispatchProxy.Create<IBrowserRendererViewFactory, NoopHistoryProxy>(),
            history: history);
    }

    private sealed class BlockedHistory(string expectedFinalTitle) : IBrowserHistory
    {
        private readonly object _gate = new();
        private readonly List<(BrowserAddress Address, string Title)> _records = [];
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startedWrites;
        private int _activeWrites;
        public int StartedWrites => Volatile.Read(ref _startedWrites);
        public int MaximumConcurrentWrites { get; private set; }
        public TaskCompletionSource FinalTitleSaved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstWriteCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<(BrowserAddress Address, string Title)> Records
        {
            get
            {
                lock (_gate)
                {
                    return [.. _records];
                }
            }
        }
        public void ReleaseFirstWrite() => _release.TrySetResult();
        public async ValueTask RecordAsync(BrowserProfileSelection profile, BrowserAddress address,
            string title, CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref _activeWrites);
            var started = Interlocked.Increment(ref _startedWrites);
            lock (_gate)
            {
                MaximumConcurrentWrites = Math.Max(MaximumConcurrentWrites, current);
            }
            try
            {
                if (started == 1)
                {
                    await _release.Task.WaitAsync(cancellationToken);
                }
                lock (_gate)
                {
                    _records.Add((address, title));
                }
            }
            catch (OperationCanceledException)
            {
                FirstWriteCancelled.TrySetResult();
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _activeWrites);
            }
            if (string.Equals(title, expectedFinalTitle, StringComparison.Ordinal))
            {
                FinalTitleSaved.TrySetResult();
            }
        }
        public ValueTask<IReadOnlyList<BrowserHistoryEntry>> SearchAsync(BrowserProfileSelection profile,
            string query, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<BrowserHistoryEntry>>([]);
        public ValueTask ClearAsync(BrowserProfileSelection profile, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _records.Clear();
            }
            return ValueTask.CompletedTask;
        }
    }

    private class NoopHistoryProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("History tests do not initialize a renderer or hosted session.");
    }

    private static BrowserAddress Address(string value) => new(new Uri(value));
}
