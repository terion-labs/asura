using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Asura.App.Views;

public sealed partial class GitStatisticsDialog : Window
{
    private readonly IReadOnlyList<GitDirectorySize> _directories = [];

    public GitStatisticsDialog() => InitializeComponent();

    public GitStatisticsDialog(GitRepositoryStatistics statistics) : this()
    {
        _directories = [.. statistics.Directories.Where(directory => directory.Bytes > 0)];
        Summary.Text = $"{statistics.Directories.Sum(directory => directory.Files)} tracked files · {statistics.Directories.Sum(directory => directory.Bytes):N0} bytes";
        Contributors.Text = statistics.Contributors;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
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
            Child = new TextBlock { Text = $"{item.Path}\n{item.Bytes:N0} bytes", TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4) }
        };
        ToolTip.SetTip(card, $"{item.Path}: {item.Files} files, {item.Bytes:N0} bytes");
        Canvas.SetLeft(card, rectangle.X); Canvas.SetTop(card, rectangle.Y);
        Treemap.Children.Add(card);
    }
}
