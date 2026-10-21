using AgentSpeed.Core;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using static AgentSpeed.Presentation.Tests.TestSupport.HistoryData;

namespace AgentSpeed.Presentation.Tests;

public class HistoryTableTests
{
    private static readonly DaySummary R1 = Summary(D(2026, 10, 17), requests: 5, output: 500, input: 1000, cached: 500,
        measuredOutput: 100, measuredMs: 1000, peak: 300, agents: 2, models: [new ModelUsage("b", 1, 1)]);

    private static readonly DaySummary R2 = Summary(D(2026, 10, 18), requests: 9, output: 100, input: 0, cached: 0,
        measuredMs: 0, peak: null, agents: 5);

    private static readonly DaySummary R3 = Summary(D(2026, 10, 19), requests: 5, output: 900, input: 1000, cached: 900,
        measuredOutput: 50, measuredMs: 1000, peak: 100, agents: 1, models: [new ModelUsage("A", 1, 1)]);

    private static readonly DaySummary R4 = Summary(D(2026, 10, 20), requests: 1, output: 300, input: 1000, cached: 100,
        measuredOutput: 200, measuredMs: 1000, peak: null, agents: 3, models: [new ModelUsage("c", 1, 1)]);

    private static readonly DaySummary[] Rows = [R1, R2, R3, R4];

    public static TheoryData<HistoryColumn, bool, int[]> SortCases => new()
    {
        { HistoryColumn.Date, true, [4, 3, 2, 1] },
        { HistoryColumn.Date, false, [1, 2, 3, 4] },
        { HistoryColumn.Requests, true, [2, 3, 1, 4] },
        { HistoryColumn.Requests, false, [4, 3, 1, 2] },
        { HistoryColumn.Output, true, [3, 1, 4, 2] },
        { HistoryColumn.Output, false, [2, 4, 1, 3] },
        { HistoryColumn.Input, true, [4, 3, 1, 2] },
        { HistoryColumn.Input, false, [2, 4, 3, 1] },
        { HistoryColumn.CacheHit, true, [3, 1, 4, 2] },
        { HistoryColumn.CacheHit, false, [4, 1, 3, 2] },
        { HistoryColumn.Speed, true, [4, 1, 3, 2] },
        { HistoryColumn.Speed, false, [3, 1, 4, 2] },
        { HistoryColumn.Peak, true, [1, 3, 4, 2] },
        { HistoryColumn.Peak, false, [3, 1, 4, 2] },
        { HistoryColumn.Agents, true, [2, 4, 1, 3] },
        { HistoryColumn.Agents, false, [3, 1, 4, 2] },
        { HistoryColumn.Models, false, [3, 1, 4, 2] },
        { HistoryColumn.Models, true, [4, 1, 3, 2] },
    };

    private static int Number(DaySummary row) => Array.IndexOf(Rows, row) + 1;

    [Theory]
    [MemberData(nameof(SortCases))]
    public void SortsEveryColumnWithMissingValuesLast(HistoryColumn column, bool descending, int[] expected)
    {
        Assert.Equal(expected, HistoryTable.Sort(Rows, column, descending).Select(Number));
        Assert.Equal(expected, HistoryTable.Sort(Enumerable.Reverse(Rows), column, descending).Select(Number));
    }

    [Fact]
    public void TiesKeepTheNewestDayFirstInBothDirections()
    {
        DaySummary[] rows =
        [
            Summary(D(2026, 10, 1), requests: 3, models: [new ModelUsage("x", 1, 1)]),
            Summary(D(2026, 10, 3), requests: 3, models: [new ModelUsage("X", 1, 1)]),
            Summary(D(2026, 10, 2), requests: 3, models: [new ModelUsage("x", 1, 1)]),
        ];

        foreach (var column in new[] { HistoryColumn.Requests, HistoryColumn.Models })
        {
            Assert.Equal([D(2026, 10, 3), D(2026, 10, 2), D(2026, 10, 1)], HistoryTable.Sort(rows, column, true).Select(r => r.Day));
            Assert.Equal([D(2026, 10, 3), D(2026, 10, 2), D(2026, 10, 1)], HistoryTable.Sort(rows, column, false).Select(r => r.Day));
        }
    }

