using Asura.Application;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Asura.App.Views.Components;

/// <summary>
/// A local attachment thumbnail and bounded, zoomable enlargement. Decodes only
/// visible transcript images and owns their bitmaps until the message leaves the tree.
/// </summary>
public sealed partial class AgentChatImagePreview : UserControl
{
    public static readonly StyledProperty<AgentChatImage?> AttachmentProperty =
        AvaloniaProperty.Register<AgentChatImagePreview, AgentChatImage?>(nameof(Attachment));

    private readonly List<Visual> _ancestors = [];
    private Bitmap? _thumbnail;
    private Rect? _viewport;
    private bool _attempted;
    private long _generation;
    private long _previewGeneration;

    public AgentChatImagePreview()
    {
        InitializeComponent();
        EffectiveViewportChanged += (_, e) =>
        {
            _viewport = e.EffectiveViewport;
            LoadWhenVisible();
        };
    }

    public AgentChatImage? Attachment
    {
        get => GetValue(AttachmentProperty);
        set => SetValue(AttachmentProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AttachmentProperty)
        {
            ReleaseImages();
            var name = Attachment?.FileName ?? "Image";
            Fallback.Text = "Image · " + name;
            PreviewTitle.Text = name;
            ToolTip.SetTip(OpenPreview, name);
            AutomationProperties.SetName(OpenPreview, "Open image preview " + name);
            LoadWhenVisible();
        }
        else if (change.Property == BoundsProperty)
        {
            LoadWhenVisible();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors().Prepend(this))
        {
            _ancestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorVisibilityChanged;
        }
        LoadWhenVisible();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var ancestor in _ancestors)
        {
            ancestor.PropertyChanged -= OnAncestorVisibilityChanged;
        }
        _ancestors.Clear();
        ReleaseImages();
        _viewport = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAncestorVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != IsVisibleProperty)
        {
            return;
        }
        if (sender is Visual { IsVisible: false })
        {
            ReleaseImages();
        }
        else
        {
            LoadWhenVisible();
        }
    }

    private void LoadWhenVisible()
    {
        if (_attempted || VisualRoot is null || !IsEffectivelyVisible
            || _viewport is not { } viewport || !viewport.Intersects(new Rect(Bounds.Size)))
        {
            return;
        }
        _attempted = true;
        _ = LoadThumbnailAsync();
    }

    private async Task LoadThumbnailAsync()
    {
        var generation = _generation;
        var image = await DecodeAsync(Attachment, 540);
        if (generation != _generation)
        {
            image?.Dispose();
            return;
        }
        _thumbnail = image;
        Thumbnail.Source = image;
        Thumbnail.IsVisible = image is not null;
        Fallback.IsVisible = image is null;
        Fallback.Text = "Image preview unavailable · " + Attachment?.FileName;
        OpenPreview.IsEnabled = image is not null;
    }

    private void OnOpenPreviewClick(object? sender, RoutedEventArgs e)
    {
        PreviewPopup.IsOpen = true;
    }

    private async void OnPreviewOpened(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        var generation = ++_previewGeneration;
        var size = TopLevel.GetTopLevel(this)?.Bounds.Size ?? new Size(800, 600);
        PreviewSurface.Width = Math.Max(160, Math.Min(720, size.Width - 64));
        PreviewSurface.Height = Math.Max(160, Math.Min(520, size.Height - 96));
        PreviewStatus.Text = "Loading image…";
        PreviewStatus.IsVisible = true;
        var image = await DecodeAsync(Attachment, OrdinaryImagePreviewDecoder.PreferredMaximumWidth);
        if (generation != _previewGeneration)
        {
            image?.Dispose();
            return;
        }
        EnlargedImage.Source = image;
        PreviewStatus.Text = "Image preview unavailable";
        PreviewStatus.IsVisible = image is null;
    }

    private void OnClosePreviewClick(object? sender, RoutedEventArgs e) => PreviewPopup.IsOpen = false;

    private void OnPreviewClosed(object? sender, EventArgs e) => ReleaseEnlargement();

    private void ReleaseEnlargement()
    {
        _previewGeneration++;
        var image = EnlargedImage.Source;
        EnlargedImage.Source = null;
        image?.Dispose();
    }

    private void ReleaseImages()
    {
        _generation++;
        PreviewPopup.IsOpen = false;
        ReleaseEnlargement();
        Thumbnail.Source = null;
        _thumbnail?.Dispose();
        _thumbnail = null;
        Thumbnail.IsVisible = false;
        Fallback.IsVisible = true;
        OpenPreview.IsEnabled = false;
        _attempted = false;
    }

    private static Task<Bitmap?> DecodeAsync(AgentChatImage? image, int width) => Task.Run(() =>
    {
        if (image?.Attachment is not { } attachment)
        {
            return null;
        }
        try
        {
            using var content = new AttachmentContent(attachment.Content.ToArray());
            return OrdinaryImagePreviewDecoder.Decode(content, width);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        {
            return null;
        }
    });

    private sealed class AttachmentContent(byte[] bytes) : FilePreviewContent
    {
        public override long Length => bytes.Length;
        public override Stream OpenRead() => new MemoryStream(bytes, writable: false);
    }
}
