using System.Xml.Linq;

namespace Asura.Architecture.Tests;

/// <summary>
/// Settings draws a boolean as macOS System Settings does: a switch at the
/// trailing edge of its row. A checkbox stays only where several options in
/// one list are checked together, the way a list of choices in a dialog is.
/// </summary>
public sealed class ToggleAffordanceContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>
    /// The checkboxes Settings keeps, by accessible name: each marks one item
    /// of a list in which any number may be checked at once.
    /// </summary>
    private static readonly string[] ListOptionCheckBoxes =
    [
        "{Binding Name, StringFormat=Offer {0} in this workspace}",
    ];

    private static bool IsSettingsSurface(string file) =>
        file.Contains(Path.Combine("Views", "SettingsPages"), StringComparison.Ordinal)
        || Path.GetFileName(file) is "SettingsView.axaml"
            or "WorkspaceEditorView.axaml"
            or "LocalArtifactControlView.axaml"
            or "RecoveryDataControlView.axaml"
            or "DiagnosticsExportView.axaml";

    [Fact]
    public void Settings_draw_booleans_as_switches_and_keep_checkboxes_for_option_lists()
    {
        var offenders = Directory
            .EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "Asura.App", "Views"),
                "*.axaml",
                SearchOption.AllDirectories)
            .Where(IsSettingsSurface)
            .SelectMany(file => XDocument.Load(file)
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "CheckBox", StringComparison.Ordinal))
                .Select(element => (
                    File: Path.GetRelativePath(RepositoryRoot, file),
                    Name: element.Attribute("AutomationProperties.Name")?.Value)))
            .Where(item => item.Name is null
                || !ListOptionCheckBoxes.Contains(item.Name, StringComparer.Ordinal))
            .Select(item => $"{item.File}: {item.Name ?? "<unnamed checkbox>"}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "These Settings controls are checkboxes. A setting that is on or off is a "
            + "ToggleSwitch at its row's trailing edge; a checkbox is only for one item "
            + "of a list whose items are checked together:"
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
