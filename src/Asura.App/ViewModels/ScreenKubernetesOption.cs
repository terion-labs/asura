using Asura.Core;

namespace Asura.App.ViewModels;

public sealed record ScreenKubernetesOption(KubernetesConnectionProfileId Id, string Name, bool IsAvailable)
{
    public string DisplayName => IsAvailable ? Name : $"Missing or disabled · {Name}";

    internal static IReadOnlyList<ScreenKubernetesOption> Build(
        IEnumerable<KubernetesConnectionProfile> profiles,
        IEnumerable<ScreenPanelDefinition> panels)
    {
        var options = profiles.Select(profile => new ScreenKubernetesOption(profile.Id, profile.Name, profile.IsEnabled))
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in panels.Select(panel => panel.KubernetesTarget?.ProfileId).OfType<KubernetesConnectionProfileId>().Distinct())
        {
            if (options.All(option => option.Id != id))
            {
                options.Add(new(id, id.Value, false));
            }
        }
        return options.AsReadOnly();
    }
}
