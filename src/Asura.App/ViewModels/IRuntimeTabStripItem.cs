using FluentIcons.Common;

namespace Asura.App.ViewModels;

/// <summary>The compile-time view contract rendered by the runtime tab strip.</summary>
public interface IRuntimeTabStripItem
{
    string Title { get; }

    /// <summary>
    /// What the strip writes on the tab. The durable title unless the tab
    /// can name itself better — a browser tab nobody renamed reads its
    /// page's title or host rather than "Browser".
    /// </summary>
    string DisplayTitle => Title;

    string Icon { get; }

    Symbol IconSymbol { get; }

    bool IsActive { get; }

    bool CanClose { get; }

    bool HasAttention { get; }

    string AgentActivity { get; }

    bool HasAgentActivity { get; }
}
