using Asura.Core;
using Avalonia;
using Avalonia.Controls;

namespace Asura.App.Views.Components;

/// <summary>
/// Names the registry command a context-menu item written in markup runs.
/// The item takes the registry's title, as its menu-bar copy does, and
/// carries the command in its Tag, so one click handler in the view runs
/// every item and a test can find each item's menu-bar path.
/// </summary>
public static class PanelMenuCommand
{
    public static readonly AttachedProperty<CommandId> CommandProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, CommandId>("Command", typeof(PanelMenuCommand));

    static PanelMenuCommand()
    {
        CommandProperty.Changed.AddClassHandler<MenuItem>((item, e) =>
        {
            if (e.NewValue is CommandId { Value: not null } command)
            {
                PanelCommandTargets.Bind(item, command);
            }
        });
    }

    public static CommandId GetCommand(MenuItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.GetValue(CommandProperty);
    }

    public static void SetCommand(MenuItem item, CommandId value)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.SetValue(CommandProperty, value);
    }
}
