namespace Asura.Application;

/// <summary>
/// The closed set of governed desktop-automation tool names. The agent reads
/// and operates other applications through their accessibility trees; every
/// name here maps to one trusted catalog descriptor (ADR 0061).
/// </summary>
public static class DesktopAgentToolNames
{
    public const string ListApps = "desktop.list_apps";
    public const string ListWindows = "desktop.list_windows";
    public const string Snapshot = "desktop.snapshot";
    public const string Wait = "desktop.wait";
    public const string Click = "desktop.click";
    public const string SetValue = "desktop.set_value";
    public const string TypeText = "desktop.type_text";
    public const string Clear = "desktop.clear";
    public const string Select = "desktop.select";
    public const string Toggle = "desktop.toggle";
    public const string Expand = "desktop.expand";
    public const string Scroll = "desktop.scroll";
    public const string PressKey = "desktop.press_key";
    public const string FocusWindow = "desktop.focus_window";
    public const string LaunchApp = "desktop.launch_app";
    public const string CloseApp = "desktop.close_app";

    public static bool Owns(string toolName) => toolName is
        ListApps or ListWindows or Snapshot or Wait
        or Click or SetValue or TypeText or Clear or Select or Toggle or Expand or Scroll
        or PressKey or FocusWindow or LaunchApp or CloseApp;
}
