using Asura.Application;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Asura.App.Controls;

/// <summary>
/// The terminal's find bar, link confirmation, command status, copy-mode hint,
/// and screen-reader announcer. The surface owns the state; this view only
/// presents it and forwards what people do.
/// </summary>
public sealed partial class ManagedTerminalOverlay : UserControl
{
    private ManagedTerminalSurface? _surface;
    private bool _isSynchronizingFindText;

    public ManagedTerminalOverlay()
    {
        InitializeComponent();
        FindBox.MaxLength = TerminalFindInput.MaximumQueryLength;
    }

    public static string FindNextTip { get; } = OperatingSystem.IsMacOS()
        ? "Next match (⌘G or Return)"
        : "Next match (F3 or Enter)";

    public static string FindPreviousTip { get; } = OperatingSystem.IsMacOS()
        ? "Previous match (⇧⌘G or ⇧Return)"
        : "Previous match (Shift+F3 or Shift+Enter)";

    public static string CopyModeHintText { get; } = OperatingSystem.IsMacOS()
        ? "Copy Mode: ↑ ↓ move  ·  ⇧ selects lines  ·  ⌘C copies  ·  Esc exits"
        : "Copy Mode: ↑ ↓ move  ·  Shift selects lines  ·  Ctrl+C copies  ·  Esc exits";

    /// <summary>Raised when the surface should take keyboard focus back.</summary>
    internal event EventHandler? SurfaceFocusRequested;

    internal TextBox FindField => FindBox;

    internal string AnnouncementText => Announcer.Text ?? string.Empty;

    internal void Attach(ManagedTerminalSurface surface)
    {
        if (_surface is not null)
        {
            throw new InvalidOperationException("The terminal overlay is already attached.");
        }

        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        DataContext = surface;
        surface.FindFocusRequested += OnFindFocusRequested;
        surface.AnnouncementRequested += OnAnnouncementRequested;
        surface.PropertyChanged += OnSurfacePropertyChanged;
    }

    private void OnSurfacePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == ManagedTerminalSurface.IsFindVisibleProperty
            && !e.GetNewValue<bool>())
        {
            var hadFocus = FindBox.IsKeyboardFocusWithin;
            _isSynchronizingFindText = true;
            FindBox.Text = string.Empty;
            _isSynchronizingFindText = false;
            if (hadFocus)
            {
                SurfaceFocusRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void OnFindFocusRequested(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        // A context-menu choice returns focus to the surface as the menu closes,
        // so the field takes focus once that has happened.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_surface?.IsFindVisible == true)
                {
                    FindBox.Focus(NavigationMethod.Tab);
                    FindBox.SelectAll();
                }
            },
            DispatcherPriority.Input);
    }

    /// <summary>
    /// Clears and then sets the text so a repeated message is announced again;
    /// the empty state is never announced.
    /// </summary>
    private void OnAnnouncementRequested(object? sender, string text)
    {
        _ = sender;
        Announcer.Text = string.Empty;
        Announcer.Text = text;
    }

    private void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_isSynchronizingFindText || _surface is not { IsFindVisible: true } surface)
        {
            return;
        }

        Observe(surface.UpdateFindQueryAsync(FindBox.Text ?? string.Empty).AsValueTask());
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (_surface is not { } surface)
        {
            return;
        }

        var findModifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.F && e.KeyModifiers == findModifier)
        {
            // Find again from the field selects its text, ready for a new query.
            FindBox.SelectAll();
            e.Handled = true;
            return;
        }

        var handling = surface.HandleFindKeyAsync(e.Key, e.KeyModifiers);
        if (handling.IsCompletedSuccessfully)
        {
            e.Handled = handling.Result;
            return;
        }

        e.Handled = true;
        Observe(handling.AsValueTask());
    }

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_surface is { } surface)
        {
            Observe(surface.FindNextAsync(-1).AsValueTask());
        }
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_surface is { } surface)
        {
            Observe(surface.FindNextAsync(1).AsValueTask());
        }
    }

    private void OnCloseFindClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_surface is { } surface)
        {
            Observe(surface.CloseFindAsync());
            SurfaceFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCancelLinkClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _surface?.CancelPendingLink();
        SurfaceFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_surface is { } surface)
        {
            Observe(surface.ConfirmPendingLinkAsync().AsValueTask());
            SurfaceFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void Observe(ValueTask task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            _ = ObserveCoreAsync(task);
        }
    }

    private static async Task ObserveCoreAsync(ValueTask task)
    {
        try
        {
            await task;
        }
        catch (Exception exception)
        {
            SecretSafeDiagnostics.WriteTrace("terminal.overlay.failed", exception);
        }
    }
}
