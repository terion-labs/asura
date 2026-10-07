using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Overlays;

public sealed partial class CommandPaletteView : UserControl
{
    private const double PreferredWidth = 680;
    private const double PreferredMaxHeight = 700;

    private TopLevel? _topLevel;

    public CommandPaletteView()
    {
        InitializeComponent();
    }

    public event EventHandler<RoutedEventArgs>? ActivateSearchResultRequested;

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<KeyEventArgs>? SearchKeyDownRequested;

    internal void FocusSearch() =>
        CommandSearchBox.Focus();

    internal void ScrollSelectedResultIntoView()
    {
        if (LauncherSearchResultList.SelectedItem is { } selected)
        {
            LauncherSearchResultList.ScrollIntoView(selected);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.PropertyChanged += OnTopLevelPropertyChanged;
        FitToWindow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.PropertyChanged -= OnTopLevelPropertyChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.Property == BoundsProperty)
        {
            FitToWindow();
        }
    }

    /// <summary>
    /// The palette's preferred size, or the window's less a margin on each
    /// side when the window is smaller.
    /// </summary>
    private void FitToWindow()
    {
        if (_topLevel is not { Bounds.Size: var window })
        {
            return;
        }

        var margin = this.TryFindResource("ShellSpaceXl", out var value) && value is double spacing
            ? spacing
            : 24;
        PaletteCard.Width = Math.Max(0, Math.Min(PreferredWidth, window.Width - (2 * margin)));
        PaletteCard.MaxWidth = PaletteCard.Width;
        PaletteCard.MaxHeight = Math.Max(0, Math.Min(PreferredMaxHeight, window.Height - (2 * margin)));
    }

    /// <summary>
    /// A click on a row opens it, as Return does. The shell reads the row's
    /// result from the sender and ignores one that cannot be used.
    /// </summary>
    private void OnActivateSearchResultTapped(object? sender, TappedEventArgs e) =>
        ActivateSearchResultRequested?.Invoke(sender, e);

    private void OnCloseClick(object? sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(sender, e);

    private void OnSearchKeyDown(object? sender, KeyEventArgs e) =>
        SearchKeyDownRequested?.Invoke(sender, e);
}
