using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using FluentIcons.Common;

namespace Asura.App.Controls;

internal sealed partial class PanelChrome
{
    public static readonly RoutedEvent<RoutedEventArgs> CollapseRequestedEvent =
        RoutedEvent.Register<PanelChrome, RoutedEventArgs>(nameof(CollapseRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> ExpandRequestedEvent =
        RoutedEvent.Register<PanelChrome, RoutedEventArgs>(nameof(ExpandRequested), RoutingStrategies.Bubble);

    public static readonly DirectProperty<PanelChrome, string> ExpansionLabelProperty =
        AvaloniaProperty.RegisterDirect<PanelChrome, string>(nameof(ExpansionLabel), chrome => chrome.ExpansionLabel);

    public static readonly DirectProperty<PanelChrome, Symbol> ExpansionIconProperty =
        AvaloniaProperty.RegisterDirect<PanelChrome, Symbol>(nameof(ExpansionIcon), chrome => chrome.ExpansionIcon);

    private Button? _collapse;
    private Button? _expand;

    public string ExpansionLabel => IsZoomed ? "Restore all panels" : "Expand this panel";

    public Symbol ExpansionIcon => IsZoomed ? Symbol.ArrowMinimize : Symbol.ArrowExpand;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsZoomedProperty)
        {
            RaisePropertyChanged(ExpansionLabelProperty,
                change.GetOldValue<bool>() ? "Restore all panels" : "Expand this panel", ExpansionLabel);
            RaisePropertyChanged(ExpansionIconProperty,
                change.GetOldValue<bool>() ? Symbol.ArrowMinimize : Symbol.ArrowExpand, ExpansionIcon);
        }
    }

    public event EventHandler<RoutedEventArgs>? CollapseRequested
    {
        add => AddHandler(CollapseRequestedEvent, value);
        remove => RemoveHandler(CollapseRequestedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? ExpandRequested
    {
        add => AddHandler(ExpandRequestedEvent, value);
        remove => RemoveHandler(ExpandRequestedEvent, value);
    }

    private void AttachCollapseActions(TemplateAppliedEventArgs e)
    {
        Detach(_collapse, OnCollapseClick);
        Detach(_expand, OnExpandClick);
        _collapse = e.NameScope.Find<Button>("PART_Collapse");
        _expand = e.NameScope.Find<Button>("PART_Expand");
        Attach(_collapse, OnCollapseClick);
        Attach(_expand, OnExpandClick);
    }

    private void OnCollapseClick(object? sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(CollapseRequestedEvent, this));

    private void OnExpandClick(object? sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(ExpandRequestedEvent, this));
}
