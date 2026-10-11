using System.ComponentModel;
using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Asura.App.Views.Components;

/// <summary>
/// Presents the active document's typed runtime-panel context without a
/// reflection binding or view locator at the third-party model boundary.
/// </summary>
public sealed class RuntimePanelContentControl : ContentControl
{
    private INotifyPropertyChanged? _observedDock;
    private INotifyPropertyChanged? _observedDocument;
    private RuntimePanelViewModel? _observedPanel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        ObserveDock();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveDock();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _observedDock?.PropertyChanged -= OnDockPropertyChanged;
        _observedDocument?.PropertyChanged -= OnDocumentPropertyChanged;
        _observedPanel?.PropertyChanged -= OnPanelPropertyChanged;
        _observedDock = null;
        _observedDocument = null;
        _observedPanel = null;
    }

    private void ObserveDock()
    {
        _observedDock?.PropertyChanged -= OnDockPropertyChanged;
        _observedDock = DataContext is IDocumentDock
            ? DataContext as INotifyPropertyChanged
            : null;
        _observedDock?.PropertyChanged += OnDockPropertyChanged;

        ObserveActiveDocument();
    }

    private void OnDockPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.PropertyName is null or nameof(IDock.ActiveDockable))
        {
            ObserveActiveDocument();
        }
    }

    private void ObserveActiveDocument()
    {
        _observedDocument?.PropertyChanged -= OnDocumentPropertyChanged;

        _observedDocument = (DataContext as IDocumentDock)?.ActiveDockable as INotifyPropertyChanged;
        _observedDocument?.PropertyChanged += OnDocumentPropertyChanged;

        PublishContext();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        _ = sender;
        if (eventArgs.PropertyName is null or nameof(IDocument.Context))
        {
            PublishContext();
        }
    }

    private void PublishContext()
    {
        // A runtime panel hides while it is collapsed. Any other document
        // context — a Layout Designer slot — is shown as it is: dropping
        // everything that wasn't a runtime panel left the designer's canvas
        // empty.
        var context = DataContext as RuntimePanelViewModel
            ?? (DataContext as IDocumentDock)?.ActiveDockable?.Context;
        var panel = context as RuntimePanelViewModel;
        if (_observedPanel != panel)
        {
            _observedPanel?.PropertyChanged -= OnPanelPropertyChanged;
            _observedPanel = panel;
            _observedPanel?.PropertyChanged += OnPanelPropertyChanged;
        }

        Content = panel is { IsCollapsed: true } ? null : context;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RuntimePanelViewModel.IsCollapsed))
        {
            PublishContext();
        }
    }
}
