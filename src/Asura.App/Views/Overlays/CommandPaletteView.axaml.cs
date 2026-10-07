using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Overlays;

public sealed partial class CommandPaletteView : UserControl
{
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
