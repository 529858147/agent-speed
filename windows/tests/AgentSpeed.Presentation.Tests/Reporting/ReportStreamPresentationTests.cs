using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Reporting;

namespace AgentSpeed.Presentation.Tests.Reporting;

/// <summary>
/// What the API pane says about a WebSocket session (DESIGN 11, Status): its own status lines, the stats line with every
/// counter in its tooltip, and the request the Preview window shows.
/// </summary>
public class ReportStreamPresentationTests
{
    private static readonly StringTable Table = Localizer.LoadEmbeddedTable();
    private static readonly Localizer En = new(Table, LanguageCodes.En);

    private static readonly string[] StreamStatusKeys =
    [
        "report.status.streaming", "report.status.wsReconnecting", "report.status.wsReconnectingNever", "report.status.wsUnsupported",
        "report.status.wsMismatch", "report.status.wsReplaced", "report.status.wsIncompatible", "report.status.wsNonce",
        "report.status.wsClientState", "report.ws.stats", "report.ws.noLatency", "report.ws.counters", ReportStreamStats.MillisecondsKey,
    ];

    [Fact]
    public void AnOpenHealthySessionUsesTheSharedStatuses()
    {
        Assert.Null(ReportStreamStatus.Of(null, ReportStreamConnection.Open, 0, ReportMode.Active));
        Assert.Null(ReportStreamStatus.Of(null, ReportStreamConnection.Closed, 0, ReportMode.Quiet));
    }

    [Fact]
    public void AServiceWithoutWebSocketIsNotAStatusLine()
    {
        // Reports go over HTTP meanwhile, so the line is HTTP's; the note lives under the Transport control.
        var keys = Enum.GetValues<ReportStreamStatusKind>().Select(k => ReportStreamStatus.Key(k, everDelivered: true));
        Assert.DoesNotContain("report.status.wsUnsupported", keys);
    }

    [Theory]
    [InlineData(ReportStreamConnection.Closed, 1, ReportMode.Active, true)]
    [InlineData(ReportStreamConnection.Connecting, 3, ReportMode.Idle, true)]
    [InlineData(ReportStreamConnection.Opening, 1, ReportMode.Active, true)]
    [InlineData(ReportStreamConnection.Open, 2, ReportMode.Active, false)]
    [InlineData(ReportStreamConnection.Closed, 0, ReportMode.Active, false)]
    [InlineData(ReportStreamConnection.Closed, 4, ReportMode.Quiet, false)]
    public void ReconnectingNeedsAFailureAndAModeThatConnects(ReportStreamConnection connection, int failures, ReportMode mode, bool reconnecting) =>
        Assert.Equal(reconnecting ? ReportStreamStatusKind.Reconnecting : null, ReportStreamStatus.Of(null, connection, failures, mode));

    [Theory]
    [InlineData(ReportStreamStop.Mismatch, ReportStreamStatusKind.Mismatch)]
    [InlineData(ReportStreamStop.Replaced, ReportStreamStatusKind.Replaced)]
    [InlineData(ReportStreamStop.Incompatible, ReportStreamStatusKind.Incompatible)]
    [InlineData(ReportStreamStop.Nonce, ReportStreamStatusKind.Nonce)]
    [InlineData(ReportStreamStop.ClientState, ReportStreamStatusKind.ClientState)]
    public void TheStreamsOwnStopsHaveTheirOwnLines(ReportStreamStop stop, ReportStreamStatusKind kind)
    {
        Assert.Equal(kind, ReportStreamStatus.Of(stop, ReportStreamConnection.Closed, 5, ReportMode.Active));
        Assert.Null(ReportStreamStatus.ProblemOf(stop));
        Assert.True(ReportStreamStatus.NeedsUser(kind));
        Assert.Equal(kind is ReportStreamStatusKind.Mismatch or ReportStreamStatusKind.Replaced, ReportStreamStatus.OffersRetry(kind));
    }

