using System.Text.Json.Nodes;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;
using AgentSpeed.Core.Unpriced;

namespace AgentSpeed.Core.Tests;

/// <summary>spec/fixtures/unpriced-cases.json (METRICS 9.10): every case group, as <c>spec/tools/unpriced.py</c> checks them.</summary>
public sealed class UnpricedCaseTests
{
    private static readonly JsonNode Cases =
        JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureLocator.FixturesDirectory, "unpriced-cases.json")))!;

    private static readonly PriceTable Current = Table(Cases["table"]!.GetValue<string>());

    [Fact]
    public void EveryCaseGroupIsPresent()
    {
        Assert.Equal((24, 7, 17, 1, 6), (Count("diagnose"), Count("read"), Count("quote"), Count("live"), Count("reports")));
    }

    [Fact]
    public void DiagnoseCases()
    {
        foreach (var c in Cases["diagnose"]!.AsArray())
        {
            var model = c!["model"]?.GetValue<string>();
            var provider = c["provider"]?.GetValue<string>();
            var fast = c["fast"]?.GetValue<bool>() ?? false;
            var got = Current.Resolver.Diagnose(model, provider, fast);
            var priced = Current.Cost(model, provider, new TokenCounts(Input: 1000, Output: 100), null, fast);
            Assert.True((got is null) == (priced is not null), $"{model}: diagnose and cost disagree");
            var expect = c["expect"];
            if (expect is null)
            {
                Assert.Null(got);
                continue;
            }

            Assert.NotNull(got);
            Assert.Equal(
                (expect["reason"]!.GetValue<string>(), expect["canonical"]?.GetValue<string>(), expect["rule"]?.GetValue<string>()),
                (UnpricedReasons.Name(got.Reason), got.Canonical, got.Rule));
        }
    }

    [Fact]
    public void ReadCases()
    {
        foreach (var c in Cases["read"]!.AsArray())
        {
            var stored = c!["stored"]!;
            var got = Read(stored);
            var expect = c["expect"]!;
            var name = c["name"]!.GetValue<string>();
            Assert.Equal(expect["requests"]!.GetValue<long>(), stored["requests"]!.GetValue<long>());
            Assert.Equal((expect["noCostData"]!.GetValue<long>(), expect["derived"]!.GetValue<long>()), (got.NoCostData, got.Derived));
            Assert.True(JsonNode.DeepEquals(expect["facts"], FactsJson(got.Facts)), $"{name}: {FactsJson(got.Facts).ToJsonString()}");
        }
    }

    [Fact]
    public void QuoteCases()
    {
        foreach (var c in Cases["quote"]!.AsArray())
        {
            var text = c!["text"]!.GetValue<string>();
            Assert.Equal(c["expect"]!.GetValue<string>(), UnpricedText.Quote(text, c["redact"]?.GetValue<bool>() ?? true));
        }
    }

    [Fact]
    public void QuoteReplacesLoneSurrogates()
    {
        Assert.Equal("\"a�b�\"", UnpricedText.Quote("a\uD800b\uDC00"));
        Assert.Equal("null", UnpricedText.Quote(null));
    }

    [Fact]
    public void LiveCasesFollowTheDayLedger()
    {
        foreach (var c in Cases["live"]!.AsArray())
        {
            var parts = LiveParts(c!);
            Assert.True(JsonNode.DeepEquals(c!["expect"], PartsJson(parts)), PartsJson(parts).ToJsonString());
        }
    }

    [Fact]
    public void LiveFactsMoveWithReplacementsAndRepricingAndRestartAtTheDayChange()
    {
        var start = DateTimeOffset.Parse("2026-10-26T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var ledger = new TodayLedger(start);
        ledger.Upsert(Response("a", "neko-model-1", "custom", start + 1000, null));
        ledger.Upsert(Response("b", "neko-model-1", "custom", start + 2000, null));
        Assert.Equal([new UnpricedCount(new UnpricedFact("neko-model-1", "custom", false), 2)], ledger.UnpricedFacts(SourceKind.Codex));

        ledger.Upsert(Response("b", "gpt-5.5", null, start + 2500, 900L));
        Assert.Equal([new UnpricedCount(new UnpricedFact("neko-model-1", "custom", false), 1)], ledger.UnpricedFacts(SourceKind.Codex));

        ledger.Upsert(Response("a", "neko-model-1", "custom", start + 1000, 40));
        Assert.Empty(ledger.UnpricedFacts(SourceKind.Codex));

        ledger.Upsert(Response("c", " x ", "", start + 3000, null));
        Assert.Equal([new UnpricedCount(new UnpricedFact("x", null, false), 1)], ledger.UnpricedFacts(SourceKind.Codex));
        ledger.Reset(start + 86_400_000);
        Assert.Empty(ledger.UnpricedFacts(SourceKind.Codex));
    }

    [Fact]
    public void ReportCasesAreByteIdentical()
    {
        foreach (var c in Cases["reports"]!.AsArray())
        {
            var name = c!["name"]!.GetValue<string>();
            var table = c["table"] is { } path ? Table(path.GetValue<string>()) : Current;
            var view = c["view"]!;
            var app = c["app"]!;
            IReadOnlyList<UnpricedPart> parts = view["view"]!.GetValue<string>() == "live"
                ? LiveParts(Cases["live"]!.AsArray().First(l => l!["name"]!.GetValue<string>() == c["input"]!.GetValue<string>())!)
                : StoredParts();
            var source = view["source"] is { } s ? SourceNames.Parse(s.GetValue<string>()) : null;
            var excluded = SourceSet.From((view["excluded"]?.AsArray() ?? []).Select(e => SourceNames.Parse(e!.GetValue<string>())!.Value));
            var first = view["first"]!.GetValue<string>();
            var last = view["last"]!.GetValue<string>();
            var built = UnpricedView.Build(parts, table, first, last, source, excluded);
            var text = UnpricedText.Render(built, new UnpricedApp(app["version"]!.GetValue<string>(), app["platform"]!.GetValue<string>(),
                app["os"]!.GetValue<string>()), table, new UnpricedRange(view["view"]!.GetValue<string>(), first, last, source, excluded));
            Assert.True(c["expect"]!.GetValue<string>() == text, $"{name}:\n{text}");
            foreach (var group in built.Groups)
            {
                Assert.Equal(group.Unpriced, group.RowRequests + group.PricedNow);
            }
        }
    }

    internal static UnpricedReading Read(JsonNode stored) => UnpricedReading.Read(
        stored["fileVersion"]!.GetValue<int>(),
        stored["requests"]!.GetValue<long>(),
        stored["unpriced"]?.GetValue<long>(),
        stored["models"]!.AsObject().Select(m => KeyValuePair.Create(m.Key, m.Value?.GetValue<long>())),
        [.. stored["side"]!.AsArray().Select(r => new UnpricedSideRow(r!["model"]!.GetValue<string>(), r["provider"]?.GetValue<string>(),
            r["fast"]!.GetValue<bool>(), r["requests"]!.GetValue<long>(), r["version"]!.GetValue<int>()))]);

    internal static JsonArray FactsJson(IEnumerable<UnpricedCount> facts) => new([.. facts.Select(f =>
    {
        var node = new JsonObject { ["model"] = f.Fact.Model, ["provider"] = f.Fact.Provider, ["fast"] = f.Fact.Fast };
        if (f.Fact.NoDate)
        {
            node["noDate"] = true;
        }

        node["requests"] = f.Requests;
        return (JsonNode)node;
    })]);

    private static int Count(string group) => Cases[group]!.AsArray().Count;

    private static PriceTable Table(string relative) => PriceTable.Load(File.ReadAllBytes(FixtureLocator.PathOf(relative))).Table!;

    /// <summary>The stored contributions after <i>Reading</i>, summed per day and source like the history's daily rows.</summary>
    private static List<UnpricedPart> StoredParts()
    {
        var merged = new Dictionary<(string Day, SourceKind Source), DayAggregate>();
        var requests = new Dictionary<(string, SourceKind), long>();
        foreach (var entry in Cases["stored"]!.AsArray())
        {
            var key = (entry!["day"]!.GetValue<string>(), SourceNames.Parse(entry["source"]!.GetValue<string>())!.Value);
            var aggregate = new DayAggregate { Requests = entry["stored"]!["requests"]!.GetValue<long>() };
            aggregate.SetUnpriced(Read(entry["stored"]!));
            if (!merged.TryGetValue(key, out var total))
            {
                merged[key] = total = new DayAggregate();
            }

            total.Merge(aggregate);
            requests[key] = requests.GetValueOrDefault(key) + aggregate.Requests;
        }

        return [.. merged.Select(m => new UnpricedPart(m.Key.Day, m.Key.Source, requests[m.Key], m.Value.NoCostData, m.Value.Derived,
            UnpricedFacts.Sorted(m.Value.Unpriced)))];
    }

    private static List<UnpricedPart> LiveParts(JsonNode c)
    {
        var today = c["today"]!.GetValue<string>();
        var start = DateTimeOffset.Parse(today + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var ledger = new TodayLedger(start);
        var i = 0;
        foreach (var r in c["responses"]!.AsArray())
        {
            ledger.Upsert(new ParsedResponse(
                "k" + i++, SourceNames.Parse(r!["source"]!.GetValue<string>())!.Value, "agent", r["model"]?.GetValue<string>(), null,
                r["endMs"]!.GetValue<long>(), new TokenCounts(Input: 10, Output: 5), Provider: r["provider"]?.GetValue<string>(),
                Fast: r["fast"]!.GetValue<bool>(), CostMicroUsd: r["costMicroUsd"]?.GetValue<long>(),
                RequestCount: r["requestCount"]!.GetValue<long>()));
        }

        var sources = Enum.GetValues<SourceKind>().Select(kind => new SourceStatus(kind, true, string.Empty, true, 0, default,
            ledger.ForSource(kind), 0) { UnpricedFacts = ledger.UnpricedFacts(kind) });
        return [.. UnpricedPart.Live(sources, today)];
    }

    private static JsonArray PartsJson(IEnumerable<UnpricedPart> parts) => new([.. parts.Select(p => (JsonNode)new JsonObject
    {
        ["day"] = p.Day,
        ["source"] = SourceNames.Of(p.Source),
        ["requests"] = p.Requests,
        ["noCostData"] = p.NoCostData,
        ["derived"] = p.Derived,
        ["facts"] = FactsJson(p.Facts),
    })]);

    private static ParsedResponse Response(string key, string? model, string? provider, long endMs, long? cost) =>
        new(key, SourceKind.Codex, "agent", model, null, endMs, new TokenCounts(Input: 10, Output: 5), Provider: provider, CostMicroUsd: cost);
}
