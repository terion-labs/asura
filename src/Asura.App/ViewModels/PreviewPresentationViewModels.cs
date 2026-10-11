using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Asura.Application.Previews;

namespace Asura.App.ViewModels;

/// <summary>
/// The choice the current format offers between its preview and its source,
/// drawn as a two-segment control. Changing it re-reads the bytes already in
/// hand rather than asking the provider for them again.
/// </summary>
public sealed class PreviewToggleViewModel : ObservableObject
{
    /// <summary>The segment that shows the format laid out.</summary>
    public const int PreviewSegment = 0;

    /// <summary>The segment that shows the file as it is written.</summary>
    public const int SourceSegment = 1;

    private readonly Action<string, bool> _changed;
    private readonly bool _onShowsSource;
    private bool _isOn;

    public PreviewToggleViewModel(FilePreviewToggle toggle, Action<string, bool> changed)
    {
        ArgumentNullException.ThrowIfNull(toggle);
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Id = toggle.Id;
        Label = toggle.Label;
        _onShowsSource = toggle.OnShowsSource;
        _isOn = toggle.IsOn;
    }

    public string Id { get; }

    /// <summary>The source segment's name: "Source", or "Hex".</summary>
    public string Label { get; }

    public static string PreviewLabel => "Preview";

    /// <summary>The control's spoken name, naming both sides.</summary>
    public string AccessibleName => $"Show preview or {Label.ToLowerInvariant()}";

    /// <summary>The underlying switch, in the previewer's own polarity.</summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
            {
                OnPropertyChanged(nameof(ShowsSource));
                OnPropertyChanged(nameof(SelectedSegment));
                _changed(Id, value);
            }
        }
    }

    /// <summary>Whether the source side is chosen, whatever the switch is called.</summary>
    public bool ShowsSource
    {
        get => _isOn == _onShowsSource;
        set => IsOn = value == _onShowsSource;
    }

    /// <summary>The chosen segment: <see cref="PreviewSegment"/> or <see cref="SourceSegment"/>.</summary>
    public int SelectedSegment
    {
        get => ShowsSource ? SourceSegment : PreviewSegment;
        set
        {
            if (value is PreviewSegment or SourceSegment)
            {
                ShowsSource = value == SourceSegment;
            }
        }
    }
}

/// <summary>
/// A delimited file shown as the table it describes. Column widths are
/// measured from the content once, so the header and every row line up without
/// each cell negotiating its own width.
/// </summary>
public sealed class PreviewTableViewModel
{
    /// <summary>Bounds a column measured from an unusually long value.</summary>
    private const double MinimumColumnWidth = 72;
    private const double MaximumColumnWidth = 320;
    private const double CharacterWidth = 7.2;

    private readonly IReadOnlyList<PreviewTableRowViewModel> _rows;

    public PreviewTableViewModel(TablePreviewRendering rendering)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        Summary = rendering.Summary;
        var widths = MeasureColumns(rendering);
        Columns = [.. rendering.Columns
            .Select((name, index) => new PreviewTableColumnViewModel(
                name.Length == 0 ? $"Column {index + 1}" : name,
                widths[index]))];
        _rows = [.. rendering.Rows.Select((cells, index) => new PreviewTableRowViewModel(index + 1, cells, widths))];
    }

    public string Summary { get; }

    public IReadOnlyList<PreviewTableColumnViewModel> Columns { get; }

    /// <summary>
    /// Filled a handful of rows at a time, so a table arrives without the panel
    /// stopping to attach all of it.
    /// </summary>
    public ObservableCollection<PreviewTableRowViewModel> Rows { get; } = [];

    public Task FillAsync(CancellationToken cancellationToken) =>
        IncrementalFill.FillAsync(Rows, _rows, cancellationToken);

    private static double[] MeasureColumns(TablePreviewRendering rendering)
    {
        var widths = new double[rendering.Columns.Count];
        for (var index = 0; index < widths.Length; index++)
        {
            var longest = rendering.Columns[index].Length;
            foreach (var row in rendering.Rows)
            {
                if (index < row.Count && row[index].Length > longest)
                {
                    longest = row[index].Length;
                }
            }

            widths[index] = Math.Clamp(
                (longest * CharacterWidth) + 16,
                MinimumColumnWidth,
                MaximumColumnWidth);
        }

        return widths;
    }
}

public sealed record PreviewTableColumnViewModel(string Name, double Width);

public sealed class PreviewTableRowViewModel
{
    public PreviewTableRowViewModel(
        int number,
        IReadOnlyList<string> cells,
        IReadOnlyList<double> widths)
    {
        Number = number;
        Cells = [.. widths
            .Select((width, index) => new PreviewTableCellViewModel(
                index < cells.Count ? cells[index] : string.Empty,
                width))];
    }

    public int Number { get; }

    public bool IsEven => Number % 2 == 0;

