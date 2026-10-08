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
/// The workspace editor and the Layout Designer, each in a window of its own
/// beside this one. An edit session begins in the view model; this part shows
/// it in its window, brings that window forward when the same edit is asked
/// for again, closes it when the edit ends inside it, and ends the edit when
/// the window is closed — asking to save first when there is something to
/// save.
/// </summary>
public sealed partial class MainWindow
{
    private WorkspaceEditorWindow? _workspaceEditorWindow;
    private LayoutDesignerWindow? _layoutDesignerWindow;
    private WorkspaceEditorViewModel? _titledWorkspaceEditor;
    private bool _closingWithEditorWindows;

    /// <summary>This window's workspace editor window, while one is open.</summary>
    internal WorkspaceEditorWindow? OpenWorkspaceEditorWindow => _workspaceEditorWindow;

    /// <summary>This window's Layout Designer window, while one is open.</summary>
    internal LayoutDesignerWindow? OpenLayoutDesignerWindow => _layoutDesignerWindow;

    /// <summary>
    /// Where an editor's alerts go: over the editor's window when it is open,
    /// so the question appears where the person is working.
    /// </summary>
    private Window WorkspaceEditorDialogOwner => _workspaceEditorWindow ?? (Window)this;

    private Window LayoutDesignerDialogOwner => _layoutDesignerWindow ?? (Window)this;

    /// <summary>
    /// Edits <paramref name="id"/>. The same workspace asked for again brings
    /// its window forward with the edits as they are; another workspace takes
    /// the window over once the edits in it are saved, as choosing one in the
    /// editor's own list does.
    /// </summary>
    internal async Task EditWorkspaceAsync(WorkspaceId id)
    {
        BringForwardForEditor();
        if (_workspaceEditorWindow is not null && ViewModel.WorkspaceEditor is { } editing)
        {
            if (editing.Id == id && !editing.IsNew)
            {
                PresentWorkspaceEditor();
                return;
            }

            _workspaceEditorWindow.BringForward();
            if (!await CommitWorkspaceEditsBeforeSwitchAsync())
            {
                return;
            }
        }

        ViewModel.BeginEditWorkspace(id);
        PresentWorkspaceEditor();
    }

    /// <summary>Opens the editor over a new, unsaved workspace.</summary>
    internal async Task CreateWorkspaceAsync()
    {
        BringForwardForEditor();
        if (_workspaceEditorWindow is not null)
        {
            _workspaceEditorWindow.BringForward();
            if (!await CommitWorkspaceEditsBeforeSwitchAsync())
            {
                return;
            }
        }

        ViewModel.BeginCreateWorkspace();
        PresentWorkspaceEditor();
    }

    /// <summary>
    /// Shows the edit session that has just begun in its window, or brings
    /// the window forward. Nothing opens when the session could not begin.
    /// </summary>
    private void PresentWorkspaceEditor()
    {
        if (!ViewModel.IsDefinitionEditorVisible)
        {
            return;
        }

        if (_workspaceEditorWindow is { } open)
        {
            RefreshWorkspaceEditorTitle();
            open.BringForward();
            FocusNavigator.FocusDefinitionEditor();
            return;
        }

        var editor = EnsureWorkspaceEditorView();
        var window = new WorkspaceEditorWindow();
        window.Closed += OnWorkspaceEditorWindowClosed;
        _workspaceEditorWindow = window;
        RefreshWorkspaceEditorTitle();
        window.Present(this, editor, ConfirmCloseWorkspaceEditorAsync);
        FocusNavigator.FocusDefinitionEditor();
    }

