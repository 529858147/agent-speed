using AgentSpeed.Presentation.History;
using static AgentSpeed.Presentation.Tests.TestSupport.HistoryData;

namespace AgentSpeed.Presentation.Tests;

public class HistoryRangeTests
{
    [Fact]
    public void ConstructorOrdersTheEnds()
    {
        var range = new DayRange(D(2026, 10, 21), D(2026, 10, 1));
        Assert.Equal(D(2026, 10, 1), range.Start);
        Assert.Equal(D(2026, 10, 21), range.End);
        Assert.Equal(21, range.DayCount);
    }

    [Fact]
    public void PositionsInsideTheRange()
    {
        var range = new DayRange(D(2026, 9, 22), D(2026, 10, 21));
        Assert.Equal(30, range.DayCount);
        Assert.True(range.Contains(D(2026, 9, 22)));
        Assert.True(range.Contains(D(2026, 10, 21)));
        Assert.False(range.Contains(D(2026, 9, 21)));
        Assert.False(range.Contains(D(2026, 10, 22)));
        Assert.Equal(0, range.IndexOf(D(2026, 9, 22)));
        Assert.Equal(9, range.IndexOf(D(2026, 10, 1)));
        Assert.Equal(-1, range.IndexOf(D(2026, 9, 21)));
        Assert.Equal(D(2026, 10, 1), range.DayAt(9));
        Assert.Equal(D(2026, 10, 21), range.DayAt(range.DayCount - 1));
    }

    [Fact]
    public void SingleDay()
    {
        var range = DayRange.Single(Today);
        Assert.Equal(Today, range.Start);
        Assert.Equal(Today, range.End);
        Assert.Equal(1, range.DayCount);
    }

    [Theory]
    [InlineData(HistoryRangeKind.Days7, 2026, 10, 15, 7)]
    [InlineData(HistoryRangeKind.Days30, 2026, 9, 22, 30)]
    [InlineData(HistoryRangeKind.Days90, 2026, 7, 24, 90)]
    [InlineData(HistoryRangeKind.Year, 2026, 1, 1, 294)]
    public void PresetsEndToday(HistoryRangeKind kind, int year, int month, int day, int count)
    {
        var range = HistoryRanges.Resolve(kind, Today, D(2025, 1, 1));
        Assert.Equal(D(year, month, day), range.Start);
        Assert.Equal(Today, range.End);
        Assert.Equal(count, range.DayCount);
    }

    [Fact]
    public void YearOnNewYearsDayIsOneDay()
    {
        var range = HistoryRanges.Resolve(HistoryRangeKind.Year, D(2027, 1, 1), null);
        Assert.Equal(DayRange.Single(D(2027, 1, 1)), range);
    }

    [Fact]
    public void AllStartsAtTheFirstDayWithData()
    {
        Assert.Equal(new DayRange(D(2026, 3, 5), Today), HistoryRanges.Resolve(HistoryRangeKind.All, Today, D(2026, 3, 5)));
        Assert.Equal(DayRange.Single(Today), HistoryRanges.Resolve(HistoryRangeKind.All, Today, null));
        Assert.Equal(DayRange.Single(Today), HistoryRanges.Resolve(HistoryRangeKind.All, Today, Today));
        // Data stamped in the future (clock changes) never moves the range past today.
        Assert.Equal(DayRange.Single(Today), HistoryRanges.Resolve(HistoryRangeKind.All, Today, D(2026, 11, 2)));
    }

    [Fact]
    public void CustomUsesTheChosenRangeOrFallsBackToThirtyDays()
    {
        var chosen = new DayRange(D(2026, 5, 1), D(2026, 5, 31));
        Assert.Equal(chosen, HistoryRanges.Resolve(HistoryRangeKind.Custom, Today, D(2026, 1, 1), chosen));
        Assert.Equal(HistoryRanges.Resolve(HistoryRangeKind.Days30, Today, null), HistoryRanges.Resolve(HistoryRangeKind.Custom, Today, null));
    }

    [Theory]
    [InlineData(HistoryRangeKind.Days7)]
    [InlineData(HistoryRangeKind.Days30)]
    [InlineData(HistoryRangeKind.Days90)]
    [InlineData(HistoryRangeKind.Year)]
    [InlineData(HistoryRangeKind.All)]
    public void PresetsIgnoreTheCustomRange(HistoryRangeKind kind)
    {
        var custom = new DayRange(D(2020, 1, 1), D(2020, 1, 2));
        Assert.Equal(HistoryRanges.Resolve(kind, Today, D(2026, 2, 1)), HistoryRanges.Resolve(kind, Today, D(2026, 2, 1), custom));
    }

