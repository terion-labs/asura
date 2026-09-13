using System.Diagnostics;
using Asura.Infrastructure;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Asura.Desktop;

internal static class DesktopStartupFailurePresenter
{
    internal const string RecoveryUiSwitch = "--startup-recovery";

    public static void TryShow(
        string title,
        string message,
        string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(arguments);

        try
        {
            AppBuilder
                .Configure(() => new StartupFailureApplication(title, message, arguments))
                .UsePlatformDetect()
                .WithInterFont()
                .StartWithClassicDesktopLifetime(
                    arguments,
                    ShutdownMode.OnMainWindowClose);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // stderr remains the deterministic fallback for headless or unavailable desktops.
            Asura.Application.SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.startup-message.failed",
                exception);
        }
    }

    private sealed class StartupFailureApplication(
        string title,
        string message,
        string[] arguments) : Avalonia.Application
    {
        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = CreateWindow(title, message, arguments);
            }

            base.OnFrameworkInitializationCompleted();
        }

        internal static Window CreateWindow(string title, string message, string[] arguments)
        {
            var retryButton = new Button
            {
                Content = "Try again",
                MinWidth = 96,
            };
            var recoveryButton = new Button { Content = "Open recovery workspace" };
            AutomationProperties.SetName(retryButton, "Try opening saved profile again");
            AutomationProperties.SetName(recoveryButton, "Open a separate recovery workspace");
            var failureText = new TextBlock { TextWrapping = TextWrapping.Wrap };

            var window = new Window
            {
                Title = "Asura",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new Border
                {
                    Padding = new Thickness(24),
                    Child = new StackPanel
                    {
                        Spacing = 14,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontSize = 20,
                                FontWeight = FontWeight.SemiBold,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock
                            {
                                Text = message,
                                FontSize = 14,
                                Opacity = 0.82,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock
                            {
                                Text = "Your saved profile is kept. You can retry or use a separate recovery workspace. Work done there is saved separately.",
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 10,
                                Children = { retryButton, recoveryButton },
                            },
                            failureText,
                        },
                    },
                },
            };
            AutomationProperties.SetName(window, title);
            retryButton.Click += (_, _) => Launch(arguments);
            recoveryButton.Click += (_, _) => Launch(DesktopProfileConfiguration.NextRecoveryArguments(arguments));
            return window;

            void Launch(string[] nextArguments)
            {
                try
                {
                    DesktopStartupFailurePresenter.Launch(nextArguments);
                    window.Close();
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Asura.Application.SecretSafeDiagnosticProjection.WriteStandardError("desktop.recovery-launch.failed", error);
                    failureText.Text = "Asura could not start another window. You can try again after checking available disk space and access to the app.";
                }
            }
        }
    }

    internal static void Launch(string[] arguments)
    {
        using var process = Process.Start(CreateRestartStartInfo(arguments))
            ?? throw new IOException("The new Asura process did not start.");
    }

    internal static ProcessStartInfo CreateRestartStartInfo(string[] arguments)
    {
        var launch = SelfReentryLaunch.Detect();
        var start = new ProcessStartInfo(launch.Executable) { UseShellExecute = false };
        foreach (var argument in launch.PrefixArguments.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
