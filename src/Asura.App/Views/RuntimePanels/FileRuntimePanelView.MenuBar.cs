using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// The menu bar's Files items. Each runs the same file action as the
/// toolbar, the overflow menu and the right-click menu, so all four dim
/// together and carry the same title.
/// </summary>
public sealed partial class FileRuntimePanelView : IPanelCommandTarget
{
    /// <summary>
    /// The menu-bar command for each file action. Copy, Cut and Paste are the
    /// Edit menu's own commands, which the listing answers as an
    /// <see cref="IEditTarget"/>.
    /// </summary>
    internal static CommandId MenuCommand(FilePanelAction action) => action switch
    {
        FilePanelAction.Open => BuiltInCommands.FilesOpen,
        FilePanelAction.OpenExternally => BuiltInCommands.FilesOpenExternally,
        FilePanelAction.Download => BuiltInCommands.FilesDownload,
        FilePanelAction.Upload => BuiltInCommands.FilesUpload,
        FilePanelAction.Transfer => BuiltInCommands.FilesTransfer,
        FilePanelAction.Copy => BuiltInCommands.EditCopy,
        FilePanelAction.Cut => BuiltInCommands.EditCut,
        FilePanelAction.Paste => BuiltInCommands.EditPaste,
        FilePanelAction.NewFolder => BuiltInCommands.FilesNewFolder,
        FilePanelAction.Rename => BuiltInCommands.FilesRename,
        FilePanelAction.Delete => BuiltInCommands.FilesDelete,
        FilePanelAction.CopyName => BuiltInCommands.FilesCopyName,
        FilePanelAction.CopyPath => BuiltInCommands.FilesCopyPath,
        FilePanelAction.AccessControl => BuiltInCommands.FilesPermissions,
        FilePanelAction.Refresh => BuiltInCommands.FilesRefresh,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>The actions the Files menu runs, by their menu-bar command.</summary>
    private static readonly Dictionary<CommandId, FilePanelAction> PanelMenuActions =
        Enum.GetValues<FilePanelAction>()
            .Where(action => BuiltInCommands.PanelContextCommands.Contains(MenuCommand(action)))
            .ToDictionary(MenuCommand);

    bool IPanelCommandTarget.CanExecute(CommandId command) =>
        DataContext is FileRuntimePanelViewModel panel
        && PanelMenuActions.TryGetValue(command, out var action)
        && panel.IsActionEnabled(action);

    Task IPanelCommandTarget.ExecuteAsync(CommandId command)
    {
        if (DataContext is FileRuntimePanelViewModel panel
            && PanelMenuActions.TryGetValue(command, out var action))
        {
            _ = panel.RequestActionIfEnabled(action);
        }

        return Task.CompletedTask;
    }

    IEnumerable<KeyValuePair<string, bool>> IPanelCommandTarget.States =>
        DataContext is FileRuntimePanelViewModel panel
            ? [new(CommandStates.MovesToTrash, panel.CanMoveToTrash)]
            : [];
}
