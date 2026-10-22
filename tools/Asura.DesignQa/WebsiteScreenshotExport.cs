using Asura.App.Views;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ImageMagick;
using ImageMagick.Drawing;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace Asura.DesignQa;

/// <summary>
/// Turns deterministic QA routes into website-ready window assets. The app
/// supplies the pixels inside the frame; this boundary supplies only chrome
/// that the headless platform cannot draw and the alpha mask a browser needs.
/// </summary>
/// <remarks>
/// Every frame is the headless compositor's own output at a render scaling of
/// 2, the density of the Retina display the real app is judged on. Text and
/// strokes are therefore rasterized once at 2x, the way the screen draws them.
/// An earlier pass redrew the 1x tree through a <c>VisualBrush</c> onto a 2x
/// surface; that upscaled glyphs already rasterized at 1x and left every
/// label soft.
/// </remarks>
internal static class WebsiteScreenshotExport
{
    public const int LogicalWidth = 1440;
    public const int LogicalHeight = 900;
    public const int Scale = 2;
    public const int PixelWidth = LogicalWidth * Scale;
    public const int PixelHeight = LogicalHeight * Scale;

    // The product enum calls the middle density "Cozy"; the UI calls it
    // "Normal". Website artwork always uses that exact setting, never the
    // 1.22x Spacious setting.
    private const InterfaceDensity NormalDensity = InterfaceDensity.Cozy;
    private const double NormalTextScale = 1;

    private const double TrafficLightDiameter = 12;
    private const double TrafficLightTop = 16;
    private const double TrafficLightFirstLeft = 18;
    private const double TrafficLightSpacing = 20;
    /// <summary>The Settings window's frame in artwork: wider than its 850 × 800 default.</summary>
    public const int SettingsWidth = 1180;
    public const int SettingsHeight = 760;

    private const int DialogInset = 48;

    /// <summary>
    /// The tallest a floating window can be and still fit the frame with its
    /// inset. A route that asks for a taller editor (to reach rows below the
    /// fold) gets this much; taller would only be scaled back down.
    /// </summary>
    public const int MaxFloatingHeight = LogicalHeight - (DialogInset * 2);
    private const int WindowShadowOffset = 18;
    private const double WindowShadowSigma = 24;
    private const int BackdropBlurSigma = 36;

    private const string TrafficLightsName = "WebsiteTrafficLights";

    private static readonly double CornerRadius =
        DensityCornerScale.WindowRadius(NormalDensity);

    public static ThemePreference NormalizeTheme(ThemePreference source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ThemePreference(
            source.Id,
            source.Name,
            source.Appearance,
            source.PlatformProfile,
            AccentPreference.AsuraBronze,
            NormalTextScale,
            NormalDensity,
            showTabBar: true,
            source.ShowWorkspacesPanel,
            TabStripPlacement.Top,
            source.WorkspacePanelPlacement,
            isTranslucent: true,
            source.BackdropOpacityPercent,
            source.HasGlassPanels,
            overridesBackdropOpacity: true);
    }

    public static DefinitionCatalogSnapshot NormalizeSnapshot(
        DefinitionCatalogSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source with
        {
            Themes =
            [
                .. source.Themes.Select(stored =>
                    stored with { Value = NormalizeTheme(stored.Value) }),
            ],
        };
    }

    public static void PrepareWindow(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.DataTemplates.Add(
            new FuncDataTemplate<WebsiteDummyRuntimePanelViewModel>(
                static (panel, _) => new WebsiteDummyRuntimePanelView
                {
                    DataContext = panel,
                }));
        window.Background = Brushes.Transparent;
        window.Width = LogicalWidth;
        window.Height = LogicalHeight;
        window.SetRenderScaling(Scale);

        AddTrafficLights(window);
    }

    /// <summary>
    /// The headless platform draws no title-bar controls, so artwork adds the
    /// standard macOS traffic lights to the windows it frames. The main window
    /// must take them; an editor window with a non-panel root is a sheet-like
    /// surface that has none in the product either, and is left as it is.
    /// </summary>
    private static void AddTrafficLights(Window window)
    {
        if (window.Content is not Panel shell)
        {
            if (window is MainWindow)
            {
                throw new InvalidOperationException(
                    "Website export requires the MainWindow panel root.");
            }

            return;
        }

        if (shell.Children.Any(child => string.Equals(child.Name, TrafficLightsName, StringComparison.Ordinal)))
        {
            return;
        }

        var trafficLights = new Canvas
        {
            Name = TrafficLightsName,
            Width = 82,
            Height = 44,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        trafficLights.Children.Add(TrafficLight("#FF5F57", 0));
        trafficLights.Children.Add(TrafficLight("#FEBC2E", 1));
        trafficLights.Children.Add(TrafficLight("#28C840", 2));
        trafficLights.SetValue(Panel.ZIndexProperty, int.MaxValue);
        shell.Children.Add(trafficLights);
    }

    public static void WriteFrame(Window target, string path)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var bytes = CaptureFrame(target);
        using var frame = new MagickImage(bytes);
        ApplyWindowShape(frame);
        frame.Format = MagickFormat.Png;
        frame.Write(path);
    }

    public static void WriteDialogFrame(
        MainWindow backdrop,
        Window dialog,
        string path)
    {
        ArgumentNullException.ThrowIfNull(backdrop);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        AddTrafficLights(dialog);
        using var backdropBytes = CaptureFrame(backdrop);
        using var dialogBytes = CaptureFrame(dialog);
        using var frame = new MagickImage(backdropBytes);
        using var dialogImage = new MagickImage(dialogBytes);

        FitDialog(dialogImage);
        var x = ((int)frame.Width - (int)dialogImage.Width) / 2;
        var y = ((int)frame.Height - (int)dialogImage.Height) / 2;
        CompositeFloatingWindow(frame, dialogImage, x, y);
        ApplyWindowShape(frame);
        frame.Format = MagickFormat.Png;
        frame.Write(path);
    }

