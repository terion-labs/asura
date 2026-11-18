using Asura.App.ViewModels;
using Asura.Core;

namespace Asura.App.Views;

/// <summary>What to open from the Add Panel menu, and the edge it goes against.</summary>
public sealed record AddPanelRequest(AddPanelChoice Choice, PanelSide Side);

/// <summary>One thing the Add Panel menu offers to open.</summary>
public abstract record AddPanelChoice
{
    private AddPanelChoice()
    {
    }

    /// <summary>An empty panel that asks what to open in it.</summary>
    public sealed record Launcher : AddPanelChoice;

    /// <summary>A panel of one type, opened as the launcher would open it.</summary>
    public sealed record Panel(PanelKind Kind) : AddPanelChoice;

    /// <summary>A saved connection, opened its usual way.</summary>
    public sealed record Connection(SavedConnectionLaunchViewModel Launch) : AddPanelChoice;
}