    public IReadOnlyList<PreviewTableCellViewModel> Cells { get; }
}

public sealed record PreviewTableCellViewModel(string Text, double Width);

/// <summary>
/// A file's bytes, arriving a handful of rows at a time, sixteen to a row
/// where there is room and eight where there is not, so a narrow preview
/// still shows every byte of a row and its characters without scrolling.
/// </summary>
public sealed class PreviewHexViewModel : ObservableObject
{
    public const int WideRow = 16;
    public const int NarrowRow = 8;

    private readonly IReadOnlyList<HexPreviewRow> _rows;
    private ObservableCollection<HexPreviewRow> _shown = [];
    private int _bytesPerRow = WideRow;

    public PreviewHexViewModel(HexPreviewRendering rendering)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rows = rendering.Rows;
        Summary = rendering.Summary;
    }

    public string Summary { get; }

    public ObservableCollection<HexPreviewRow> Rows
    {
        get => _shown;
        private set => SetProperty(ref _shown, value);
    }

    /// <summary>
    /// Sixteen or eight. Changing it re-lays the rows already shown at once;
    /// the dump is bounded, so this costs one pass over it.
    /// </summary>
    public int BytesPerRow
    {
        get => _bytesPerRow;
        set
        {
            var bytes = value <= NarrowRow ? NarrowRow : WideRow;
            if (bytes == _bytesPerRow)
            {
                return;
            }

            _bytesPerRow = bytes;
            OnPropertyChanged();
            // Rows not yet attached are laid out by the fill that attaches
            // them; rows already on screen are laid out again, all of them.
            if (Rows.Count > 0)
            {
                Rows = new ObservableCollection<HexPreviewRow>(_rows.SelectMany(row => Lay(row, bytes)));
            }
        }
    }

    public Task FillAsync(CancellationToken cancellationToken) =>
        _bytesPerRow == WideRow
            ? IncrementalFill.FillAsync(Rows, _rows, cancellationToken)
            : IncrementalFill.FillAsync(Rows, [.. _rows.SelectMany(row => Lay(row, NarrowRow))], cancellationToken);

    /// <summary>A sixteen-byte row as itself, or as the two eight-byte rows it holds.</summary>
    internal static IEnumerable<HexPreviewRow> Lay(HexPreviewRow row, int bytesPerRow)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (bytesPerRow >= WideRow)
        {
            yield return row;
            yield break;
        }

        // "XX XX XX XX XX XX XX XX  XX …": eight bytes are 23 characters,
        // then the two-space gutter between the halves.
        const int HalfWidth = (NarrowRow * 3) - 1;
        yield return new HexPreviewRow(
            row.Offset,
            row.Bytes.Length > HalfWidth ? row.Bytes[..HalfWidth] : row.Bytes,
            row.Characters.Length > NarrowRow ? row.Characters[..NarrowRow] : row.Characters);
        if (row.Bytes.Length > HalfWidth + 2 && row.Characters.Length > NarrowRow)
        {
            var offset = long.Parse(row.Offset, NumberStyles.HexNumber, CultureInfo.InvariantCulture) + NarrowRow;
            yield return new HexPreviewRow(
                offset.ToString("X8", CultureInfo.InvariantCulture),
                row.Bytes[(HalfWidth + 2)..],
                row.Characters[NarrowRow..]);
        }
    }

}

/// <summary>An archive's contents, as the folders its entry paths describe.</summary>
public sealed class PreviewTreeViewModel
{
    private readonly IReadOnlyList<PreviewTreeNodeViewModel> _nodes;

    public PreviewTreeViewModel(IReadOnlyList<PreviewTreeNode> roots, string summary)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Summary = summary;
        // Opened at the top level: an archive with a single wrapping folder —
        // the common shape — should not need a click to show anything at all.
        _nodes = [.. roots.Select(node => new PreviewTreeNodeViewModel(node, roots.Count == 1))];
    }

    public string Summary { get; }

    public ObservableCollection<PreviewTreeNodeViewModel> Nodes { get; } = [];

    public Task FillAsync(CancellationToken cancellationToken) =>
        IncrementalFill.FillAsync(Nodes, _nodes, cancellationToken);
}

public sealed class PreviewTreeNodeViewModel : ObservableObject
{
    private bool _isExpanded;

    public PreviewTreeNodeViewModel(PreviewTreeNode node, bool expanded = false)
    {
        ArgumentNullException.ThrowIfNull(node);
        Name = node.Name;
        Detail = node.Detail;
        IsContainer = node.IsContainer;
        _isExpanded = expanded && node.IsContainer;
        Children = [.. node.Children.Select(child => new PreviewTreeNodeViewModel(child))];
    }

    public string Name { get; }

    public string? Detail { get; }

    public bool IsContainer { get; }

    public IReadOnlyList<PreviewTreeNodeViewModel> Children { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
