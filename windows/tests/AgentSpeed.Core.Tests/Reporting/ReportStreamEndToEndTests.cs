using AgentSpeed.Core.Reporting;
using AgentSpeed.Core.Traffic;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>
/// The WebSocket transport against <c>spec/tools/report_server.py --websocket</c> on loopback: the real ClientWebSocket, the
/// signed upgrade and report messages, and the service's answers. Never reaches beyond 127.0.0.1.
/// </summary>
[Collection("report server")]
public sealed class ReportStreamEndToEndTests
{
    private static readonly ReportClientInfo Client = new("1.3.0", "Windows", 11);

    [Fact]
    public async Task ReadyThenAckedAndDuplicateReports()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket", "--api-path", "/agent-speed", "--ws-min-interval", "50");
        var device = await Device.Register(server, "ws-e2e");
        Assert.Equal(ReportStreamSettings.Default, device.Document.WebSocket);
        Assert.Equal($"ws://127.0.0.1:{server.Port}/agent-speed/v1/reports/ws", ReportWebSocketProtocol.Url(device.Document.Api).AbsoluteUri);

        var events = new ReportStreamSocketTests.EventLog();
        var socket = device.Open(events);
        Assert.IsType<ReportStreamEvent.Opened>(await events.Next());
        var ready = Assert.IsType<ReportServiceMessage.Ready>(Message(await events.Next()));
        Assert.Equal(device.Document.WebSocket, ready.Limits);

