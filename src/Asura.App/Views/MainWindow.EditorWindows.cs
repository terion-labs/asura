using System.ComponentModel;
using Asura.App.ViewModels;
using Asura.App.Views.Overlays;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace Asura.App.Views;

/// <summary>
/// The workspace editor and the Layout Designer, each shown in Settings in
/// place of the Workspaces page they belong to, as System Settings opens an
/// item: the title band names it, and Back returns to the page. They were
/// windows of their own, which fell behind the main window and took Settings
/// with them. An edit session begins in the view model; this part shows it,
/// takes it down when the edit ends inside it, and ends the edit when Back is
/// chosen — asking to save first when there is something to save.
/// </summary>
public sealed partial class MainWindow
{
    private WorkspaceEditorViewModel? _titledWorkspaceEditor;
    private LayoutDesignerViewModel? _titledLayoutDesigner;

    /// <summary>Whether Settings shows this window's workspace editor.</summary>
    internal bool IsWorkspaceEditorShown =>
        _workspaceDefinitionEditor is { } editor && ReferenceEquals(_settingsRoute?.DetailContent, editor);

    /// <summary>Whether Settings shows this window's Layout Designer.</summary>
    internal bool IsLayoutDesignerShown =>
        _layoutDesignerOverlay is { } designer && ReferenceEquals(_settingsRoute?.DetailContent, designer);

    /// <summary>Where an editor's questions go: over Settings, where it is shown.</summary>
    private Window WorkspaceEditorDialogOwner => SettingsDialogOwner;

    private Window LayoutDesignerDialogOwner => SettingsDialogOwner;

    /// <summary>
    /// Edits <paramref name="id"/>. The same workspace asked for again shows
    /// its edits as they are; another workspace takes the editor over once
    /// the edits in it are saved.
    /// </summary>
    internal async Task EditWorkspaceAsync(WorkspaceId id)
    {
        if (!await LeaveOtherSettingsItemAsync(_workspaceDefinitionEditor))
        {
            return;
        }

        if (IsWorkspaceEditorShown && ViewModel.WorkspaceEditor is { } editing)
        {
            if (editing.Id == id && !editing.IsNew)
            {
                await PresentWorkspaceEditorAsync();
                return;
            }

            if (!await CommitWorkspaceEditsBeforeSwitchAsync())
            {
                return;
            }
        }

        ViewModel.BeginEditWorkspace(id);
        await PresentWorkspaceEditorAsync();
    }

    /// <summary>Opens the editor over a new, unsaved workspace.</summary>
    internal async Task CreateWorkspaceAsync()
    {
        if (!await LeaveOtherSettingsItemAsync(_workspaceDefinitionEditor))
        {
            return;
        }

        if (IsWorkspaceEditorShown && !await CommitWorkspaceEditsBeforeSwitchAsync())
        {
            return;
        }

        ViewModel.BeginCreateWorkspace();
        await PresentWorkspaceEditorAsync();
    }

    /// <summary>
    /// Shows the edit session that has just begun in Settings. Nothing opens
    /// when the session could not begin.
    /// </summary>
    private async Task PresentWorkspaceEditorAsync()
    {
        if (!ViewModel.IsDefinitionEditorVisible)
        {
            return;
        }

        var editor = EnsureWorkspaceEditorView();
        await ShowSettingsItemAsync(SettingsPage.Workspaces, editor, WorkspaceEditorTitle(), LeaveWorkspaceEditorAsync);
        RefreshWorkspaceEditorTitle();
        FocusNavigator.FocusDefinitionEditor();
    }