    [Theory]
    [InlineData(ReportStreamStop.ApiKey, ReportProblem.ApiKey)]
    [InlineData(ReportStreamStop.Forbidden, ReportProblem.Forbidden)]
    [InlineData(ReportStreamStop.KeyMismatch, ReportProblem.KeyMismatch)]
    [InlineData(ReportStreamStop.Upgrade, ReportProblem.Upgrade)]
    [InlineData(ReportStreamStop.Rejected, ReportProblem.Rejected)]
    [InlineData(ReportStreamStop.Gone, ReportProblem.Gone)]
    public void StopsTheSharedLineAlreadySaysBecomeItsProblems(ReportStreamStop stop, ReportProblem problem)
    {
        Assert.Equal(problem, ReportStreamStatus.ProblemOf(stop));
        Assert.Null(ReportStreamStatus.Of(stop, ReportStreamConnection.Closed, 1, ReportMode.Active));
    }

    [Fact]
    public void AmberLinesDoNotAskTheUser()
    {
        Assert.False(ReportStreamStatus.NeedsUser(ReportStreamStatusKind.Reconnecting));
        Assert.False(ReportStreamStatus.OffersRetry(null));
        Assert.Equal("report.status.wsReconnecting", ReportStreamStatus.Key(ReportStreamStatusKind.Reconnecting, everDelivered: true));
        Assert.Equal("report.status.wsReconnectingNever", ReportStreamStatus.Key(ReportStreamStatusKind.Reconnecting, everDelivered: false));
    }

    [Fact]
    public void EveryStreamStatusKeyExistsInEveryLanguage()
    {
        var kinds = Enum.GetValues<ReportStreamStatusKind>().Select(k => ReportStreamStatus.Key(k, everDelivered: false));
        foreach (var language in LanguageCodes.All)
        {
            var loc = new Localizer(Table, language);
            foreach (var key in StreamStatusKeys.Concat(kinds))
            {
                Assert.NotEqual(key, loc[key]);
            }
        }
    }

    [Fact]
    public void NoStatsLineBeforeAnythingWasDeliveredOrLost()
    {
        var scheduler = Session(out _);
        Assert.Null(ReportStreamStats.Line(En, new ReportStreamScheduler(ReportStreamSettings.Default, 60).Counters, scheduler.Latency));
    }

    [Fact]
    public void TheStatsLineCountsTheSessionAndItsLatency()
    {
        var scheduler = Session(out var log);
        Assert.Equal(["build 1 active 80 40", "write 1", "delivered 1 accepted", "build 2 active 100 50", "write 2", "delivered 2 duplicate",
            "build 3 active 100 -", "write 3", "dropped 3 unknown", "backoff 820"], log);
        Assert.Equal("Latency p50 70 ms · p95 80 ms · delivered 2 · not delivered 1",
            ReportStreamStats.Line(En, scheduler.Counters, scheduler.Latency));
        Assert.Equal("Built 3 · accepted 1 · duplicate 1 · rejected 0 · dropped as stale 0 · dropped while full 0 · outcome unknown 1",
            ReportStreamStats.Tooltip(En, scheduler.Counters));
    }

    [Fact]
    public void LatencyIsWholeMillisecondsOrADash()
    {
        Assert.Equal("–", ReportStreamStats.Latency(En, null));
        Assert.Equal("1,234 ms", ReportStreamStats.Latency(En, 1234));
        Assert.Equal("0 ms", ReportStreamStats.Latency(En, -3));
    }

