using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.Components;

/// <summary>
/// The menu bar's Database items: the result grid's context menu, acting on
/// the grid's current cell and row as the context menu's keyboard gesture
/// does. Each item runs the context menu's own handler.
/// </summary>
public sealed partial class DatabaseWorkspaceView : IPanelCommandTarget
{
    /// <summary>
    /// Names each context-menu item by its menu-bar command. The Copy Row As
    /// and Set Value submenus keep their short titles; the menu bar, which
    /// keeps to one submenu level, spells them out ("Copy Row as JSON").
    /// </summary>
    private void BindContextMenuCommands()
    {
        PanelCommandTargets.Bind(CellEditorMenuItem, BuiltInCommands.DatabaseOpenCellEditor);
        PanelCommandTargets.Bind(CopyRowMenuItem, BuiltInCommands.DatabaseCopyRow);
        PanelCommandTargets.Bind(CopyCellMenuItem, BuiltInCommands.DatabaseCopyCell);
        PanelCommandTargets.Bind(CopyColumnMenuItem, BuiltInCommands.DatabaseCopyColumn);
        PanelCommandTargets.Bind(ExportPageMenuItem, BuiltInCommands.DatabaseExportPage);
        PanelCommandTargets.Bind(RefreshTableMenuItem, BuiltInCommands.DatabaseRefresh);
        PanelCommandTargets.Bind(PasteCellMenuItem, BuiltInCommands.DatabasePaste);
        PanelCommandTargets.Bind(AddRowMenuItem, BuiltInCommands.DatabaseAddRow);
        PanelCommandTargets.Bind(DuplicateRowMenuItem, BuiltInCommands.DatabaseDuplicateRow);
        PanelCommandTargets.Bind(ImportMenuItem, BuiltInCommands.DatabaseImportCsv);
        PanelCommandTargets.Bind(DeleteRowMenuItem, BuiltInCommands.DatabaseDeleteRow);
        CopyRowJsonMenuItem.Tag = BuiltInCommands.DatabaseCopyRowAsJson;
        CopyRowCsvMenuItem.Tag = BuiltInCommands.DatabaseCopyRowAsCsv;
        CopyRowInsertMenuItem.Tag = BuiltInCommands.DatabaseCopyRowAsInsert;
        SetEmptyMenuItem.Tag = BuiltInCommands.DatabaseSetEmpty;
        SetNullContextMenuItem.Tag = BuiltInCommands.DatabaseSetNull;
        SetDefaultContextMenuItem.Tag = BuiltInCommands.DatabaseSetDefault;
        SetFileMenuItem.Tag = BuiltInCommands.DatabaseSetFromFile;
    }