    /// <summary>Shows the layout design that has just begun in Settings.</summary>
    private async Task PresentLayoutDesignerAsync()
    {
        if (!ViewModel.IsLayoutDesignerVisible)
        {
            return;
        }

        var designer = EnsureLayoutDesignerView();
        var wasShown = IsLayoutDesignerShown;
        await ShowSettingsItemAsync(SettingsPage.Workspaces, designer, LayoutDesignerTitle(), LeaveLayoutDesignerAsync);
        RefreshLayoutDesignerTitle();
        if (!wasShown && IsLayoutDesignerShown && SettingsWindow.Current is { } settings)
        {
            TrackDockPresses(settings, track: true);
            TrackDockPresses(this, track: true);
        }

        FocusNavigator.FocusLayoutDesignerName();
    }

    /// <summary>
    /// Opens Settings on <paramref name="page"/> with <paramref name="item"/>
    /// in the page's place, bringing the Settings window forward.
    /// </summary>
    private async Task ShowSettingsItemAsync(SettingsPage page, Control item, string title, Func<Task> back)
    {
        if (SettingsWindow.Current is { Host: { } owner } open && !ReferenceEquals(owner, this))
        {
            // The one Settings window shows another window's settings; it
            // takes this window's over once that window's page lets it go.
            if (!await owner.ReleaseSettingsWindowAsync())
            {
                return;
            }

            open.Retarget(this, ViewModel, SettingsRoute);
        }

        var view = SettingsRoute;
        if (!ViewModel.IsSettingsVisible || SettingsPanes.SidebarPage(ViewModel.SettingsPage) != page)
        {
            if (ViewModel.IsSettingsVisible && !await ConfirmLeavingSettingsPageAsync(SettingsDialogOwner))
            {
                return;
            }

            ViewModel.ShowSettings(page);
        }

        PresentSettingsWindow();
        SettingsWindow.Current?.BringForward();
        if (ReferenceEquals(view.DetailContent, item))
        {
            view.RetitleDetail(item, title);
            return;
        }

        view.ShowDetail(item, title, back);
    }

    /// <summary>
    /// Another item is in Settings — a connection being edited, the Layout
    /// Designer — so it is left first, the way Back leaves it.
    /// </summary>
    private async Task<bool> LeaveOtherSettingsItemAsync(Control? item)
    {
        if (_settingsRoute is not { HasDetail: true } view
            || (item is not null && ReferenceEquals(view.DetailContent, item)))
        {
            return true;
        }

        await view.LeaveDetailAsync();
        return !view.HasDetail;
    }

    /// <summary>
    /// Lets the Settings window go to another main window: its item and its
    /// page's draft are asked about first.
    /// </summary>
    internal async Task<bool> ReleaseSettingsWindowAsync()
    {
        if (!await LeaveOtherSettingsItemAsync(null))
        {
            return false;
        }

        return await ConfirmLeavingSettingsPageAsync(SettingsDialogOwner);
    }

    /// <summary>Back from the workspace editor: asks about unsaved edits, then ends the edit.</summary>
    private async Task LeaveWorkspaceEditorAsync()
    {
        if (await ConfirmCloseWorkspaceEditorAsync()
            && ViewModel.IsDefinitionEditorVisible)
        {
            ViewModel.DismissWorkspaceEditor();
        }
    }

    /// <summary>Back from the Layout Designer: asks about unsaved edits, then ends the design.</summary>
    private async Task LeaveLayoutDesignerAsync()
    {
        if (await ConfirmCloseLayoutDesignerAsync()
            && ViewModel.IsLayoutDesignerVisible)
        {
            ViewModel.DismissLayoutDesigner();
        }
    }

    private WorkspaceEditorView EnsureWorkspaceEditorView() =>
        _workspaceDefinitionEditor ??= BuildEditorView<WorkspaceEditorView>("WorkspaceEditorTemplate");

    private LayoutDesignerView EnsureLayoutDesignerView() =>
        _layoutDesignerOverlay ??= BuildEditorView<LayoutDesignerView>("LayoutDesignerTemplate");