    [Fact]
    public void PreviousPeriodHasTheSameLengthAndEndsTheDayBefore()
    {
        var range = new DayRange(D(2026, 9, 22), D(2026, 10, 21));
        var previous = HistoryRanges.Previous(range);
        Assert.Equal(new DayRange(D(2026, 8, 23), D(2026, 9, 21)), previous);
        Assert.Equal(range.DayCount, previous.DayCount);

        Assert.Equal(DayRange.Single(D(2026, 10, 20)), HistoryRanges.Previous(DayRange.Single(Today)));

        var year = HistoryRanges.Resolve(HistoryRangeKind.Year, Today, null);
        var beforeYear = HistoryRanges.Previous(year);
        Assert.Equal(D(2025, 12, 31), beforeYear.End);
        Assert.Equal(year.DayCount, beforeYear.DayCount);
    }

    [Fact]
    public void ClampKeepsTheRangeInsideTheData()
    {
        var minimum = D(2026, 3, 1);
        var maximum = Today;
        Assert.Equal(new DayRange(minimum, maximum), HistoryRanges.Clamp(D(2026, 1, 1), D(2026, 12, 31), minimum, maximum));
        Assert.Equal(new DayRange(D(2026, 4, 1), D(2026, 5, 1)), HistoryRanges.Clamp(D(2026, 4, 1), D(2026, 5, 1), minimum, maximum));
    }

    [Fact]
    public void ClampNeverEndsBeforeTheStart()
    {
        var range = HistoryRanges.Clamp(D(2026, 10, 10), D(2026, 10, 1), D(2026, 3, 1), Today);
        Assert.Equal(DayRange.Single(D(2026, 10, 10)), range);

        var bothLate = HistoryRanges.Clamp(D(2027, 1, 1), D(2027, 2, 1), D(2026, 3, 1), Today);
        Assert.Equal(DayRange.Single(Today), bothLate);
    }

    [Fact]
    public void ClampAcceptsSwappedBounds()
    {
        var range = HistoryRanges.Clamp(D(2026, 1, 1), D(2026, 12, 31), Today, D(2026, 3, 1));
        Assert.Equal(new DayRange(D(2026, 3, 1), Today), range);
    }

    [Theory]
    [InlineData("2026-10-21", true)]
    [InlineData("2024-02-29", true)]
    [InlineData("2026-02-29", false)]
    [InlineData("2026-13-01", false)]
    [InlineData("2026/10/21", false)]
    [InlineData("2026-1-5", false)]
    [InlineData(" 2026-10-21", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParsesStoredDaysStrictly(string? text, bool valid)
    {
        Assert.Equal(valid, HistoryRanges.TryParseDay(text, out var day));
        if (valid)
        {
            Assert.Equal(text, HistoryRanges.FormatDay(day));
        }
    }

    [Fact]
    public void FormatDayIsIsoWhateverTheCulture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal("2026-01-05", HistoryRanges.FormatDay(D(2026, 1, 5)));
            Assert.True(HistoryRanges.TryParseDay("2026-01-05", out var day));
            Assert.Equal(D(2026, 1, 5), day);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void EveryKindHasALabel()
    {
        Assert.Equal(
            [HistoryRangeKind.Days7, HistoryRangeKind.Days30, HistoryRangeKind.Days90, HistoryRangeKind.Year, HistoryRangeKind.All],
            HistoryRanges.Presets);
        var loc = For(Localization.LanguageCodes.ZhHant);
        Assert.Equal("7 天", loc[HistoryRanges.LabelKey(HistoryRangeKind.Days7)]);
        Assert.Equal("30 天", loc[HistoryRanges.LabelKey(HistoryRangeKind.Days30)]);
        Assert.Equal("90 天", loc[HistoryRanges.LabelKey(HistoryRangeKind.Days90)]);
        Assert.Equal("今年", loc[HistoryRanges.LabelKey(HistoryRangeKind.Year)]);
        Assert.Equal("全部", loc[HistoryRanges.LabelKey(HistoryRangeKind.All)]);
        Assert.Equal("自訂…", loc[HistoryRanges.LabelKey(HistoryRangeKind.Custom)]);
    }
}
