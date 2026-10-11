using System.ComponentModel;
using System.Globalization;
using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentIcons.Common;

namespace Asura.App.Tests;

/// <summary>
/// The Statistics and Processes panels against the Mac's conventions: charts
/// that state their scale and describe their data, a status mark that does not
/// rely on colour alone, and a process table that sorts, copies and jumps to a
/// typed name.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class MonitorPanelHigConformanceTests
{
    [Theory]
    [InlineData(100d, TimeSeriesUnit.Percent, "100%")]
    [InlineData(5.5d, TimeSeriesUnit.Percent, "5.5%")]
    [InlineData(2147483648d, TimeSeriesUnit.Bytes, "2.1 GB")]
    [InlineData(512d, TimeSeriesUnit.BytesPerSecond, "512 B/s")]
    public void Chart_values_are_written_with_their_unit(double value, TimeSeriesUnit unit, string expected)
    {
        Assert.Equal(expected, TimeSeriesChart.Format(value, unit));
    }

    [Theory]
    [InlineData(1_850_000_000d, 2_000_000_000d)]
    [InlineData(700d, 1000d)]
    [InlineData(300d, 500d)]
    [InlineData(37d, 50d)]
    [InlineData(0d, 1d)]
    public void An_automatic_scale_tops_out_at_a_round_number(double peak, double expected)
    {
        Assert.Equal(expected, TimeSeriesChart.NiceCeiling(peak));
    }

    [Fact]
    public void A_chart_says_which_way_it_is_heading()
    {
        Assert.Equal("rising", TimeSeriesChart.Trend([1, 1, 1, 5, 5, 5]));
        Assert.Equal("falling", TimeSeriesChart.Trend([5, 5, 5, 1, 1, 1]));
        Assert.Equal("steady", TimeSeriesChart.Trend([3, 3, 3, 3]));
    }

    [Fact]
    public void A_chart_describes_its_latest_value_range_and_trend()
    {
        Assert.Equal("No samples yet", TimeSeriesChart.Describe([], TimeSeriesUnit.Percent));
        Assert.Equal("Latest 12%", TimeSeriesChart.Describe([12d], TimeSeriesUnit.Percent));
        Assert.Equal(
            "Latest 30%, lowest 10%, highest 30%, rising",
            TimeSeriesChart.Describe([10d, null, 20d, 30d], TimeSeriesUnit.Percent));
    }

    [Fact]
    public Task A_chart_tells_assistive_technology_its_values_and_scale() => RunAsync(() =>
    {
        var chart = new TimeSeriesChart
        {
            Unit = TimeSeriesUnit.Bytes,
            Values = [1024d * 1024 * 512, 1024d * 1024 * 900],
        };

        Assert.Equal(
            "Latest 943.7 MB, lowest 536.9 MB, highest 943.7 MB, rising",
            AutomationProperties.GetItemStatus(chart));
        Assert.Equal("Scale from 0 to 1 GB", AutomationProperties.GetHelpText(chart));
        Assert.Equal("1 GB", chart.CeilingLabel);

        chart.Maximum = 100;
        chart.Unit = TimeSeriesUnit.Percent;
        Assert.Equal("100%", chart.CeilingLabel);
    });

    [Fact]
    public Task Pointing_at_a_chart_reads_out_the_sample_under_the_pointer() => RunAsync(() =>
    {
        var chart = new TimeSeriesChart
        {
            Width = 300,
            Height = 100,
            Unit = TimeSeriesUnit.Percent,
            Maximum = 100,
            SampleInterval = TimeSpan.FromSeconds(2),
            Values = [10d, 20d, 30d, 40d],
        };
        var window = new Window { Width = 300, Height = 100, Content = chart };
        window.Show();
        try
        {
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(chart.HoverReadout);

            window.MouseMove(new Point(1, 50));
            Assert.Equal(0, chart.HoverIndex);
            Assert.Equal("10% · 6 s ago", chart.HoverReadout);

            window.MouseMove(new Point(299, 50));
            Assert.Equal("40% · now", chart.HoverReadout);
            Assert.Equal("40% · now", ToolTip.GetTip(chart));

            chart.Values = [20d, 30d, 40d, 55d];
            Assert.Equal("55% · now", chart.HoverReadout);
            Assert.Equal("55% · now", ToolTip.GetTip(chart));
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(StatusTone.Success, Symbol.CheckmarkCircle)]
    [InlineData(StatusTone.Warning, Symbol.Warning)]
    [InlineData(StatusTone.Danger, Symbol.ErrorCircle)]
    [InlineData(StatusTone.Neutral, Symbol.Circle)]
    public void Every_status_tone_has_its_own_shape(StatusTone tone, Symbol expected)
    {
        Assert.Equal(expected, StatusToneGlyph.For(tone));
    }

    [Fact]
    public Task A_status_mark_names_its_state_and_adds_a_shape_when_colour_is_not_enough() => RunAsync(() =>
    {
        var mark = new StatusMark { Tone = StatusTone.Danger, Description = "Local unavailable" };
        var window = new Window { Content = mark };
        window.Show();
        try
        {
            Assert.Equal("Local unavailable", AutomationProperties.GetName(mark));
            Assert.Equal("Local unavailable", ToolTip.GetTip(mark));

            window.Resources["ShellDifferentiateWithoutColor"] = false;
            Assert.False(mark.ShowsGlyph);
            Assert.True(mark.Dot.IsVisible);
            Assert.False(mark.Glyph.IsVisible);

            window.Resources["ShellDifferentiateWithoutColor"] = true;
            Assert.True(mark.ShowsGlyph);
            Assert.False(mark.Dot.IsVisible);
            Assert.True(mark.Glyph.IsVisible);
            Assert.Equal(Symbol.ErrorCircle, mark.Glyph.Symbol);

            mark.Tone = StatusTone.Success;
            Assert.Equal(Symbol.CheckmarkCircle, mark.Glyph.Symbol);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Typing_a_name_builds_on_quick_keystrokes_and_starts_over_after_a_pause()
    {
        var buffer = new TypeSelectBuffer(TimeSpan.FromSeconds(1));
        var start = DateTimeOffset.UnixEpoch;

        Assert.Equal("s", buffer.Append("s", start));
        Assert.Equal("ss", buffer.Append("s", start.AddMilliseconds(300)));
        Assert.Equal("ssh", buffer.Append("h", start.AddMilliseconds(600)));
        Assert.Equal("z", buffer.Append("z", start.AddSeconds(3)));
        Assert.Null(buffer.Append("\t", start.AddSeconds(3)));
        Assert.Null(buffer.Append(string.Empty, start.AddSeconds(3)));

        string[] names = ["launchd", "sshd", "Safari", "ssh-agent"];
        Assert.Equal("sshd", TypeSelectBuffer.FindMatch(names, name => name, "SSH"));
        Assert.Equal("Safari", TypeSelectBuffer.FindMatch(names, name => name, "sa"));
        Assert.Null(TypeSelectBuffer.FindMatch(names, name => name, "zsh"));
    }

    [Theory]
    [InlineData("ProcessId", ProcessMonitorSort.ProcessIdAscending)]
    [InlineData("Name", ProcessMonitorSort.NameAscending)]
    [InlineData("Cpu", ProcessMonitorSort.CpuDescending)]
    [InlineData("Memory", ProcessMonitorSort.MemoryDescending)]
    public void A_process_column_header_asks_for_its_starting_order(string column, ProcessMonitorSort expected)
    {
        Assert.Equal(expected, ProcessMonitorRuntimePanelView.SortFor(column));
        Assert.NotNull(ProcessMonitorRuntimePanelView.DirectionFor(column, expected));
    }

    [Fact]
    public void Only_the_sorted_process_column_shows_a_direction()
    {
        Assert.Null(ProcessMonitorRuntimePanelView.SortFor("Started"));
        Assert.Equal(
            System.ComponentModel.ListSortDirection.Descending,
            ProcessMonitorRuntimePanelView.DirectionFor("Cpu", ProcessMonitorSort.CpuDescending));
        Assert.Equal(
            System.ComponentModel.ListSortDirection.Ascending,
            ProcessMonitorRuntimePanelView.DirectionFor("Name", ProcessMonitorSort.NameAscending));
        Assert.Null(ProcessMonitorRuntimePanelView.DirectionFor("Memory", ProcessMonitorSort.CpuDescending));
    }

    [Fact]
    public void Copied_process_rows_keep_their_columns_apart()
    {
        using var culture = new CultureScope("en-US");
        var started = new DateTimeOffset(2026, 10, 7, 9, 30, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new ProcessMonitorEntryViewModel(new ProcessMonitorEntry(42, "sshd", 1.5, 2048, null, started, IsAsura: false)),
            new ProcessMonitorEntryViewModel(new ProcessMonitorEntry(7, "launchd", null, null, null, null, IsAsura: false)),
        };

        var text = ProcessMonitorRuntimePanelView.RowsText(rows);

        Assert.Equal(
            $"42\tsshd\t1.5%\t2 KB\t{DisplayDates.Format(started)}\n7\tlaunchd\t—\tUnavailable\tUnknown\n",
            text);
        Assert.Null(ProcessMonitorRuntimePanelView.RowsText([]));
    }

    [Fact]
    public void Process_start_times_are_written_in_the_readers_culture()
    {
        using var culture = new CultureScope("de-DE");
        var started = new DateTimeOffset(2026, 8, 20, 21, 15, 0, TimeSpan.Zero);
        var row = new ProcessMonitorEntryViewModel(
            new ProcessMonitorEntry(42, "sshd", null, null, null, started, IsAsura: false));

        Assert.Equal(
            started.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE")),
            row.Started);
        Assert.Equal("sshd", row.ToString());
    }

    [Fact]
    public Task VoiceOver_reads_each_process_column_header_by_title_and_sort_order() => RunAsync(() =>
    {
        var processes = new ProcessMonitorRuntimePanelView();
        var window = new Window { Width = 900, Height = 400, Content = processes };
        window.Show();
        try
        {
            var grid = processes.FindControl<DataGrid>("ProcessGrid");
            Assert.NotNull(grid);
            grid.IsVisible = true;
            window.UpdateLayout();

            var names = grid.GetVisualDescendants()
                .OfType<DataGridColumnHeader>()
                .Where(header => header.Content is ProcessColumnHeader)
                .Select(header => ControlAutomationPeer.CreatePeerForElement(header).GetName())
                .ToArray();

            Assert.Contains("CPU, sorted descending", names, StringComparer.Ordinal);
            Assert.Contains("PID", names, StringComparer.Ordinal);
            Assert.Contains("Name", names, StringComparer.Ordinal);
            Assert.Contains("Memory", names, StringComparer.Ordinal);
            Assert.DoesNotContain(names, name => name.Contains("Avalonia", StringComparison.Ordinal));
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(ListSortDirection.Ascending, "Name, sorted ascending")]
    [InlineData(ListSortDirection.Descending, "Name, sorted descending")]
    [InlineData(null, "Name")]
    public void A_process_column_header_states_its_sort_order(ListSortDirection? direction, string expected)
    {
        Assert.Equal(expected, new ProcessColumnHeader("Name", direction).ToString());
    }

    [Fact]
    public Task The_three_panels_load_with_their_mac_controls() => RunAsync(() =>
    {
        var processes = new ProcessMonitorRuntimePanelView();
        var statistics = new StatisticsRuntimePanelView();
        var files = new FileRuntimePanelView();
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Content = new StackPanel { Children = { processes, statistics, files } },
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            var grid = processes.FindControl<DataGrid>("ProcessGrid");
            Assert.NotNull(grid);
            Assert.Equal(DataGridSelectionMode.Extended, grid.SelectionMode);
            Assert.All(
                grid.Columns.Where(column => column.Tag is "ProcessId" or "Cpu" or "Memory"),
                column => Assert.Contains("NumericCell", column.CellStyleClasses, StringComparer.Ordinal));
            Assert.Equal(3, files.GetVisualDescendants().OfType<StatusMark>().Count()
                + statistics.GetVisualDescendants().OfType<StatusMark>().Count()
                + processes.GetVisualDescendants().OfType<StatusMark>().Count());
        }
        finally
        {
            window.Close();
        }
    });

    private static async Task RunAsync(Action action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await session.Dispatch(
                () =>
                {
                    action();
                    return true;
                },
                timeout.Token));
        }
        finally
        {
            await session.DisposeAsync();
        }
    }
}
