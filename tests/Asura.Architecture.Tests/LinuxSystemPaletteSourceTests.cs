using Asura.App;
using Asura.Desktop;
using Avalonia.Media;
using Tmds.DBus.Protocol;

namespace Asura.Architecture.Tests;

public sealed class LinuxSystemPaletteSourceTests
{
    [Fact]
    public async Task A_portal_accent_is_carried_as_the_host_accent()
    {
        var client = new FakePortalClient();
        client.Set(Accent(0.239, 0.682, 0.914));
        using var source = new Owned(client);

        source.Source.Start();
        await source.Source.Initialization;

        Assert.False(source.Source.Current.AccentIsAbsent);
        Assert.Equal(Color.FromRgb(61, 174, 233), source.Source.Current.Accent);
    }

    [Fact]
    public async Task A_portal_without_an_accent_key_reports_the_accent_absent()
    {
        var client = new FakePortalClient();
        using var source = new Owned(client);

        source.Source.Start();
        await source.Source.Initialization;

        Assert.True(source.Source.Current.AccentIsAbsent);
        Assert.Null(source.Source.Current.Accent);
    }

    [Fact]
    public async Task No_portal_at_all_reports_the_accent_absent()
    {
        var client = new FakePortalClient { WatchFails = true };
        using var source = new Owned(client);

        source.Source.Start();
        await source.Source.Initialization;

        Assert.True(source.Source.Current.AccentIsAbsent);
    }

    [Fact]
    public async Task An_out_of_range_accent_counts_as_unset()
    {
        var client = new FakePortalClient();
        client.Set(Accent(1.2, 0.5, 0.5));
        using var source = new Owned(client);

        source.Source.Start();
        await source.Source.Initialization;

        Assert.True(source.Source.Current.AccentIsAbsent);
    }

    [Fact]
    public async Task A_changed_accent_is_published_live()
    {
        var client = new FakePortalClient();
        using var source = new Owned(client);
        var changes = 0;
        source.Source.Changed += (_, _) => Interlocked.Increment(ref changes);
        source.Source.Start();
        await source.Source.Initialization;
        Assert.True(source.Source.Current.AccentIsAbsent);

        client.Set(Accent(0.929, 0.357, 0.0));
        client.Emit(Accent(0.929, 0.357, 0.0));
        await WaitUntilAsync(() => source.Source.Current.Accent is not null);

        Assert.Equal(Color.FromRgb(237, 91, 0), source.Source.Current.Accent);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task Stop_disposes_the_subscription_and_the_client()
    {
        var client = new FakePortalClient();
        using var source = new LinuxSystemPaletteSource(client);
        source.Start();
        await source.Initialization;

        source.Stop();

        Assert.True(client.SubscriptionDisposed);
        Assert.True(client.Disposed);
    }

    private static VariantValue Accent(double red, double green, double blue) =>
        VariantValue.Struct(VariantValue.Double(red), VariantValue.Double(green), VariantValue.Double(blue));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class Owned(FakePortalClient client) : IDisposable
    {
        public LinuxSystemPaletteSource Source { get; } = new(client);

        public void Dispose() => Source.Dispose();
    }

    private sealed class FakePortalClient : ILinuxPortalSettingsClient
    {
        private VariantValue? _accent;
        private Action<Exception?, LinuxPortalSettingChanged>? _handler;

        public bool WatchFails { get; init; }

        public bool Disposed { get; private set; }

        public bool SubscriptionDisposed { get; private set; }

        public void Set(VariantValue accent) => _accent = accent;

        public void Emit(VariantValue accent) =>
            _handler?.Invoke(
                null,
                new LinuxPortalSettingChanged(
                    LinuxSystemPaletteSource.AppearanceNamespace,
                    LinuxSystemPaletteSource.AccentColorKey,
                    accent));

        public Task<IDisposable> WatchSettingChangedAsync(
            Action<Exception?, LinuxPortalSettingChanged> handler)
        {
            if (WatchFails)
            {
                throw new InvalidOperationException("no portal");
            }

            _handler = handler;
            return Task.FromResult<IDisposable>(new CallbackDisposable(() => SubscriptionDisposed = true));
        }

        public Task<uint> GetVersionAsync() => Task.FromResult(2u);

        public Task<VariantValue> ReadAsync(string settingNamespace, string key, uint version)
        {
            if (_accent is { } accent
                && string.Equals(settingNamespace, LinuxSystemPaletteSource.AppearanceNamespace, StringComparison.Ordinal)
                && string.Equals(key, LinuxSystemPaletteSource.AccentColorKey, StringComparison.Ordinal))
            {
                return Task.FromResult(accent);
            }

            throw new InvalidOperationException("Requested setting not found");
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class CallbackDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
