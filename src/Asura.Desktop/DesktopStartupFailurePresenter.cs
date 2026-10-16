using System.Diagnostics;
using Asura.Infrastructure;
using Asura.Processes;
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
            RequestedThemeVariant = ThemeVariant.Default;
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
            var failureText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                IsVisible = false,
            };
            var buttons = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                ColumnSpacing = 8,
                Margin = new Thickness(0, 6, 0, 0),
            };

            var window = new Window
            {
                Title = "Asura",
                Width = 440,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                CanMinimize = false,
                CanMaximize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new Border
                {
                    Padding = new Thickness(20),
                    Child = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = title,
                                FontSize = 15,
                                FontWeight = FontWeight.Bold,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock
                            {
                                Text = message,
                                FontSize = 13,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            new TextBlock
                            {
                                Text = "Your saved profile is kept. You can try again or use a separate recovery workspace. Work done there is saved separately.",
                                FontSize = 12,
                                Opacity = 0.82,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            failureText,
                            buttons,
                        },
                    },
                },
            };
            AutomationProperties.SetName(window, title);
            foreach (var action in StartupFailureAlert.Actions)
            {
                var button = new Button
                {
                    Content = action.Label,
                    MinWidth = 80,
                    IsDefault = action.IsDefault,
                    IsCancel = action.IsCancel,
                };
                AutomationProperties.SetName(button, action.AutomationName);
                Grid.SetColumn(button, action.Column);
                button.Click += (_, _) => Run(action.Kind);
                buttons.Children.Add(button);
            }

            return window;

            void Run(StartupFailureActionKind kind)
            {
                switch (kind)
                {
                    case StartupFailureActionKind.Quit:
                        window.Close();
                        break;
                    case StartupFailureActionKind.TryAgain:
                        Launch(arguments);
                        break;
                    case StartupFailureActionKind.OpenRecoveryWorkspace:
                        Launch(DesktopProfileConfiguration.NextRecoveryArguments(arguments));
                        break;
                }
            }

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
                    failureText.IsVisible = true;
                }
            }
        }
    }

    internal static void Launch(string[] arguments)
    {
        // Spawned, not forked: the new Asura inherits none of this one's
        // descriptors, so it never holds the profile lock this run releases.
        using var process = ChildProcess.Start(CreateRestartStartInfo(arguments));
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

internal enum StartupFailureActionKind
{
    Quit,
    OpenRecoveryWorkspace,
    TryAgain,
}

internal sealed record StartupFailureAction(
    StartupFailureActionKind Kind,
    string Label,
    string AutomationName,
    int Column,
    bool IsDefault = false,
    bool IsCancel = false);

/// <summary>
/// The startup failure is an alert, and its buttons sit where a Mac alert's
/// do: Quit on the leading edge, where Escape lands; Try Again as the
/// trailing default, which Return presses; and the recovery workspace as the
/// alternate beside it.
/// </summary>
internal static class StartupFailureAlert
{
    public static IReadOnlyList<StartupFailureAction> Actions { get; } =
    [
        new(
            StartupFailureActionKind.Quit,
            "Quit",
            "Quit Asura",
            Column: 0,
            IsCancel: true),
        new(
            StartupFailureActionKind.OpenRecoveryWorkspace,
            "Open Recovery Workspace",
            "Open a separate recovery workspace",
            Column: 2),
        new(
            StartupFailureActionKind.TryAgain,
            "Try Again",
            "Try opening saved profile again",
            Column: 3,
            IsDefault: true),
    ];
}
