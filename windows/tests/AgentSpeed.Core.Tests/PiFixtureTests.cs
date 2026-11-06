using AgentSpeed.Core.IO;
using AgentSpeed.Core.Monitoring;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pi;

namespace AgentSpeed.Core.Tests;

/// <summary>parser-expected.json <c>pi</c>: traces, discovery and the automatic roots, like the reference's checks.</summary>
public sealed class PiFixtureTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    public static TheoryData<string, int, string, long?> Traces()
    {
        var data = new TheoryData<string, int, string, long?>();
        foreach (var trace in ParserFixtureTests.Expected.GetProperty("pi").GetProperty("traces").EnumerateArray())
        {
            data.Add(
                trace.GetProperty("file").GetString()!,
                trace.GetProperty("afterLine").GetInt32(),
                trace.GetProperty("phase").GetString()!,
                ParserFixtureTests.NullableLong(trace.GetProperty("inFlightStartMs")));
        }

        return data;
    }

    [Fact]
    public void EveryTraceIsListed() => Assert.Equal(19, Traces().Count);

    [Fact]
    public void EveryPiFixtureFileIsParsed() => Assert.Equal(6,
        ParserFixtureTests.Expected.GetProperty("order").EnumerateArray().Count(e => e.GetString()!.StartsWith("pi/", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Traces))]
    public void PhaseAfterLineMatchesTrace(string file, int afterLine, string phase, long? inFlightStartMs)
    {
        var deduper = ReadFilesBefore(file);
        var parser = ParserFixtureTests.ParseFixture(file, deduper, new RecordingSink(), afterLine);

        Assert.Equal((phase, inFlightStartMs), (ParserFixtureTests.PhaseName(parser.Phase), parser.InFlightStartMs));
    }

    [Fact]
    public void DiscoveryFindsSessionsSubAgentsAndFlatCustomFilesButNoTempsBackupsOrArtifacts()
    {
        var expected = ParserFixtureTests.Expected.GetProperty("pi").GetProperty("discover").EnumerateArray()
            .Select(e => e.GetString()!).ToList();

        var found = PiFixture.Roots
            .SelectMany(root => TranscriptDiscovery.Enumerate(root, TranscriptNaming.PiSession))
            .Select(f => Path.GetRelativePath(FixtureLocator.FixturesDirectory, f.Path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, found);
    }

    [Fact]
    public void FixtureRootsAreTheDefaultRootAndTheCustomFolderOfPisSettings()
    {
        Assert.Equal([FixtureLocator.PathOf("pi/agent/sessions"), FixtureLocator.PathOf("pi/custom-sessions")], PiFixture.Roots);
    }

    [Fact]
    public void WatcherPathsFollowTheDiscoveryRules()
    {
        var root = PiFixture.Roots[0];
        var custom = PiFixture.Roots[1];
        string P(string folder, string relative) => Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        const string Session = "2026-10-20T12-00-00-000Z_01a1592e-c600-7b2f-b88d-001328b7bc6f";

        Assert.True(TranscriptDiscovery.IsAcceptablePath(root, P(root, $"--Users-dev-projects-halo--/{Session}.jsonl"), TranscriptNaming.PiSession));
        Assert.True(TranscriptDiscovery.IsAcceptablePath(root, P(root, $"--Users-dev-projects-halo--/{Session}/reviewer.jsonl"),
            TranscriptNaming.PiSession));
        Assert.True(TranscriptDiscovery.IsAcceptablePath(custom,
            P(custom, "2026-10-20T12-30-00-000Z_01a1594a-3d40-76f3-bfd9-0015a195a333.jsonl"), TranscriptNaming.PiSession));
        Assert.False(TranscriptDiscovery.IsAcceptablePath(root, P(root, $".trash/{Session}.jsonl"), TranscriptNaming.PiSession));
        Assert.False(TranscriptDiscovery.IsAcceptablePath(root, P(root, $"--Users-dev-projects-halo--/.{Session}.tmp.jsonl"),
            TranscriptNaming.PiSession));
        Assert.False(TranscriptDiscovery.IsAcceptablePath(custom,
            P(custom, "2026-10-20T12-30-00-000Z_01a1594a-3d40-76f3-bfd9-0015a195a333.jsonl.bak"), TranscriptNaming.PiSession));
        Assert.False(TranscriptDiscovery.IsAcceptablePath(root, P(root, "--Users-dev-projects-halo--/notes.md"), TranscriptNaming.PiSession));
    }

    /// <summary>The cases of the reference's <c>check_pi_roots</c>.</summary>
    [Fact]
    public void AutomaticRootsFollowTheEnvironmentAndPisSettings()
    {
        var home = _temp.Combine("home");
        var agent = Path.Combine(home, ".pi", "agent");
        var sessions = Path.Combine(agent, "sessions");
        var settings = Path.Combine(agent, "settings.json");
        Directory.CreateDirectory(agent);
        IReadOnlyList<string> Roots(params (string Name, string Value)[] env) =>
            PiPaths.Roots(name => env.FirstOrDefault(e => e.Name == name).Value, home);

        Assert.Equal([sessions], Roots());

        File.WriteAllText(settings, """{"sessionDir":"~/elsewhere"}""");
        Assert.Equal([sessions, Path.Combine(home, "elsewhere")], Roots());

        File.WriteAllText(settings, """{"sessionDir":"   "}""");
        Assert.Equal([sessions], Roots());

        File.WriteAllText(settings, """{"sessionDir":7}""");
        Assert.Equal([sessions], Roots());

        File.WriteAllText(settings, "{ not json");
        Assert.Equal([sessions], Roots());

        var shared = Path.GetFullPath(_temp.Combine("srv", "pi"));
        File.WriteAllText(settings, "{\"sessionDir\":\"" + shared.Replace("\\", "\\\\") + "\"}");
        Assert.Equal([sessions, shared], Roots(("PI_CODING_AGENT_SESSION_DIR", shared)));

        File.Delete(settings);
        Assert.Equal([Path.Combine(home, "pi-agent", "sessions")], Roots(("PI_CODING_AGENT_DIR", "~/pi-agent")));
        Assert.Equal(Path.Combine(home, "pi-agent", "sessions"),
            PiPaths.DefaultRoot(name => name == "PI_CODING_AGENT_DIR" ? "~/pi-agent" : null, home));
    }

    [Fact]
    public void AnOmpRootThatIsOrLiesInsideAPiRootIsPisOnly()
    {
        var home = _temp.Path;
        var pi = Path.Combine(home, ".pi", "agent", "sessions");
        string[] omp =
        [
            pi,
            Path.Combine(pi, "x"),
            Path.Combine(home, ".omp", "agent", "sessions"),
            Path.Combine(home, ".pi", "agent", "sessions2"),
        ];

        Assert.Equal(omp[2..], PiPaths.WithoutPi(omp, [pi]));
        Assert.Equal(omp[2..], PiPaths.WithoutPi(omp, [pi + Path.DirectorySeparatorChar]));
        Assert.Equal(omp, PiPaths.WithoutPi(omp, []));
    }

    [Fact]
    public void TheFileRootsReadAnOmpFolderThatIsPisAsPiOnly()
    {
        var pi = Path.GetFullPath(_temp.Combine(".pi", "agent", "sessions"));
        var omp = Path.GetFullPath(_temp.Combine(".omp", "agent", "sessions"));
        var locations = new SourceLocations(string.Empty, string.Empty, string.Empty, string.Empty, [],
            OmpRoots: [pi, omp], PiRoots: [pi, pi]);

        var roots = locations.FileRoots().Where(r => r.Family is RootFamily.Omp or RootFamily.Pi).ToList();

        Assert.Equal([(RootFamily.Omp, omp), (RootFamily.Pi, pi)], roots);
    }

    [Fact]
    public void AChosenFolderReplacesEveryAutomaticRoot()
    {
        var chosen = _temp.Combine("chosen");
        var locations = SourceLocations.Resolve(new MonitorConfiguration(new HashSet<SourceKind> { SourceKind.Pi }, PiRoot: chosen));

        Assert.Equal([Path.GetFullPath(chosen)], locations.PiRoots);
        Assert.False(locations.PiAutomatic);
        Assert.True(SourceLocations.Resolve(MonitorConfiguration.Default).PiAutomatic);
    }

    /// <summary>A sub-agent's parent is the session file named after its folder, also in a flat custom folder (METRICS 4.13).</summary>
    [Fact]
    public void ASubAgentIsAFileInAFolderNamedAfterASessionFile()
    {
        var halo = FixtureLocator.PathOf("pi/agent/sessions/--Users-dev-projects-halo--/2026-10-20T12-00-00-000Z_01a1592e-c600-7b2f-b88d-001328b7bc6f");
        var flat = FixtureLocator.PathOf("pi/custom-sessions/2026-10-20T12-30-00-000Z_01a1594a-3d40-76f3-bfd9-0015a195a333");

        Assert.Equal(halo + ".jsonl", PiPaths.ParentFileOf(Path.Combine(halo, "reviewer.jsonl")));
        Assert.Equal(flat + ".jsonl", PiPaths.ParentFileOf(Path.Combine(flat, "helper.jsonl")));
        Assert.Null(PiPaths.ParentFileOf(halo + ".jsonl"));
        Assert.Null(PiPaths.ParentFileOf(flat + ".jsonl"));
        Assert.Null(PiPaths.ParentFileOf(FixtureLocator.PathOf("pi/agent/sessions/.trash/x.jsonl")));
    }

    private static Deduper ReadFilesBefore(string file)
    {
        var deduper = new Deduper();
        foreach (var element in ParserFixtureTests.Expected.GetProperty("order").EnumerateArray())
        {
            var relative = element.GetString()!;
            if (relative == file)
            {
                break;
            }

            ParserFixtureTests.ParseFixture(relative, deduper, new RecordingSink());
        }

        return deduper;
    }
}
