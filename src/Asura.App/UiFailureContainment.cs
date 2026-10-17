using Asura.Application;
using Avalonia.Threading;

namespace Asura.App;

/// <summary>
/// Keeps one failed UI action from ending the UI loop. An exception escaping
/// an event handler — an async click handler's continuation included — left
/// Avalonia's dispatcher; shutdown then waited on work that needed it, and
/// Asura hung until it was force-quit, holding up typing system-wide. The
/// failure is recorded, without its message or stack, and the app keeps
/// running.
/// </summary>
internal static class UiFailureContainment
{
    public static void Attach(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        dispatcher.UnhandledException -= OnUnhandledException;
        dispatcher.UnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is OutOfMemoryException)
        {
            return;
        }

        SecretSafeDiagnosticProjection.WriteStandardError("desktop.ui.unhandled", e.Exception);
        e.Handled = true;
    }
}
