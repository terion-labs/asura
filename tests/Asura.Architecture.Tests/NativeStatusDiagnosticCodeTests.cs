using Asura.Application;
using Asura.Desktop;

namespace Asura.Architecture.Tests;

/// <summary>
/// Native status numbers stay out of the messages people read, so they must
/// reach diagnostics through the stable code instead.
/// </summary>
public sealed class NativeStatusDiagnosticCodeTests
{
    [Fact]
    public void Windows_native_failure_code_carries_the_win32_error()
    {
        Assert.Equal(
            "desktop.hotkey.windows-native-failure.win32-5",
            WindowsGlobalHotkeyService.NativeFailureDiagnosticCode(5));
    }

    [Fact]
    public void MacOs_native_failure_code_carries_the_step_and_negative_osstatus()
    {
        Assert.Equal(
            "desktop.hotkey.macos-native-failure.register.osstatus-neg9878",
            MacOsGlobalHotkeyService.NativeFailureDiagnosticCode("register", -9878));
        Assert.Equal(
            "desktop.hotkey.macos-native-failure.handler-install.osstatus-neg50",
            MacOsGlobalHotkeyService.NativeFailureDiagnosticCode("handler-install", -50));
    }

    [Fact]
    public void X11_native_failure_code_carries_the_x11_error()
    {
        Assert.Equal(
            "desktop.hotkey.x11-native-failure.x11-error-3",
            LinuxX11GlobalHotkeyService.NativeFailureDiagnosticCode(3));
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    public void Every_native_status_forms_a_valid_stable_code(int status)
    {
        var code = NativeStatusDiagnosticCode.Format("desktop.test", "status", status);

        // The projection rejects codes outside its character set, so this
        // proves the code can be written to diagnostics.
        var projection = SecretSafeDiagnosticProjection.FromEvent(code, SecretSafeDiagnosticKind.Unexpected);
        Assert.Contains($"code={code} ", projection, StringComparison.Ordinal);
    }
}
