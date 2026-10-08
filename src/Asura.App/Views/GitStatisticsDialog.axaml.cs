using Asura.App.Controls;
using Asura.Application;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Asura.App.Views;

public sealed partial class GitStatisticsDialog : DialogWindow
{
    private readonly IReadOnlyList<GitDirectorySize> _directories = [];

    public GitStatisticsDialog() => InitializeComponent();

    public GitStatisticsDialog(GitRepositoryStatistics statistics) : this()
    {
        _directories = [.. statistics.Directories.Where(directory => directory.Bytes > 0)];
        Summary.Text = $"{CountLabel.Format(statistics.Directories.Sum(directory => directory.Files), "tracked file")} · {CountLabel.Format(statistics.Directories.Sum(directory => directory.Bytes), "byte")}";
        Contributors.Text = statistics.Contributors;
    }

    private void OnTreemapSize(object? sender, SizeChangedEventArgs e)
    {
        Treemap.Children.Clear();
        Draw(_directories, new Rect(e.NewSize));
    }

    private void Draw(IReadOnlyList<GitDirectorySize> directories, Rect rectangle)
    {
        if (directories.Count == 0)
        {
            return;
        }

        if (directories.Count > 1)
        {
            var split = Math.Max(1, directories.Count / 2);
            var left = directories.Take(split).ToArray();
            var right = directories.Skip(split).ToArray();
            var proportion = (double)left.Sum(directory => directory.Bytes) / directories.Sum(directory => directory.Bytes);
            if (rectangle.Width >= rectangle.Height)
            {
                var width = rectangle.Width * proportion;
                Draw(left, new Rect(rectangle.X, rectangle.Y, width, rectangle.Height));
                Draw(right, new Rect(rectangle.X + width, rectangle.Y, rectangle.Width - width, rectangle.Height));
            }
            else
            {
                var height = rectangle.Height * proportion;
                Draw(left, new Rect(rectangle.X, rectangle.Y, rectangle.Width, height));
                Draw(right, new Rect(rectangle.X, rectangle.Y + height, rectangle.Width, rectangle.Height - height));
            }
            return;
        }
        var item = directories[0];
        var card = new Border
        {
            Width = Math.Max(0, rectangle.Width - 2),
            Height = Math.Max(0, rectangle.Height - 2),
            Background = this.FindResource("ShellAccentSoftBrush") as IBrush,
            BorderBrush = this.FindResource("ShellBorderBrush") as IBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            // The folder keeps its last segment, as Finder truncates a path.
            Child = new StackPanel
            {
                Margin = new Thickness(4),
                Children =
                {
                    new TextBlock { Text = item.Path, TextTrimming = TextTrimming.PathSegmentEllipsis },
                    new TextBlock { Text = ByteSize.Format(item.Bytes), TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
        };
        ToolTip.SetTip(card, $"{item.Path}: {CountLabel.Format(item.Files, "file")}, {ByteSize.Format(item.Bytes)}");
        Canvas.SetLeft(card, rectangle.X); Canvas.SetTop(card, rectangle.Y);
        Treemap.Children.Add(card);
    }
}
