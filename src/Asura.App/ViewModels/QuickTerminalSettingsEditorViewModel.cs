using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

public sealed record QuickTerminalSettingsSaveRequest(
    QuickTerminalSettings Settings,
    long ExpectedRevision);

public sealed record QuickTerminalMonitorOption(
    QuickTerminalMonitorPolicy Policy,
    string DisplayName);

public sealed class QuickTerminalSettingsEditorViewModel : ObservableObject
{
    private string _hotkeyText;
    private QuickTerminalMonitorPolicy _monitorPolicy;
    private double _heightPercent;
    private double _opacityPercent;
    private bool _isTranslucent;
    private bool _animateSlide;
    private int _animationDurationMilliseconds;
    private bool _reduceMotion;
    private bool _restoreLastSession;
    private bool _restoreOnStart;
    private bool _hideOnFocusLoss;
    private bool _hideOnEscape;
    private string _registrationStatus = "Shortcut registration has not been checked yet.";
    private StatusTone _registrationStatusTone = StatusTone.Neutral;

    public QuickTerminalSettingsEditorViewModel(
        QuickTerminalSettings settings,
        long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsId = settings.Id;
        Name = settings.Name;
        ExpectedRevision = expectedRevision;
        _hotkeyText = QuickTerminalHotkeyText.Format(settings.Hotkey);
        _monitorPolicy = settings.MonitorPolicy;
        _heightPercent = Math.Round(
            settings.HeightFraction * 100,
            MidpointRounding.AwayFromZero);
        _opacityPercent = settings.Opacity * 100;
        _isTranslucent = settings.IsTranslucent;
        _animateSlide = settings.AnimateSlide;
        _animationDurationMilliseconds = settings.AnimationDurationMilliseconds;
        _reduceMotion = settings.ReduceMotion;
        _restoreLastSession = settings.RestoreLastSession;
        _restoreOnStart = settings.RestoreOnStart;
        _hideOnFocusLoss = settings.HideOnFocusLoss;
        _hideOnEscape = settings.HideOnEscape;
    }

    public QuickTerminalSettingsId SettingsId { get; }

    public string Name { get; }

    /// <summary>
    /// The revision the next save is checked against. It moves forward when a
    /// save from this editor lands, so edits made meanwhile are kept and saved
    /// on top of it.
    /// </summary>
    public long ExpectedRevision { get; private set; }

    public void AcceptSavedRevision(long revision) => ExpectedRevision = revision;

    public IReadOnlyList<QuickTerminalMonitorOption> MonitorOptions { get; } =
    [
        new(QuickTerminalMonitorPolicy.ActiveWindow, "Active window"),
        new(QuickTerminalMonitorPolicy.MainWindow, "Asura window"),
        new(QuickTerminalMonitorPolicy.Primary, "Primary display"),
    ];

    public QuickTerminalMonitorOption? SelectedMonitorOption
    {
        get => MonitorOptions.Single(option => option.Policy == MonitorPolicy);
        set
        {
            if (value is not null)
            {
                MonitorPolicy = value.Policy;
            }
        }
    }

    public string HotkeyExample => QuickTerminalHotkeyText.Example;

    public string HotkeyHelpText =>
        $"Enter a modifier and key, for example {HotkeyExample}.";

    public string HotkeyText
    {
        get => _hotkeyText;
        set
        {
            if (SetProperty(ref _hotkeyText, value))
            {
                OnPropertyChanged(nameof(HotkeyWarning));
                OnPropertyChanged(nameof(HasHotkeyWarning));
            }
        }
    }

    /// <summary>
    /// Warns while the field holds a shortcut that macOS or Asura's own menu
    /// bar already answers. Saving is still allowed; the warning says what the
    /// hotkey would take away.
    /// </summary>
    public string? HotkeyWarning => ShortcutText.TryParseStroke(HotkeyText, out var stroke)
        ? QuickTerminalHotkeyText.ReservedWarning(stroke, HostPlatform.Current)
        : null;

    public bool HasHotkeyWarning => HotkeyWarning is not null;

