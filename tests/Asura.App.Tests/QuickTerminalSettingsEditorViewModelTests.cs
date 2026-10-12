using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class QuickTerminalSettingsEditorViewModelTests
{
    /// <summary>
    /// The shortcut is recorded, not typed, so Use Default is how a recorded
    /// one is undone; it is dimmed while the default is already in use.
    /// </summary>
    [Fact]
    public void Use_default_puts_back_the_shortcut_quick_terminal_arrives_with()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1);
        Assert.True(editor.UsesDefaultHotkey);

        editor.HotkeyText = QuickTerminalHotkeyText.Format(new KeyStroke("K", KeyModifiers.Control | KeyModifiers.Alt));
        Assert.False(editor.UsesDefaultHotkey);

        editor.UseDefaultHotkey();

        Assert.True(editor.UsesDefaultHotkey);
        Assert.Equal(QuickTerminalSettings.DefaultHotkey, editor.CreateSaveRequest().Settings.Hotkey);
    }

    [Fact]
    public void Save_request_parses_hotkey_and_percent_fields()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 7)
        {
            HotkeyText = "Control + Option + K",
            MonitorPolicy = QuickTerminalMonitorPolicy.ActiveWindow,
            HeightPercent = 40,
            OpacityPercent = 40,
            IsTranslucent = false,
            AnimateSlide = false,
            AnimationDurationMilliseconds = 90,
            ReduceMotion = true,
            RestoreLastSession = false,
            RestoreOnStart = false,
            HideOnFocusLoss = false,
        };

        var request = editor.CreateSaveRequest();

        Assert.Equal(7, request.ExpectedRevision);
        Assert.Equal(
            new KeyStroke("K", KeyModifiers.Control | KeyModifiers.Alt),
            request.Settings.Hotkey);
        Assert.Equal(QuickTerminalMonitorPolicy.ActiveWindow, request.Settings.MonitorPolicy);
        Assert.Equal(0.4, request.Settings.HeightFraction);
        Assert.Equal(0.4, request.Settings.Opacity);
        Assert.False(request.Settings.RestoreLastSession);
        Assert.False(request.Settings.RestoreOnStart);
        Assert.False(request.Settings.HideOnFocusLoss);
    }

    [Fact]
    public void Height_percentage_is_presented_as_a_whole_number()
    {
        var defaults = QuickTerminalSettings.Default;
        var settings = new QuickTerminalSettings(
            defaults.Id,
            defaults.Name,
            defaults.Hotkey,
            defaults.MonitorPolicy,
            heightFraction: 0.404981549815498,
            defaults.Opacity,
            defaults.AnimateSlide,
            defaults.AnimationDurationMilliseconds,
            defaults.ReduceMotion,
            defaults.RestoreLastSession,
            defaults.HideOnFocusLoss,
            defaults.IsTranslucent,
            defaults.RestoreOnStart);

        var editor = new QuickTerminalSettingsEditorViewModel(settings, expectedRevision: 1);

        Assert.Equal(40, editor.HeightPercent);
    }

    [Fact]
    public void Monitor_options_explain_each_distinct_display_policy()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1);

        Assert.Equal(
            new[]
            {
                (QuickTerminalMonitorPolicy.ActiveWindow, "Active window"),
                (QuickTerminalMonitorPolicy.MainWindow, "Asura window"),
                (QuickTerminalMonitorPolicy.Primary, "Primary display"),
            },
            editor.MonitorOptions
                .Select(option => (option.Policy, option.DisplayName))
                .ToArray());

        editor.SelectedMonitorOption = editor.MonitorOptions[0];

        Assert.Equal(QuickTerminalMonitorPolicy.ActiveWindow, editor.MonitorPolicy);
        Assert.Equal(
            QuickTerminalMonitorPolicy.ActiveWindow,
            editor.CreateSaveRequest().Settings.MonitorPolicy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(40)]
    public void Save_request_preserves_low_opacity_percentages(double opacityPercent)
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1)
        {
            OpacityPercent = opacityPercent,
        };

        var request = editor.CreateSaveRequest();

        Assert.Equal(opacityPercent / 100, request.Settings.Opacity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("GRAVE")]
    [InlineData("Command + K + L")]
    public void Invalid_hotkey_text_is_rejected_before_persistence(string text)
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1)
        {
            HotkeyText = text,
        };

        var exception = Record.Exception(() => editor.CreateSaveRequest());
        Assert.True(exception is ArgumentException or FormatException);
    }

    [Fact]
    public void Registration_failure_reports_that_no_hotkey_is_active()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1);
        var configured = new KeyStroke("K", KeyModifiers.Meta);
        var failure = new GlobalHotkeyRegistrationResult.Failure(new(
            GlobalHotkeyRegistrationErrorCode.Conflict,
            "global_hotkey_conflict",
            "Another application owns the shortcut."));

        editor.ApplyRegistration(configured, failure);

        Assert.Contains("Another application", editor.RegistrationStatus);
        Assert.Contains("no global shortcut", editor.RegistrationStatus);
        Assert.DoesNotContain("remains active", editor.RegistrationStatus);
        Assert.Equal(StatusTone.Warning, editor.RegistrationStatusTone);
    }

    [Theory]
    [InlineData(HostOperatingSystem.MacOS, "⌃⌥⇧⌘K")]
    [InlineData(HostOperatingSystem.Windows, "Ctrl+Alt+Shift+Win+K")]
    [InlineData(HostOperatingSystem.Linux, "Ctrl+Alt+Shift+Super+K")]
    public void Shortcut_format_uses_host_conventions(
        HostOperatingSystem host,
        string expected)
    {
        var stroke = new KeyStroke(
            "K",
            KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta);

        Assert.Equal(expected, QuickTerminalHotkeyText.Format(stroke, host));
    }

    [Theory]
    [InlineData(HostOperatingSystem.MacOS, "⌘K")]
    [InlineData(HostOperatingSystem.Windows, "Ctrl+K")]
    [InlineData(HostOperatingSystem.Linux, "Ctrl+K")]
    public void Application_shortcut_format_uses_host_conventions(
        HostOperatingSystem host,
        string expected)
    {
        Assert.Equal(
            expected,
            QuickTerminalHotkeyText.FormatApplicationCommand("K", host));
    }

    [Theory]
    [InlineData("⌃`")]
    [InlineData("Control+`")]
    [InlineData("Ctrl + `")]
    [InlineData("control + grave")]
    public void Hotkey_field_reads_the_notation_it_displays(string text)
    {
        Assert.Equal(
            new KeyStroke("GRAVE", KeyModifiers.Control),
            QuickTerminalHotkeyText.Parse(text));
    }

    [Fact]
    public void Displayed_hotkey_round_trips_through_the_field()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1);

        Assert.Equal(
            QuickTerminalSettings.Default.Hotkey,
            editor.CreateSaveRequest().Settings.Hotkey);
    }

    [Fact]
    public void Escape_setting_is_off_by_default_and_saved_when_chosen()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1);

        Assert.False(editor.HideOnEscape);
        Assert.Contains("Escape goes to the terminal", editor.DismissSummary);

        editor.HideOnEscape = true;

        Assert.True(editor.CreateSaveRequest().Settings.HideOnEscape);
        Assert.Contains("or Escape", editor.DismissSummary);
    }

    [Theory]
    [InlineData(HostOperatingSystem.MacOS, "GRAVE", KeyModifiers.Meta, true)]
    [InlineData(HostOperatingSystem.MacOS, "SPACE", KeyModifiers.Control, true)]
    [InlineData(HostOperatingSystem.MacOS, "3", KeyModifiers.Shift | KeyModifiers.Meta, true)]
    [InlineData(HostOperatingSystem.MacOS, "N", KeyModifiers.Meta, true)]
    [InlineData(HostOperatingSystem.MacOS, "GRAVE", KeyModifiers.Control, false)]
    [InlineData(HostOperatingSystem.Windows, "GRAVE", KeyModifiers.Meta, false)]
    public void Reserved_hotkeys_are_explained_before_saving(
        HostOperatingSystem host,
        string key,
        KeyModifiers modifiers,
        bool expectWarning)
    {
        var warning = QuickTerminalHotkeyText.ReservedWarning(new KeyStroke(key, modifiers), host);

        Assert.Equal(expectWarning, warning is not null);
    }

    [Fact]
    public void Hotkey_warning_follows_the_field_on_macOS()
    {
        var editor = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            expectedRevision: 1);
        Assert.False(editor.HasHotkeyWarning);

        editor.HotkeyText = "⌘`";

        Assert.Equal(HostPlatform.Current == HostOperatingSystem.MacOS, editor.HasHotkeyWarning);
    }
}
