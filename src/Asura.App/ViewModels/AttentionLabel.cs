using System.Globalization;

namespace Asura.App.ViewModels;

/// <summary>
/// Names a control together with the attention mark drawn on it.
///
/// The mark is a dot, and a dot has nothing to say to a screen reader: the
/// control it sits on was announced as "Workspaces" or "Restore Terminal"
/// whether or not anything inside was asking for you. The state goes into the
/// host's own name instead, where it is heard with the thing it is about.
/// </summary>
public static class AttentionLabel
{
    public const string NeedsAttention = "needs attention";

    /// <summary>"Restore Terminal" or "Restore Terminal, needs attention".</summary>
    public static string Compose(string name, bool hasAttention, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var parts = new List<string>(3) { name };
        if (hasAttention)
        {
            parts.Add(NeedsAttention);
        }

        if (!string.IsNullOrWhiteSpace(detail))
        {
            parts.Add(detail.Trim());
        }

        return string.Join(", ", parts);
    }

    /// <summary>"Workspaces", "Workspaces, 1 needs attention", "Workspaces, 3 need attention".</summary>
    public static string Count(string name, int attentionCount)
    {
        ArgumentNullException.ThrowIfNull(name);
        return attentionCount switch
        {
            <= 0 => name,
            1 => $"{name}, 1 needs attention",
            _ => string.Create(
                CultureInfo.CurrentCulture,
                $"{name}, {attentionCount} need attention"),
        };
    }
}
