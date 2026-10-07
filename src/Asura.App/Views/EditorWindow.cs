using Avalonia.Controls;

namespace Asura.App.Views;

/// <summary>
/// A main window's editor in a window of its own: the workspace editor and
/// the Layout Designer. They used to be overlays that blacked out the window
/// and took the whole shell with them. A window leaves the workspace live
/// beside it, moves out of the way, and closes like any other.
///
/// One per main window and kind, owned by that window so it stays in front
/// of it and closes with it. Closing it ends the edit; with unsaved edits it
/// asks to save first, and the main window gets focus back. When the main
/// window or the app closes, that close asks about the edits instead, once.
/// </summary>
public abstract class EditorWindow : Window
{
    private Func<Task<bool>>? _confirmClose;
    private bool _closeApproved;
    private bool _closeInProgress;
    private bool _isClosed;

    protected EditorWindow()
    {
        // Small enough for half a 13-inch screen; the content scrolls and the
        // buttons stay pinned below it.
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    /// <summary>
    /// Shows the editor, owned by <paramref name="owner"/>.
    /// <paramref name="confirmClose"/> is asked before a close by the user
    /// and says whether the window may go: true once saved or discarded.
    /// </summary>
    internal void Present(Window owner, Control editor, Func<Task<bool>> confirmClose)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(confirmClose);
        _confirmClose = confirmClose;
        // The editor binds to the main window's view model, as it did when
        // it was drawn inside that window.
        DataContext = owner.DataContext;
        Content = editor;
        FitToOwner(owner);
        Show(owner);
        Activate();
    }

    /// <summary>A second request for the same edit brings this one forward.</summary>
    internal void BringForward()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Closes without asking: the edit was saved or cancelled inside the
    /// window, or the main window is closing and has asked already.
    /// </summary>
    internal void CloseApproved()
    {
        _closeApproved = true;
        if (!_isClosed)
        {
            Close();
        }
    }

    /// <summary>
    /// The main window is closing with this one and has asked about unsaved
    /// edits already, so this one goes without asking again.
    /// </summary>
    internal void ApproveClose() => _closeApproved = true;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // Avalonia asks owned windows before their owner. When the main
        // window or the application is closing, its own close flow asks about
        // these edits, and this window only closes if that flow lets the main
        // window go; holding it here would keep the main window open.
        if (e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown)
        {
            base.OnClosing(e);
            return;
        }

        if (!_closeApproved && _confirmClose is { } confirm)
        {
            e.Cancel = true;
            if (!_closeInProgress)
            {
                _ = ConfirmCloseAsync(confirm);
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // The editor view belongs to the main window, which keeps it for the
        // next edit; it has to leave this window's tree first.
        _isClosed = true;
        Content = null;
        base.OnClosed(e);
    }

    /// <summary>
    /// The first frame is the window's own size, but never more than the
    /// screen the owner is on can show.
    /// </summary>
    private void FitToOwner(Window owner)
    {
        var screen = owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var available = screen.WorkingArea.Size.ToSize(screen.Scaling);
        Width = Math.Max(MinWidth, Math.Min(Width, available.Width * 0.9));
        Height = Math.Max(MinHeight, Math.Min(Height, available.Height * 0.9));
    }

    private async Task ConfirmCloseAsync(Func<Task<bool>> confirm)
    {
        _closeInProgress = true;
        try
        {
            if (await confirm())
            {
                CloseApproved();
            }
        }
        finally
        {
            _closeInProgress = false;
        }
    }
}
