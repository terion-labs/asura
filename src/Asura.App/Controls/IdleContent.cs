using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;

namespace Asura.App.Controls;

/// <summary>
/// Builds its content only once the window has nothing more urgent to do, so a
/// heavy view does not hold up the frame that first shows its surroundings. An
/// empty cell is the case it was made for: the cell's chrome appears with the
/// layout change that placed it, and the launcher inside it, several hundred
/// controls, follows once that frame is drawn instead of delaying it.
/// </summary>
public sealed class IdleContent : Decorator
{
    public static readonly StyledProperty<IDataTemplate?> ContentTemplateProperty =
        AvaloniaProperty.Register<IdleContent, IDataTemplate?>(nameof(ContentTemplate));

    private bool _isBuildQueued;

    public IDataTemplate? ContentTemplate
    {
        get => GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (Child is not null || _isBuildQueued)
        {
            return;
        }

        // Background runs after layout and render jobs, so the frame that placed
        // this control is drawn before its content is built.
        _isBuildQueued = true;
        Dispatcher.UIThread.Post(Build, DispatcherPriority.Background);
    }

    private void Build()
    {
        _isBuildQueued = false;
        if (Child is null && VisualRoot is not null && ContentTemplate is { } template)
        {
            Child = template.Build(DataContext);
        }
    }
}
