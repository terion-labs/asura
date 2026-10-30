using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// The menu bar's Browser items. Reload Page runs the header's Reload, so
/// Command-R and the button do one thing: reload the page, or try again
/// when the browser could not start.
/// </summary>
public sealed partial class BrowserRuntimePanelView : IPanelCommandTarget
{
    bool IPanelCommandTarget.CanExecute(CommandId command) =>
        command == BuiltInCommands.BrowserReload
        && DataContext is BrowserRuntimePanelViewModel;

    Task IPanelCommandTarget.ExecuteAsync(CommandId command)
    {
        if (command == BuiltInCommands.BrowserReload)
        {
            OnReloadClick(this, new RoutedEventArgs());
        }

        return Task.CompletedTask;
    }
}