    [Fact]
    public void SortingNothingIsEmpty()
    {
        foreach (var column in HistoryTable.Columns)
        {
            Assert.Empty(HistoryTable.Sort([], column, true));
        }
    }

    [Fact]
    public void ColumnsAndTheirDefaults()
    {
        Assert.Equal(
            [
                HistoryColumn.Date, HistoryColumn.Requests, HistoryColumn.Output, HistoryColumn.Input, HistoryColumn.CacheHit,
                HistoryColumn.Speed, HistoryColumn.Peak, HistoryColumn.Agents, HistoryColumn.Models,
            ],
            HistoryTable.Columns);
        Assert.All(HistoryTable.Columns.Where(c => c != HistoryColumn.Models), c => Assert.True(HistoryTable.DefaultDescending(c)));
        Assert.False(HistoryTable.DefaultDescending(HistoryColumn.Models));
        Assert.False(HistoryTable.IsNumeric(HistoryColumn.Date));
        Assert.False(HistoryTable.IsNumeric(HistoryColumn.Models));
        Assert.All(HistoryTable.Columns.Skip(1).SkipLast(1), c => Assert.True(HistoryTable.IsNumeric(c)));
    }

    [Fact]
    public void EveryHeaderIsLocalized()
    {
        foreach (var language in LanguageCodes.All)
        {
            var loc = For(language);
            Assert.All(HistoryTable.Columns, c => Assert.NotEqual(HistoryTable.HeaderKey(c), loc[HistoryTable.HeaderKey(c)]));
        }

        Assert.Equal("日期\t請求\t輸出\t輸入（含快取）\t快取命中\t平均速度\t峰值\tAgent\t主要模型", HistoryTable.HeaderText(For(LanguageCodes.ZhHant)));
        Assert.Equal("Date\tRequests\tOutput\tInput (incl. cache)\tCache hit\tAvg speed\tPeak\tAgents\tTop models",
            HistoryTable.HeaderText(For(LanguageCodes.En)));
    }

    [Fact]
    public void CellsFormatEachColumn()
    {
        var loc = For(LanguageCodes.ZhHant);
        var row = Summary(Today, requests: 12_345, output: 98_765, input: 2_000_000, cached: 1_840_000, measuredOutput: 85_000,
            measuredMs: 1_000_000, peak: 412, agents: 4,
            models: [new ModelUsage("opus", 3, 50_000), new ModelUsage("gpt", 2, 40_000), new ModelUsage("flash", 1, 8_765)]);

        Assert.Equal("今天", HistoryTable.Cell(row, HistoryColumn.Date, Today, loc));
        Assert.Equal("12,345", HistoryTable.Cell(row, HistoryColumn.Requests, Today, loc));
        Assert.Equal("98.8k", HistoryTable.Cell(row, HistoryColumn.Output, Today, loc));
        Assert.Equal("2.00M", HistoryTable.Cell(row, HistoryColumn.Input, Today, loc));
        Assert.Equal("92%", HistoryTable.Cell(row, HistoryColumn.CacheHit, Today, loc));
        Assert.Equal("85 tok/s", HistoryTable.Cell(row, HistoryColumn.Speed, Today, loc));
        Assert.Equal("412 tok/s", HistoryTable.Cell(row, HistoryColumn.Peak, Today, loc));
        Assert.Equal("4", HistoryTable.Cell(row, HistoryColumn.Agents, Today, loc));
        Assert.Equal("opus, gpt +1", HistoryTable.Cell(row, HistoryColumn.Models, Today, loc));

        Assert.Equal("今天\t12,345\t98.8k\t2.00M\t92%\t85 tok/s\t412 tok/s\t4\topus, gpt +1", HistoryTable.RowText(row, Today, loc));
    }

