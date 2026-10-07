namespace Asura.Core;

public static class BuiltInCommands
{
    private static readonly Lazy<CommandRegistry> RegistryFactory = new(CreateRegistry);

    public static CommandId NewTab { get; } = new("tab.new");
    public static CommandId SplitPanel { get; } = new("panel.split");
    public static CommandId FocusPanel { get; } = new("panel.focus");
    public static CommandId TogglePanelZoom { get; } = new("panel.zoom.toggle");
    public static CommandId ClosePanel { get; } = new("panel.close");
    public static CommandId RenameTab { get; } = new("tab.rename");
    public static CommandId CloseTab { get; } = new("tab.close");
    public static CommandId MoveTabLeft { get; } = new("tab.move-left");
    public static CommandId MoveTabRight { get; } = new("tab.move-right");
    public static CommandId MoveTabToWorkspace { get; } = new("tab.move-to-workspace");
    public static CommandId MovePanelToWorkspace { get; } = new("panel.move-to-workspace");
    public static CommandId NextTab { get; } = new("tab.next");
    public static CommandId PreviousTab { get; } = new("tab.previous");
    public static CommandId LastTab { get; } = new("tab.last");
    public static CommandId SelectTab { get; } = new("tab.select-position");
    public static CommandId SelectWorkspace { get; } = new("workspace.select-position");
    public static CommandId EnterTerminalCopyMode { get; } = new("terminal.copy-mode");
    public static CommandId SendPrefix { get; } = new("terminal.send-prefix");
    public static CommandId FocusNextArea { get; } = new("window.focus-next-area");
    public static CommandId FocusPreviousArea { get; } = new("window.focus-previous-area");

    public static CommandId Copy { get; } = new("terminal.copy");
    public static CommandId Paste { get; } = new("terminal.paste");
    public static CommandId SelectAll { get; } = new("terminal.select-all");
    public static CommandId MoveWordLeft { get; } = new("terminal.word-left");
    public static CommandId MoveWordRight { get; } = new("terminal.word-right");
    public static CommandId DeleteWordBackward { get; } = new("terminal.delete-word-backward");
    public static CommandId DeleteWordForward { get; } = new("terminal.delete-word-forward");
    public static CommandId MoveToLineStart { get; } = new("terminal.line-start");
    public static CommandId MoveToLineEnd { get; } = new("terminal.line-end");
    public static CommandId Find { get; } = new("terminal.find");
    public static CommandId IncreaseFontSize { get; } = new("terminal.font-increase");
    public static CommandId DecreaseFontSize { get; } = new("terminal.font-decrease");
    public static CommandId ResetFontSize { get; } = new("terminal.font-reset");
    public static CommandId ClearScrollback { get; } = new("terminal.clear-scrollback");
    public static CommandId SendInterrupt { get; } = new("terminal.send-interrupt");
    public static CommandId SendEndOfFile { get; } = new("terminal.send-eof");
    public static CommandId ClearScreen { get; } = new("terminal.clear-screen");

    public static CommandRegistry Registry => RegistryFactory.Value;