    /// <summary>
    /// Builds an editor's view from this window's template once, so its
    /// events reach this window's handlers; it is kept between openings.
    /// </summary>
    private T BuildEditorView<T>(string templateKey)
        where T : Control
    {
        if (!Resources.TryGetResource(templateKey, ActualThemeVariant, out var resource)
            || resource is not IDataTemplate template
            || template.Build(ViewModel) is not T view)
        {
            throw new InvalidOperationException(
                $"The editor template '{templateKey}' is unavailable.");
        }

        return view;
    }

    /// <summary>
    /// Leaving with unsaved edits: Save keeps them, Don't Save drops them,
    /// and Cancel goes back to editing. True lets the editor go.
    /// </summary>
    private async Task<bool> ConfirmCloseWorkspaceEditorAsync()
    {
        if (ViewModel.WorkspaceEditor is not { } editor
            || editor.RequestCancel() != WorkspaceEditorCancelDisposition.ConfirmDiscard)
        {
            return true;
        }

        var answer = await Confirmations.SaveChanges(
                editor.IsNew ? "the new workspace" : $"the workspace “{editor.Name}”",
                editor.IsValid
                    ? "Your changes will be lost if you don't save them."
                    : "These changes can’t be saved yet: " + editor.ValidationSummary)
            .AskAsync(WorkspaceEditorDialogOwner);
        switch (answer)
        {
            case ConfirmationAnswer.Alternate:
                return true;
            case ConfirmationAnswer.Confirm when editor.IsValid:
                try
                {
                    return (await SaveWorkspaceEditorFromWindowAsync(editor.CreateSaveRequest())).IsSuccess;
                }
                catch (InvalidOperationException exception)
                {
                    ViewModel.SetError(ShellFailureText.WorkspaceSave(exception));
                    return false;
                }

            default:
                return false;
        }
    }

    private async Task<bool> ConfirmCloseLayoutDesignerAsync()
    {
        if (ViewModel.LayoutDesignerEditor is not { } designer
            || designer.RequestCancel() != LayoutDesignerCancelDisposition.ConfirmDiscard)
        {
            return true;
        }

        var answer = await Confirmations.SaveChanges(
                designer.IsNew ? "the new layout" : $"the layout “{designer.Name}”",
                designer.CanSave
                    ? "Your changes will be lost if you don't save them."
                    : "These changes can’t be saved yet: " + designer.ValidationSummary)
            .AskAsync(LayoutDesignerDialogOwner);
        switch (answer)
        {
            case ConfirmationAnswer.Alternate:
                return true;
            case ConfirmationAnswer.Confirm when designer.CanSave:
                return (await ViewModel.SaveLayoutDesignerAsync(_lifetime.Token)).IsSuccess;
            default:
                return false;
        }
    }