    /// <summary>
    /// Whether a grid command can run now: what the context menu would show
    /// and enable for the current cell. Commands on the page need no cell;
    /// the rest need a current one.
    /// </summary>
    private bool CanRun(CommandId command)
    {
        if (Panel is not { } panel)
        {
            return false;
        }

        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (command == BuiltInCommands.DatabaseRefresh)
        {
            return panel.CanRefreshTable;
        }

        if (command == BuiltInCommands.DatabaseExportPage)
        {
            return panel.HasResults && storage?.CanSave == true;
        }

        if (command == BuiltInCommands.DatabaseAddRow)
        {
            return panel.HasResults && panel.CanMutateRows;
        }

        if (command == BuiltInCommands.DatabaseImportCsv)
        {
            return panel.HasResults && panel.CanMutateRows && storage?.CanOpen == true;
        }

        if (ResolveCurrentCellTarget() is not { } target || !IsTargetCurrent(panel, target))
        {
            return false;
        }

        var cell = target.Cell;
        var mutable = panel.HasResults && panel.CanMutateRows;
        return command switch
        {
            _ when command == BuiltInCommands.DatabaseOpenCellEditor
                || command == BuiltInCommands.DatabaseCopyRow
                || command == BuiltInCommands.DatabaseCopyCell => true,
            _ when command == BuiltInCommands.DatabaseCopyColumn => panel.ResultRows.Count > 0,
            _ when command == BuiltInCommands.DatabaseCopyRowAsJson
                || command == BuiltInCommands.DatabaseCopyRowAsCsv => !panel.HasMultipleSelectedRows,
            _ when command == BuiltInCommands.DatabaseCopyRowAsInsert =>
                !panel.HasMultipleSelectedRows && panel.CanCopySelectedRowAsInsert,
            _ when command == BuiltInCommands.DatabasePaste => mutable && cell.CanSetText,
            _ when command == BuiltInCommands.DatabaseDuplicateRow =>
                panel.HasResults && panel.CanDuplicateSelectedRow,
            _ when command == BuiltInCommands.DatabaseSetEmpty =>
                panel.HasResults
                && cell.CanSetEmpty
                && !string.Equals(panel.SelectedDriver.Id, "oracle", StringComparison.Ordinal)
                && panel.CanSetSelectedCellEmpty(target.Ordinal),
            _ when command == BuiltInCommands.DatabaseSetNull => mutable && cell.CanSetNull,
            _ when command == BuiltInCommands.DatabaseSetDefault =>
                mutable && target.Row.IsNew && cell.CanSetDefault,
            _ when command == BuiltInCommands.DatabaseSetFromFile =>
                mutable && cell.CanSetBinary && storage?.CanOpen == true,
            _ when command == BuiltInCommands.DatabaseDeleteRow =>
                panel.HasResults && panel.CanDeleteSelectedRow,
            _ => false,
        };
    }

    bool IPanelCommandTarget.CanExecute(CommandId command) => CanRun(command);

    Task IPanelCommandTarget.ExecuteAsync(CommandId command)
    {
        if (!CanRun(command))
        {
            return Task.CompletedTask;
        }

        var click = new RoutedEventArgs(MenuItem.ClickEvent);
        Action<object?, RoutedEventArgs>? handler =
            command == BuiltInCommands.DatabaseOpenCellEditor ? OnCellEditorClick
            : command == BuiltInCommands.DatabaseCopyRow ? OnCopyRowClick
            : command == BuiltInCommands.DatabaseCopyCell ? OnCopyCellClick
            : command == BuiltInCommands.DatabaseCopyColumn ? OnCopyColumnClick
            : command == BuiltInCommands.DatabaseCopyRowAsJson ? OnCopyRowJsonClick
            : command == BuiltInCommands.DatabaseCopyRowAsCsv ? OnCopyRowCsvClick
            : command == BuiltInCommands.DatabaseCopyRowAsInsert ? OnCopyRowInsertClick
            : command == BuiltInCommands.DatabaseExportPage ? OnExportPageClick
            : command == BuiltInCommands.DatabaseRefresh ? OnRefreshTableClick
            : command == BuiltInCommands.DatabasePaste ? OnPasteCellClick
            : command == BuiltInCommands.DatabaseSetEmpty ? OnSetEmptyClick
            : command == BuiltInCommands.DatabaseSetNull ? OnSetNullContextClick
            : command == BuiltInCommands.DatabaseSetDefault ? OnSetDefaultContextClick
            : command == BuiltInCommands.DatabaseSetFromFile ? OnSetFileClick
            : command == BuiltInCommands.DatabaseAddRow ? OnContextAddRowClick
            : command == BuiltInCommands.DatabaseDuplicateRow ? OnDuplicateRowClick
            : command == BuiltInCommands.DatabaseImportCsv ? OnImportCsvClick
            : command == BuiltInCommands.DatabaseDeleteRow ? OnContextDeleteRowClick
            : null;
        handler?.Invoke(null, click);
        return Task.CompletedTask;
    }

    IEnumerable<KeyValuePair<string, bool>> IPanelCommandTarget.States =>
        [new(CommandStates.HasMultipleSelection, Panel?.SelectedRows.Count > 1)];
}
