using System.Xml.Linq;

namespace Asura.Architecture.Tests;

/// <summary>
/// The HIG reserves a switch for a prominent setting in the body of a window
/// and gives lists, groups of options, and minor options a checkbox. Settings
/// keeps a switch only for the few settings that turn a whole feature on or
/// off; everything else there is a checkbox.
/// </summary>
public sealed class ToggleAffordanceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// The prominent, top-level settings that keep a switch, by accessible name.
    /// Each turns a feature on or off rather than tuning one.
    /// </summary>
    private static readonly string[] ProminentSettingSwitches =
    [
        "Restore sessions on start",
        "Multiplex SSH terminals",
        "Enable app encryption",
        "Terminal notifications",
        "Agent run notifications",
        "File transfer notifications",
        "Enable Asura MCP server",
        "Use an application network connection",
    ];

    private static bool IsSettingsSurface(string file) =>
        file.Contains(Path.Combine("Views", "SettingsPages"), StringComparison.Ordinal)
        || Path.GetFileName(file) is "SettingsView.axaml";

    [Fact]
    public void Settings_keep_switches_for_prominent_settings_and_checkboxes_for_minor_options()
    {
        var offenders = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "Asura.App", "Views"),
                "*.axaml",
                SearchOption.AllDirectories)
            .Where(IsSettingsSurface)
            .SelectMany(file => XDocument.Load(file)
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ToggleSwitch", StringComparison.Ordinal))
                .Select(element => (
                    File: Path.GetRelativePath(RepositoryRoot, file),
                    Name: element.Attribute("AutomationProperties.Name")?.Value)))
            .Where(item => item.Name is null
                || !ProminentSettingSwitches.Contains(item.Name, StringComparer.Ordinal))
            .Select(item => $"{item.File}: {item.Name ?? "<unnamed switch>"}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These Settings rows use a switch for a minor option. Use a CheckBox, or "
            + "add the setting to the prominent list with the reason:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The On/Off labels are cleared once in the style sheet. Restating them per
    /// instance is how one switch ended up showing its state twice — once in the
    /// row's own status text and again beside the switch.
    /// </summary>
    [Fact]
    public void Switches_do_not_restate_their_state_labels_per_instance()
    {
        var offenders = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "Asura.App", "Views"),
                "*.axaml",
                SearchOption.AllDirectories)
            .Where(file => XDocument.Load(file)
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "ToggleSwitch", StringComparison.Ordinal))
                .Any(element =>
                    element.Attribute("OnContent") is not null
                    || element.Attribute("OffContent") is not null))
            .Select(file => Path.GetRelativePath(RepositoryRoot, file))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These views set OnContent/OffContent on a ToggleSwitch. The style sheet "
            + "already clears them:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Asura.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the Asura repository root.");
    }
}