    [Fact]
    public void CellsWithoutValues()
    {
        var loc = For(LanguageCodes.En);
        Assert.Equal("—", HistoryTable.Cell(R2, HistoryColumn.CacheHit, Today, loc));
        Assert.Equal("No measurement", HistoryTable.Cell(R2, HistoryColumn.Speed, Today, loc));
        Assert.Equal("—", HistoryTable.Cell(R2, HistoryColumn.Peak, Today, loc));
        Assert.Equal("—", HistoryTable.Cell(R2, HistoryColumn.Models, Today, loc));
        Assert.Equal("Oct 18, Sun", HistoryTable.Cell(R2, HistoryColumn.Date, Today, loc));
        Assert.Equal(9, HistoryTable.RowText(R2, Today, loc).Split('\t').Length);
    }

    [Fact]
    public void LayoutGivesSpareWidthToWeightedColumns()
    {
        ColumnSpec[] specs = [new(50, 40), new(60, 30), new(100, 80, 1)];
        var spans = HistoryTable.Layout(specs, 300, 10);
        Assert.Equal([new ColumnSpan(0, 50), new ColumnSpan(60, 60), new ColumnSpan(130, 170)], spans);
        Assert.Equal(300, spans[^1].Right, 9);

        ColumnSpec[] weighted = [new(50, 40, 1), new(60, 30), new(100, 80, 3)];
        var shared = HistoryTable.Layout(weighted, 290);
        Assert.Equal(70, shared[0].Width, 9);
        Assert.Equal(60, shared[1].Width, 9);
        Assert.Equal(160, shared[2].Width, 9);
    }

    [Fact]
    public void LayoutWithoutWeightsGivesSpareWidthToTheLastColumn()
    {
        ColumnSpec[] specs = [new(50, 40), new(60, 30), new(100, 80)];
        var spans = HistoryTable.Layout(specs, 250);
        Assert.Equal([50.0, 60.0, 140.0], spans.Select(s => s.Width));
        Assert.Equal([0.0, 50.0, 110.0], spans.Select(s => s.X));
    }

    [Fact]
    public void LayoutShrinksInProportionToSlack()
    {
        ColumnSpec[] specs = [new(50, 40), new(60, 30), new(100, 80, 1)];
        var spans = HistoryTable.Layout(specs, 180);
        Assert.Equal([45.0, 45.0, 90.0], spans.Select(s => s.Width));
        Assert.Equal(180, spans[^1].Right, 9);
    }

    [Fact]
    public void LayoutNeverGoesBelowTheMinimum()
    {
        ColumnSpec[] specs = [new(50, 40), new(60, 30), new(100, 80, 1)];
        Assert.Equal([40.0, 30.0, 80.0], HistoryTable.Layout(specs, 100).Select(s => s.Width));
        Assert.Equal([40.0, 30.0, 80.0], HistoryTable.Layout(specs, double.NaN).Select(s => s.Width));
        Assert.Equal([40.0, 30.0, 80.0], HistoryTable.Layout(specs, -50).Select(s => s.Width));
    }

    [Fact]
    public void LayoutTreatsAMinimumAboveTheNaturalWidthAsTheNaturalWidth()
    {
        ColumnSpec[] specs = [new(20, 60), new(-5, 0)];
        var spans = HistoryTable.Layout(specs, 100);
        Assert.Equal([60.0, 40.0], spans.Select(s => s.Width));
    }

    [Fact]
    public void LayoutCountsGapsBetweenColumnsOnly()
    {
        ColumnSpec[] specs = [new(50, 50), new(50, 50), new(50, 50)];
        var spans = HistoryTable.Layout(specs, 170, 10);
        Assert.Equal([0.0, 60.0, 120.0], spans.Select(s => s.X));
        Assert.Equal(170, spans[^1].Right, 9);
        Assert.Empty(HistoryTable.Layout([], 300, 10));
    }
}