    /// <summary>
    /// Cancel, or Esc, in the Layout Designer: unsaved edits are discarded
    /// only when the person says so, and the designer goes with the session.
    /// </summary>
    private async void OnLayoutDesignerCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel.LayoutDesignerEditor is { } designer
            && designer.RequestCancel() == LayoutDesignerCancelDisposition.ConfirmDiscard
            && !await Confirmations.DiscardChanges().ShowDialog<bool>(LayoutDesignerDialogOwner))
        {
            return;
        }

        ViewModel.DismissLayoutDesigner();
    }

    /// <summary>
    /// The edit ended or changed in the view model: Save or Cancel in the
    /// editor, a workspace recreated, another workspace chosen.
    /// </summary>
    private void OnEditorSessionPropertyChanged(PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.IsDefinitionEditorVisible)
                when !ViewModel.IsDefinitionEditorVisible && _workspaceDefinitionEditor is { } editor:
                _settingsRoute?.HideDetail(editor);
                break;
            case nameof(MainWindowViewModel.IsLayoutDesignerVisible)
                when !ViewModel.IsLayoutDesignerVisible && _layoutDesignerOverlay is { } designer:
                if (IsLayoutDesignerShown)
                {
                    StopTrackingDockPresses();
                }

                _settingsRoute?.HideDetail(designer);
                break;
            case nameof(MainWindowViewModel.WorkspaceEditor):
                RefreshWorkspaceEditorTitle();
                break;
            case nameof(MainWindowViewModel.LayoutDesignerEditor):
                RefreshLayoutDesignerTitle();
                break;
        }
    }

    /// <summary>The workspace being edited, by name, in the title band.</summary>
    private string WorkspaceEditorTitle() => ViewModel.WorkspaceEditor is { } editor
        ? editor.IsNew ? "New Workspace" : string.IsNullOrWhiteSpace(editor.Name) ? "Workspace" : editor.Name.Trim()
        : "Workspace";

    private string LayoutDesignerTitle() => ViewModel.LayoutDesignerEditor is { } designer
        ? designer.IsNew ? "New Layout" : string.IsNullOrWhiteSpace(designer.Name) ? "Layout" : designer.Name.Trim()
        : "Layout Designer";

    private void RefreshWorkspaceEditorTitle()
    {
        var editor = ViewModel.WorkspaceEditor;
        if (!ReferenceEquals(_titledWorkspaceEditor, editor))
        {
            _titledWorkspaceEditor?.PropertyChanged -= OnTitledWorkspaceEditorPropertyChanged;
            _titledWorkspaceEditor = editor;
            _titledWorkspaceEditor?.PropertyChanged += OnTitledWorkspaceEditorPropertyChanged;
        }

        if (_workspaceDefinitionEditor is { } view && editor is not null)
        {
            _settingsRoute?.RetitleDetail(view, WorkspaceEditorTitle());
        }
    }

    private void OnTitledWorkspaceEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is nameof(WorkspaceEditorViewModel.Name) or null)
        {
            RefreshWorkspaceEditorTitle();
        }
    }

    private void RefreshLayoutDesignerTitle()
    {
        var designer = ViewModel.LayoutDesignerEditor;
        if (!ReferenceEquals(_titledLayoutDesigner, designer))
        {
            _titledLayoutDesigner?.PropertyChanged -= OnTitledLayoutDesignerPropertyChanged;
            _titledLayoutDesigner = designer;
            _titledLayoutDesigner?.PropertyChanged += OnTitledLayoutDesignerPropertyChanged;
        }

        if (_layoutDesignerOverlay is { } view && designer is not null)
        {
            _settingsRoute?.RetitleDetail(view, LayoutDesignerTitle());
        }
    }

    private void OnTitledLayoutDesignerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName is nameof(LayoutDesignerViewModel.Name) or null)
        {
            RefreshLayoutDesignerTitle();
        }
    }

    private void StopTrackingDockPresses()
    {
        if (SettingsWindow.Current is { } settings)
        {
            TrackDockPresses(settings, track: false);
        }

        TrackDockPresses(this, track: false);
        (DataContext as MainWindowViewModel)?.SetDockDragOrigin(DockDragOrigin.None);
    }

    /// <summary>
    /// While the designer is open in Settings beside the workspace, a press on a panel in
    /// one of them stops the other taking drops until the button is let go,
    /// so a slot cannot land in the live workspace or a live panel in the
    /// design. Otherwise both dock as usual.
    /// </summary>
    private void TrackDockPresses(Window window, bool track)
    {
        if (track)
        {
            window.AddHandler(PointerPressedEvent, OnDockPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(PointerReleasedEvent, OnDockPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
        else
        {
            window.RemoveHandler(PointerPressedEvent, OnDockPointerPressed);
            window.RemoveHandler(PointerReleasedEvent, OnDockPointerReleased);
        }
    }

    private void OnDockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || e.Source is not Visual source
            || source.FindAncestorOfType<DockControl>(includeSelf: true) is null)
        {
            return;
        }

        viewModel.SetDockDragOrigin(ReferenceEquals(sender, this)
            ? DockDragOrigin.Workspace
            : DockDragOrigin.LayoutDesigner);
    }

    private void OnDockPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _ = sender;
        _ = e;
        (DataContext as MainWindowViewModel)?.SetDockDragOrigin(DockDragOrigin.None);
    }
}
