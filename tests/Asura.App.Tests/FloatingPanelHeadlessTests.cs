using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Model.Inpc.Controls;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class FloatingPanelHeadlessTests
{
    [Theory]
    [InlineData("PART_StatusDragHandle")]
    [InlineData("PART_FooterDragHandle")]
    public async Task Passive_status_regions_move_a_floating_panel(string handleName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(() =>
        {
            var panel = new UnavailableRuntimePanelViewModel(
                PanelInstanceId.New(), PanelKind.Browser, "Browser", "LOCAL", "Test");
            var floating = new FloatingRuntimePanelViewModel(panel, new Document(), 0);
            var layer = new FloatingPanelLayer { ItemsSource = new[] { floating } };
            var editor = new TextBox { Text = "about:blank" };
            var chrome = new PanelChrome
            {
                DataContext = panel,
                Title = "Browser",
                Status = new Border { Width = 8, Height = 8, Background = Avalonia.Media.Brushes.Green },
                HeaderContent = editor,
                Footer = new TextBlock { [!TextBlock.TextProperty] = new Binding(nameof(panel.Title)) },
            };
            var window = new Window { Width = 1200, Height = 800, Content = layer };
            window.DataTemplates.Add(new FuncDataTemplate<UnavailableRuntimePanelViewModel>((_, _) => chrome));
            window.Show();
            try
            {
                window.UpdateLayout();
                var handle = Assert.Single(chrome.GetVisualDescendants().OfType<PanelDockHandle>(),
                    item => item.Name == handleName);
                Assert.True(handle.IsEffectivelyVisible);
                Assert.True(handle.Bounds.Height >= 24);
                Assert.Equal("Browser", Assert.IsType<TextBlock>(chrome.Footer).Text);
                var start = Assert.NotNull(handle.TranslatePoint(
                    new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2), window));
                var before = new Point(floating.X, floating.Y);
                var delta = new Vector(100, 60);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(start + delta, RawInputModifiers.LeftMouseButton);
                window.MouseUp(start + delta, MouseButton.Left);
                window.UpdateLayout();
                Assert.Equal(before.X + delta.X, floating.X);
                Assert.Equal(before.Y + delta.Y, floating.Y);

                var editorPoint = Assert.NotNull(editor.TranslatePoint(new Point(20, 10), window));
                window.MouseDown(editorPoint, MouseButton.Left);
                window.MouseMove(editorPoint + delta, RawInputModifiers.LeftMouseButton);
                window.MouseUp(editorPoint + delta, MouseButton.Left);
                Assert.Equal(before.X + delta.X, floating.X);
                Assert.Equal(before.Y + delta.Y, floating.Y);
                return true;
            }
            finally
            {
                window.Close();
            }
        }, timeout.Token));
    }
}
