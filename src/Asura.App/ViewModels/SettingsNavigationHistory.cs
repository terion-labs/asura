namespace Asura.App.ViewModels;

/// <summary>
/// The pages visited in the Settings window, for its back and forward
/// buttons and ⌘[ and ⌘], as System Settings keeps them: opening a page
/// adds it after the one on show and drops whatever was ahead of it, and
/// going back or forward moves along the list without changing it.
///
/// Moving is asked for rather than done. The window shows a page only once a
/// draft on the one being left is settled, and a person who keeps the draft
/// stays where they are; so going back names the page to request, and the
/// list moves when that page actually comes on show.
/// </summary>
public sealed class SettingsNavigationHistory : ObservableObject
{
    /// <summary>How many pages are remembered, oldest forgotten first.</summary>
    internal const int Capacity = 64;

    private readonly List<SettingsPage> _visited = [];
    private int _current = -1;
    private int? _pending;

    /// <summary>The page on show, if any has been.</summary>
    public SettingsPage? Current => _current >= 0 ? _visited[_current] : null;

    public bool CanGoBack => _current > 0;

    public bool CanGoForward => _current >= 0 && _current < _visited.Count - 1;

    /// <summary>The page the back button would show.</summary>
    public SettingsPage? BackTarget => CanGoBack ? _visited[_current - 1] : null;

    /// <summary>The page the forward button would show.</summary>
    public SettingsPage? ForwardTarget => CanGoForward ? _visited[_current + 1] : null;

    /// <summary>
    /// A page came on show. When it is the one going back or forward asked
    /// for, the list moves to it; otherwise it is opened after the page on
    /// show, and the pages that were ahead are dropped. Showing the page that
    /// is already current changes nothing.
    /// </summary>
    public void Visit(SettingsPage page)
    {
        var pending = _pending;
        _pending = null;
        if (pending is { } target && target < _visited.Count && _visited[target] == page)
        {
            _current = target;
            Raise();
            return;
        }

        if (Current == page)
        {
            return;
        }

        if (_current < _visited.Count - 1)
        {
            _visited.RemoveRange(_current + 1, _visited.Count - _current - 1);
        }

        _visited.Add(page);
        if (_visited.Count > Capacity)
        {
            _visited.RemoveAt(0);
        }

        _current = _visited.Count - 1;
        Raise();
    }

    /// <summary>
    /// The page to request for going back, if there is one. The list moves
    /// when that page is visited.
    /// </summary>
    public bool TryGoBack(out SettingsPage page) => TryMove(_current - 1, out page);

    /// <summary>
    /// The page to request for going forward, if there is one. The list moves
    /// when that page is visited.
    /// </summary>
    public bool TryGoForward(out SettingsPage page) => TryMove(_current + 1, out page);

    private bool TryMove(int target, out SettingsPage page)
    {
        if (_current < 0 || target < 0 || target >= _visited.Count)
        {
            page = default;
            return false;
        }

        _pending = target;
        page = _visited[target];
        return true;
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(BackTarget));
        OnPropertyChanged(nameof(ForwardTarget));
    }
}
