using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// One tab in a tab strip, as assistive technology needs to hear it.
///
/// The strip drew its tabs as push buttons wearing an "active" class, so
/// VoiceOver read "Activate tab Browser, button" for every one of them: no
/// tab role, no selected state, no position, and the one that was open
/// sounded exactly like the others. This is still a button to the pointer and
/// to the theme, but it reports itself as a tab item that is selected or not,
/// and choosing it through the accessibility API activates it the way a click
/// does.
/// </summary>
internal sealed class RuntimeTabButton : Button
{
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<RuntimeTabButton, bool>(nameof(IsSelected));

    static RuntimeTabButton()
    {
        IsSelectedProperty.Changed.AddClassHandler<RuntimeTabButton>(
            (tab, change) => tab.OnIsSelectedChanged(change));
    }

    /// <summary>Whether this is the tab whose content is showing.</summary>
    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new RuntimeTabButtonAutomationPeer(this);

    private void OnIsSelectedChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (ControlAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(
                SelectionItemPatternIdentifiers.IsSelectedProperty,
                change.OldValue,
                change.NewValue);
        }
    }

    private void Select() => RaiseEvent(new RoutedEventArgs(ClickEvent));

    /// <summary>
    /// A tab item that can be selected and invoked. Selecting is the same as
    /// clicking: the strip has one gesture for "show this tab" and the
    /// accessibility API should not invent a second.
    /// </summary>
    private sealed class RuntimeTabButtonAutomationPeer(RuntimeTabButton owner)
        : ButtonAutomationPeer(owner), ISelectionItemProvider
    {
        private new RuntimeTabButton Owner => (RuntimeTabButton)base.Owner;

        public bool IsSelected => Owner.IsSelected;

        /// <summary>
        /// The tab list the tab belongs to, so assistive technology hears the
        /// selection as one choice among the strip's tabs.
        /// </summary>
        public ISelectionProvider? SelectionContainer =>
            Owner.FindAncestorOfType<RuntimeTabList>() is { } list
                ? CreatePeerForElement(list) as ISelectionProvider
                : null;

        public void AddToSelection() => Select();

        public void RemoveFromSelection()
        {
        }

        public void Select()
        {
            if (!Owner.IsSelected && Owner.IsEffectivelyEnabled)
            {
                Owner.Select();
            }
        }

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.TabItem;
    }
}

/// <summary>
/// The strip's tabs as one tab group. It answers which tab is selected, so the
/// selected state each <see cref="RuntimeTabButton"/> reports belongs to a
/// container rather than standing alone, as a tab view's selection does.
/// </summary>
internal sealed class RuntimeTabList : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new RuntimeTabListAutomationPeer(this);

    private sealed class RuntimeTabListAutomationPeer(RuntimeTabList owner)
        : ItemsControlAutomationPeer(owner), ISelectionProvider
    {
        public bool CanSelectMultiple => false;

        public bool IsSelectionRequired => true;

        public IReadOnlyList<AutomationPeer> GetSelection() =>
            [.. Owner.GetVisualDescendants()
                .OfType<RuntimeTabButton>()
                .Where(tab => tab.IsSelected)
                .Select(CreatePeerForElement)];

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Tab;
    }
}
