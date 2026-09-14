using Asura.App;
using Asura.App.ViewModels;
using Asura.Application.ApplicationUpdates;

namespace Asura.App.Tests;

public sealed class ApplicationUpdateViewModelTests
{
    [Fact]
    public void Direct_update_moves_from_available_to_ready()
    {
        var service = new FakeApplicationUpdates();
        using var viewModel = new ApplicationUpdateViewModel(
            service,
            new ImmediateDispatcher());

        service.Set(ApplicationUpdateStage.Available, "1.4.0");

        Assert.True(viewModel.CanDownload);
        Assert.Contains("1.4.0", viewModel.Status, StringComparison.Ordinal);

        service.Set(
            ApplicationUpdateStage.ReadyToRestart,
            "1.4.0",
            downloadProgress: 100);

        Assert.True(viewModel.CanRestartToApply);
        Assert.False(viewModel.CanDownload);
        Assert.Contains("Restart", viewModel.Status, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DistributionSource.AppleAppStore, "Apple App Store")]
    [InlineData(DistributionSource.MicrosoftStore, "Microsoft Store")]
    [InlineData(DistributionSource.LinuxPackageManager, "Linux package manager")]
    public void Managed_distribution_uses_its_own_update_source(DistributionSource source, string channel)
    {
        var distribution = new DistributionIdentity(
            source,
            ApplicationUpdateStrategy.PlatformManaged,
            "osx-arm64-stable");
        using var viewModel = new ApplicationUpdateViewModel(
            new PassiveApplicationUpdateService(distribution),
            new ImmediateDispatcher());

        Assert.Equal(channel, viewModel.Channel);
        Assert.False(viewModel.CanCheck);
        Assert.False(viewModel.CanDownload);
        Assert.False(viewModel.CanRestartToApply);
        Assert.Contains("install source", viewModel.Status, StringComparison.Ordinal);
    }

    private sealed class ImmediateDispatcher : IUiThreadDispatcher
    {
        public Task InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApplicationUpdates : IApplicationUpdateService
    {
        private static readonly DistributionIdentity DirectDistribution = new(
            DistributionSource.GitHubRelease,
            ApplicationUpdateStrategy.Velopack,
            "osx-arm64-stable");

        public event EventHandler<ApplicationUpdateSnapshot>? Changed;

        public ApplicationUpdateSnapshot Snapshot { get; private set; } = new(
            DirectDistribution,
            ApplicationUpdateStage.Idle);

        public Task CheckAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DownloadAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void RestartToApply()
        {
        }

        public void Set(
            ApplicationUpdateStage stage,
            string version,
            int? downloadProgress = null)
        {
            Snapshot = new(
                DirectDistribution,
                stage,
                version,
                downloadProgress);
            Changed?.Invoke(this, Snapshot);
        }
    }
}
