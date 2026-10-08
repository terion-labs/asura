using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class ShortcutRecorderDialog : DialogWindow
{
    public const int DefaultMaximumStrokes = 8;
    private readonly List<KeyStroke> _strokes = [];
    private readonly int _maximumStrokes;

    public ShortcutRecorderDialog()
        : this(initial: null, DefaultMaximumStrokes)
    {
    }

    public ShortcutRecorderDialog(KeySequence? initial)
        : this(initial, DefaultMaximumStrokes)
    {
    }

    public ShortcutRecorderDialog(KeySequence? initial, int maximumStrokes)
    {
        if (maximumStrokes is < 1 or > DefaultMaximumStrokes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumStrokes),
                $"Shortcut capture supports between 1 and {DefaultMaximumStrokes} strokes.");
        }

        _maximumStrokes = maximumStrokes;
        InitializeComponent();
        Opened += (_, _) => this.FindControl<SurfaceCard>("CaptureArea")?.Focus();
        if (initial is not null)
        {
            _strokes.AddRange(initial.Strokes);
        }

        Refresh();
    }

    /// <summary>
    /// Escape cancels everywhere, as the status line says. ⌘. is a chord
    /// people bind, so while the capture area has focus it is recorded rather
    /// than taken as cancel.
    /// </summary>
    protected override bool LetsCancelGestureThrough(KeyEventArgs e) =>
        e.KeyModifiers == Avalonia.Input.KeyModifiers.Meta
        && this.FindControl<SurfaceCard>("CaptureArea") is { IsKeyboardFocusWithin: true };

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (IsModifierKey(e.Key))
        {
            this.FindControl<TextBlock>("RecorderStatusText")!.Text =
                "Add a non-modifier key to complete the stroke.";
            e.Handled = true;
            return;
        }

        if (_strokes.Count >= _maximumStrokes)
        {
            this.FindControl<TextBlock>("RecorderStatusText")!.Text =
                _maximumStrokes == 1
                    ? "A terminal shortcut has exactly one stroke."
                    : $"A shortcut can’t have more than {_maximumStrokes} strokes.";
            e.Handled = true;
            return;
        }

        _strokes.Add(ApplicationKeyStrokeMapper.Map(e.Key, e.KeyModifiers, e.KeySymbol));
        Refresh();
        e.Handled = true;
    }

    private void OnRemoveLastClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_strokes.Count > 0)
        {
            _strokes.RemoveAt(_strokes.Count - 1);
            Refresh();
        }
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _strokes.Clear();
        Refresh();
        this.FindControl<SurfaceCard>("CaptureArea")?.Focus();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnUseShortcutClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(_strokes.Count is 0 || _strokes.Count > _maximumStrokes
            ? null
            : new KeySequence(_strokes));
    }

    private void Refresh()
    {
        var hasStrokes = _strokes.Count > 0;
        this.FindControl<TextBlock>("RecordedShortcutText")!.Text = hasStrokes
            ? KeySequenceDisplay.Format(_strokes)
            : "No keys recorded";
        var warning = hasStrokes ? ReservedWarning(_strokes[0]) : null;
        var warningText = this.FindControl<TextBlock>("ReservedShortcutText")!;
        warningText.Text = warning;
        warningText.IsVisible = warning is not null;
        this.FindControl<TextBlock>("StrokeCountText")!.Text =
            $"{_strokes.Count} of {_maximumStrokes} {(_maximumStrokes == 1 ? "stroke" : "strokes")}";
        this.FindControl<DialogShell>("Shell")!.Subtitle = _maximumStrokes == 1
            ? "Record one terminal stroke. Conflicts are checked before the profile can be saved."
            : $"Record up to {_maximumStrokes} strokes. Conflicts are checked before the profile can be saved.";
        this.FindControl<TextBlock>("RecorderStatusText")!.Text =
            _strokes.Count > _maximumStrokes
                ? $"This imported shortcut has too many strokes. Remove {_strokes.Count - _maximumStrokes} to repair it."
                : _strokes.Count == _maximumStrokes
                    ? "The shortcut is ready to use."
                    : "Focus the capture area to add another stroke.";
        this.FindControl<Button>("RemoveLastButton")!.IsEnabled = hasStrokes;
        this.FindControl<Button>("ClearButton")!.IsEnabled = hasStrokes;
        this.FindControl<Button>("UseShortcutButton")!.IsEnabled =
            hasStrokes && _strokes.Count <= _maximumStrokes;
    }

    /// <summary>
    /// A shortcut macOS or Asura's menu bar answers first never reaches the
    /// keymap. Say so while recording, before the profile's validator does.
    /// </summary>
    private static string? ReservedWarning(KeyStroke first)
    {
        if (HostPlatform.Current != HostOperatingSystem.MacOS)
        {
            return null;
        }

        if (MacOsStandardShortcuts.TryFindSystem(first, out var system))
        {
            return $"{KeySequenceDisplay.Format(first)} is reserved by macOS for {system.Owner}. Asura never receives it.";
        }

        return MacOsStandardShortcuts.TryFindMenu(first, out var menu)
            ? $"{KeySequenceDisplay.Format(first)} is the menu shortcut for {menu.Owner}."
            : null;
    }

    private static bool IsModifierKey(Key key) => key.ToString() is
        "LeftCtrl" or "RightCtrl"
        or "LeftAlt" or "RightAlt"
        or "LeftShift" or "RightShift"
        or "LWin" or "RWin";
}
