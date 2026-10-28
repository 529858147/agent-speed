using System.Text.Json;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pi;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The pi conformance fixtures (METRICS 4.13) from their own file, <c>spec/fixtures/pi-expected.json</c>. They are not
/// part of <c>parser-expected.json</c> on purpose: that file is read by both platforms' tests and only the Windows
/// reader exists so far, so this suite leaves the macOS ones untouched.
/// </summary>
public sealed class PiFixtureTests
{
    private static readonly Lazy<JsonDocument> ExpectedDocument = new(() =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureLocator.FixturesDirectory, "pi-expected.json"))));

    private static JsonElement Expected => ExpectedDocument.Value.RootElement;

    /// <summary>The fixture folder <c>pi/agent</c> plays <c>~/.pi/agent</c>.</summary>
    internal static string Root => Path.Combine(FixtureLocator.FixturesDirectory, "pi", "agent", "sessions");

    public static TheoryData<string> FileNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Expected.GetProperty("order").EnumerateArray())
        {
            data.Add(name.GetString()!);
        }

        return data;
    }

    public static TheoryData<string, int, string, long?> Traces()
    {
        var data = new TheoryData<string, int, string, long?>();
        foreach (var element in Expected.GetProperty("traces").EnumerateArray())
        {
            data.Add(
                element.GetProperty("file").GetString()!,
                element.GetProperty("afterLine").GetInt32(),
                element.GetProperty("phase").GetString()!,
                ParserFixtureTests.NullableLong(element.GetProperty("inFlightStartMs")));
        }

        return data;
    }

    public static TheoryData<int> TailReadIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Expected.GetProperty("tailReads").GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    /// <summary>Every file of `order` with one shared dedupe set, so a clone's copied keys stay with their owner.</summary>
    private static readonly Lazy<Dictionary<string, FileResult>> Results = new(RunAll);

    private sealed record FileResult(TranscriptParser Parser, RecordingSink Sink);

    private static Dictionary<string, FileResult> RunAll()
    {
        var deduper = new Deduper();
        var results = new Dictionary<string, FileResult>(StringComparer.Ordinal);
        foreach (var relative in Order())
        {
            var sink = new RecordingSink();
            results[relative] = new FileResult(ParserFixtureTests.ParseFixture(relative, deduper, sink), sink);
        }

        return results;
    }

    [Fact]
    public void EveryFixtureFileIsListedInOrder()
    {
        var listed = Order().ToHashSet();
        var expectedFiles = Expected.GetProperty("files").EnumerateObject().Select(p => p.Name).ToHashSet();
        Assert.Equal(expectedFiles, listed);
        foreach (var name in listed)
        {
            Assert.True(File.Exists(FixtureLocator.PathOf(name)), name);
        }
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void ParsedFileMatchesExpectation(string relative)
    {
        var expected = Expected.GetProperty("files").GetProperty(relative);
        var actual = Results.Value[relative];

        Assert.Equal(expected.GetProperty("source").GetString(), ParserFixtureTests.SourceName(actual.Parser.Source));
        Assert.Equal(expected.GetProperty("project").GetString(), actual.Parser.Project);
        Assert.Equal(expected.GetProperty("model").GetString(), actual.Parser.Model);
        Assert.Equal(expected.GetProperty("isSubagent").GetBoolean(), actual.Parser.IsSubagent);
        Assert.Equal(expected.GetProperty("phase").GetString(), ParserFixtureTests.PhaseName(actual.Parser.Phase));
        Assert.Equal(ParserFixtureTests.NullableLong(expected.GetProperty("inFlightStartMs")), actual.Parser.InFlightStartMs);
        Assert.Equal(ParserFixtureTests.NullableLong(expected.GetProperty("lastLineMs")), actual.Parser.LastLineMs);
        Assert.Equal(expected.GetProperty("malformedLines").GetInt64(), actual.Parser.MalformedLines);

        var expectedResponses = expected.GetProperty("responses").EnumerateArray().ToList();
        Assert.Equal(expectedResponses.Select(r => r.GetProperty("key").GetString()!).ToList(),
            actual.Sink.Latest.Select(r => r.Key).ToList());
        ParserFixtureTests.AssertResponses(expectedResponses, actual.Sink.Latest, relative, actual.Parser);
    }

    [Theory]
    [MemberData(nameof(FileNames))]
    public void ParentFileMatchesExpectation(string relative)
    {
        var parents = ParentLinks.Resolve(
            Results.Value.Values.Select(r => AgentLink.Of(r.Parser)).ToList(), StringComparer.Ordinal);
        var expected = Expected.GetProperty("files").GetProperty(relative).GetProperty("parentFile").GetString();

        var parent = parents.GetValueOrDefault(Results.Value[relative].Parser.AgentId);
        var actual = parent is null
            ? null
            : Path.GetRelativePath(FixtureLocator.FixturesDirectory, parent).Replace(Path.DirectorySeparatorChar, '/');
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Traces))]
    public void PhaseAfterLineMatchesTrace(string file, int afterLine, string phase, long? inFlightStartMs)
    {
        var deduper = ReadFilesBefore(file);
        var parser = ParserFixtureTests.ParseFixture(file, deduper, new RecordingSink(), afterLine);

        Assert.Equal((phase, inFlightStartMs), (ParserFixtureTests.PhaseName(parser.Phase), parser.InFlightStartMs));
    }

    /// <summary>A read that starts at a byte offset still takes the header from the file start (METRICS 4.13).</summary>
    [Theory]
    [MemberData(nameof(TailReadIndexes))]
    public void ReadingFromAnOffsetTakesTheHeaderFirst(int index)
    {
        var expected = Expected.GetProperty("tailReads")[index];
        var file = expected.GetProperty("file").GetString()!;
        var path = FixtureLocator.PathOf(file);
        var parser = new PiParser(path, ReadFilesBefore(file), Root);
        var tailer = new FileTailer(path);
        var sink = new RecordingSink();

        tailer.StartAt(expected.GetProperty("offset").GetInt64(), StartAlignment.SkipPartialLine);
        var batch = parser.NewBatch();
        parser.PrepareBatch(batch, tailer.Offset);
        tailer.ReadAppended(batch);
        batch.ApplyTo(sink);

        Assert.Equal(expected.GetProperty("project").GetString(), parser.Project);
        Assert.Equal(expected.GetProperty("phase").GetString(), ParserFixtureTests.PhaseName(parser.Phase));
        Assert.Equal(ParserFixtureTests.NullableLong(expected.GetProperty("inFlightStartMs")), parser.InFlightStartMs);
        Assert.Equal(ParserFixtureTests.NullableLong(expected.GetProperty("lastLineMs")), parser.LastLineMs);
        Assert.Equal(expected.GetProperty("responseKeys").EnumerateArray().Select(k => k.GetString()!).ToList(),
            sink.Latest.Select(r => r.Key).ToList());
    }

    [Fact]
    public void DiscoveryFindsSessionsAndRunSubsessionsOnly()
    {
        var expected = Expected.GetProperty("discover").EnumerateArray().Select(e => e.GetString()!).ToList();

        var found = TranscriptDiscovery.Enumerate(Root, TranscriptNaming.PiSession)
            .Select(f => Path.GetRelativePath(FixtureLocator.FixturesDirectory, f.Path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, found);
    }

    [Fact]
    public void WatcherPathsFollowTheDiscoveryRules()
    {
        string P(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        var sessions = Expected.GetProperty("discover").EnumerateArray().Select(e => e.GetString()!)
            .Select(r => r["pi/agent/sessions/".Length..]).ToList();

        Assert.True(PiDiscovery.IsSessionPath(Root, P(sessions[0])));
        Assert.True(PiDiscovery.IsSessionPath(Root, P(sessions[^1])));
        Assert.False(PiDiscovery.IsSessionPath(Root, P("stray.jsonl")));
        Assert.False(PiDiscovery.IsSessionPath(Root, P("-projects-pulsar/ollama.json")));
        Assert.False(PiDiscovery.IsSessionPath(Root, P("-projects-pulsar/notes/draft.jsonl")));
        Assert.False(PiDiscovery.IsSessionPath(Root, P(".hidden/session.jsonl")));
    }

    private static List<string> Order() =>
        Expected.GetProperty("order").EnumerateArray().Select(e => e.GetString()!).ToList();

    private static Deduper ReadFilesBefore(string file)
    {
        var deduper = new Deduper();
        foreach (var relative in Order())
        {
            if (relative == file)
            {
                break;
            }

            ParserFixtureTests.ParseFixture(relative, deduper, new RecordingSink());
        }

        return deduper;
    }
}
