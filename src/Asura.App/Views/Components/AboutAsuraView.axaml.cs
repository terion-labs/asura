using Asura.App.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Asura.App.Views.Components;

public sealed partial class AboutAsuraView : UserControl
{
    private CancellationTokenSource? _lifetime;

    /// <summary>
    /// Whether the bundled components are listed under the About content.
    /// Settings' About page lists them; the About window leaves them to
    /// Acknowledgments….
    /// </summary>
    public static readonly StyledProperty<bool> ShowsComponentsProperty =
        AvaloniaProperty.Register<AboutAsuraView, bool>(nameof(ShowsComponents));

    public AboutAsuraView()
    {
        InitializeComponent();
    }

    public bool ShowsComponents
    {
        get => GetValue(ShowsComponentsProperty);
        set => SetValue(ShowsComponentsProperty, value);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowsComponentsProperty || change.Property == DataContextProperty)
        {
            this.FindControl<StackPanel>("ProductComponentList")!.IsVisible =
                ShowsComponents && ViewModel is { HasNoProductComponents: false };
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lifetime = new CancellationTokenSource();
        var icon = this.FindControl<Image>("ApplicationIcon")!;
        icon.Source ??= MacOsApplicationIconImage.Read() ?? MacOsApplicationIconImage.Bundled();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || _lifetime is not { } lifetime)
        {
            return;
        }

        try
        {
            await viewModel.ApplicationUpdates.CheckAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnDownloadUpdateClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is not { } viewModel || _lifetime is not { } lifetime)
        {
            return;
        }

        try
        {
            await viewModel.ApplicationUpdates.DownloadAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
    }

    private async void OnRestartToApplyUpdateClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewModel is { } viewModel)
        {
            await viewModel.ApplicationUpdates.RestartToApplyAsync();
        }
    }

    private async void OnOpenThirdPartyNoticesClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var path = ProductDocumentLocator.FindThirdPartyNotices();
        if (path is null)
        {
            ViewModel?.SetError(
                "Asura couldn’t find its acknowledgments file. Reinstall Asura to restore it.");
            return;
        }

        if (TopLevel.GetTopLevel(this) is { } topLevel
            && !await topLevel.Launcher.LaunchFileInfoAsync(new FileInfo(path)))
        {
            ViewModel?.SetError(
                "Asura couldn’t find an app to open the acknowledgments file.");
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
