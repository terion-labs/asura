using Asura.App.Controls;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A path on this computer is typed or chosen in the Open panel. The panel
/// starts where the entered path points, and either way the path reaches the
/// field's binding.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class PathFieldTests
{
    [Fact]
    public void The_open_panel_starts_where_the_entered_path_points()
    {
        var folder = Directory.CreateTempSubdirectory("asura-path-field-");
        try
        {
            var file = Path.Combine(folder.FullName, "config");
            Assert.Equal(folder.FullName, PathField.StartFolder(file, PathFieldKind.File));
            Assert.Equal(folder.FullName, PathField.StartFolder(folder.FullName, PathFieldKind.Folder));
            Assert.Equal(folder.FullName, PathField.StartFolder($"  {file}  ", PathFieldKind.File));
            Assert.Null(PathField.StartFolder(Path.Combine(folder.FullName, "missing", "config"), PathFieldKind.File));
            Assert.Null(PathField.StartFolder("relative/config", PathFieldKind.File));
            Assert.Null(PathField.StartFolder(" ", PathFieldKind.File));
            Assert.Null(PathField.StartFolder(null, PathFieldKind.Folder));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_leading_tilde_is_the_home_folder()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(home, PathField.StartFolder("~", PathFieldKind.Folder));
        Assert.Equal(home, PathField.StartFolder("~/not-a-real-asura-file", PathFieldKind.File));
    }

    [Fact]
    public Task Typing_and_setting_the_path_meet_in_one_value_and_both_controls_are_named() =>
        RunAsync(() =>
        {
            var field = new PathField { Path = "~/.kube/config" };
            AutomationProperties.SetName(field, "Kubeconfig file");
            var window = new Window { Content = field };
            try
            {
                window.Show();
                window.UpdateLayout();
                var input = field.GetVisualDescendants().OfType<TextBox>().Single();
                var choose = field.GetVisualDescendants().OfType<Button>().Single();

                Assert.Equal("~/.kube/config", input.Text);
                input.Text = "/etc/kubeconfig";
                Assert.Equal("/etc/kubeconfig", field.Path);
                Assert.Equal("Kubeconfig file", AutomationProperties.GetName(input));
                Assert.Equal("Choose Kubeconfig file", AutomationProperties.GetName(choose));
                Assert.Equal("Choose…", choose.Content);
                Assert.Equal(
                    Assert.IsType<double>(window.FindResource("ShellSpaceSm")),
                    field.GetVisualDescendants().OfType<Grid>().First().ColumnSpacing);
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task RunAsync(Action assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(
            () =>
            {
                assertion();
                return true;
            },
            timeout.Token));
    }
}