    private static CommandRegistry CreateRegistry()
    {
        var orientationSchema = new CommandParameterSchema(
            [new CommandParameter("orientation", CommandParameterType.Choice, true, ["left-right", "top-bottom"])]);
        var directionSchema = new CommandParameterSchema(
            [new CommandParameter("direction", CommandParameterType.Choice, true, ["left", "right", "up", "down", "next"])]);
        var positionSchema = new CommandParameterSchema(
            [new CommandParameter("position", CommandParameterType.Integer, true)]);

        return new CommandRegistry(
        [
            Define(NewTab, "New Tab", "Tabs", CommandContext.Workspace),
            Define(
                SplitPanel,
                "Split Panel",
                "Panels",
                CommandContext.Panel,
                orientationSchema,
                arguments => arguments.GetValueOrDefault("orientation") switch
                {
                    "left-right" => "Split Panel Right",
                    "top-bottom" => "Split Panel Down",
                    _ => null,
                }),
            Define(
                FocusPanel,
                "Focus Panel",
                "Panels",
                CommandContext.Panel,
                directionSchema,
                arguments => arguments.GetValueOrDefault("direction") switch
                {
                    "left" => "Focus Panel Left",
                    "right" => "Focus Panel Right",
                    "up" => "Focus Panel Up",
                    "down" => "Focus Panel Down",
                    "next" => "Focus Next Panel",
                    _ => null,
                }),
            Define(TogglePanelZoom, "Toggle Panel Zoom", "Panels", CommandContext.Panel),
            Define(ClosePanel, "Close Panel", "Panels", CommandContext.Panel),
            Define(RenameTab, "Rename Tab", "Tabs", CommandContext.Tab),
            Define(CloseTab, "Close Tab", "Tabs", CommandContext.Tab),
            Define(MoveTabLeft, "Move Tab Left", "Tabs", CommandContext.Tab),
            Define(MoveTabRight, "Move Tab Right", "Tabs", CommandContext.Tab),
            Define(
                MoveTabToWorkspace,
                "Move Tab to Open Workspace",
                "Tabs",
                CommandContext.Tab,
                positionSchema,
                arguments => PositionTitle("Move Tab to Workspace", arguments)),
            Define(
                MovePanelToWorkspace,
                "Move Panel to Open Workspace",
                "Panels",
                CommandContext.Panel,
                positionSchema,
                arguments => PositionTitle("Move Panel to Workspace", arguments)),
            Define(NextTab, "Show Next Tab", "Tabs", CommandContext.Tab),
            Define(PreviousTab, "Show Previous Tab", "Tabs", CommandContext.Tab),
            Define(LastTab, "Show Last Active Tab", "Tabs", CommandContext.Tab),
            Define(
                SelectTab,
                "Select Tab by Position",
                "Tabs",
                CommandContext.Tab,
                positionSchema,
                arguments => PositionTitle("Select Tab", arguments)),
            Define(
                SelectWorkspace,
                "Go to Workspace by Position",
                "Workspaces",
                CommandContext.Window,
                positionSchema,
                arguments => PositionTitle("Go to Workspace", arguments)),
            Define(FocusNextArea, "Move Focus to Next Area", "Window", CommandContext.Window),
            Define(FocusPreviousArea, "Move Focus to Previous Area", "Window", CommandContext.Window),
            Define(EnterTerminalCopyMode, "Enter Terminal Copy Mode", "Terminal", CommandContext.Terminal),
            Define(SendPrefix, "Send Literal Prefix", "Terminal", CommandContext.Terminal),
            Define(Copy, "Copy", "Terminal", CommandContext.Terminal),
            Define(Paste, "Paste", "Terminal", CommandContext.Terminal),
            Define(SelectAll, "Select Visible Terminal Content", "Terminal", CommandContext.Terminal),
            Define(MoveWordLeft, "Move One Word Left", "Terminal", CommandContext.Terminal),
            Define(MoveWordRight, "Move One Word Right", "Terminal", CommandContext.Terminal),
            Define(DeleteWordBackward, "Delete Previous Word", "Terminal", CommandContext.Terminal),
            Define(DeleteWordForward, "Delete Next Word", "Terminal", CommandContext.Terminal),
            Define(MoveToLineStart, "Move to Line Start", "Terminal", CommandContext.Terminal),
            Define(MoveToLineEnd, "Move to Line End", "Terminal", CommandContext.Terminal),
            Define(Find, "Find", "Terminal", CommandContext.Terminal),
            Define(IncreaseFontSize, "Increase Font Size", "Terminal", CommandContext.Terminal),
            Define(DecreaseFontSize, "Decrease Font Size", "Terminal", CommandContext.Terminal),
            Define(ResetFontSize, "Reset Font Size", "Terminal", CommandContext.Terminal),
            Define(ClearScrollback, "Clear Scrollback", "Terminal", CommandContext.Terminal),
            Define(SendInterrupt, "Send Interrupt", "Terminal", CommandContext.Terminal),
            Define(SendEndOfFile, "Send End of File", "Terminal", CommandContext.Terminal),
            Define(ClearScreen, "Clear Screen", "Terminal", CommandContext.Terminal),
        ]);
    }

    private static CommandDefinition Define(
        CommandId id,
        string title,
        string category,
        CommandContext contexts,
        CommandParameterSchema? parameters = null,
        Func<IReadOnlyDictionary<string, string>, string?>? invocationTitle = null)
    {
        var defaults = BuiltInKeymaps.All
            .SelectMany(profile => profile.Bindings)
            .Where(binding => binding.CommandId == id)
            .ToArray();

        return new CommandDefinition(
            id,
            title,
            category,
            contexts,
            parameters,
            defaults,
            invocationTitle: invocationTitle);
    }

    // Positions are stored from zero, as the router reads them; people count
    // tabs and workspaces from one.
    private static string? PositionTitle(
        string title,
        IReadOnlyDictionary<string, string> arguments) =>
        int.TryParse(
            arguments.GetValueOrDefault("position"),
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var position)
            ? $"{title} {position + 1}"
            : null;
}
