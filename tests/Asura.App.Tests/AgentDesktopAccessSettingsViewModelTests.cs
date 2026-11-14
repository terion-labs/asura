using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

/// <summary>
/// Settings › AI › Desktop automation (ADR 0061): the row says what the
/// session host reports, reads it only when asked to, and leads to System
/// Settings only on a Mac.
/// </summary>
public sealed class AgentDesktopAccessSettingsViewModelTests
{
    [Theory]
    [InlineData(AgentDesktopAccessState.Unavailable, "Unavailable", StatusTone.Neutral, false)]
    [InlineData(AgentDesktopAccessState.NotGranted, "Access not granted", StatusTone.Warning, true)]
    [InlineData(AgentDesktopAccessState.Granted, "Ready", StatusTone.Success, false)]
    public async Task The_row_says_what_the_host_reports(
        AgentDesktopAccessState state,
        string label,
        StatusTone tone,
        bool offersSystemSettings)
    {
        var host = new AgentDesktopAccessTestHost { State = state };
        var viewModel = Create(host, new SystemSettingsOpener());

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(state, viewModel.State);
        Assert.Equal(label, viewModel.StateLabel);
        Assert.Equal(tone, viewModel.StateTone);
        Assert.Equal(offersSystemSettings, viewModel.OffersSystemSettings);
        Assert.EndsWith(".", viewModel.StateDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_state_explains_itself_differently()
    {
        var details = new[]
        {
            AgentDesktopAccessState.Unavailable,
            AgentDesktopAccessState.NotGranted,
            AgentDesktopAccessState.Granted,
        }.Select(state =>
        {
            var viewModel = Create(new AgentDesktopAccessTestHost { State = state }, new SystemSettingsOpener());
            Assert.True(viewModel.RefreshAsync(CancellationToken.None).IsCompletedSuccessfully);
            return viewModel.StateDetail;
        }).ToArray();

        Assert.Equal(details.Length, details.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Nothing is read until the row is looked at, so starting Asura never
    /// touches the system's accessibility setting.
    /// </summary>
    [Fact]
    public void A_row_nobody_has_looked_at_has_read_nothing()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.Granted };
        var viewModel = Create(host, new SystemSettingsOpener());

        Assert.Equal(0, host.ReadCount);
        Assert.Null(viewModel.State);
        Assert.Equal("Checking…", viewModel.StateLabel);
        Assert.Equal(StatusTone.Neutral, viewModel.StateTone);
        Assert.False(viewModel.OffersSystemSettings);
    }

    [Fact]
    public async Task Refreshing_follows_a_grant_given_and_taken_away_while_Asura_runs()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
        var viewModel = Create(host, new SystemSettingsOpener());
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.True(viewModel.OffersSystemSettings);

        changed.Clear();
        host.State = AgentDesktopAccessState.Granted;
        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("Ready", viewModel.StateLabel);
        Assert.False(viewModel.OffersSystemSettings);
        Assert.Equal(
            [
                nameof(AgentDesktopAccessSettingsViewModel.State),
                nameof(AgentDesktopAccessSettingsViewModel.StateLabel),
                nameof(AgentDesktopAccessSettingsViewModel.StateDetail),
                nameof(AgentDesktopAccessSettingsViewModel.StateTone),
                nameof(AgentDesktopAccessSettingsViewModel.OffersSystemSettings),
            ],
            changed);

        changed.Clear();
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Empty(changed);

        host.State = AgentDesktopAccessState.NotGranted;
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Equal("Access not granted", viewModel.StateLabel);
        Assert.True(viewModel.OffersSystemSettings);
        Assert.Equal(4, host.ReadCount);
    }

    [Fact]
    public async Task Without_a_desktop_host_the_row_is_unavailable_and_stays_so()
    {
        var opener = new SystemSettingsOpener();
        var viewModel = Create(null, opener);

        Assert.Equal(AgentDesktopAccessState.Unavailable, viewModel.State);

        await viewModel.RefreshAsync(CancellationToken.None);
        await viewModel.OpenSystemSettingsAsync();

        Assert.Equal(AgentDesktopAccessState.Unavailable, viewModel.State);
        Assert.Equal("Unavailable", viewModel.StateLabel);
        Assert.False(viewModel.OffersSystemSettings);
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task The_action_opens_the_Accessibility_list_and_nothing_else()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
        var opener = new SystemSettingsOpener();
        var viewModel = Create(host, opener);
        await viewModel.RefreshAsync(CancellationToken.None);
        var detail = viewModel.StateDetail;

        await viewModel.OpenSystemSettingsAsync();

        var opened = Assert.Single(opener.Opened);
        Assert.Equal(
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            opened.OriginalString);
        // Opening the pane neither reads the state nor changes what the row says.
        Assert.Equal(1, host.ReadCount);
        Assert.Equal(detail, viewModel.StateDetail);
    }

    /// <summary>
    /// macOS 27 calls the list Device Control and Data Access; through
    /// macOS 26 it is Accessibility. The link is the same for both.
    /// </summary>
    [Theory]
    [InlineData(false, "Accessibility")]
    [InlineData(true, "Device Control and Data Access")]
    public async Task The_row_names_the_list_this_Mac_shows(bool isMacOS27OrLater, string list)
    {
        Assert.Equal(list, AgentDesktopAccessSettingsViewModel.MacSystemSettingsListFor(isMacOS27OrLater));
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
        var opener = new SystemSettingsOpener { Opens = false };
        var viewModel = new AgentDesktopAccessSettingsViewModel(
            host,
            AgentDesktopAccessTestDispatcher.Instance,
            opener.OpenAsync,
            AgentDesktopAccessSettingsViewModel.MacSystemSettingsListFor(isMacOS27OrLater));
        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(
            $"Turn on Asura in Privacy & Security › {list} in System Settings. Until then, the agent asks you for it when it needs another app.",
            viewModel.StateDetail);
        Assert.Equal($"Open {list} in System Settings", viewModel.OpenSystemSettingsName);

        await viewModel.OpenSystemSettingsAsync();

        Assert.Equal(
            $"Asura couldn’t open System Settings. Open it, go to Privacy & Security › {list}, and turn on Asura.",
            viewModel.StateDetail);
        Assert.Equal(
            "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            Assert.Single(opener.Opened).OriginalString);
    }

    [Fact]
    public void On_this_computer_the_row_names_the_list_its_macOS_version_shows()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
        var viewModel = AgentDesktopAccessSettingsViewModel.ForThisPlatform(host, AgentDesktopAccessTestDispatcher.Instance);

        Assert.Equal(
            OperatingSystem.IsMacOSVersionAtLeast(27)
                ? "Open Device Control and Data Access in System Settings"
                : "Open Accessibility in System Settings",
            viewModel.OpenSystemSettingsName);
    }

    [Fact]
    public void The_list_name_is_required()
    {
        Assert.Throws<ArgumentException>(() => new AgentDesktopAccessSettingsViewModel(
            null,
            AgentDesktopAccessTestDispatcher.Instance,
            null,
            " "));
    }

    [Theory]
    [InlineData(AgentDesktopAccessState.Unavailable)]
    [InlineData(AgentDesktopAccessState.Granted)]
    public async Task The_action_does_nothing_unless_access_is_what_is_missing(AgentDesktopAccessState state)
    {
        var opener = new SystemSettingsOpener();
        var viewModel = Create(new AgentDesktopAccessTestHost { State = state }, opener);
        await viewModel.RefreshAsync(CancellationToken.None);

        await viewModel.OpenSystemSettingsAsync();

        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task Off_a_Mac_there_is_no_system_pane_to_offer()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
        var withoutPane = new AgentDesktopAccessSettingsViewModel(host, AgentDesktopAccessTestDispatcher.Instance);
        await withoutPane.RefreshAsync(CancellationToken.None);

        Assert.Equal("Access not granted", withoutPane.StateLabel);
        Assert.False(withoutPane.OffersSystemSettings);
        Assert.DoesNotContain("System Settings", withoutPane.StateDetail, StringComparison.Ordinal);
        await withoutPane.OpenSystemSettingsAsync();

        var onThisPlatform = AgentDesktopAccessSettingsViewModel.ForThisPlatform(
            host,
            AgentDesktopAccessTestDispatcher.Instance);
        await onThisPlatform.RefreshAsync(CancellationToken.None);

        Assert.Equal(OperatingSystem.IsMacOS(), onThisPlatform.OffersSystemSettings);
    }

    [Fact]
    public async Task A_pane_that_did_not_open_says_where_to_go_until_the_state_changes()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.NotGranted };
        var opener = new SystemSettingsOpener { Opens = false };
        var viewModel = Create(host, opener);
        await viewModel.RefreshAsync(CancellationToken.None);
        var offered = viewModel.StateDetail;