    public static void WriteChromeMask(string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        using var mask = CreateWindowMask();
        mask.Format = MagickFormat.Png;
        var path = Path.Combine(outputDirectory, "window-chrome-mask.png");
        mask.Write(path);
        Console.WriteLine($"MASK window chrome -> {path} ({PixelWidth}x{PixelHeight})");
    }

    public static bool IncludesRoute(string name) => name switch
    {
        // These are QA comparisons of alternate sizes, densities, focus, or
        // chrome. They are useful to the product suite but are not app screens.
        "settings-security" or
        "workspace-docker-narrow" or
        "workspace-git-narrow" or
        "settings-appearance-focused" or
        "settings-appearance-density-spacious" or
        "settings-appearance-full" or
        "settings-appearance-override" or
        "settings-terminal-full" or
        "appearance-corners-tight" or
        "appearance-corners-round" or
        "design-system-high-contrast" or
        "design-system-scale-200" or
        "design-system-scale-250" or
        "workspace-tabs-side" => false,
        _ => true,
    };

    private static Ellipse TrafficLight(string color, int index)
    {
        var light = new Ellipse
        {
            Width = TrafficLightDiameter,
            Height = TrafficLightDiameter,
            Fill = new SolidColorBrush(Color.Parse(color)),
            Stroke = new SolidColorBrush(Color.Parse("#33000000")),
            StrokeThickness = 0.75,
        };
        Canvas.SetLeft(light, TrafficLightFirstLeft + (TrafficLightSpacing * index));
        Canvas.SetTop(light, TrafficLightTop);
        return light;
    }

    /// <summary>
    /// The window's last composited frame at 2x, as PNG bytes. A window that
    /// still renders at 1x (a dialog opened by a route) is switched first; the
    /// scaling change runs a layout pass, which settles before the capture.
    /// </summary>
    private static MemoryStream CaptureFrame(TopLevel target)
    {
        if (target.RenderScaling != Scale)
        {
            target.SetRenderScaling(Scale);
            Dispatcher.UIThread.RunJobs();
            target.UpdateLayout();
        }

        using var frame = target.CaptureRenderedFrame()
            ?? throw new InvalidOperationException(
                $"The headless compositor rendered no frame for {target.GetType().Name}.");
        var bytes = new MemoryStream();
        frame.Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    /// <summary>
    /// Draws a window over the backdrop the way macOS shows it: with the
    /// window's rounded silhouette, a soft shadow, and the backdrop blurred
    /// behind the window's translucent material, which the capture leaves as
    /// alpha. A vibrant Settings sidebar then reads as vibrancy rather than
    /// as a hole showing the sharp terminal behind it.
    /// </summary>
    private static void CompositeFloatingWindow(
        MagickImage frame,
        MagickImage window,
        int x,
        int y)
    {
        using var silhouette = CreateRoundedMask(window.Width, window.Height);

        using var shadow = silhouette.Clone();
        shadow.BackgroundColor = MagickColors.Black;
        shadow.Shadow(0, WindowShadowOffset, WindowShadowSigma, new Percentage(60));
        // Shadow() grows the canvas by the blur; its page offset says by how much.
        frame.Composite(
            shadow,
            x + shadow.Page.X,
            y + shadow.Page.Y,
            CompositeOperator.Over);

        using var behind = frame.Clone();
        behind.Crop(new MagickGeometry(x, y, window.Width, window.Height));
        behind.ResetPage();
        behind.Blur(0, BackdropBlurSigma);
        behind.Alpha(AlphaOption.Set);
        behind.Composite(silhouette, CompositeOperator.DstIn);
        frame.Composite(behind, x, y, CompositeOperator.Over);

        window.Alpha(AlphaOption.Set);
        window.Composite(silhouette, CompositeOperator.DstIn);
        frame.Composite(window, x, y, CompositeOperator.Over);
    }

    private static void FitDialog(MagickImage dialog)
    {
        var maximumWidth = (uint)(PixelWidth - (DialogInset * Scale * 2));
        var maximumHeight = (uint)(PixelHeight - (DialogInset * Scale * 2));
        if (dialog.Width <= maximumWidth && dialog.Height <= maximumHeight)
        {
            return;
        }

        dialog.Resize(new MagickGeometry(maximumWidth, maximumHeight)
        {
            IgnoreAspectRatio = false,
            Greater = true,
        });
    }

    private static void ApplyWindowShape(MagickImage image)
    {
        if (image.Width != PixelWidth || image.Height != PixelHeight)
        {
            throw new InvalidOperationException(
                $"Website frames must be {PixelWidth}x{PixelHeight}; got {image.Width}x{image.Height}.");
        }

        using var mask = CreateWindowMask();
        image.Alpha(AlphaOption.Set);
        image.Composite(mask, CompositeOperator.DstIn);
    }

    private static MagickImage CreateWindowMask() => CreateRoundedMask(PixelWidth, PixelHeight);

    private static MagickImage CreateRoundedMask(uint width, uint height)
    {
        var mask = new MagickImage(MagickColors.Transparent, width, height);
        new Drawables()
            .FillColor(MagickColors.White)
            .StrokeColor(MagickColors.Transparent)
            .RoundRectangle(
                0,
                0,
                width - 1,
                height - 1,
                CornerRadius * Scale,
                CornerRadius * Scale)
            .Draw(mask);
        return mask;
    }
}
