using System.Text.Json;

namespace Asura.Core.Tests;

public sealed class QuickTerminalSettingsTests
{
    [Fact]
    public void Defaults_match_the_functional_drop_down_profile()
    {
        var settings = QuickTerminalSettings.Default;

        Assert.Equal(DefinitionKind.QuickTerminalSettings, settings.Key.Kind);
        // Command-Grave cycles windows in every Mac app; the default takes
        // Control-Grave instead.
        Assert.Equal(new KeyStroke("GRAVE", KeyModifiers.Control), settings.Hotkey);
        Assert.False(settings.HideOnEscape);
        Assert.Equal(QuickTerminalMonitorPolicy.MainWindow, settings.MonitorPolicy);
        Assert.Equal(0.55, settings.HeightFraction);
        Assert.Equal(0.82, settings.Opacity);
        Assert.True(settings.IsTranslucent);
        Assert.True(settings.AnimateSlide);
        Assert.True(settings.RestoreLastSession);
        Assert.True(settings.RestoreOnStart);
        Assert.True(settings.HideOnFocusLoss);
    }

    [Theory]
    [InlineData(0.24, 0.82, 180)]
    [InlineData(0.91, 0.82, 180)]
    [InlineData(0.55, -0.01, 180)]
    [InlineData(0.55, 1.01, 180)]
    [InlineData(0.55, 0.82, -1)]
    [InlineData(0.55, 0.82, 1001)]
    public void Invalid_visual_ranges_are_rejected(
        double height,
        double opacity,
        int duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            height,
            opacity,
            duration));
    }

    [Theory]
    [InlineData(0.00)]
    [InlineData(0.20)]
    [InlineData(0.40)]
    public void Low_opacity_values_are_valid(double opacity)
    {
        var settings = Create(
            height: 0.55,
            opacity,
            duration: 180);

        Assert.Equal(opacity, settings.Opacity);
    }

    [Fact]
    public void Durable_payload_round_trips_every_behavior_choice()
    {
        var original = new QuickTerminalSettings(
            new QuickTerminalSettingsId("custom"),
            "Custom Quick Terminal",
            new KeyStroke("K", KeyModifiers.Control | KeyModifiers.Shift),
            QuickTerminalMonitorPolicy.ActiveWindow,
            0.4,
            0.2,
            animateSlide: false,
            animationDurationMilliseconds: 0,
            reduceMotion: true,
            restoreLastSession: false,
            hideOnFocusLoss: false,
            restoreOnStart: false,
            hideOnEscape: true);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<QuickTerminalSettings>(json);

        Assert.Equal(original, restored);
        Assert.False(restored!.RestoreOnStart);
        Assert.True(restored.HideOnEscape);
    }

    [Fact]
    public void Payload_saved_before_the_escape_setting_loads_with_escape_left_to_the_terminal()
    {
        const string legacy = """
            {"id":{"value":"builtin.quick-terminal.default"},"name":"Quick Terminal","hotkey":{"key":"GRAVE","modifiers":"Control"},"monitorPolicy":"MainWindow","heightFraction":0.55,"opacity":0.82,"animateSlide":true,"animationDurationMilliseconds":180,"reduceMotion":false,"restoreLastSession":true,"hideOnFocusLoss":true,"isTranslucent":true,"restoreOnStart":true}
            """;

        var restored = JsonSerializer.Deserialize<QuickTerminalSettings>(
            legacy,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });

        Assert.NotNull(restored);
        Assert.False(restored.HideOnEscape);
        Assert.True(restored.HideOnFocusLoss);
    }

    private static QuickTerminalSettings Create(
        double height,
        double opacity,
        int duration) => new(
            new QuickTerminalSettingsId("test"),
            "Test",
            new KeyStroke("GRAVE", KeyModifiers.Meta),
            QuickTerminalMonitorPolicy.MainWindow,
            height,
            opacity,
            animateSlide: true,
            animationDurationMilliseconds: duration,
            reduceMotion: false,
            restoreLastSession: true,
            hideOnFocusLoss: true);
}