        await viewModel.OpenSystemSettingsAsync();

        Assert.Contains("couldn’t open System Settings", viewModel.StateDetail, StringComparison.Ordinal);
        Assert.True(viewModel.OffersSystemSettings);

        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Contains("couldn’t open System Settings", viewModel.StateDetail, StringComparison.Ordinal);

        opener.Opens = true;
        await viewModel.OpenSystemSettingsAsync();
        Assert.Equal(offered, viewModel.StateDetail);

        opener.Opens = false;
        await viewModel.OpenSystemSettingsAsync();
        host.State = AgentDesktopAccessState.Granted;
        await viewModel.RefreshAsync(CancellationToken.None);
        host.State = AgentDesktopAccessState.NotGranted;
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Equal(offered, viewModel.StateDetail);
    }

    /// <summary>The row never claims access it could not confirm.</summary>
    [Fact]
    public async Task A_reading_that_fails_is_unavailable()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.Granted };
        var viewModel = Create(host, new SystemSettingsOpener());
        await viewModel.RefreshAsync(CancellationToken.None);
        Assert.Equal(AgentDesktopAccessState.Granted, viewModel.State);

        host.Failure = new ObjectDisposedException("host");
        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(AgentDesktopAccessState.Unavailable, viewModel.State);
        Assert.False(viewModel.OffersSystemSettings);
    }

    [Fact]
    public async Task A_canceled_reading_leaves_the_row_as_it_was()
    {
        var host = new AgentDesktopAccessTestHost { State = AgentDesktopAccessState.Granted };
        var viewModel = Create(host, new SystemSettingsOpener());
        await viewModel.RefreshAsync(CancellationToken.None);
        host.State = AgentDesktopAccessState.NotGranted;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await viewModel.RefreshAsync(cancellation.Token);

        Assert.Equal(AgentDesktopAccessState.Granted, viewModel.State);
    }

    /// <summary>
    /// Opening the page and coming back to the window can both be reading at
    /// once; the older answer must not replace the newer one.
    /// </summary>
    [Fact]
    public async Task An_older_reading_that_finishes_last_is_not_shown()
    {
        var older = new TaskCompletionSource<AgentDesktopAccessState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new AgentDesktopAccessTestHost { Pending = older };
        var viewModel = Create(host, new SystemSettingsOpener());

        var first = viewModel.RefreshAsync(CancellationToken.None);
        host.State = AgentDesktopAccessState.Granted;
        await viewModel.RefreshAsync(CancellationToken.None);
        older.SetResult(AgentDesktopAccessState.NotGranted);
        await first;

        Assert.Equal(AgentDesktopAccessState.Granted, viewModel.State);
    }

    private static AgentDesktopAccessSettingsViewModel Create(
        AgentDesktopAccessTestHost? host,
        SystemSettingsOpener opener) =>
        new(host, AgentDesktopAccessTestDispatcher.Instance, opener.OpenAsync);

    private sealed class SystemSettingsOpener
    {
        public List<Uri> Opened { get; } = [];

        public bool Opens { get; set; } = true;

        public Task<bool> OpenAsync(Uri pane)
        {
            Opened.Add(pane);
            return Task.FromResult(Opens);
        }
    }
}