    [Fact]
    public void TheWebSocketPreviewShowsTheSignedUpgradeWithoutSecrets()
    {
        var api = Document("https://api.example.com/agent-speed").Api;
        var request = ReportPreviewRequest.WebSocket(ReportWebSocketProtocol.Url(api), "AgentSpeed/1.3.0 (Windows 11)", "6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b",
            1_792_000_000, hasApiKey: true);
        Assert.Equal("GET wss://api.example.com/agent-speed/v1/reports/ws", request.RequestLine);
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value);
        Assert.Equal("api.example.com", headers["Host"]);
        Assert.Equal("websocket", headers["Upgrade"]);
        Assert.Equal("agent-speed-report.v1", headers["Sec-WebSocket-Protocol"]);
        Assert.Equal("1792000000", headers[ReportCanonical.TimestampHeader]);
        Assert.Equal("…", headers[ReportCanonical.NonceHeader]);
        Assert.Equal("…", headers[ReportCanonical.SignatureHeader]);
        Assert.Equal("…", headers["Sec-WebSocket-Key"]);
        Assert.Equal("Bearer ••••", headers["Authorization"]);
        Assert.DoesNotContain("Content-Length", headers.Keys);
    }

    [Fact]
    public void ALoopbackTestServicePreviewsAPlainConnection()
    {
        var api = Document("http://127.0.0.1:8787/agent-speed").Api;
        var request = ReportPreviewRequest.WebSocket(ReportWebSocketProtocol.Url(api), "ua", "d", 1, hasApiKey: false);
        Assert.Equal("GET ws://127.0.0.1:8787/agent-speed/v1/reports/ws", request.RequestLine);
        Assert.Equal("127.0.0.1:8787", request.Headers.First(h => h.Key == "Host").Value);
        Assert.DoesNotContain(request.Headers, h => h.Key == "Authorization");
    }

    [Fact]
    public void TheHttpPreviewShowsTheReportsPost()
    {
        var api = Document("https://api.example.com/agent-speed").Api;
        var request = ReportPreviewRequest.Http(api.Endpoint(ReportEndpoint.Reports), "ua", "d", 7, 1234, hasApiKey: false);
        Assert.Equal("POST /agent-speed/v1/reports HTTP/1.1", request.RequestLine);
        Assert.Equal("1234", request.Headers.First(h => h.Key == "Content-Length").Value);
        Assert.Equal("…", request.Headers.First(h => h.Key == ReportCanonical.SignatureHeader).Value);
        Assert.DoesNotContain(request.Headers, h => h.Key == "Authorization");
    }

    /// <summary>A session that delivered two reports (one duplicate) and lost a third when the network dropped.</summary>
    private static ReportStreamScheduler Session(out List<string> log)
    {
        var scheduler = new ReportStreamScheduler(ReportStreamSettings.Default, 60, jitter: () => 1);
        var actions = new List<ReportStreamAction>();
        actions.AddRange(scheduler.Tick(0, active: true));
        actions.AddRange(scheduler.Opened(10));
        actions.AddRange(scheduler.Ready(20, ReportStreamSettings.Default));
        actions.AddRange(scheduler.Tick(100, active: true, changed: true, observed: 60));
        actions.AddRange(scheduler.Ack(130, 1, ReportAckStatus.Accepted));
        actions.AddRange(scheduler.Tick(200, active: true, changed: true, observed: 150));
        actions.AddRange(scheduler.Ack(230, 2, ReportAckStatus.Duplicate));
        actions.AddRange(scheduler.Tick(300, active: true, changed: true));
        actions.AddRange(scheduler.Closed(320, 1006));
        log = [.. actions.Where(a => a is not ReportStreamAction.Connect).Select(a => a.Text).OfType<string>()];
        return scheduler;
    }

    private static ReportDiscovery Document(string api) => ReportSettingsTests.Document($$$"""
        {"protocol":"agent-speed-report","versions":[1],
         "service":{"name":"Live board","terms":"https://example.com/terms","privacy":"https://example.com/privacy"},
         "api":"{{{api}}}","extensions":{"reportWebSocket":1},
         "websocket":{"path":"/v1/reports/ws","subprotocol":"agent-speed-report.v1"}}
        """, api.StartsWith("http://", StringComparison.Ordinal) ? "http://127.0.0.1:8787" : "https://board.example.com");
}
