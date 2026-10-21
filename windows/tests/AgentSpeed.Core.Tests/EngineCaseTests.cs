using System.Globalization;
using System.Text.Json;
using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Tests;

public sealed class EngineCaseTests
{
    private const double Tolerance = 1e-6;

    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureLocator.FixturesDirectory, "engine-cases.json"))));

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var item in Cases())
        {
            data.Add(item.GetProperty("name").GetString()!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EngineCaseMatches(string name)
    {
        var item = Cases().Single(c => c.GetProperty("name").GetString() == name);
        var todayStartMs = item.TryGetProperty("todayStartMs", out var today) && today.ValueKind != JsonValueKind.Null
            ? today.GetInt64()
            : 0;

        var engine = new SpeedEngine(todayStartMs);
        foreach (var agent in item.GetProperty("agents").EnumerateArray())
        {
            engine.UpdateAgent(new AgentInfo(
                agent.GetProperty("id").GetString()!,
                ParseSource(agent.GetProperty("source").GetString()!),
                null,
                OptionalString(agent, "model"),
                false,
                ParsePhase(agent.GetProperty("phase").GetString()!),
                OptionalLong(agent, "inFlightStartMs"),
                OptionalLong(agent, "lastLineMs")));
        }

        foreach (var response in item.GetProperty("responses").EnumerateArray())
        {
            engine.Upsert(
                new ParsedResponse(
                    response.GetProperty("key").GetString()!,
                    ParseSource(response.GetProperty("source").GetString()!),
                    response.GetProperty("agentId").GetString()!,
                    OptionalString(response, "model"),
                    OptionalLong(response, "startMs"),
                    response.GetProperty("endMs").GetInt64(),
                    new TokenCounts(Input: response.GetProperty("input").GetInt64(), Output: response.GetProperty("output").GetInt64()),
                    !response.TryGetProperty("final", out var final) || final.GetBoolean()),
                isCompletion: true,
                seeded: true);
        }

        foreach (var query in item.GetProperty("queries").EnumerateArray())
        {
            var nowMs = query.GetProperty("nowMs").GetInt64();
            var frame = engine.Compute(nowMs, todayStartMs, observeLive: true);
            var at = $"[{name} @ {nowMs}]";

            AssertClose(query.GetProperty("liveOutput").GetDouble(), frame.Live.Output, at + " liveOutput");
            AssertClose(query.GetProperty("liveTotal").GetDouble(), frame.Live.Total, at + " liveTotal");
            AssertClose(query.GetProperty("estimatedShare").GetDouble(), frame.EstimatedShare, at + " estimatedShare");

            if (query.TryGetProperty("todayRequests", out var requests))
            {
                Assert.True(requests.GetInt64() == frame.Today.Requests, $"{at} todayRequests: got {frame.Today.Requests}");
            }

            if (query.TryGetProperty("todayOutput", out var output))
            {
                Assert.True(output.GetInt64() == frame.Today.Output, $"{at} todayOutput: got {frame.Today.Output}");
            }

            if (query.TryGetProperty("todayInput", out var input))
            {
                Assert.True(input.GetInt64() == frame.Today.Input, $"{at} todayInput: got {frame.Today.Input}");
            }

            if (query.TryGetProperty("averageToday", out var average))
            {
                if (average.ValueKind == JsonValueKind.Null)
                {
                    Assert.Null(frame.AverageToday);
                }
                else
                {
                    Assert.True(frame.AverageToday.HasValue, at + " averageToday is null");
                    AssertClose(average.GetDouble(), frame.AverageToday.GetValueOrDefault(), at + " averageToday");
                }
            }

            if (query.TryGetProperty("todayUnfinalized", out var unfinalized))
            {
                Assert.True(unfinalized.GetInt32() == frame.UnfinalizedToday, $"{at} todayUnfinalized: got {frame.UnfinalizedToday}");
            }

            if (query.TryGetProperty("peakTodayOutput", out var peak))
            {
                AssertClose(peak.GetDouble(), frame.PeakToday.Output, at + " peakTodayOutput");
            }

            if (query.TryGetProperty("historyMeasuredOutput", out var history))
            {
                Assert.Equal(SpeedEngine.HistorySeconds, frame.History.Count);
                foreach (var bucket in history.EnumerateObject())
                {
                    var startMs = long.Parse(bucket.Name, CultureInfo.InvariantCulture);
                    var point = frame.History.SingleOrDefault(p => p.Time.ToUnixTimeMilliseconds() == startMs);
                    Assert.True(point is not null, $"{at} history bucket {startMs} missing");
                    AssertClose(bucket.Value.GetDouble(), point.Measured.Output, $"{at} history[{startMs}]");
                }
            }
        }
    }

    internal static void AssertClose(double expected, double actual, string label) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance, $"{label}: expected {expected}, got {actual}");

    internal static SourceKind ParseSource(string name) => name switch
    {
        "codex" => SourceKind.Codex,
        "claudeCode" => SourceKind.ClaudeCode,
        "claudeDesktop" => SourceKind.ClaudeDesktop,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    internal static AgentPhase ParsePhase(string name) => name switch
    {
        "generating" => AgentPhase.Generating,
        "tools" => AgentPhase.Tools,
        "idle" => AgentPhase.Idle,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static IEnumerable<JsonElement> Cases() => Document.Value.RootElement.GetProperty("cases").EnumerateArray();

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? OptionalLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
}
