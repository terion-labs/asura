using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class SettingsSearchAndNotificationSettingsTests
{
    [Fact]
    public void Every_sidebar_pane_has_one_title_used_everywhere()
    {
        Assert.Equal(
            SettingsPanes.Sidebar.Count,
            SettingsPanes.Sidebar.Select(SettingsPanes.Title).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Workspaces & Screens", SettingsPanes.Title(SettingsPage.Workspaces));
        Assert.Equal("Keyboard Shortcuts", SettingsPanes.Title(SettingsPage.Keybindings));
        Assert.Equal("AI", SettingsPanes.Title(SettingsPage.Mcp));
        Assert.Equal(SettingsPage.Agent, SettingsPanes.SidebarPage(SettingsPage.Mcp));
        Assert.DoesNotContain(SettingsPanes.Sidebar, page => page == SettingsPage.Mcp);
        Assert.Contains(SettingsPage.Notifications, SettingsPanes.Sidebar);
    }

    [Theory]
    [InlineData("Notifications", SettingsPage.Notifications)]
    [InlineData("Appearance", SettingsPage.Appearance)]
    [InlineData(null, null)]
    [InlineData("Settings", null)]
    [InlineData("99", null)]
    public void A_remembered_page_is_read_back_only_when_this_build_knows_it(
        string? stored,
        SettingsPage? expected) =>
        Assert.Equal(expected, SettingsPanes.Parse(stored));

    [Fact]
    public void An_empty_query_lists_every_pane()
    {
        var index = new SettingsSearchIndex();

        Assert.Equal(SettingsPanes.Sidebar, index.Match(null));
        Assert.Equal(SettingsPanes.Sidebar, index.Match("   "));
    }

    [Theory]
    [InlineData("dark mode", SettingsPage.Appearance)]
    [InlineData("KEYCHAIN", SettingsPage.Secrets)]
    [InlineData("hotkey", SettingsPage.Keybindings)]
    [InlineData("banner", SettingsPage.Notifications)]
    [InlineData("mcp", SettingsPage.Agent)]
    public void Pane_names_and_keywords_find_their_pane(string query, SettingsPage expected) =>
        Assert.Contains(expected, new SettingsSearchIndex().Match(query));

    [Fact]
    public void Row_text_is_searchable_and_every_word_must_appear_in_one_entry()
    {
        var index = new SettingsSearchIndex();
        index.Add(SettingsPage.Terminal, "Multiplex SSH terminals New SSH terminals use an app-managed tmux session");
        index.Add(SettingsPage.Mcp, "Copy Token");

        Assert.Equal([SettingsPage.Terminal], index.Match("multiplex ssh"));
        Assert.Equal([SettingsPage.Agent], index.Match("copy token"));
        Assert.Empty(index.Match("multiplex token"));
        Assert.True(SettingsSearchIndex.MatchesPage(SettingsPage.Mcp, "ai"));
        Assert.False(SettingsSearchIndex.MatchesPage(SettingsPage.Terminal, "kill switch"));
        Assert.True(SettingsSearchIndex.Matches("Show tab bar", "TAB"));
        Assert.False(SettingsSearchIndex.Matches(null, "tab"));
    }

    [Fact]
    public void Matches_come_back_in_sidebar_order()
    {
        var index = new SettingsSearchIndex();
        index.Add(SettingsPage.About, "zzz-marker");
        index.Add(SettingsPage.Appearance, "zzz-marker");

        Assert.Equal([SettingsPage.Appearance, SettingsPage.About], index.Match("zzz-marker"));
    }

    [Fact]
    public async Task Notification_switches_apply_at_once_and_follow_changes_from_another_window()
    {
        var settings = new FakeNotificationSettings();
        using var first = new NotificationSettingsViewModel(settings, ImmediateDispatcher.Instance);
        using var second = new NotificationSettingsViewModel(settings, ImmediateDispatcher.Instance);
        var changed = new List<string?>();
        second.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        Assert.True(first.IsAvailable);
        Assert.True(first.ShowsAgent);
        first.ShowsAgent = false;

        Assert.False(settings.Preferences.Agent);
        Assert.Equal(1, settings.SaveCount);
        Assert.False(second.ShowsAgent);
        Assert.Contains(changed, name => string.Equals(name, nameof(NotificationSettingsViewModel.ShowsAgent), StringComparison.Ordinal));

        first.ShowsAgent = false;
        Assert.Equal(1, settings.SaveCount);

        settings.Authorization = NotificationAuthorization.Denied;
        await first.RefreshAuthorizationAsync(CancellationToken.None);
        Assert.True(first.IsDenied);
        Assert.Equal(StatusTone.Danger, first.AuthorizationTone);
        Assert.True(first.OffersSystemSettings);
        first.OpenSystemSettings();
        Assert.Equal(1, settings.OpenedSystemSettings);

        settings.Authorization = NotificationAuthorization.Authorized;
        await first.RefreshAuthorizationAsync(CancellationToken.None);
        Assert.Equal("Allowed", first.AuthorizationLabel);
        Assert.Equal(StatusTone.Success, first.AuthorizationTone);
        Assert.False(first.OffersSystemSettings);
    }

    [Fact]
    public void A_host_without_notifications_says_so_and_offers_no_switches()
    {
        using var viewModel = new NotificationSettingsViewModel(null, ImmediateDispatcher.Instance);

        Assert.False(viewModel.IsAvailable);
        Assert.Equal(NotificationAuthorization.Unavailable, viewModel.Authorization);
        Assert.Equal("Unavailable", viewModel.AuthorizationLabel);
        Assert.False(viewModel.OffersSystemSettings);
    }

    [Theory]
    [InlineData(GovernedAgentState.Failed, "stopped with an error")]
    [InlineData(GovernedAgentState.Ready, "finished its run")]
    public void Agent_notifications_put_the_outcome_in_the_body(GovernedAgentState state, string expected)
    {
        Assert.Contains(expected, MainWindowViewModel.AgentRunOutcomeText("Main", state), StringComparison.Ordinal);
        Assert.Contains("“Main”", MainWindowViewModel.AgentRunOutcomeText("Main", state), StringComparison.Ordinal);
    }

    private sealed class FakeNotificationSettings : INativeNotificationSettings
    {
        public NotificationPreferences Preferences { get; private set; } = NotificationPreferences.Default;

        public NotificationAuthorization Authorization { get; set; } = NotificationAuthorization.NotDetermined;

        public int SaveCount { get; private set; }

        public int OpenedSystemSettings { get; private set; }

        public event EventHandler? PreferencesChanged;

        public bool CanOpenSystemSettings => true;

        public void SetPreferences(NotificationPreferences preferences)
        {
            if (Preferences == preferences)
            {
                return;
            }

            Preferences = preferences;
            SaveCount++;
            PreferencesChanged?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask<NotificationAuthorization> ReadAuthorizationAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Authorization);

        public void OpenSystemSettings() => OpenedSystemSettings++;
    }

    private sealed class ImmediateDispatcher : IUiThreadDispatcher
    {
        public static ImmediateDispatcher Instance { get; } = new();

        public Task InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
