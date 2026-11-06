using System.Text;
using System.Text.Json;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// <c>spec/fixtures/model-attribution-cases.json</c> (METRICS 4.1 step 4, 11.3, 11.4 Attribution), as
/// <c>spec/tools/reference.py --check-fixtures</c> checks it: every response and every report ledger increment keeps the
/// model of its own message, never a session's earlier one.
/// </summary>
public sealed class ModelAttributionCaseTests
{
    private static readonly Lazy<JsonElement> Document = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("model-attribution-cases.json"))).RootElement);

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var item in Document.Value.GetProperty("cases").EnumerateArray())
        {
            data.Add(item.GetProperty("name").GetString()!);
        }

        return data;
    }

    [Fact]
    public void EveryCaseIsListed() => Assert.Equal(4, Document.Value.GetProperty("cases").GetArrayLength());

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EachResponseAndIncrementKeepsItsOwnModel(string name)
    {
        var item = Document.Value.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        var table = PriceBook.Bundled;
        var deduper = new Deduper();
        long lastEnd = 0;
        SpeedEngine? engine = null;
        foreach (var file in item.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            var parser = new ClaudeParser("/fixture/" + path, deduper);
            var sink = new RecordingSink();
            foreach (var line in file.GetProperty("lines").EnumerateArray())
            {
                parser.Feed(Encoding.UTF8.GetBytes(line.GetString()!), sink);
            }

            foreach (var (response, isCompletion) in sink.Emissions)
            {
                engine ??= new SpeedEngine(response.EndMs - 3_600_000, new PriceBook());
                lastEnd = Math.Max(lastEnd, response.EndMs);
                engine.Upsert(response, isCompletion, seeded: false, liveNowMs: response.EndMs);
            }

            var got = new Agent(
                ReportBody.SourceId(parser.Source), parser.IsSubagent, parser.Model, table.ReportModelId(parser.Model, null),
                sink.Latest.Select(r => new Response(r.Key, r.Model, r.Tokens.Output, table.ReportModelId(r.Model, r.Provider))).ToList());
            Assert.Equal(Expected(item.GetProperty("expect").GetProperty(path)), got);
        }

        var counters = engine!.Compute(lastEnd, lastEnd - 3_600_000, observeLive: true).Report.Counters;
        var ledger = counters.ByModel
            .Select(kv => new LedgerRow(kv.Key.ModelId, ReportBody.SourceId(kv.Key.Source), kv.Value.Requests, kv.Value.Output))
            .OrderBy(r => r.ModelId, StringComparer.Ordinal).ToList();
        var wanted = item.GetProperty("ledger").EnumerateArray()
            .Select(e => new LedgerRow(e.GetProperty("modelId").GetString()!, e.GetProperty("source").GetString()!,
                e.GetProperty("requests").GetInt64(), e.GetProperty("output").GetInt64()))
            .OrderBy(r => r.ModelId, StringComparer.Ordinal).ToList();
        Assert.Equal(wanted, ledger);
    }

    [Fact]
    public void OpusFiveFiveSpellingsNeverReportOpusFive()
    {
        var pairs = Document.Value.GetProperty("reportIds").EnumerateArray().ToList();
        Assert.NotEmpty(pairs);
        foreach (var pair in pairs)
        {
            var model = pair[0].GetString();
            Assert.True(PriceBook.Bundled.ReportModelId(model, null) == pair[1].GetString(), model);
        }
    }

    private static Agent Expected(JsonElement json) => new(
        json.GetProperty("source").GetString()!,
        json.GetProperty("isSubagent").GetBoolean(),
        json.GetProperty("model").GetString(),
        json.GetProperty("reportModelId").GetString()!,
        json.GetProperty("responses").EnumerateArray().Select(r => new Response(
            r.GetProperty("key").GetString()!, r.GetProperty("model").GetString(), r.GetProperty("output").GetInt64(),
            r.GetProperty("reportModelId").GetString()!)).ToList());

    private sealed record Response(string Key, string? Model, long Output, string ReportModelId);

    private sealed record LedgerRow(string ModelId, string Source, long Requests, long Output);

    private sealed record Agent(string Source, bool IsSubagent, string? Model, string ReportModelId, List<Response> Responses)
    {
        public bool Equals(Agent? other) => other is not null && Source == other.Source && IsSubagent == other.IsSubagent
            && Model == other.Model && ReportModelId == other.ReportModelId && Responses.SequenceEqual(other.Responses);

        public override int GetHashCode() => HashCode.Combine(Source, IsSubagent, Model, ReportModelId, Responses.Count);
    }
}
