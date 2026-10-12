using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Asura.App.Controls;

/// <summary>
/// Exposes the terminal's text to assistive technology as a read-only document
/// value: the visible screen plus a bounded scrollback window.
/// </summary>
/// <remarks>
/// The surface deliberately carries no live setting. Avalonia 12.0.5's macOS bridge
/// announces a live element by its name and crashes when that name converts to nil,
/// so output is announced through a separate <see cref="LiveRegionTextBlock"/>. A
/// name change here (the panel or window title) must never become an announcement.
/// </remarks>
internal sealed class ManagedTerminalSurfaceAutomationPeer(ManagedTerminalSurface owner)
    : ControlAutomationPeer(owner), IValueProvider
{
    private readonly ManagedTerminalSurface _surface = owner;
    private string _notifiedValue = string.Empty;

    public bool IsReadOnly => true;

    public string? Value => _surface.AccessibleText;

    public void SetValue(string? value) =>
        throw new InvalidOperationException(
            "Terminal output is read-only. Type to send input to the session.");

    /// <summary>Tells assistive technology the text changed, once per change.</summary>
    internal void NotifyValueChanged()
    {
        var value = _surface.AccessibleText;
        if (string.Equals(_notifiedValue, value, StringComparison.Ordinal))
        {
            return;
        }

        var previous = _notifiedValue;
        _notifiedValue = value;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, value);
    }

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        AutomationControlType.Document;

    protected override string GetClassNameCore() => "Terminal";

    protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Off;

    protected override string GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrWhiteSpace(name) ? "Terminal" : name;
    }
}