    public QuickTerminalMonitorPolicy MonitorPolicy
    {
        get => _monitorPolicy;
        set
        {
            if (SetProperty(ref _monitorPolicy, value))
            {
                OnPropertyChanged(nameof(SelectedMonitorOption));
            }
        }
    }

    public double HeightPercent
    {
        get => _heightPercent;
        set => SetProperty(ref _heightPercent, value);
    }

    public double OpacityPercent
    {
        get => _opacityPercent;
        set => SetProperty(ref _opacityPercent, value);
    }

    public bool IsTranslucent
    {
        get => _isTranslucent;
        set => SetProperty(ref _isTranslucent, value);
    }

    public bool AnimateSlide
    {
        get => _animateSlide;
        set => SetProperty(ref _animateSlide, value);
    }

    public int AnimationDurationMilliseconds
    {
        get => _animationDurationMilliseconds;
        set => SetProperty(ref _animationDurationMilliseconds, value);
    }

    public bool ReduceMotion
    {
        get => _reduceMotion;
        set => SetProperty(ref _reduceMotion, value);
    }

    public bool RestoreLastSession
    {
        get => _restoreLastSession;
        set => SetProperty(ref _restoreLastSession, value);
    }

    public bool RestoreOnStart
    {
        get => _restoreOnStart;
        set => SetProperty(ref _restoreOnStart, value);
    }

    public bool HideOnFocusLoss
    {
        get => _hideOnFocusLoss;
        set => SetProperty(ref _hideOnFocusLoss, value);
    }

    public bool HideOnEscape
    {
        get => _hideOnEscape;
        set
        {
            if (SetProperty(ref _hideOnEscape, value))
            {
                OnPropertyChanged(nameof(DismissSummary));
            }
        }
    }

    /// <summary>Every way the panel can be dismissed with these settings.</summary>
    public string DismissSummary
    {
        get
        {
            var close = QuickTerminalHotkeyText.FormatApplicationCommand("W");
            return HideOnEscape
                ? $"Hide the panel with its global hotkey, {close}, or Escape."
                : $"Hide the panel with its global hotkey or {close}. Escape goes to the terminal.";
        }
    }

    public string RegistrationStatus
    {
        get => _registrationStatus;
        private set => SetProperty(ref _registrationStatus, value);
    }

    /// <summary>
    /// How registration went, as the status tone the mark beside the status
    /// draws in: success, warning, or neutral before anything was tried.
    /// </summary>
    public StatusTone RegistrationStatusTone
    {
        get => _registrationStatusTone;
        private set => SetProperty(ref _registrationStatusTone, value);
    }

    public QuickTerminalSettingsSaveRequest CreateSaveRequest()
    {
        var settings = new QuickTerminalSettings(
            SettingsId,
            Name,
            QuickTerminalHotkeyText.Parse(HotkeyText),
            MonitorPolicy,
            HeightPercent / 100,
            OpacityPercent / 100,
            AnimateSlide,
            AnimationDurationMilliseconds,
            ReduceMotion,
            RestoreLastSession,
            HideOnFocusLoss,
            IsTranslucent,
            RestoreOnStart,
            HideOnEscape);
        return new QuickTerminalSettingsSaveRequest(settings, ExpectedRevision);
    }

    /// <summary>
    /// Reports what the system made of the configured hotkey. A failure is
    /// final: no other shortcut is registered in its place, so the status says
    /// that Quick Terminal has no hotkey until another one is chosen.
    /// </summary>
    public void ApplyRegistration(
        KeyStroke configuredGesture,
        GlobalHotkeyRegistrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var configured = QuickTerminalHotkeyText.Format(configuredGesture);
        if (result is GlobalHotkeyRegistrationResult.Success)
        {
            RegistrationStatus = $"{configured} is registered globally.";
            RegistrationStatusTone = StatusTone.Success;
            return;
        }

        var error = ((GlobalHotkeyRegistrationResult.Failure)result).Error;
        RegistrationStatus =
            $"Asura couldn't set {configured} as the global shortcut. {error.Message} "
            + "Quick Terminal has no global shortcut until you choose another one. "
            + "Meanwhile, show it from the View menu.";
        RegistrationStatusTone = StatusTone.Warning;
    }
}
