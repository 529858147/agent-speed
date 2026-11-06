using AgentSpeed.Core.Droid;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AgentSpeed.Core.Omp;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pi;
using AgentSpeed.Core.Work;

namespace AgentSpeed.Core.Tests;

/// <summary>Work signals (METRICS 6.10) against <c>spec/fixtures/work-signal-cases.json</c>.</summary>
public sealed class WorkSignalCaseTests
{
    private static readonly Lazy<JsonElement> Document = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("work-signal-cases.json"))).RootElement);

    public static TheoryData<int> DigestIndexes() => Indexes("digests");

    public static TheoryData<string> SignalNames() => Names("signals");

    public static TheoryData<string> FileNames() => Names("files");

    [Theory]
    [MemberData(nameof(DigestIndexes))]
    public void DigestMatches(int index)
    {
        var item = Document.Value.GetProperty("digests")[index];
        var input = item.GetProperty("input");
        var stored = input.ValueKind == JsonValueKind.String ? Encoding.UTF8.GetBytes(input.GetString()!) : Compact(input);
        Assert.Equal(item.GetProperty("expect").GetString(), ActionDigest.Hex(ActionDigest.Of(stored)));
    }

    [Theory]
    [MemberData(nameof(SignalNames))]
    public void SignalsMatch(string name)
    {
        var item = Case("signals", name);
        var actions = item.GetProperty("actions").EnumerateArray().Select(a => new WorkAction(
            a.GetProperty("at").GetInt64(),
            a.GetProperty("tool").GetString()!,
            ulong.Parse(a.GetProperty("digest").GetString()!, System.Globalization.NumberStyles.HexNumber, null),
            a.TryGetProperty("failed", out var failed) && failed.ValueKind != JsonValueKind.Null ? failed.GetBoolean() : null)).ToArray();
        var expect = item.GetProperty("expect");

        var signals = WorkSignals.Of(actions, item.GetProperty("nowMs").GetInt64());

        Assert.Equal(OptionalInt(expect, "looping"), signals.Looping);
        Assert.Equal(OptionalInt(expect, "repeating"), signals.Repeating);
        Assert.Equal(expect.GetProperty("failed").GetInt32(), signals.Failed);
        Assert.Equal(expect.GetProperty("recovered").GetInt32(), signals.Recovered);
        if (expect.GetProperty("novelty").ValueKind == JsonValueKind.Null)
        {
            Assert.Null(signals.Novelty);
        }
        else
        {
            EngineCaseTests.AssertClose(expect.GetProperty("novelty").GetDouble(), signals.Novelty!.Value, name + " novelty");
        }
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void FileActionsMatch(string name)
    {
        var item = Case("files", name);
        Func<byte[], LineTools> read = item.GetProperty("format").GetString() switch
        {
            "claude" => line => ClaudeLineReader.Read(line).Tools,
            "codex" => line => CodexLineReader.Read(line).Tools,
            "omp" => line => OmpLineReader.Read(line).Tools,
            "pi" => line => PiLineReader.Read(line).Tools,
            "droid" => line => DroidLineReader.Read(line).Tools,
            var format => throw new ArgumentOutOfRangeException(nameof(name), format, null),
        };
        var log = new WorkLog();
        foreach (var entry in item.GetProperty("lines").EnumerateArray())
        {
            log.Apply(read(Compact(entry[1])), entry[0].GetInt64());
        }

        var expected = item.GetProperty("expect").EnumerateArray().Select(a => (
            a.GetProperty("at").GetInt64(),
            a.GetProperty("tool").GetString(),
            a.GetProperty("digest").GetString(),
            a.GetProperty("failed").ValueKind == JsonValueKind.Null ? (bool?)null : a.GetProperty("failed").GetBoolean()));
        Assert.Equal(expected, log.Actions.Select(a => (a.AtMs, (string?)a.Tool, (string?)ActionDigest.Hex(a.Digest), a.Failed)));
    }

    [Fact]
    public void ToolLinesKeepTheirResponsesAndAnUndecodableEscapeNeverFailsTheLine()
    {
        var call = CodexLines.Envelope(CodexLines.T0, "response_item",
            """{"type":"custom_tool_call","call_id":"c1","name":"exec","input":"bad \ud800 escape"}""");
        var output = CodexLines.Envelope(CodexLines.T0 + 10, "response_item",
            """{"type":"custom_tool_call_output","call_id":"c1","output":"{\"exit_code\":1,\"note\":\"\ud800\"}"}""");
        Assert.Equal(LineStatus.Valid, CodexLineReader.Read(Encoding.UTF8.GetBytes(call)).Status);
        Assert.Equal("exec", CodexLineReader.Read(Encoding.UTF8.GetBytes(call)).Tools.Calls![0].Tool);
        Assert.Equal(LineStatus.Valid, CodexLineReader.Read(Encoding.UTF8.GetBytes(output)).Status);
        Assert.Null(CodexLineReader.Read(Encoding.UTF8.GetBytes(output)).Tools.Outcomes![0].Failed);
    }

    /// <summary>The compact JSON text the fixture's lines stand for (as <c>json.dumps(..., ensure_ascii=False)</c> writes it).</summary>
    private static byte[] Compact(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            element.WriteTo(writer);
        }

        return buffer.ToArray();
    }

    private static int? OptionalInt(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetInt32();

    private static TheoryData<int> Indexes(string group)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Document.Value.GetProperty(group).GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static TheoryData<string> Names(string group)
    {
        var data = new TheoryData<string>();
        foreach (var item in Document.Value.GetProperty(group).EnumerateArray())
        {
            data.Add(item.GetProperty("name").GetString()!);
        }

        return data;
    }

    private static JsonElement Case(string group, string name) =>
        Document.Value.GetProperty(group).EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
}
