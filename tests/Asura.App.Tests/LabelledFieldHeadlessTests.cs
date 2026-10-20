using Asura.App.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// A form component draws a name beside its control; these check that a
/// screen reader hears that name, by asking the control's automation peer —
/// the thing VoiceOver actually reads — rather than the markup around it.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class LabelledFieldHeadlessTests
{
    [Fact]
    public Task A_labelled_field_names_its_control_and_describes_it_with_the_hint() =>
        RunHeadlessAsync(window =>
        {
            var input = new TextBox();
            var field = new LabeledField { Label = "Host", Hint = "Name or address.", Content = input };
            Show(window, field);

            var peer = ControlAutomationPeer.CreatePeerForElement(input);
            Assert.Equal("Host", peer.GetName());
            Assert.Equal("Name or address.", peer.GetHelpText());

            field.Label = "Server";
            Assert.Equal("Server", peer.GetName());
        });

    [Fact]
    public Task A_field_with_an_error_shows_it_under_the_control_and_announces_it() =>
        RunHeadlessAsync(window =>
        {
            var input = new TextBox { Text = "70000" };
            var field = new LabeledField { Label = "Port", Hint = "The port to listen on.", Content = input };
            Show(window, field);

            field.Error = "Enter a port between 1 and 65535.";
            window.UpdateLayout();

            Assert.True(field.HasError);
            Assert.Contains(":error", field.Classes, StringComparer.Ordinal);
            var error = field.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "PART_Error");
            Assert.True(error.IsEffectivelyVisible);
            var hint = field.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Hint");
            Assert.False(hint.IsEffectivelyVisible);
            // Under the control, not above it or in a window of its own.
            Assert.True(error.TranslatePoint(default, field)!.Value.Y > input.TranslatePoint(default, field)!.Value.Y);
            Assert.Equal(
                "Enter a port between 1 and 65535.",
                ControlAutomationPeer.CreatePeerForElement(input).GetHelpText());
            // What was typed is kept.
            Assert.Equal("70000", input.Text);

            field.Error = null;
            window.UpdateLayout();
            Assert.False(field.HasError);
            Assert.False(error.IsEffectivelyVisible);
            Assert.Equal("The port to listen on.", ControlAutomationPeer.CreatePeerForElement(input).GetHelpText());
        });

    [Fact]
    public Task Focusing_the_first_error_lands_on_its_control() =>
        RunHeadlessAsync(window =>
        {
            var host = new TextBox();
            var port = new TextBox();
            var panel = new StackPanel
            {
                Children =
                {
                    new LabeledField { Label = "Host", Content = host },
                    new LabeledField { Label = "Port", Content = port, Error = "Enter a number." },
                },
            };
            Show(window, panel);

            Assert.True(LabeledField.FocusFirstError(window));
            Assert.True(port.IsFocused);
        });

    [Fact]
    public Task A_name_the_view_set_on_its_control_is_never_replaced() =>
        RunHeadlessAsync(window =>
        {
            var input = new ComboBox();
            AutomationProperties.SetName(input, "Connection type");
            Show(window, new LabeledField { Label = "Type", Content = input });

            Assert.Equal("Connection type", ControlAutomationPeer.CreatePeerForElement(input).GetName());
        });

    [Fact]
    public Task A_field_whose_content_is_a_layout_names_the_input_inside_it() =>
        RunHeadlessAsync(window =>
        {
            var input = new TextBox();
            var browse = new Button { Content = "Choose…" };
            Show(window, new LabeledField
            {
                Label = "Folder",
                Content = new Grid { Children = { input, browse } },
            });

            Assert.Equal("Folder", ControlAutomationPeer.CreatePeerForElement(input).GetName());
            Assert.Equal("Choose…", ControlAutomationPeer.CreatePeerForElement(browse).GetName());
        });

    [Fact]
    public Task A_setting_row_names_its_control_and_describes_it() =>
        RunHeadlessAsync(window =>
        {
            var picker = new ComboBox();
            var row = new SettingRow
            {
                Label = "Show on display",
                Description = "Follow the window or use the primary display.",
                Content = picker,
            };
            Show(window, row);

            var peer = ControlAutomationPeer.CreatePeerForElement(picker);
            Assert.Equal("Show on display", peer.GetName());
            Assert.Equal("Follow the window or use the primary display.", peer.GetHelpText());

            // A stacked row is a different template; the link follows it.
            row.IsStacked = true;
            window.UpdateLayout();
            Assert.Equal("Show on display", ControlAutomationPeer.CreatePeerForElement(picker).GetName());
        });

    [Fact]
    public Task A_toggle_field_names_its_switch_and_no_longer_writes_on_or_off() =>
        RunHeadlessAsync(window =>
        {
            var field = new ToggleField { Label = "Keep the session alive", IsChecked = true };
            Show(window, field);

            var toggle = field.GetVisualDescendants().OfType<ToggleSwitch>().Single();
            Assert.Equal("Keep the session alive", ControlAutomationPeer.CreatePeerForElement(toggle).GetName());
            Assert.DoesNotContain(
                field.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text is "On" or "Off");

            field.Description = "The shell keeps running after the panel closes.";
            window.UpdateLayout();
            Assert.Equal(
                "The shell keeps running after the panel closes.",
                ControlAutomationPeer.CreatePeerForElement(toggle).GetHelpText());
            Assert.Contains(
                field.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == field.Description);
        });

    private static void Show(Window window, Control content)
    {
        window.Content = content;
        window.Show();
        window.UpdateLayout();
    }

    private static async Task RunHeadlessAsync(Action<Window> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                () =>
                {
                    var window = new Window { Width = 600, Height = 400 };
                    try
                    {
                        assertion(window);
                    }
                    finally
                    {
                        window.Close();
                    }

                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
