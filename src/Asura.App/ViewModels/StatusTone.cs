namespace Asura.App.ViewModels;

/// <summary>
/// What a status mark means, independent of how it is drawn. A view model
/// says which; the appearance tokens say what colour that is in the current
/// theme, contrast setting, and platform profile.
/// </summary>
public enum StatusTone
{
    /// <summary>Nothing to report: idle, stopped, waiting, not yet run.</summary>
    Neutral,

    /// <summary>Running, connected, passed.</summary>
    Success,

    /// <summary>Degraded, paused, stale, or in progress toward a result.</summary>
    Warning,

    /// <summary>Failed or unavailable.</summary>
    Danger,
}
