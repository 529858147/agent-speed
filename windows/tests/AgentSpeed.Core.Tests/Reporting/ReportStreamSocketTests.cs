using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>The WebSocket transport against a scripted loopback server (METRICS 11.9, Connection and Messages).</summary>
public sealed class ReportStreamSocketTests
{
    private const string Ready = """{"type":"ready","protocol":"agent-speed-report","version":1,"transportVersion":1,"serverTime":"2026-10-25T10:00:00.000Z","minIntervalMs":50,"maxInFlight":8,"ackTimeoutMs":2000,"maxMessageBytes":65536,"pingIntervalSeconds":20,"pongTimeoutSeconds":10}""";

    private static readonly SoftwareReportSigner Signer = new();

    [Fact]
    public async Task TheUpgradeOffersTheSubprotocolWithoutCompressionCookiesOrCredentialsAndReportsReady()
    {
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            await frames.SendText("""{"type":"hello","note":"ignored"}""");
            await frames.Receive(TimeSpan.FromSeconds(5));
        });
        var events = new EventLog();
        var socket = Open(server.Port, events);
        Assert.IsType<ReportStreamEvent.Opened>(await events.Next());
        var ready = Assert.IsType<ReportServiceMessage.Ready>(Assert.IsType<ReportStreamEvent.Message>(await events.Next()).Value);
        Assert.Equal(ReportStreamSettings.Default, ready.Limits);
        Assert.All(events.All, e => Assert.Equal(socket.Generation, e.Generation));

        var headers = await server.Headers.Task;
        Assert.Equal(ReportWebSocketProtocol.Subprotocol, headers["sec-websocket-protocol"]);
        Assert.False(headers.ContainsKey("sec-websocket-extensions"));
        Assert.False(headers.ContainsKey("cookie"));
        Assert.Equal("13", headers["sec-websocket-version"]);
        Assert.True(headers.ContainsKey("x-agentspeed-signature"));
        Assert.StartsWith("AgentSpeed/", headers["user-agent"], StringComparison.Ordinal);
        Assert.False(headers.ContainsKey("authorization"));
        socket.Abort();
    }

    [Fact]
    public async Task WritesGoOutInOrderAndCloseSendsCloseNormalWithoutEvents()
    {
        var received = new List<(int, string)>();
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            for (var i = 0; i < 3; i++)
            {
                if (await frames.Receive(TimeSpan.FromSeconds(5)) is { } frame)
                {
                    received.Add((frame.Opcode, frame.Opcode == ScriptedWebSocketServer.Frames.Close
                        ? ((frame.Payload[0] << 8) | frame.Payload[1]).ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : Encoding.UTF8.GetString(frame.Payload)));
                }
            }

            await frames.SendClose(1000);
        });
        var events = new EventLog();
        var socket = Open(server.Port, events);
        await events.Next();
        await events.Next();
        Assert.True(socket.Write("""{"type":"report","n":1}"""));
        Assert.True(socket.Write("""{"type":"report","n":2}"""));
        socket.Close();
        Assert.False(socket.Write("""{"type":"report","n":3}"""));
        await server.Finished.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([(1, """{"type":"report","n":1}"""), (1, """{"type":"report","n":2}"""), (8, "1000")], received);
        await Task.Delay(200);
        Assert.Equal(2, events.All.Count);
    }

    [Fact]
    public async Task AMissingPongAbortsTheConnection()
    {
        var pings = 0;
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            while (await frames.Receive(TimeSpan.FromSeconds(10)) is { } frame)
            {
                if (frame.Opcode == ScriptedWebSocketServer.Frames.Ping)
                {
                    Interlocked.Increment(ref pings);
                }
            }
        });
        var events = new EventLog();
        var transport = new WebSocketReportTransport { KeepAlive = (TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(600)) };
        var watch = Stopwatch.StartNew();
        Open(server.Port, events, transport);
        await events.Next();
        await events.Next();
        Assert.Equal(1006, Assert.IsType<ReportStreamEvent.Closed>(await events.Next(TimeSpan.FromSeconds(8))).Code);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0.5, 6);
        Assert.True(Volatile.Read(ref pings) >= 1, "the client sent its own Ping");
    }

    [Fact]
    public async Task A101WithoutTheSubprotocolCountsAsClose1002()
    {
        using var server = new ScriptedWebSocketServer(_ => "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n",
            async frames => await frames.Receive(TimeSpan.FromSeconds(3)));
        var events = new EventLog();
        Open(server.Port, events);
        Assert.Equal(1002, Assert.IsType<ReportStreamEvent.Closed>(await events.Next()).Code);
        Assert.Single(events.All);
    }

    [Theory]
    [InlineData("""{"type":"ready","protocol":"agent-speed-report","version":1}""")]
    [InlineData("""{"type":"ack","type":"ack"}""")]
    [InlineData("[1,2]")]
    [InlineData("binary")]
    public async Task AnInvalidOrBinaryMessageCountsAsClose1002(string message)
    {
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            if (message == "binary")
            {
                await frames.Send(ScriptedWebSocketServer.Frames.Binary, [1, 2, 3]);
            }
            else
            {
                await frames.SendText(message);
            }

            await frames.Receive(TimeSpan.FromSeconds(3));
        });
        var events = new EventLog();
        Open(server.Port, events);
        await events.Next();
        await events.Next();
        Assert.Equal(1002, Assert.IsType<ReportStreamEvent.Closed>(await events.Next()).Code);
    }

    [Fact]
    public async Task AMessageOverTheLimitCountsAsClose1002()
    {
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            await frames.SendText("{\"type\":\"other\",\"pad\":\"" + new string('x', 40_000) + "\"}");
            await frames.Receive(TimeSpan.FromSeconds(3));
        });
        var events = new EventLog();
        Open(server.Port, events, settings: ReportStreamSettings.Default with { MaxMessageBytes = 32_768 });
        await events.Next();
        await events.Next();
        Assert.Equal(1002, Assert.IsType<ReportStreamEvent.Closed>(await events.Next()).Code);
    }

    [Fact]
    public async Task TheServicesCloseCodeArrives()
    {
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            await frames.SendClose(4410);
            await frames.Receive(TimeSpan.FromSeconds(3));
        });
        var events = new EventLog();
        Open(server.Port, events);
        await events.Next();
        await events.Next();
        Assert.Equal(4410, Assert.IsType<ReportStreamEvent.Closed>(await events.Next()).Code);
    }

    [Fact]
    public async Task ARefusedUpgradeIsClassifiedByItsStatusAndRetryAfterWhenItHasNoBody()
    {
        using var server = new ScriptedWebSocketServer(_ => "HTTP/1.1 503 Service Unavailable\r\nRetry-After: 30\r\nContent-Length: 0\r\n");
        var events = new EventLog();
        Open(server.Port, events);
        var rejection = Assert.IsType<ReportStreamEvent.Rejected>(await events.Next()).Rejection;
        Assert.Equal((503, "rate_limited", 30), (rejection.Status, rejection.Code, rejection.RetryAfterSeconds));
    }

    [Fact]
    public async Task ARedirectIsNeverFollowed()
    {
        using var server = new ScriptedWebSocketServer(_ => "HTTP/1.1 302 Found\r\nLocation: ws://127.0.0.1:1/v1/reports/ws\r\nContent-Length: 0\r\n");
        var events = new EventLog();
        Open(server.Port, events);
        var rejection = Assert.IsType<ReportStreamEvent.Rejected>(await events.Next()).Rejection;
        Assert.Equal((302, (string?)null), (rejection.Status, rejection.Code));
    }

    [Theory]
    [InlineData("wss://example.com/v1/reports/ws", true)]
    [InlineData("ws://127.0.0.1:8787/v1/reports/ws", true)]
    [InlineData("ws://[::1]:8787/v1/reports/ws", true)]
    [InlineData("ws://localhost/v1/reports/ws", true)]
    [InlineData("ws://example.com/v1/reports/ws", false)]
    [InlineData("ws://192.168.1.2/v1/reports/ws", false)]
    [InlineData("https://example.com/v1/reports/ws", false)]
    [InlineData("wss://user:pass@example.com/v1/reports/ws", false)]
    public void OnlyWssOrLoopbackWsIsOpened(string url, bool allowed) => Assert.Equal(allowed, WebSocketReportTransport.IsAllowed(new Uri(url)));

    [Fact]
    public async Task AnInsecureUrlEndsAtOnceWithoutTouchingTheNetwork()
    {
        var events = new EventLog();
        var request = new ReportRequest("GET", new Uri("ws://example.com/v1/reports/ws"), [], []);
        var socket = new WebSocketReportTransport().Open(request, ReportStreamSettings.Default, events.Add);
        Assert.Equal(new ReportStreamEvent.Closed(socket.Generation, 1006), await events.Next());
        Assert.False(socket.Write("{}"));
    }

    [Fact]
    public async Task EveryConnectionHasANewGenerationAndAnAbortedOneStaysSilent()
    {
        using var server = new ScriptedWebSocketServer(script: async frames =>
        {
            await frames.SendText(Ready);
            await Task.Delay(300);
            await frames.SendText(Ready);
            await frames.Receive(TimeSpan.FromSeconds(2));
        });
        var events = new EventLog();
        var first = Open(server.Port, events);
        await events.Next();
        await events.Next();
        first.Abort();
        await Task.Delay(800);
        Assert.Equal(2, events.All.Count);
        using var other = new ScriptedWebSocketServer();
        var second = Open(other.Port, new EventLog());
        Assert.True(second.Generation > first.Generation);
        second.Abort();
    }

    private static IReportStreamSocket Open(int port, EventLog events, WebSocketReportTransport? transport = null, ReportStreamSettings? settings = null)
    {
        var api = ReportServiceUrl.Parse("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture))!;
        var builder = new ReportRequestBuilder(new ReportClientInfo("1.3.0", "Windows", 11).UserAgent, ReportCanonical.NewUuid(), null);
        var handshake = ReportWebSocketProtocol.Handshake(builder, api, Signer, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return (transport ?? new WebSocketReportTransport()).Open(handshake, settings ?? ReportStreamSettings.Default, events.Add);
    }

    /// <summary>The events of the connections a test opened, in arrival order.</summary>
    internal sealed class EventLog
    {
        private readonly Channel<ReportStreamEvent> _channel = Channel.CreateUnbounded<ReportStreamEvent>();
        private readonly List<ReportStreamEvent> _all = [];

        public IReadOnlyList<ReportStreamEvent> All
        {
            get
            {
                lock (_all)
                {
                    return [.. _all];
                }
            }
        }

        public void Add(ReportStreamEvent e)
        {
            lock (_all)
            {
                _all.Add(e);
            }

            _channel.Writer.TryWrite(e);
        }

        public async Task<ReportStreamEvent> Next(TimeSpan? timeout = null) =>
            await _channel.Reader.ReadAsync().AsTask().WaitAsync(timeout ?? TimeSpan.FromSeconds(10));
    }
}
