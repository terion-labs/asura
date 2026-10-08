using Asura.Core;

namespace Asura.Infrastructure;

/// <summary>
/// Names saved items in messages the way people know them: by the glossary
/// noun for their kind and the name they chose, such as the screen “Ops”.
/// Internal kinds and IDs never reach the text.
/// </summary>
internal static class DefinitionText
{
    /// <summary>The noun for a kind, such as "screen" or "AI provider".</summary>
    public static string Noun(DefinitionKind kind) => Entry(kind).Noun;

    /// <summary>The noun with its indefinite article, such as "a screen" or "an AI provider".</summary>
    public static string WithArticle(DefinitionKind kind)
    {
        var (article, noun) = Entry(kind);
        return $"{article} {noun}";
    }

    /// <summary>
    /// The item by kind and name, such as "the screen “Ops”", or "a screen"
    /// when it has no name.
    /// </summary>
    public static string Named(DefinitionKind kind, string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? WithArticle(kind)
            : $"the {Noun(kind)} {Quoted(name)}";

    public static string Named(IDurableDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Named(definition.Key.Kind, definition.Name);
    }

    /// <summary><see cref="Named(IDurableDefinition)"/> at the start of a sentence.</summary>
    public static string NamedAtStart(IDurableDefinition definition) => Capitalize(Named(definition));

    public static string NamedAtStart(DefinitionKind kind, string? name) => Capitalize(Named(kind, name));

    public static string Quoted(string name) => $"“{name}”";

    private static string Capitalize(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static (string Article, string Noun) Entry(DefinitionKind kind) => kind.Value switch
    {
        "connection" => ("a", "connection"),
        "layout" => ("a", "layout"),
        "screen" => ("a", "screen"),
        "workspace" => ("a", "workspace"),
        "theme" => ("a", "theme"),
        "terminal-profile" => ("a", "terminal profile"),
        "keymap" => ("a", "keymap"),
        "file-provider-profile" => ("a", "file provider"),
        "ai-provider-profile" => ("an", "AI provider"),
        "mcp-server-profile" => ("an", "MCP server"),
        "browser-profile" => ("a", "browser profile"),
        "kubernetes-connection" => ("a", "Kubernetes connection"),
        "database-connection" => ("a", "database connection"),
        "quick-terminal-settings" => ("a", "Quick Terminal setting"),
        "network-connection" => ("a", "network connection"),
        "application-network-settings" => ("a", "network setting"),
        _ => ("an", "item"),
    };
}
