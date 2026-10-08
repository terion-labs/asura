using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KeyModifiers = Asura.Core.KeyModifiers;

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

    // Find Next is Command-G on macOS and F3 elsewhere; Return also moves on
    // from the field. Every key is written by the one shortcut formatter.
    private static readonly KeyStroke FindNextStroke = OperatingSystem.IsMacOS()
        ? new("G", KeyModifiers.Meta)
        : new("F3", KeyModifiers.None);

    public static string FindNextTip { get; } =
        $"Next match ({Keys(FindNextStroke)} or {Keys(new("ENTER", KeyModifiers.None))})";

    public static string FindPreviousTip { get; } =
        $"Previous match ({Keys(new(FindNextStroke.Key, FindNextStroke.Modifiers | KeyModifiers.Shift))} or {Keys(new("ENTER", KeyModifiers.Shift))})";

    public static string CloseFindTip { get; } =
        $"Close the find bar ({Keys(new("ESCAPE", KeyModifiers.None))})";

    public static string CopyModeHintText { get; } = string.Join(
        "  ·  ",
        $"Copy mode: {Keys(new("ARROWUP", KeyModifiers.None))} {Keys(new("ARROWDOWN", KeyModifiers.None))} move",
        $"{Keys(new("ARROWUP", KeyModifiers.Shift))} {Keys(new("ARROWDOWN", KeyModifiers.Shift))} select lines",
        $"{Keys(new("C", OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))} copies",
        $"{Keys(new("ESCAPE", KeyModifiers.None))} exits");

    public static string EscapeKeyText { get; } = Keys(new("ESCAPE", KeyModifiers.None));

    public static string ReturnKeyText { get; } = Keys(new("ENTER", KeyModifiers.None));

    private static string Keys(KeyStroke stroke) => KeySequenceDisplay.Format(stroke);

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
        FindBar.SizeChanged += (_, _) => ReserveFindBand();
    }

    /// <summary>
    /// The height kept clear above the terminal's rows while the find bar is
    /// open, so the bar sits in its own band instead of over the first line
    /// of output, as Terminal's find bar does.
    /// </summary>
    internal double ReservedFindBandHeight => _surface?.Margin.Top ?? 0;

    private void ReserveFindBand()
    {
        if (_surface is not { } surface)
        {
            return;
        }

        var band = surface.IsFindVisible && FindBar.Bounds.Height > 0
            ? FindBar.Bounds.Height + FindBar.Margin.Top + FindBar.Margin.Bottom
            : 0;
        if (!surface.Margin.Top.Equals(band))
        {
            surface.Margin = new Thickness(0, band, 0, 0);
        }
    }

    private void OnSurfacePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == ManagedTerminalSurface.FindQueryProperty
            && e.GetNewValue<string>() is { } query
            && !string.Equals(FindBox.Text ?? string.Empty, query, StringComparison.Ordinal))
        {
            // A query set by the surface itself, such as Use Selection for
            // Find, shows in the field without searching a second time.
            _isSynchronizingFindText = true;
            FindBox.Text = query;
            _isSynchronizingFindText = false;
            return;
        }

        if (e.Property == ManagedTerminalSurface.IsFindVisibleProperty)
        {
            ReserveFindBand();
        }

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

        var findModifier = OperatingSystem.IsMacOS()
            ? Avalonia.Input.KeyModifiers.Meta
            : Avalonia.Input.KeyModifiers.Control;
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
