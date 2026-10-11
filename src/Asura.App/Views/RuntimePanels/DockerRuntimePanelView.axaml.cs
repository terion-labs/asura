using System.ComponentModel;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Asura.Docker;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class DockerRuntimePanelView : UserControl, IPanelCommandTarget
{
    private const double CompactLayoutMinimumWidth = 1_100;
    private const double CompactDetailHeaderMinimumWidth = 640;
    private const double LogPagingThreshold = 80;
    private const int LogScrollSettlePasses = 3;
    private static readonly FilePickerFileType LogFileType = new("Container logs")
    {
        Patterns = ["*.log", "*.txt"],
        MimeTypes = ["text/plain"],
    };
    private static readonly TimeSpan ContainerTypeSelectPause = TimeSpan.FromSeconds(1);
    private readonly TypeSelectBuffer _containerTypeSelect = new(ContainerTypeSelectPause);
    private readonly DispatcherTimer _containerTypeSelectSettle = new() { Interval = ContainerTypeSelectPause };
    private ScrollViewer? _logScrollViewer;
    private DockerRuntimePanelViewModel? _observedViewModel;
    private bool _isLoadingOlderLogs;
    private bool _isScrollingLogsToEnd;
    private bool _logScrollToEndPending;
    private bool _hasPerformedInitialLogScroll;
    private int _logScrollGeneration;

    public DockerRuntimePanelView()
    {
        InitializeComponent();
        DockerDetailHeader.SizeChanged += OnDetailHeaderSizeChanged;
        ContainerStackList.AddHandler(TextInputEvent, OnContainerTextInput, RoutingStrategies.Tunnel);
        _containerTypeSelectSettle.Tick += OnContainerTypeSelectSettled;
        LogList.AddHandler(
            PointerWheelChangedEvent,
            OnLogPointerWheelChanged,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ObserveViewModel(ViewModel);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        ApplyLayoutClass(availableSize.Width);
        return base.MeasureOverride(availableSize);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyLayoutClass(e.NewSize.Width);
    }

    private void ApplyLayoutClass(double width)
    {
        var isCompact = width < CompactLayoutMinimumWidth;
        if (Classes.Contains("narrowPanel") != isCompact)
        {
            Classes.Set("narrowPanel", isCompact);
            InvalidateMeasure();
        }
    }

    private void OnDetailHeaderSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        var compact = e.NewSize.Width < CompactDetailHeaderMinimumWidth;
        if (DockerDetailHeader.Classes.Contains("compactDetails") != compact)
        {
            DockerDetailHeader.Classes.Set("compactDetails", compact);
        }
    }

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;

    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;

    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    public event EventHandler<DockerRuntimePanelViewModel>? ShellRequested;

    public event EventHandler<DockerRuntimePanelViewModel>? InlineShellRequested;

    public event EventHandler<TerminalRuntimePanelViewModel>? InlineShellTrustHostKeyRequested;

    public event EventHandler<RoutedEventArgs>? DockerInstallGuideRequested;

    private DockerRuntimePanelViewModel? ViewModel => DataContext as DockerRuntimePanelViewModel;

    private void OnDockerInstallGuideClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        DockerInstallGuideRequested?.Invoke(this, e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ObserveViewModel(ViewModel);
    }

    private void ObserveViewModel(DockerRuntimePanelViewModel? viewModel)
    {
        _observedViewModel?.PropertyChanged -= OnViewModelPropertyChanged;

        _observedViewModel = viewModel;
        _containerTypeSelectSettle.Stop();
        _logScrollGeneration++;
        _isScrollingLogsToEnd = false;
        _logScrollToEndPending = viewModel?.HasLogs == true;
        _hasPerformedInitialLogScroll = false;
        _observedViewModel?.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = sender;
        _ = e;
        Dispatcher.UIThread.Post(AttachLogScrollViewer, DispatcherPriority.Loaded);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _ = sender;
        _ = e;
        _logScrollViewer?.ScrollChanged -= OnLogScrollChanged;
        _logScrollViewer = null;
        _containerTypeSelectSettle.Stop();

        _logScrollGeneration++;
        _isScrollingLogsToEnd = false;
        _isLoadingOlderLogs = false;
        _logScrollToEndPending = _observedViewModel?.HasLogs == true;
        _hasPerformedInitialLogScroll = false;
    }

    private void AttachLogScrollViewer()
    {
        var scrollViewer = LogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (!ReferenceEquals(scrollViewer, _logScrollViewer))
        {
            _logScrollViewer?.ScrollChanged -= OnLogScrollChanged;

            _logScrollViewer = scrollViewer;
            _logScrollViewer?.ScrollChanged += OnLogScrollChanged;
        }

        if (!_hasPerformedInitialLogScroll && ViewModel is { HasLogs: true })
        {
            _logScrollToEndPending = true;
        }

        TryStartLogScrollToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(DockerRuntimePanelViewModel.LogScrollToEndRequest), StringComparison.Ordinal))
        {
            return;
        }

        _logScrollGeneration++;
        _isScrollingLogsToEnd = false;
        _logScrollToEndPending = true;
        _hasPerformedInitialLogScroll = false;
        Dispatcher.UIThread.Post(AttachLogScrollViewer, DispatcherPriority.Loaded);
    }

    private void TryStartLogScrollToEnd()
    {
        if (!_logScrollToEndPending
            || _isScrollingLogsToEnd
            || _logScrollViewer is not { } scrollViewer
            || ViewModel is not { HasLogs: true } panel)
        {
            return;
        }

        _isScrollingLogsToEnd = true;
        var generation = _logScrollGeneration;
        SettleLogScrollToEnd(panel, scrollViewer, generation, pass: 0);
    }

    private void SettleLogScrollToEnd(
        DockerRuntimePanelViewModel panel,
        ScrollViewer scrollViewer,
        int generation,
        int pass)
    {
        if (generation != _logScrollGeneration
            || !ReferenceEquals(panel, ViewModel)
            || !ReferenceEquals(scrollViewer, _logScrollViewer)
            || panel.LogRows.Count == 0)
        {
            if (generation == _logScrollGeneration)
            {
                _isScrollingLogsToEnd = false;
            }

            return;
        }

        LogList.ScrollIntoView(panel.LogRows[^1]);
        LogList.UpdateLayout();
        scrollViewer.ScrollToEnd();

        if (pass + 1 < LogScrollSettlePasses)
        {
            Dispatcher.UIThread.Post(
                () => SettleLogScrollToEnd(panel, scrollViewer, generation, pass + 1),
                DispatcherPriority.Loaded);
            return;
        }

        _logScrollToEndPending = false;
        _isScrollingLogsToEnd = false;
        _hasPerformedInitialLogScroll = true;
    }

    private async void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer || ViewModel is not { } panel)
        {
            return;
        }

        var maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var isAtEnd = scrollViewer.Offset.Y >= maximumOffset - 12;
        if (!_isScrollingLogsToEnd
            && _hasPerformedInitialLogScroll
            && panel.FollowLogs
            && !isAtEnd
            && e.OffsetDelta.Y < 0)
        {
            _logScrollGeneration++;
            _logScrollToEndPending = false;
            panel.FollowLogs = false;
        }

        await TryLoadOlderLogsAsync(panel, scrollViewer);
    }

    private void OnLogPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _ = sender;
        if (e.Delta.Y <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (ViewModel is { } panel && _logScrollViewer is { } scrollViewer)
            {
                _ = TryLoadOlderLogsAsync(panel, scrollViewer);
            }
        }, DispatcherPriority.Background);
    }

    private async Task TryLoadOlderLogsAsync(
        DockerRuntimePanelViewModel panel,
        ScrollViewer scrollViewer)
    {
        if (!ReferenceEquals(panel, ViewModel)
            || !ReferenceEquals(scrollViewer, _logScrollViewer)
            || _isScrollingLogsToEnd
            || _logScrollToEndPending
            || !_hasPerformedInitialLogScroll
            || scrollViewer.Offset.Y > LogPagingThreshold
            || !panel.HasOlderLogs
            || panel.IsLoadingLogs
            || _isLoadingOlderLogs)
        {
            return;
        }

        _isLoadingOlderLogs = true;
        var previousExtent = scrollViewer.Extent.Height;
        var previousOffset = scrollViewer.Offset.Y;
        var restoreScheduled = false;
        try
        {
            if (!await panel.LoadOlderLogsAsync())
            {
                return;
            }

            restoreScheduled = true;
            RestoreLogViewportAfterPrepend(
                panel,
                scrollViewer,
                previousExtent,
                previousOffset,
                pass: 0);
        }
        finally
        {
            if (!restoreScheduled)
            {
                _isLoadingOlderLogs = false;
            }
        }
    }

    private void RestoreLogViewportAfterPrepend(
        DockerRuntimePanelViewModel panel,
        ScrollViewer scrollViewer,
        double previousExtent,
        double previousOffset,
        int pass)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(panel, ViewModel)
                || !ReferenceEquals(scrollViewer, _logScrollViewer))
            {
                _isLoadingOlderLogs = false;
                return;
            }

            LogList.UpdateLayout();
            var addedHeight = Math.Max(0, scrollViewer.Extent.Height - previousExtent);
            if (addedHeight <= 0 && pass + 1 < LogScrollSettlePasses)
            {
                RestoreLogViewportAfterPrepend(
                    panel,
                    scrollViewer,
                    previousExtent,
                    previousOffset,
                    pass + 1);
                return;
            }

            try
            {
                var maximumOffset = Math.Max(
                    0,
                    scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                scrollViewer.Offset = new Vector(
                    scrollViewer.Offset.X,
                    Math.Min(maximumOffset, previousOffset + addedHeight));
            }
            finally
            {
                _isLoadingOlderLogs = false;
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(sender, e);

    private void OnConnectionSelected(object? sender, PanelConnectionSelectedEventArgs e) =>
        ConnectionSelected?.Invoke(this, e);

    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) =>
        NewConnectionRequested?.Invoke(this, e);

    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) =>
        SplitRequested?.Invoke(sender, orientation);

    private void OnInfoClick(object? sender, RoutedEventArgs e) =>
        ShowDetail(DockerPanelDetail.Info);

    private void OnLogsClick(object? sender, RoutedEventArgs e) =>
        ShowDetail(DockerPanelDetail.Logs);

    /// <summary>
    /// A segment the person chose. The segments follow the view model one
    /// way, so a change that is only the view model's own selection coming
    /// back — a new resource resetting to Info — is already the current view
    /// and is left alone.
    /// </summary>
    private void OnDetailSegmentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = e;
        if (sender is SegmentedControl { SelectedIndex: >= 0 } segments
            && ViewModel is { } panel
            && Enum.IsDefined((DockerPanelDetail)segments.SelectedIndex)
            && (DockerPanelDetail)segments.SelectedIndex != panel.Detail)
        {
            ShowDetail((DockerPanelDetail)segments.SelectedIndex);
        }
    }

    /// <summary>
    /// Shows one view of the selection. The shell view starts its shell the
    /// first time it is shown, which no other view needs.
    /// </summary>
    private void ShowDetail(DockerPanelDetail detail)
    {
        if (ViewModel is not { } panel)
        {
            return;
        }

        if (detail != DockerPanelDetail.Shell)
        {
            panel.SelectDetail(detail);
            return;
        }

        if (panel.CanOpenShell)
        {
            panel.SelectDetail(DockerPanelDetail.Shell);
            RequestInlineShell(panel);
        }
    }

    private async void OnLogSearchKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Enter && ViewModel is { } panel)
        {
            e.Handled = true;
            await panel.SearchLogsAsync();
        }
    }

    private async void OnDownloadLogsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var panel = ViewModel;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (panel is null || storage?.CanSave != true)
        {
            return;
        }

        var destination = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Container Logs",
            SuggestedFileName = panel.LogDownloadFileName,
            DefaultExtension = "log",
            FileTypeChoices = [LogFileType],
            ShowOverwritePrompt = true,
        });
        if (destination is null)
        {
            return;
        }

        await using var stream = await destination.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await panel.DownloadLogsAsync(stream);
    }

    private void OnShellClick(object? sender, RoutedEventArgs e) =>
        ShowDetail(DockerPanelDetail.Shell);

    private void OnJsonClick(object? sender, RoutedEventArgs e) =>
        ShowDetail(DockerPanelDetail.Json);

    private void OnFilesClick(object? sender, RoutedEventArgs e) =>
        ShowDetail(DockerPanelDetail.Files);

    private void OnOpenShellClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } panel)
        {
            ShellRequested?.Invoke(this, panel);
        }
    }

    private void OnOpenInlineShellClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is { CanOpenShell: true } panel)
        {
            RequestInlineShell(panel);
        }
    }

    private void OnContainerResourceClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Control { DataContext: DockerResourceItemViewModel resource }
            || ViewModel is not { } panel)
        {
            return;
        }

        SelectResource(panel, resource);
    }

    /// <summary>
    /// Typing a name selects the first container, among the stacks that are
    /// open, whose name starts with it, and moves focus to its row — the
    /// type-to-select every Mac list has. The other resource types are a list
    /// box, which does the same on its own. A partly typed name only selects:
    /// the shell view opens a shell once the typing pauses, so passing over a
    /// container on the way to another never starts a session in it.
    /// </summary>
    private void OnContainerTextInput(object? sender, TextInputEventArgs e)
    {
        _ = sender;
        if (ViewModel is not { } panel
            || _containerTypeSelect.Append(e.Text, DateTimeOffset.UtcNow) is not { } prefix
            || TypeSelectBuffer.FindMatch(
                panel.ContainerStacks
                    .Where(stack => stack.IsExpanded)
                    .SelectMany(stack => stack.Containers),
                container => container.Title,
                prefix) is not { } match)
        {
            return;
        }

        e.Handled = true;
        panel.SelectResource(match);
        _containerTypeSelectSettle.Stop();
        _containerTypeSelectSettle.Start();
        if (ContainerStackList.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(row => row.Classes.Contains("DockerResourceRow")
                && ReferenceEquals(row.DataContext, match)) is { } row)
        {
            row.Focus(NavigationMethod.Directional);
            row.BringIntoView();
        }
    }

    private void OnContainerTypeSelectSettled(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        _containerTypeSelectSettle.Stop();
        if (ViewModel is { IsShellDetail: true, CanOpenShell: true } panel)
        {
            RequestInlineShell(panel);
        }
    }

    private void SelectResource(
        DockerRuntimePanelViewModel panel,
        DockerResourceItemViewModel resource)
    {
        _containerTypeSelectSettle.Stop();
        panel.SelectResource(resource);
        if (panel.IsShellDetail && panel.CanOpenShell)
        {
            RequestInlineShell(panel);
        }
    }

    /// <summary>
    /// A secondary click on a resource, or the context-menu key on one,
    /// selects it as Finder does and offers what the toolbar offers for it.
    /// A stack's heading offers the stack's own lifecycle commands.
    /// </summary>
    private void OnResourceContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        _ = sender;
        if (ViewModel is not { } panel || e.Source is not Control source)
        {
            return;
        }

        ContextMenu? menu = null;
        switch (source.DataContext)
        {
            case DockerResourceItemViewModel resource when panel.Resources.Contains(resource):
                SelectResource(panel, resource);
                menu = CreateResourceContextMenu(panel, resource);
                break;
            case DockerContainerStackViewModel { HasLifecycleControls: true } stack
                when panel.ContainerStacks.Contains(stack):
                menu = CreateStackContextMenu(panel, stack);
                break;
        }

        if (menu is null)
        {
            return;
        }

        e.Handled = true;
        menu.Open(source);
    }

    /// <summary>
    /// What can be done to one resource: its views first, then a container's
    /// lifecycle, then copying what identifies it. Docker resources here are
    /// never removed, so there is no destructive group to put last.
    /// </summary>
    internal ContextMenu CreateResourceContextMenu(
        DockerRuntimePanelViewModel panel,
        DockerResourceItemViewModel resource)
    {
        var menu = new ContextMenu();
        if (resource.IsContainer)
        {
            menu.Items.Add(Item(BuiltInCommands.DockerShowLogs));
            menu.Items.Add(Item(BuiltInCommands.DockerBrowseFiles));
            menu.Items.Add(Item(BuiltInCommands.DockerOpenShell));
            menu.Items.Add(new Separator());
            menu.Items.Add(LifecycleItem(resource.IsRunning || resource.IsPaused
                ? BuiltInCommands.DockerStop
                : BuiltInCommands.DockerStart));
            menu.Items.Add(LifecycleItem(BuiltInCommands.DockerRestart));
            menu.Items.Add(LifecycleItem(resource.IsPaused
                ? BuiltInCommands.DockerResume
                : BuiltInCommands.DockerPause));
        }
        else
        {
            if (!resource.IsNetwork)
            {
                menu.Items.Add(Item(BuiltInCommands.DockerBrowseFiles));
            }

            menu.Items.Add(Item(BuiltInCommands.DockerShowJson));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(BuiltInCommands.DockerCopyName));
        if (!resource.IsVolume)
        {
            menu.Items.Add(Item(BuiltInCommands.DockerCopyId));
        }

        return menu;

        MenuItem Item(CommandId command) => PanelCommandTargets.MenuItem(
            command,
            CanRun(panel, resource, command),
            () => RunAsync(panel, resource, command));

        MenuItem LifecycleItem(CommandId command) => new()
        {
            Header = PanelCommandTargets.Title(command),
            Command = LifecycleCommand(panel, command),
            Tag = command,
        };
    }

    /// <summary>
    /// Whether a resource command can run on <paramref name="resource"/> now.
    /// The context menu and the menu bar's Docker items both ask here; the
    /// menu bar offers Start and Stop, Pause and Resume side by side, so each
    /// is available only in the state the context menu shows it in.
    /// </summary>
    private static bool CanRun(
        DockerRuntimePanelViewModel panel,
        DockerResourceItemViewModel resource,
        CommandId command)
    {
        if (LifecycleCommand(panel, command) is { } lifecycle)
        {
            var isUp = resource.IsRunning || resource.IsPaused;
            var inState = command == BuiltInCommands.DockerStart ? !isUp
                : command == BuiltInCommands.DockerStop ? isUp
                : command == BuiltInCommands.DockerPause ? !resource.IsPaused
                : command != BuiltInCommands.DockerResume || resource.IsPaused;
            return resource.IsContainer && inState && lifecycle.CanExecute(null);
        }

        if (command == BuiltInCommands.DockerShowLogs)
        {
            return resource.IsContainer && panel.CanShowLogs;
        }

        if (command == BuiltInCommands.DockerBrowseFiles)
        {
            return !resource.IsNetwork && panel.CanShowFiles;
        }

        if (command == BuiltInCommands.DockerOpenShell)
        {
            return resource.IsContainer && panel.CanOpenShell;
        }

        if (command == BuiltInCommands.DockerShowJson)
        {
            return panel.CanShowJson;
        }

        return command == BuiltInCommands.DockerCopyName
            || (command == BuiltInCommands.DockerCopyId && !resource.IsVolume);
    }

    private Task RunAsync(
        DockerRuntimePanelViewModel panel,
        DockerResourceItemViewModel resource,
        CommandId command)
    {
        if (LifecycleCommand(panel, command) is { } lifecycle)
        {
            lifecycle.Execute(null);
        }
        else if (command == BuiltInCommands.DockerShowLogs)
        {
            ShowDetail(DockerPanelDetail.Logs);
        }
        else if (command == BuiltInCommands.DockerBrowseFiles)
        {
            ShowDetail(DockerPanelDetail.Files);
        }
        else if (command == BuiltInCommands.DockerOpenShell)
        {
            ShellRequested?.Invoke(this, panel);
        }
        else if (command == BuiltInCommands.DockerShowJson)
        {
            ShowDetail(DockerPanelDetail.Json);
        }
        else if (command == BuiltInCommands.DockerCopyName)
        {
            return CopyTextAsync(resource.Title);
        }
        else if (command == BuiltInCommands.DockerCopyId)
        {
            return CopyTextAsync(resource.Resource.Id);
        }

        return Task.CompletedTask;
    }

    private static System.Windows.Input.ICommand? LifecycleCommand(
        DockerRuntimePanelViewModel panel,
        CommandId command) =>
        command == BuiltInCommands.DockerStart ? panel.StartCommand
        : command == BuiltInCommands.DockerStop ? panel.StopCommand
        : command == BuiltInCommands.DockerRestart ? panel.RestartCommand
        : command == BuiltInCommands.DockerPause ? panel.PauseCommand
        : command == BuiltInCommands.DockerResume ? panel.ResumeCommand
        : null;

    bool IPanelCommandTarget.CanExecute(CommandId command)
    {
        if (ViewModel is not { } panel)
        {
            return false;
        }

        if (IsStackCommand(command))
        {
            return SelectedStack(panel) is { } stack && CanRunOnStack(stack, command);
        }

        return panel.SelectedResource is { } resource
            && panel.Resources.Contains(resource)
            && CanRun(panel, resource, command);
    }

    Task IPanelCommandTarget.ExecuteAsync(CommandId command)
    {
        if (ViewModel is not { } panel)
        {
            return Task.CompletedTask;
        }

        if (IsStackCommand(command))
        {
            return SelectedStack(panel) is { } stack && CanRunOnStack(stack, command)
                ? RunOnStackAsync(panel, stack, command)
                : Task.CompletedTask;
        }

        return panel.SelectedResource is { } resource
            && panel.Resources.Contains(resource)
            && CanRun(panel, resource, command)
                ? RunAsync(panel, resource, command)
                : Task.CompletedTask;
    }

    IEnumerable<KeyValuePair<string, bool>> IPanelCommandTarget.States =>
        ViewModel is { } panel && SelectedStack(panel) is { } stack
            ? [new(CommandStates.IsDockerStackCollapsed, !stack.IsExpanded)]
            : [];

    /// <summary>
    /// The stack heading's own buttons, as a menu on the heading. The menu
    /// bar's copies act on the stack of the selected container.
    /// </summary>
    internal ContextMenu CreateStackContextMenu(
        DockerRuntimePanelViewModel panel,
        DockerContainerStackViewModel stack)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(BuiltInCommands.DockerStackStart));
        menu.Items.Add(Item(BuiltInCommands.DockerStackStop));
        menu.Items.Add(Item(BuiltInCommands.DockerStackRestart));
        menu.Items.Add(Item(stack.CanResume && !stack.CanPause
            ? BuiltInCommands.DockerStackResume
            : BuiltInCommands.DockerStackPause));
        menu.Items.Add(new Separator());
        menu.Items.Add(PanelCommandTargets.MenuItem(
            BuiltInCommands.DockerStackToggle,
            isEnabled: true,
            () => RunOnStackAsync(panel, stack, BuiltInCommands.DockerStackToggle),
            stack.IsExpanded ? null : PanelCommandTargets.StateTitle(BuiltInCommands.DockerStackToggle)));
        return menu;

        MenuItem Item(CommandId command) => PanelCommandTargets.MenuItem(
            command,
            CanRunOnStack(stack, command),
            () => RunOnStackAsync(panel, stack, command));
    }

    /// <summary>
    /// The stack the selected container belongs to, when it has a heading of
    /// its own; a standalone container's stack has no lifecycle controls.
    /// </summary>
    private static DockerContainerStackViewModel? SelectedStack(DockerRuntimePanelViewModel panel) =>
        panel is { IsContainersSection: true, SelectedResource: { } resource }
            ? panel.ContainerStacks.FirstOrDefault(stack =>
                stack.HasLifecycleControls && stack.Containers.Contains(resource))
            : null;

    private static bool IsStackCommand(CommandId command) =>
        command == BuiltInCommands.DockerStackToggle || StackAction(command) is not null;

    private static DockerContainerAction? StackAction(CommandId command) =>
        command == BuiltInCommands.DockerStackStart ? DockerContainerAction.Start
        : command == BuiltInCommands.DockerStackStop ? DockerContainerAction.Stop
        : command == BuiltInCommands.DockerStackRestart ? DockerContainerAction.Restart
        : command == BuiltInCommands.DockerStackPause ? DockerContainerAction.Pause
        : command == BuiltInCommands.DockerStackResume ? DockerContainerAction.Resume
        : null;

    private static bool CanRunOnStack(DockerContainerStackViewModel stack, CommandId command) =>
        command == BuiltInCommands.DockerStackToggle
        || (StackAction(command) switch
        {
            DockerContainerAction.Start => stack.CanStart,
            DockerContainerAction.Stop => stack.CanStop,
            DockerContainerAction.Restart => stack.CanRestart,
            DockerContainerAction.Pause => stack.CanPause,
            DockerContainerAction.Resume => stack.CanResume,
            _ => false,
        });

    private static Task RunOnStackAsync(
        DockerRuntimePanelViewModel panel,
        DockerContainerStackViewModel stack,
        CommandId command)
    {
        if (command == BuiltInCommands.DockerStackToggle)
        {
            stack.IsExpanded = !stack.IsExpanded;
            return Task.CompletedTask;
        }

        return StackAction(command) is { } action
            ? panel.RunStackActionAsync(stack, action)
            : Task.CompletedTask;
    }

    private async Task CopyTextAsync(string text)
    {
        if (!string.IsNullOrEmpty(text)
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void OnStartStackClick(object? sender, RoutedEventArgs e) =>
        await RunStackActionAsync(sender, e, DockerContainerAction.Start);

    private async void OnStopStackClick(object? sender, RoutedEventArgs e) =>
        await RunStackActionAsync(sender, e, DockerContainerAction.Stop);

    private async void OnRestartStackClick(object? sender, RoutedEventArgs e) =>
        await RunStackActionAsync(sender, e, DockerContainerAction.Restart);

    private async void OnPauseStackClick(object? sender, RoutedEventArgs e) =>
        await RunStackActionAsync(sender, e, DockerContainerAction.Pause);

    private async void OnResumeStackClick(object? sender, RoutedEventArgs e) =>
        await RunStackActionAsync(sender, e, DockerContainerAction.Resume);

    private void OnToggleStackClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: DockerContainerStackViewModel stack })
        {
            stack.IsExpanded = !stack.IsExpanded;
        }
    }

    private async Task RunStackActionAsync(
        object? sender,
        RoutedEventArgs e,
        DockerContainerAction action)
    {
        e.Handled = true;
        if (sender is Control { DataContext: DockerContainerStackViewModel stack }
            && ViewModel is { } panel)
        {
            await panel.RunStackActionAsync(stack, action);
        }
    }

    private void OnInlineShellTrustHostKeyRequested(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel?.InlineShell is { } shell)
        {
            InlineShellTrustHostKeyRequested?.Invoke(this, shell);
        }
    }

    private async void OnEmbeddedFileActionRequested(
        object? sender,
        FilePanelActionEventArgs e)
    {
        if (sender is not Control { DataContext: FileRuntimePanelViewModel panel })
        {
            return;
        }

        switch (e.Action)
        {
            case FilePanelAction.Open when panel.SelectedEntry is { } entry:
                await panel.OpenEntryAsync(entry);
                break;
            case FilePanelAction.Refresh:
                await panel.RefreshAsync();
                break;
            case FilePanelAction.CopyName:
                await CopyEmbeddedFileTextAsync(panel.SelectedEntry?.Name);
                break;
            case FilePanelAction.CopyPath:
                await CopyEmbeddedFileTextAsync(panel.SelectedEntryPath);
                break;
        }
    }

    private static void OnEmbeddedFileDismissOperationIssueRequested(
        object? sender,
        RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: FileRuntimePanelViewModel panel })
        {
            panel.ClearOperationIssue();
        }
    }

    private static async void OnEmbeddedFileEntryDoubleTapped(
        object? sender,
        TappedEventArgs e)
    {
        _ = e;
        if (sender is ListBox
            {
                DataContext: FileRuntimePanelViewModel panel,
                SelectedItem: FileEntryViewModel entry,
            })
        {
            await panel.OpenEntryAsync(entry);
        }
    }

    private static async void OnEmbeddedFileEntrySelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        _ = e;
        if (sender is ListBox { DataContext: FileRuntimePanelViewModel panel } list)
        {
            panel.SetSelectedEntries(list.SelectedItems?
                .OfType<FileEntryViewModel>()
                .Select(item => item.Entry)
                .ToArray() ?? []);
            await panel.PreviewSelectedAsync();
        }
    }

    private static async void OnEmbeddedFileLocationKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && sender is Control { DataContext: FileRuntimePanelViewModel panel })
        {
            await panel.NavigateFromTextAsync();
            e.Handled = true;
        }
    }

    private static async void OnEmbeddedFileNavigateUpRequested(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: FileRuntimePanelViewModel panel })
        {
            await panel.NavigateUpAsync();
        }
    }

    private static async void OnEmbeddedFileRefreshRequested(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: FileRuntimePanelViewModel panel })
        {
            await panel.RefreshAsync();
        }
    }

    private Task CopyEmbeddedFileTextAsync(string? text) =>
        string.IsNullOrEmpty(text) ? Task.CompletedTask : CopyTextAsync(text);

    private void RequestInlineShell(DockerRuntimePanelViewModel panel)
    {
        if (!panel.HasInlineShell)
        {
            InlineShellRequested?.Invoke(this, panel);
        }
    }
}
