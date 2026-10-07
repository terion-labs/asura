using Asura.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// The Undo notice a deleted connection or workspace leaves, wherever the
/// delete was made. Undo and Dismiss act on the window's view model; the
/// window that hosts the notice decides where focus goes afterwards.
/// </summary>
public sealed partial class DefinitionDeleteUndoNoticeView : UserControl
{
    public DefinitionDeleteUndoNoticeView() => InitializeComponent();

    /// <summary>Undo or Dismiss finished; the notice is gone.</summary>
    public event EventHandler<RoutedEventArgs>? Finished;

    /// <summary>The delete was made in this notice's window: Undo takes focus.</summary>
    internal bool FocusUndo() => UndoDeletedDefinitionButton.Focus(NavigationMethod.Tab);

    private async void OnUndoClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && (await viewModel.UndoDefinitionDeleteAsync(CancellationToken.None)).IsSuccess)
        {
            Finished?.Invoke(sender, e);
        }
    }

    private async void OnDismissClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.DismissDefinitionDeleteUndoAsync();
            Finished?.Invoke(sender, e);
        }
    }
}
