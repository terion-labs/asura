using Asura.App.ViewModels;

namespace Asura.App.Tests;

/// <summary>
/// The Settings window's back and forward, as System Settings keeps them.
/// </summary>
public sealed class SettingsNavigationHistoryTests
{
    [Fact]
    public void A_fresh_history_goes_nowhere()
    {
        var history = new SettingsNavigationHistory();

        Assert.Null(history.Current);
        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.False(history.TryGoBack(out _));
        Assert.False(history.TryGoForward(out _));
    }

    [Fact]
    public void Opening_pages_adds_them_after_the_one_on_show()
    {
        var history = new SettingsNavigationHistory();
        history.Visit(SettingsPage.Appearance);
        history.Visit(SettingsPage.Terminal);
        history.Visit(SettingsPage.Git);

        Assert.Equal(SettingsPage.Git, history.Current);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);
        Assert.Equal(SettingsPage.Terminal, history.BackTarget);
    }

    [Fact]
    public void Showing_the_page_already_on_show_changes_nothing()
    {
        var history = new SettingsNavigationHistory();
        history.Visit(SettingsPage.Appearance);
        history.Visit(SettingsPage.Appearance);

        Assert.False(history.CanGoBack);
    }

    /// <summary>
    /// Back names the page to ask for; the list moves only once that page is
    /// on show, so a draft kept on the page being left keeps the person —
    /// and the history — where they are.
    /// </summary>
    [Fact]
    public void Going_back_and_forward_moves_along_the_list_once_the_page_shows()
    {
        var history = new SettingsNavigationHistory();
        history.Visit(SettingsPage.Appearance);
        history.Visit(SettingsPage.Terminal);
        history.Visit(SettingsPage.Git);

        Assert.True(history.TryGoBack(out var back));
        Assert.Equal(SettingsPage.Terminal, back);
        // Not moved yet: the page has only been asked for.
        Assert.Equal(SettingsPage.Git, history.Current);

        history.Visit(back);
        Assert.Equal(SettingsPage.Terminal, history.Current);
        Assert.True(history.CanGoForward);
        Assert.Equal(SettingsPage.Git, history.ForwardTarget);

        Assert.True(history.TryGoBack(out back));
        history.Visit(back);
        Assert.Equal(SettingsPage.Appearance, history.Current);
        Assert.False(history.CanGoBack);

        Assert.True(history.TryGoForward(out var forward));
        Assert.Equal(SettingsPage.Terminal, forward);
        history.Visit(forward);
        Assert.Equal(SettingsPage.Terminal, history.Current);
        Assert.True(history.CanGoBack);
        Assert.True(history.CanGoForward);
    }

    [Fact]
    public void Opening_a_page_after_going_back_drops_the_pages_ahead()
    {
        var history = new SettingsNavigationHistory();
        history.Visit(SettingsPage.Appearance);
        history.Visit(SettingsPage.Terminal);
        history.Visit(SettingsPage.Git);
        Assert.True(history.TryGoBack(out var back));
        history.Visit(back);

        history.Visit(SettingsPage.Notifications);

        Assert.Equal(SettingsPage.Notifications, history.Current);
        Assert.False(history.CanGoForward);
        Assert.Equal(SettingsPage.Terminal, history.BackTarget);
    }

    /// <summary>
    /// Back was asked for and refused — the person kept a draft — and then a
    /// different page was opened from the sidebar: that is a new visit, not
    /// the move back.
    /// </summary>
    [Fact]
    public void A_refused_move_does_not_move_a_later_visit()
    {
        var history = new SettingsNavigationHistory();
        history.Visit(SettingsPage.Appearance);
        history.Visit(SettingsPage.Terminal);
        Assert.True(history.TryGoBack(out _));

        history.Visit(SettingsPage.Git);

        Assert.Equal(SettingsPage.Git, history.Current);
        Assert.Equal(SettingsPage.Terminal, history.BackTarget);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void The_oldest_pages_are_forgotten_past_the_capacity()
    {
        var history = new SettingsNavigationHistory();
        var pages = SettingsPanes.Sidebar;
        for (var visit = 0; visit < SettingsNavigationHistory.Capacity + 10; visit++)
        {
            history.Visit(pages[visit % pages.Count]);
        }

        var steps = 0;
        while (history.TryGoBack(out var back))
        {
            history.Visit(back);
            steps++;
        }

        Assert.Equal(SettingsNavigationHistory.Capacity - 1, steps);
    }

    [Fact]
    public void Moving_says_so()
    {
        var history = new SettingsNavigationHistory();
        var changed = new List<string?>();
        history.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        history.Visit(SettingsPage.Appearance);
        history.Visit(SettingsPage.Git);

        Assert.Contains(nameof(SettingsNavigationHistory.CanGoBack), changed, StringComparer.Ordinal);
        Assert.Contains(nameof(SettingsNavigationHistory.Current), changed, StringComparer.Ordinal);
    }
}