        var body = device.Body(1);
        Assert.True(socket.Write(device.Message(body)));
        var ack = Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next()));
        Assert.Equal((device.RunId, 1L, ReportAckStatus.Accepted, ReportHash.Sha256Hex(body)), (ack.RunId, ack.Seq, ack.Status, ack.BodySha256));

        Assert.True(socket.Write(device.Message(body)));
        Assert.Equal(ReportAckStatus.Duplicate, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Status);

        var second = device.Body(2);
        Assert.True(socket.Write(device.Message(second)));
        Assert.Equal(2, Assert.IsType<ReportServiceMessage.Ack>(Message(await events.Next())).Seq);
        socket.Close();
        await Task.Delay(300);
        Assert.Equal(5, events.All.Count);
        Assert.Contains("ws: ", server.Log, StringComparison.Ordinal);
        Assert.DoesNotContain(device.Signature, server.Log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedHandshakeIsClassifiedFromItsV1ErrorBody()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket");
        var device = await Device.Register(server, "ws-refused");
        var events = new ReportStreamSocketTests.EventLog();
        device.Open(events, new SoftwareReportSigner());
        var rejection = Assert.IsType<ReportStreamEvent.Rejected>(await events.Next()).Rejection;
        Assert.Equal((401, "signature_invalid"), (rejection.Status, rejection.Code));

        var unknown = new Device(device.Document, ReportCanonical.NewUuid(), "nobody");
        unknown.Open(events);
        rejection = Assert.IsType<ReportStreamEvent.Rejected>(await events.Next()).Rejection;
        Assert.Equal((404, "unknown_device"), (rejection.Status, rejection.Code));
    }

    [Fact]
    public async Task ANewerConnectionReplacesTheOlderWith4009()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket");
        var device = await Device.Register(server, "ws-replaced");
        var first = new ReportStreamSocketTests.EventLog();
        var older = device.Open(first);
        await first.Next();
        await first.Next();
        var second = new ReportStreamSocketTests.EventLog();
        var newer = device.Open(second);
        await second.Next();
        Assert.IsType<ReportServiceMessage.Ready>(Message(await second.Next()));
        Assert.Equal(new ReportStreamEvent.Closed(older.Generation, 4009), await first.Next());

        Assert.True(newer.Write(device.Message(device.Body(1))));
        Assert.Equal(ReportAckStatus.Accepted, Assert.IsType<ReportServiceMessage.Ack>(Message(await second.Next())).Status);
        newer.Abort();
    }

    /// <summary>
    /// The path the app takes: the stream scheduler connects, a valid <c>ready</c> opens the connection, changed ticks build
    /// reports that are signed and written, and the service's acks deliver them with a latency sample each.
    /// </summary>
    [Fact]
    public async Task TheStreamSchedulerDeliversOverTheRealConnection()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket", "--ws-min-interval", "20");
        var device = await Device.Register(server, "ws-scheduler");
        var driver = new Driver(device);
        driver.Tick(active: true, changed: true);
        Assert.Equal(ReportStreamConnection.Connecting, driver.Scheduler.Connection);
        await driver.Until(() => driver.Scheduler.Connection == ReportStreamConnection.Open);

        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(30);
            driver.Tick(active: true, changed: true, observedAgo: 25);
        }

        await driver.Until(() => driver.Scheduler.Counters.Accepted == 4 && driver.Scheduler.AwaitingCount == 0);
        Assert.Equal((4, 0, 0, 0), (driver.Scheduler.Counters.Built, driver.Scheduler.Counters.Unknown, driver.Scheduler.Counters.Rejected,
            driver.Scheduler.Counters.Unmatched));
        Assert.Equal(4, driver.Scheduler.Latency.Samples.Count);
        Assert.All(driver.Scheduler.Latency.Samples, sample => Assert.InRange(sample, 25, 5000));
        Assert.Equal(0, driver.Scheduler.Failures);
        driver.Close();
    }

    /// <summary>A refused handshake reaches the scheduler with the code of its v1 body: re-check the registration, then a backoff.</summary>
    [Fact]
    public async Task ARefusedHandshakeBacksOffAndRechecksTheRegistration()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket");
        var device = await Device.Register(server, "ws-refused-run");
        var driver = new Driver(device, new SoftwareReportSigner());
        driver.Tick(active: true, changed: true);
        await driver.Until(() => driver.Scheduler.Connection == ReportStreamConnection.Closed);
        Assert.Contains("react recheckRegistration", driver.Log);
        Assert.Contains(driver.Log, line => line.StartsWith("backoff ", StringComparison.Ordinal));
        Assert.Equal(1, driver.Scheduler.Failures);
        Assert.Null(driver.Scheduler.Stop);
    }

    /// <summary>A second connection of the same device takes over: the first session stops as <c>replaced</c> and never reconnects.</summary>
    [Fact]
    public async Task AReplacedSessionStopsUntilTheUserActs()
    {
        if (!ReferencePython.Available)
        {
            return;
        }

        using var server = new ReferenceServer("--websocket");
        var device = await Device.Register(server, "ws-replaced-run");
        var driver = new Driver(device);
        driver.Tick(active: true, changed: true);
        await driver.Until(() => driver.Scheduler.Connection == ReportStreamConnection.Open);

        var other = new ReportStreamSocketTests.EventLog();
        var newer = device.Open(other);
        await other.Next();
        await driver.Until(() => driver.Scheduler.Stop == ReportStreamStop.Replaced);
        Assert.Contains("stop replaced", driver.Log);
        driver.Tick(active: true, changed: true);
        Assert.Equal(ReportStreamConnection.Closed, driver.Scheduler.Connection);
        Assert.DoesNotContain("connect", driver.Log.Skip(1));
        newer.Abort();
    }

    private static ReportServiceMessage Message(ReportStreamEvent e) => Assert.IsType<ReportStreamEvent.Message>(e).Value;

    /// <summary>
    /// A small owner of the stream scheduler like the app's controller: it performs connect, build, write, close and abort and
    /// feeds the current connection's events back, on one lock instead of the UI thread.
    /// </summary>
    private sealed class Driver(Device device, IReportSigner? handshakeSigner = null)
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, byte[]> _bodies = [];
        private readonly List<string> _log = [];
        private IReportStreamSocket? _socket;

        public ReportStreamScheduler Scheduler { get; } = new(device.Document.WebSocket, device.Document.Limits.HeartbeatSeconds, jitter: () => 1);

        public IReadOnlyList<string> Log
        {
            get
            {
                lock (_gate)
                {
                    return [.. _log];
                }
            }
        }

        private static long Now => Environment.TickCount64;

        public void Tick(bool active, bool changed, long? observedAgo = null)
        {
            lock (_gate)
            {
                var now = Now;
                Apply(Scheduler.Tick(now, active, changed, observedAgo is { } ago ? now - ago : null));
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _socket?.Close();
                _socket = null;
            }
        }

        public async Task Until(Func<bool> condition)
        {
            for (var waited = 0; waited < 10_000; waited += 20)
            {
                lock (_gate)
                {
                    if (condition())
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            lock (_gate)
            {
                Assert.True(condition(), string.Join("\n", _log));
            }
        }

        private void OnEvent(ReportStreamEvent e)
        {
            lock (_gate)
            {
                if (_socket?.Generation != e.Generation)
                {
                    return;
                }

                var now = Now;
                Apply(e switch
                {
                    ReportStreamEvent.Opened => Scheduler.Opened(now),
                    ReportStreamEvent.Message { Value: ReportServiceMessage.Ready ready } => Scheduler.Ready(now, ready.Limits),
                    ReportStreamEvent.Message { Value: ReportServiceMessage.Ack ack } => Scheduler.Ack(now, (int)ack.Seq, ack.Status,
                        ack.RunId == device.RunId && _bodies.TryGetValue((int)ack.Seq, out var body) && ReportHash.Sha256Hex(body) == ack.BodySha256,
                        ack.Rank),
                    ReportStreamEvent.Message { Value: ReportServiceMessage.Error error } => Scheduler.Error(now, (int?)error.Seq, error.Code,
                        error.HttpStatus, error.RetryAfterSeconds, error.Fatal),
                    ReportStreamEvent.Rejected rejected => Ended(Scheduler.Rejected(now, rejected.Rejection.Status, rejected.Rejection.Code,
                        rejected.Rejection.RetryAfterSeconds)),
                    ReportStreamEvent.Closed closed => Ended(Scheduler.Closed(now, closed.Code)),
                    _ => [],
                });
            }
        }

        private List<ReportStreamAction> Ended(List<ReportStreamAction> actions)
        {
            _socket = null;
            return actions;
        }

        private void Apply(IEnumerable<ReportStreamAction> actions)
        {
            foreach (var action in actions)
            {
                if (action.Text is { } text)
                {
                    _log.Add(text);
                }

                switch (action)
                {
                    case ReportStreamAction.Connect:
                        _socket = device.Open(OnEvent, handshakeSigner);
                        break;
                    case ReportStreamAction.Build build:
                        _bodies[build.Ticket.Seq] = device.Body(build.Ticket.Seq, build.Ticket.ObservationAgeMs);
                        break;
                    case ReportStreamAction.Write write:
                        Assert.True(_socket?.Write(device.Message(_bodies[write.Seq])));
                        break;
                    case ReportStreamAction.Close:
                        _socket?.Close();
                        _socket = null;
                        break;
                    case ReportStreamAction.Abort:
                        _socket?.Abort();
                        _socket = null;
                        break;
                }
            }
        }
    }

    /// <summary>A registered device and the bodies and messages of its one run.</summary>
    private sealed class Device(ReportDiscovery document, string deviceId, string username)
    {
        private readonly SoftwareReportSigner _signer = new();

        public ReportDiscovery Document { get; } = document;

        public string RunId { get; } = ReportCanonical.NewUuid();

        /// <summary>The last message's signature (it must never reach the server's log).</summary>
        public string Signature { get; private set; } = "-";

        public static async Task<Device> Register(ReferenceServer server, string username)
        {
            using var transport = new HttpReportTransport();
            var api = new ReportApi(transport, Client.UserAgent);
            var document = Assert.IsType<ReportDiscoveryOutcome.Document>(await api.DiscoverAsync(server.Service)).Discovery;
            var device = new Device(document, ReportCanonical.NewUuid(), username);
            var request = device.Builder.Make("POST", document.Api.Endpoint(ReportEndpoint.Devices),
                ReportDocuments.Registration(device.DeviceId, username, device._signer.PublicKey, Client), device._signer, Now);
            var result = await api.CallAsync(request);
            Assert.Equal("created", result.Reply?.Result);
            return device;
        }

        public string DeviceId { get; } = deviceId;

        private ReportRequestBuilder Builder => new(Client.UserAgent, DeviceId, null);

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public IReportStreamSocket Open(ReportStreamSocketTests.EventLog events, IReportSigner? signer = null) => Open(events.Add, signer);

        public IReportStreamSocket Open(Action<ReportStreamEvent> events, IReportSigner? signer = null) =>
            new WebSocketReportTransport().Open(ReportWebSocketProtocol.Handshake(Builder, Document.Api, signer ?? _signer, Now),
                Document.WebSocket!, events);

        public string Message(byte[] body)
        {
            var request = Builder.Make("POST", Document.Api.Endpoint(ReportEndpoint.Reports), body, _signer, Now);
            Signature = request.Header(ReportCanonical.SignatureHeader)!;
            return ReportWebSocketProtocol.ReportMessage(request.Headers, body);
        }

        public byte[] Body(int seq, long? observationAgeMs = 40)
        {
            var counters = new ReportCounters();
            counters.Add(new ReportUsage(1, 120, 9000, 8000, 0, 0, 120, 900, 300), new ReportModelKey("gpt-5.5", SourceKind.Codex));
            var facts = new ReportLiveFacts(
                new Dictionary<SourceKind, ReportAgentCounts> { [SourceKind.Codex] = new(1, 0, 1) },
                new Dictionary<ReportModelKey, int> { [new("gpt-5.5", SourceKind.Codex)] = 1 }, counters, 1);
            var sample = new ReportSample(new Rate(133.3, 9000), 0, new LiveCost(900, 133.3),
                new Dictionary<SourceKind, ReportSourceSample> { [SourceKind.Codex] = new(new Rate(133.3, 9000), Rate.Zero, new LiveCost(900, 133.3)) },
                facts, ReportTrafficSample.Unavailable);
            var envelope = new ReportEnvelope(Client, DeviceId, username, RunId, seq, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 120,
                ReportActivity.Active, 3);
            var baseline = ReportSample.Empty with { Facts = ReportLiveFacts.Empty with { Epoch = 1 } };
            var options = new ReportOptions(Document.Sections, Document.Limits.MaxModels, Document.Limits.SendLimitBytes)
            {
                Stream = new ReportStreamBody(observationAgeMs, Document.WebSocket!.MaxMessageBytes),
            };
            return ReportBody.Fitted(envelope, new ReportWindow(baseline).Content(sample), sample, options).Data;
        }
    }
}
