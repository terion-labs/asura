using System.Collections.Immutable;

namespace Asura.Core;

public sealed class CommandDefinition
{
    private readonly Func<CommandInvocation, bool> _availability;
    private readonly Func<IReadOnlyDictionary<string, string>, string?>? _invocationTitle;

    public CommandDefinition(
        CommandId id,
        string title,
        string category,
        CommandContext contexts,
        CommandParameterSchema? parameters = null,
        IEnumerable<CommandBinding>? defaultBindings = null,
        Func<CommandInvocation, bool>? availability = null,
        Func<IReadOnlyDictionary<string, string>, string?>? invocationTitle = null,
        (string State, string Title)? stateTitle = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        Id = id;
        Title = title;
        Category = category;
        Contexts = CommandContextRules.Require(contexts, nameof(contexts));
        Parameters = parameters ?? CommandParameterSchema.None;
        DefaultBindings = defaultBindings?.ToImmutableArray() ?? [];
        _availability = availability ?? AlwaysAvailable;
        _invocationTitle = invocationTitle;
        if (stateTitle is { } bound)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(bound.State, nameof(stateTitle));
            ArgumentException.ThrowIfNullOrWhiteSpace(bound.Title, nameof(stateTitle));
        }

        StateTitle = stateTitle;
    }

    public CommandId Id { get; }

    public string Title { get; }

    public string Category { get; }

    public CommandContext Contexts { get; }

    public CommandParameterSchema Parameters { get; }

    public ImmutableArray<CommandBinding> DefaultBindings { get; }

    /// <summary>
    /// A <see cref="CommandStates"/> fact and the title the command takes
    /// while it holds. A command that shows or hides something names the
    /// action it will take now: <see cref="Title"/> is "Show Sidebar", and
    /// this is "Hide Sidebar" while the sidebar is visible. Lists that don't
    /// know the state, such as Keyboard Shortcuts, show <see cref="Title"/>.
    /// </summary>
    public (string State, string Title)? StateTitle { get; }

    /// <summary>
    /// Names one invocation, so the rows of a parameterized command can be told
    /// apart: "Focus Panel Left" rather than four rows of "Focus Panel".
    /// </summary>
    public string TitleFor(IReadOnlyDictionary<string, string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Count == 0
            ? Title
            : _invocationTitle?.Invoke(arguments) ?? Title;
    }

    /// <summary>
    /// Names one invocation in the current state, as the menu bar does:
    /// "Hide Sidebar" while the sidebar is visible.
    /// </summary>
    public string TitleFor(
        IReadOnlyDictionary<string, string> arguments,
        CommandInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return StateTitle is { } bound && invocation.HasState(bound.State)
            ? bound.Title
            : TitleFor(arguments);
    }

    public bool IsAvailable(CommandInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var contextMatches = (Contexts & CommandContext.Global) != CommandContext.None || (Contexts & invocation.ActiveContexts) != CommandContext.None;

        return contextMatches
            && Parameters.Validate(invocation.Arguments).IsEmpty
            && _availability(invocation);
    }

    private static bool AlwaysAvailable(CommandInvocation _) => true;
}
