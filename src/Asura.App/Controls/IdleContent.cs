using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Controls;

/// <summary>
/// Builds its content only once the window has nothing more urgent to do, so a
/// heavy view does not hold up the frame that first shows its surroundings. An
/// empty cell is the case it was made for: the cell's chrome appears with the
/// layout change that placed it, and the launcher inside it, several hundred
/// controls, follows once that frame is drawn instead of delaying it, and
/// once the cell has stopped growing into its place.
///
/// What it builds is built once for its data context. Dock rebuilds a cell's
/// whole view when the split around it changes shape, as when a cell is placed
/// beside it; the new view takes over the content the old one built rather
/// than showing the cell empty until it builds its own.
/// </summary>
public sealed class IdleContent : Decorator
{
    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<IdleContent, IDataTemplate?>(nameof(ContentTemplate));

    /// <summary>
    /// Set on a container while the cells in it move. Content inside keeps the
    /// size it had and takes its new size once this clears, so it is laid out
    /// once, after the motion, rather than on every frame of it.
    /// </summary>
    public static readonly AttachedProperty<bool> HoldStillProperty =
        AvaloniaProperty.RegisterAttached<IdleContent, Control, bool>("HoldStill", inherits: true);

    private static readonly TimeSpan SettleCheck = TimeSpan.FromMilliseconds(40);

    /// <summary>Who holds the content built for each data context.</summary>
    private static readonly ConditionalWeakTable<object, Keeper> Holdings = [];

    private bool _isBuildQueued;
    private Size _lastSize = new(double.NaN, double.NaN);
    private object? _builtFor;
    private Size? _restingConstraint;
    private Size? _restingSize;

    static IdleContent()
    {
        AffectsMeasure<IdleContent>(HoldStillProperty);
        AffectsArrange<IdleContent>(HoldStillProperty);
    }

    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    public static bool GetHoldStill(Control control) => control.GetValue(HoldStillProperty);

    public static void SetHoldStill(Control control, bool value) => control.SetValue(HoldStillProperty, value);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Child is not null || TakeOver() || _isBuildQueued)
        {
            return;
        }

        // Background runs after layout and render jobs, so the frame that placed
        // this control is drawn before its content is built.
        _isBuildQueued = true;
        Dispatcher.UIThread.Post(Build, DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HandOver();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is { } child && GetHoldStill(this) && _restingConstraint is { } resting)
        {
            child.Measure(resting);
            return child.DesiredSize;
        }

        _restingConstraint = availableSize;
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is { } child && GetHoldStill(this) && _restingSize is { } resting)
        {
            child.Arrange(new Rect(resting));
            return finalSize;
        }

        _restingSize = finalSize;
        return base.ArrangeOverride(finalSize);
    }

    private void Build()
    {
        if (Child is not null || VisualRoot is null || ContentTemplate is not { } template)
        {
            _isBuildQueued = false;
            return;
        }

        // A cell growing into place changes size every frame; building in the
        // gap between two of them would stall the growth. Wait for a size that
        // holds from one check to the next.
        if (Bounds.Size != _lastSize)
        {
            _lastSize = Bounds.Size;
            DispatcherTimer.RunOnce(
                () => Dispatcher.UIThread.Post(Build, DispatcherPriority.Background),
                SettleCheck);
            return;
        }

        _isBuildQueued = false;
        Child = template.Build(DataContext);
        if (DataContext is { } context)
        {
            _builtFor = context;
            var holding = Holdings.GetOrCreateValue(context);
            holding.Holder = this;
            holding.Successor = null;
        }
    }

    /// <summary>
    /// Takes over what an earlier view built for the same data context. When
    /// that view is still on screen, as Dock may show the new view before it
    /// removes the old, the content comes over as the old view leaves.
    /// </summary>
    private bool TakeOver()
    {
        if (DataContext is not { } context
            || !Holdings.TryGetValue(context, out var holding)
            || holding.Holder is not { Child: not null } holder
            || ReferenceEquals(holder, this))
        {
            return false;
        }

        if (holder.IsAttachedToVisualTree())
        {
            holding.Successor = this;
            return false;
        }

        Adopt(holder, holding);
        return true;
    }

    private void HandOver()
    {
        if (_builtFor is { } context
            && Holdings.TryGetValue(context, out var holding)
            && ReferenceEquals(holding.Holder, this)
            && holding.Successor is { Child: null } successor
            && successor.IsAttachedToVisualTree()
            && ReferenceEquals(successor.DataContext, context))
        {
            successor.Adopt(this, holding);
        }
    }

    private void Adopt(IdleContent previous, Keeper holding)
    {
        var child = previous.Child;
        previous.Child = null;
        _builtFor = previous._builtFor;
        previous._builtFor = null;
        // Taken over mid-motion, the content keeps the size it had there.
        _restingConstraint = previous._restingConstraint;
        _restingSize = previous._restingSize;
        holding.Holder = this;
        holding.Successor = null;
        Child = child;
    }

    private sealed class Keeper
    {
        public IdleContent? Holder { get; set; }

        public IdleContent? Successor { get; set; }
    }
}
