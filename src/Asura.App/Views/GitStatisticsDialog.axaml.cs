using System.Globalization;
using System.Text.RegularExpressions;
using Asura.App.Controls;
using Asura.Application;
using Asura.Git;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Asura.App.Views;

/// <summary>One row of the contributors table: a commit count and who made the commits.</summary>
public sealed record GitContributorRow(int Commits, string Author)
{
    public string CommitsText => Commits.ToString("N0", CultureInfo.CurrentCulture);

    public string AccessibleName => $"{Author}, {CountLabel.InSentence(Commits, "commit")}";
}

public sealed partial class GitStatisticsDialog : DialogWindow
{
    // The treemap cells take the chart palette in turn: the accent belongs to
    // what can be pressed or is selected, and every folder in it was neither.
    private static readonly string[] CellBrushKeys =
    [
        "ShellChartSeries1",
        "ShellChartSeries2",
        "ShellChartSeries3",
        "ShellChartSeries4",
    ];

    // Below this a cell has no room for its name and size, which its tooltip
    // still carries.
    private const double LabelMinimumWidth = 56;
    private const double LabelMinimumHeight = 34;

    private readonly IReadOnlyList<GitDirectorySize> _directories = [];

    public GitStatisticsDialog() => InitializeComponent();

    public GitStatisticsDialog(GitRepositoryStatistics statistics) : this()
    {
        ArgumentNullException.ThrowIfNull(statistics);
        _directories = [.. statistics.Directories.Where(directory => directory.Bytes > 0)];
        Summary.Text = SummaryText(statistics);
        var contributors = ParseContributors(statistics.Contributors);
        Contributors.ItemsSource = contributors;
        Contributors.IsVisible = contributors.Count > 0;
        NoContributors.IsVisible = contributors.Count == 0;
    }

    /// <summary>The line over the treemap: how many files, and their size in the units the rest of the app uses.</summary>
    internal static string SummaryText(GitRepositoryStatistics statistics) =>
        $"{CountLabel.Format(statistics.Directories.Sum(directory => directory.Files), "tracked file")} · {ByteSize.Format(statistics.Directories.Sum(directory => directory.Bytes))}";

    /// <summary>
    /// The rows of <c>git shortlog -sne</c>: a count, a tab, and the author.
    /// A line that isn't in that shape is left out rather than shown raw.
    /// </summary>
    internal static IReadOnlyList<GitContributorRow> ParseContributors(string? shortlog) =>
        [.. (shortlog ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => ShortlogLine().Match(line))
            .Where(match => match.Success)
            .Select(match => new GitContributorRow(
                int.Parse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture),
                match.Groups["author"].Value.Trim()))];

    [GeneratedRegex(@"^(?<count>\d{1,9})\s+(?<author>\S.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShortlogLine();

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
        var series = CellBrushKeys[Treemap.Children.Count % CellBrushKeys.Length];
        var cellWidth = Math.Max(0, rectangle.Width - 2);
        var cellHeight = Math.Max(0, rectangle.Height - 2);
        var card = new Border
        {
            Width = cellWidth,
            Height = cellHeight,
            Background = this.FindResource(series + "FillBrush") as IBrush,
            BorderBrush = this.FindResource(series + "Brush") as IBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            // The value is on the cell, not only in its tooltip, wherever the
            // name and size fit. The folder keeps its last segment, as Finder
            // truncates a path.
            Child = cellWidth < LabelMinimumWidth || cellHeight < LabelMinimumHeight ? null : new StackPanel
            {
                Margin = new Thickness(4),
                Children =
                {
                    new TextBlock { Text = item.Path, TextTrimming = TextTrimming.PathSegmentEllipsis },
                    new TextBlock { Text = ByteSize.Format(item.Bytes), Classes = { "Muted" }, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
        };
        AutomationProperties.SetName(card, $"{item.Path}, {ByteSize.Format(item.Bytes)}");
        ToolTip.SetTip(card, $"{item.Path}: {CountLabel.Format(item.Files, "file")}, {ByteSize.Format(item.Bytes)}");
        Canvas.SetLeft(card, rectangle.X); Canvas.SetTop(card, rectangle.Y);
        Treemap.Children.Add(card);
    }
}