    /// <summary>Shows the layout design that has just begun in its window.</summary>
    private void PresentLayoutDesigner()
    {
        if (!ViewModel.IsLayoutDesignerVisible)
        {
            return;
        }

        if (_layoutDesignerWindow is { } open)
        {
            open.BringForward();
            FocusNavigator.FocusLayoutDesignerName();
            return;
        }

        var designer = EnsureLayoutDesignerView();
        var window = new LayoutDesignerWindow();
        window.Closed += OnLayoutDesignerWindowClosed;
        _layoutDesignerWindow = window;
        TrackDockPresses(window, track: true);
        TrackDockPresses(this, track: true);
        window.Present(this, designer, ConfirmCloseLayoutDesignerAsync);
        FocusNavigator.FocusLayoutDesignerName();
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
    /// The window was closed with unsaved edits: Save keeps them, Don't Save
    /// drops them, and Cancel goes back to editing. True lets the window go.
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
                    ViewModel.SetError(exception.Message);
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
    /// only when the person says so, and the window closes with the session.
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
                when !ViewModel.IsDefinitionEditorVisible:
                _workspaceEditorWindow?.CloseApproved();
                break;
            case nameof(MainWindowViewModel.IsLayoutDesignerVisible)
                when !ViewModel.IsLayoutDesignerVisible:
                _layoutDesignerWindow?.CloseApproved();
                break;
            case nameof(MainWindowViewModel.WorkspaceEditor):
                RefreshWorkspaceEditorTitle();
                break;
        }
    }

    private void RefreshWorkspaceEditorTitle()
    {
        var editor = ViewModel.WorkspaceEditor;
        if (!ReferenceEquals(_titledWorkspaceEditor, editor))
        {
            _titledWorkspaceEditor?.PropertyChanged -= OnTitledWorkspaceEditorPropertyChanged;
            _titledWorkspaceEditor = editor;
            _titledWorkspaceEditor?.PropertyChanged += OnTitledWorkspaceEditorPropertyChanged;
        }

        if (_workspaceEditorWindow is { } window && editor is not null)
        {
            window.Title = WorkspaceEditorWindow.TitleFor(editor.Name, editor.IsNew);
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

    /// <summary>
    /// Closing the window ends the edit, and the person is back where they
    /// were in this window.
    /// </summary>
    private void OnWorkspaceEditorWindowClosed(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is Window window)
        {
            window.Closed -= OnWorkspaceEditorWindowClosed;
        }

        _workspaceEditorWindow = null;
        _titledWorkspaceEditor?.PropertyChanged -= OnTitledWorkspaceEditorPropertyChanged;
        _titledWorkspaceEditor = null;
        if (DataContext is MainWindowViewModel { IsDefinitionEditorVisible: true } viewModel)
        {
            viewModel.DismissWorkspaceEditor();
        }

        ReturnFromEditorWindow();
    }

    private void OnLayoutDesignerWindowClosed(object? sender, EventArgs e)
    {
        _ = e;
        if (sender is Window window)
        {
            window.Closed -= OnLayoutDesignerWindowClosed;
        }

        _layoutDesignerWindow = null;
        if (sender is Window closed)
        {
            TrackDockPresses(closed, track: false);
        }

        TrackDockPresses(this, track: false);
        (DataContext as MainWindowViewModel)?.SetDockDragOrigin(DockDragOrigin.None);
        if (DataContext is MainWindowViewModel { IsLayoutDesignerVisible: true } viewModel)
        {
            viewModel.DismissLayoutDesigner();
        }

        ReturnFromEditorWindow();
    }

    /// <summary>
    /// While the designer is open beside the workspace, a press on a panel in
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

    private void ReturnFromEditorWindow()
    {
        if (_closingWithEditorWindows || _lifetime.IsCancellationRequested)
        {
            return;
        }

        Activate();
        FocusNavigator.FocusCurrentRoute();
    }

    /// <summary>
    /// This window is closing and has asked about unsaved edits already, so
    /// its editor windows go with it without asking again.
    /// </summary>
    private void ApproveEditorWindowsClose()
    {
        _closingWithEditorWindows = true;
        _workspaceEditorWindow?.ApproveClose();
        _layoutDesignerWindow?.ApproveClose();
    }
}
