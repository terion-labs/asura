using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class AboutAsuraTests
{
    [Theory]
    [InlineData("1.2.3.0", "1.2.3+4f2a9c1d0e", "Version 1.2.3 (4f2a9c1)")]
    [InlineData("1.2.3.0", "1.2.3", "Version 1.2.3")]
    [InlineData("1.2.3.45", null, "Version 1.2.3 (45)")]
    public void Version_reads_as_the_standard_about_panel_writes_it(
        string version,
        string? informational,
        string expected)
    {
        Assert.Equal(
            expected,
            MainWindowViewModel.FormatApplicationVersion(Version.Parse(version), informational));
    }

    [Fact]
    public async Task About_shows_identity_not_diagnostics_and_sizes_to_its_content()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var window = new AboutWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.Equal(SizeToContent.Height, window.SizeToContent);
                Assert.Empty(window.GetVisualDescendants().OfType<ScrollViewer>());
                var about = window.GetVisualDescendants().OfType<AboutAsuraView>().Single();
                Assert.False(about.FindControl<StackPanel>("ProductComponentList")!.IsVisible);
                Assert.NotNull(about.FindControl<Image>("ApplicationIcon")!.Source);
                var text = window.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Select(block => block.Text)
                    .OfType<string>()
                    .ToArray();
                foreach (var diagnostic in new[] { "Bundle identifier", "Executable", "Runtime", "Agent runtime", "Product" })
                {
                    Assert.DoesNotContain(diagnostic, text, StringComparer.Ordinal);
                }

                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }

    [Fact]
    public async Task The_bundled_icon_loads_on_every_host()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            using var icon = MacOsApplicationIconImage.Bundled();
            Assert.NotNull(icon);
            return true;
        }, timeout.Token));
    }
}
