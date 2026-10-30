namespace Asura.App.ViewModels;

/// <summary>
/// The Settings pages as the sidebar lists them, each with the one name its
/// sidebar item, its heading and the Settings window's title all use.
/// </summary>
public static class SettingsPanes
{
    /// <summary>The pages in sidebar order. MCP is a section of the AI page.</summary>
    public static IReadOnlyList<SettingsPage> Sidebar { get; } =
    [
        SettingsPage.Appearance,
        SettingsPage.Workspaces,
        SettingsPage.Connections,
        SettingsPage.Networking,
        SettingsPage.Keybindings,
        SettingsPage.Files,
        SettingsPage.Browser,
        SettingsPage.Terminal,
        SettingsPage.QuickTerminal,
        SettingsPage.Git,
        SettingsPage.Notifications,
        SettingsPage.Secrets,
        SettingsPage.Diagnostics,
        SettingsPage.Agent,
        SettingsPage.About,
    ];

    public static string Title(SettingsPage page) => page switch
    {
        SettingsPage.Appearance => "Appearance",
        SettingsPage.Workspaces => "Workspaces & Screens",
        SettingsPage.Connections => "Connections",
        SettingsPage.Networking => "Networking",
        SettingsPage.Keybindings => "Keyboard Shortcuts",
        SettingsPage.Files => "Files & Transfers",
        SettingsPage.Browser => "Browser",
        SettingsPage.Terminal => "Terminal",
        SettingsPage.QuickTerminal => "Quick Terminal",
        SettingsPage.Git => "Git",
        SettingsPage.Notifications => "Notifications",
        SettingsPage.Secrets => "Security & Secrets",
        SettingsPage.Diagnostics => "Data & Diagnostics",
        SettingsPage.Agent or SettingsPage.Mcp => "AI",
        SettingsPage.About => "About",
        _ => "Settings",
    };

    /// <summary>The sidebar page that shows <paramref name="page"/>.</summary>
    public static SettingsPage SidebarPage(SettingsPage page) =>
        page == SettingsPage.Mcp ? SettingsPage.Agent : page;

    /// <summary>A page stored by name, or null for a name this build doesn't know.</summary>
    public static SettingsPage? Parse(string? name) =>
        Enum.TryParse<SettingsPage>(name, ignoreCase: false, out var page)
        && Enum.IsDefined(page)
            ? page
            : null;
}

/// <summary>
/// What the Settings search field matches: each page's name, the words people
/// use for what is on it, and the text of its rows. A query matches when every
/// word of it appears in one entry, so "dark mode" finds Appearance and "ssh
/// tmux" finds the multiplexing row without matching every page that says
/// "SSH" somewhere.
/// </summary>
public sealed class SettingsSearchIndex
{
    private static readonly IReadOnlyDictionary<SettingsPage, string> Keywords =
        new Dictionary<SettingsPage, string>
        {
            [SettingsPage.Appearance] =
                "theme dark light mode color colour accent text size font scale density corners translucency transparency glass tab bar sidebar palette contrast",
            [SettingsPage.Workspaces] =
                "workspace screen saved screen layout restore sessions start isolation container import export definition",
            [SettingsPage.Connections] =
                "connection connections saved host server ssh local docker wsl kubernetes cluster database sql file provider sftp s3 credential",
            [SettingsPage.Networking] = "network proxy vpn socks tunnel route kill switch connection",
            [SettingsPage.Keybindings] =
                "keybinding keybindings keyboard shortcut shortcuts hotkey key binding prefix tmux record",
            [SettingsPage.Files] = "file files transfer preview cache download remote",
            [SettingsPage.Browser] = "browser web profile cookies http authentication private",
            [SettingsPage.Terminal] =
                "terminal shell cursor scrollback clipboard bell ime multiplex tmux screen ssh links keymap close running sessions",
            [SettingsPage.QuickTerminal] = "quick terminal hotkey drop-down dropdown escape display monitor",
            [SettingsPage.Git] = "git executable commit subject spelling dictionary diff whitespace fetch custom commands",
            [SettingsPage.Notifications] = "notification notifications alert alerts banner badge permission",
            [SettingsPage.Secrets] =
                "security secret secrets credential credentials password keychain encryption pin lock history privacy",
            [SettingsPage.Diagnostics] = "data diagnostics export support recovery onboarding storage logs",
            [SettingsPage.Agent] = "ai agent model provider prompt mcp server tools llm compaction permissions",
            [SettingsPage.About] = "about version update updates license licenses acknowledgments",
        };

    private readonly List<(SettingsPage Page, string Text)> _entries = [];

    public SettingsSearchIndex()
    {
        foreach (var page in SettingsPanes.Sidebar)
        {
            Add(page, SettingsPanes.Title(page));
            if (Keywords.TryGetValue(page, out var keywords))
            {
                Add(page, keywords);
            }
        }
    }

    /// <summary>Adds the text of something on <paramref name="page"/>: a row, a heading.</summary>
    public void Add(SettingsPage page, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _entries.Add((SettingsPanes.SidebarPage(page), text));
        }
    }

    /// <summary>The pages with something matching <paramref name="query"/>, in sidebar order.</summary>
    public IReadOnlyList<SettingsPage> Match(string? query)
    {
        var tokens = Tokens(query);
        if (tokens.Length == 0)
        {
            return SettingsPanes.Sidebar;
        }

        var matched = _entries
            .Where(entry => Contains(entry.Text, tokens))
            .Select(entry => entry.Page)
            .ToHashSet();
        return [.. SettingsPanes.Sidebar.Where(matched.Contains)];
    }

    /// <summary>
    /// Whether the page itself answers the query — its name or its keywords —
    /// in which case every row on it stays in view.
    /// </summary>
    public static bool MatchesPage(SettingsPage page, string? query)
    {
        var tokens = Tokens(query);
        if (tokens.Length == 0)
        {
            return true;
        }

        page = SettingsPanes.SidebarPage(page);
        return Contains(SettingsPanes.Title(page), tokens)
            || (Keywords.TryGetValue(page, out var keywords) && Contains(keywords, tokens));
    }

    /// <summary>Whether one piece of text holds every word of the query.</summary>
    public static bool Matches(string? text, string? query)
    {
        var tokens = Tokens(query);
        return tokens.Length == 0 || (text is not null && Contains(text, tokens));
    }

    private static string[] Tokens(string? query) =>
        string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Contains(string text, string[] tokens) =>
        tokens.All(token => text.Contains(token, StringComparison.CurrentCultureIgnoreCase));
}
