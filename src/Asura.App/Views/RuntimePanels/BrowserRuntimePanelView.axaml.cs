using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Application;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class BrowserRuntimePanelView : UserControl
{
    private const string BlankAddressPlaceholder = "about:blank";
    private BrowserRuntimePanelViewModel? _historyPanel;

    public BrowserRuntimePanelView()
    {
        InitializeComponent();
        AddHandler(
            KeyDownEvent,
            OnPanelKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DismissHistory();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _historyPanel?.HideHistory();
        _historyPanel = DataContext as BrowserRuntimePanelViewModel;
        base.OnDataContextChanged(e);
    }

    public event EventHandler<KeyEventArgs>? AddressKeyDown;

    public event EventHandler<RoutedEventArgs>? BackRequested;

    public event EventHandler<BrowserStateChangedEventArgs>? BrowserStateChanged;

    public event EventHandler<RoutedEventArgs>? CloseRequested;

    public event EventHandler<PanelConnectionSelectedEventArgs>? ConnectionSelected;

    public event EventHandler<RoutedEventArgs>? DeveloperToolsRequested;

    public event EventHandler<RoutedEventArgs>? OpenInSystemBrowserRequested;

    public event EventHandler<RoutedEventArgs>? NewConnectionRequested;

    /// <summary>
    /// Splitting places an empty panel beside this one; what it becomes is chosen
    /// there rather than in a modal over the window.
    /// </summary>
    public event EventHandler<PanelSplitOrientation>? SplitRequested;

    public event EventHandler<RoutedEventArgs>? ForwardRequested;

    public event EventHandler<RoutedEventArgs>? ReloadRequested;

    public event EventHandler<RoutedEventArgs>? StopRequested;

    /// <summary>
    /// Browser actions are raised with the presentation host as the sender.
    ///
    /// They used to be resolved from the row's data context, which meant the row
    /// had to point at the host — and that broke Close, which the shell resolves
    /// from the same context but needs to be the panel. One context cannot answer
    /// two questions; naming the host is how the view says which it means.
    /// </summary>
    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key is Key.Down or Key.Up && HistoryPopup.IsOpen)
        {
            var count = HistoryList.ItemCount;
            if (count > 0)
            {
                HistoryList.SelectedIndex = Math.Clamp(
                    HistoryList.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, count - 1);
                HistoryList.ScrollIntoView(HistoryList.SelectedItem!);
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            DismissHistory();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter)
        {
            if (HistoryPopup.IsOpen && HistoryList.SelectedItem is BrowserHistoryEntry entry)
            {
                RuntimeBrowser.AddressText = entry.Address;
            }
            DismissHistory();
        }
        AddressKeyDown?.Invoke(RuntimeBrowser, e);
    }

    private void OnAddressGotFocus(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is TextBox addressBox)
        {
            addressBox.PlaceholderText = null;
            _historyPanel?.ShowHistory(string.Empty);
        }
    }

    private void OnAddressLostFocus(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is TextBox addressBox)
        {
            addressBox.PlaceholderText = BlankAddressPlaceholder;
            _historyPanel?.CancelHistorySearch();
        }
    }

    private void OnAddressTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (AddressBox.IsFocused)
        {
            _historyPanel?.ShowHistory(AddressBox.Text ?? string.Empty);
        }
    }

    private void DismissHistory()
    {
        _historyPanel?.HideHistory();
        HistoryPopup.IsOpen = false;
        HistoryList.SelectedIndex = -1;
    }

    private void OnHistoryEntryClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is Control { DataContext: BrowserHistoryEntry entry })
        {
            RuntimeBrowser.AddressText = entry.Address;
            DismissHistory();
            AddressKeyDown?.Invoke(RuntimeBrowser, new KeyEventArgs { Key = Key.Enter });
        }
    }

    private void OnClearHistoryClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        DismissHistory();
        if (DataContext is BrowserRuntimePanelViewModel panel)
        {
            _ = panel.ClearHistoryAsync(CancellationToken.None);
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        BackRequested?.Invoke(RuntimeBrowser, e);
    }

    private void OnBrowserStateChanged(
        object? sender,
        BrowserStateChangedEventArgs e) =>
        BrowserStateChanged?.Invoke(sender, e);

    private void OnCloseClick(object? sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(sender, e);

    private void OnConnectionSelected(
        object? sender,
        PanelConnectionSelectedEventArgs e) =>
        ConnectionSelected?.Invoke(this, e);

    private void OnNewConnectionRequested(object? sender, RoutedEventArgs e) =>
        NewConnectionRequested?.Invoke(this, e);

    private void OnSplitRequested(object? sender, PanelSplitOrientation orientation) =>
        SplitRequested?.Invoke(sender, orientation);

    private void OnForwardClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        ForwardRequested?.Invoke(RuntimeBrowser, e);
    }

    private void OnDeveloperToolsClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        DeveloperToolsRequested?.Invoke(RuntimeBrowser, e);
    }

    private void OnOpenInSystemBrowserClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        OpenInSystemBrowserRequested?.Invoke(RuntimeBrowser, e);
    }

    private void OnReloadClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        ReloadRequested?.Invoke(RuntimeBrowser, e);
    }

    private void OnStopClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        StopRequested?.Invoke(RuntimeBrowser, e);
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        var findModifier = OperatingSystem.IsMacOS()
            ? KeyModifiers.Meta
            : KeyModifiers.Control;
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(findModifier))
        {
            OpenFind();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && RuntimeBrowser.IsFindVisible)
        {
            RuntimeBrowser.CloseFind();
            e.Handled = true;
        }
    }

    private void OnFindClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        OpenFind();
    }

    private void OpenFind()
    {
        RuntimeBrowser.OpenFind();
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = e;
        if (sender is TextBox textBox)
        {
            RuntimeBrowser.UpdateFind(textBox.Text);
        }
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        _ = sender;
        if (e.Key == Key.Enter)
        {
            RuntimeBrowser.FindNext(
                e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? BrowserFindDirection.Previous
                    : BrowserFindDirection.Next);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RuntimeBrowser.CloseFind();
            e.Handled = true;
        }
    }

    private void OnFindPreviousClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RuntimeBrowser.FindNext(BrowserFindDirection.Previous);
    }

    private void OnFindNextClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RuntimeBrowser.FindNext(BrowserFindDirection.Next);
    }

    private void OnCloseFindClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RuntimeBrowser.CloseFind();
    }

    private void OnDismissProductNoticeClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        RuntimeBrowser.DismissProductNotice();
    }

    private void OnProductActionClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = RuntimeBrowser.PerformProductActionAsync().AsTask();
    }
}
