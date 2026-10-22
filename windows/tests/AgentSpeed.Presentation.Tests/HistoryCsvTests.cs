using System.Globalization;
using System.Text;
using AgentSpeed.Core;
using AgentSpeed.Presentation.History;
using static AgentSpeed.Presentation.Tests.TestSupport.HistoryData;

namespace AgentSpeed.Presentation.Tests;

public class HistoryCsvTests
{
    private const string Header =
        "date,source,requests,input_tokens,cached_input_tokens,cache_write_input_tokens,output_tokens,reasoning_tokens," +
        "cache_hit_percent,measured_output_tokens,measured_ms,avg_speed_tok_s,peak_tok_s,agents,unfinalized,models";

    private static DaySummary Row(DateOnly day, double? peak = 412.25, params string[] models) => new(
        day, 1234, 2_000_000, 1_500_000, 25_000, 98_765, 4321, 90_000, 720_000, 2, 3, peak,
        models.Select((m, i) => new ModelUsage(m, 10 - i, 1000 - i)).ToArray(), [new SourceOutput(SourceKind.Codex, 98_765)]);

    private static string[] Lines(string csv)
    {
        Assert.EndsWith("\r\n", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", csv.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        return csv[..^2].Split("\r\n");
    }

    [Fact]
    public void HeaderColumnsAreFixedEnglishNames()
    {
        Assert.Equal(Header, string.Join(',', HistoryCsv.Columns));
        Assert.Equal(Header + "\r\n", HistoryCsv.Build([], null));
    }

    [Fact]
    public void RowsUsePlainIntegersAndIsoDates()
    {
        var csv = HistoryCsv.Build([Row(D(2026, 10, 20), 412.25, "opus", "gpt")], null);
        var lines = Lines(csv);
        Assert.Equal(2, lines.Length);
        Assert.Equal(Header, lines[0]);
        Assert.Equal("2026-10-20,all,1234,2000000,1500000,25000,98765,4321,75.0,90000,720000,125.0,412.3,3,2,opus; gpt", lines[1]);
        Assert.Equal(HistoryCsv.Columns.Count, lines[1].Split(',').Length);
    }

    [Fact]
    public void MissingValuesAreEmptyFields()
    {
        var day = new DaySummary(D(2026, 1, 5), 1, 0, 0, 0, 10, 0, 0, 0, 0, 1, null, [], []);
        var line = Lines(HistoryCsv.Build([day], SourceKind.Gemini))[1];
        Assert.Equal("2026-01-05,gemini,1,0,0,0,10,0,,0,0,,,1,0,", line);
    }

    [Fact]
    public void RowsKeepTheOrderTheyAreGiven()
    {
        var lines = Lines(HistoryCsv.Build([Row(D(2026, 10, 1)), Row(D(2026, 10, 21)), Row(D(2026, 9, 1))], SourceKind.Codex));
        Assert.Equal(["2026-10-01", "2026-10-21", "2026-09-01"], lines.Skip(1).Select(l => l[..10]));
        Assert.All(lines.Skip(1), l => Assert.StartsWith(l[..10] + ",codex,", l, StringComparison.Ordinal));
    }

    [Fact]
    public void DecimalsUseADotWhateverTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            var line = Lines(HistoryCsv.Build([Row(D(2026, 10, 20), 412.25, "opus")], null))[1];
            Assert.Contains(",75.0,", line, StringComparison.Ordinal);
            Assert.Contains(",125.0,412.3,", line, StringComparison.Ordinal);
            Assert.Contains(",1234,2000000,", line, StringComparison.Ordinal);
            Assert.DoesNotContain("75,0", line, StringComparison.Ordinal);
            Assert.DoesNotContain("2.000.000", line, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("carriage\rreturn", "\"carriage\rreturn\"")]
    [InlineData("plain-model_1.5", "plain-model_1.5")]
    public void TextFieldsAreQuotedPerRfc4180(string model, string field)
    {
        var csv = HistoryCsv.Build([Row(D(2026, 10, 20), null, model)], null);
        Assert.EndsWith("," + field + "\r\n", csv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("=a,b", "\"'=a,b\"")]
    public void FormulaLikeTextIsDefused(string model, string field)
    {
        var csv = HistoryCsv.Build([Row(D(2026, 10, 20), null, model)], null);
        Assert.EndsWith("," + field + "\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheLeadingCharacterMatters()
    {
        var csv = HistoryCsv.Build([Row(D(2026, 10, 20), null, "opus", "=gpt")], null);
        Assert.EndsWith(",opus; =gpt\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeAddsTheUtf8ByteOrderMark()
    {
        const string csv = "date,models\r\n2026-10-20,模型 ü\r\n";
        var bytes = HistoryCsv.Encode(csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(csv, new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, HistoryCsv.Encode(string.Empty));
    }

    [Fact]
    public void FileNameNamesTheSourceAndRange()
    {
        var range = new DayRange(D(2026, 9, 22), D(2026, 10, 21));
        Assert.Equal("agent-speed-history-all-2026-09-22-2026-10-21.csv", HistoryCsv.FileName(range, null));
        Assert.Equal("agent-speed-history-claude-code-2026-09-22-2026-10-21.csv", HistoryCsv.FileName(range, SourceKind.ClaudeCode));
    }

    [Theory]
    [InlineData(null, "all")]
    [InlineData(SourceKind.Codex, "codex")]
    [InlineData(SourceKind.ClaudeCode, "claude-code")]
    [InlineData(SourceKind.ClaudeDesktop, "claude-desktop")]
    [InlineData(SourceKind.DeepSeek, "deepseek")]
    [InlineData(SourceKind.OpenCode, "opencode")]
    [InlineData(SourceKind.Gemini, "gemini")]
    [InlineData(SourceKind.Omp, "omp")]
    [InlineData(SourceKind.Antigravity, "antigravity")]
    [InlineData((SourceKind)99, "unknown")]
    public void SourceIdsAreStable(SourceKind? source, string id)
    {
        Assert.Equal(id, HistoryCsv.SourceId(source));
    }

    [Fact]
    public void EverySourceHasItsOwnId()
    {
        var ids = Enum.GetValues<SourceKind>().Select(s => HistoryCsv.SourceId(s)).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain("unknown", ids);
        Assert.DoesNotContain("all", ids);
    }
}
