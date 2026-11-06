using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace AgentSpeed.Core.Reporting;

/// <summary>
/// The WebSocket transport over <see cref="ClientWebSocket"/> (METRICS 11.9, Connection): each connection gets its own
/// <see cref="System.Net.Http.SocketsHttpHandler"/> without cookies, credentials or redirects, the signed upgrade's headers,
/// the subprotocol, no compression and the document's keep-alive (a Ping every <c>pingIntervalSeconds</c>, aborted when no
/// Pong arrives within <c>pongTimeoutSeconds</c>).
/// </summary>
public sealed class WebSocketReportTransport : IReportStreamTransport
{
    private static long _generations;

    private readonly Func<HttpMessageHandler> _handler;

    public WebSocketReportTransport()
        : this(HttpReportTransport.CreateHandler)
    {
    }

    internal WebSocketReportTransport(Func<HttpMessageHandler> handler)
    {
        _handler = handler;
    }

    /// <summary>Overrides the document's keep-alive (tests); null keeps it.</summary>
    internal (TimeSpan Interval, TimeSpan Timeout)? KeepAlive { get; init; }

    public IReportStreamSocket Open(ReportRequest handshake, ReportStreamSettings settings, Action<ReportStreamEvent> events)
    {
        ArgumentNullException.ThrowIfNull(handshake);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(events);
        var keepAlive = KeepAlive ?? (TimeSpan.FromSeconds(settings.PingIntervalSeconds), TimeSpan.FromSeconds(settings.PongTimeoutSeconds));
        var socket = new ReportStreamSocket(Interlocked.Increment(ref _generations), settings.MaxMessageBytes, events);
        socket.Start(handshake, _handler(), keepAlive.Interval, keepAlive.Timeout);
        return socket;
    }

    /// <summary><c>wss</c>, or <c>ws</c> only to <c>127.0.0.1</c>, <c>[::1]</c> or <c>localhost</c> (REPORTING 2.2).</summary>
    public static bool IsAllowed(Uri url) =>
        url.IsAbsoluteUri && url.UserInfo.Length == 0 && url.Fragment.Length == 0
        && (url.Scheme == Uri.UriSchemeWss || (url.Scheme == Uri.UriSchemeWs && url.IsLoopback));
}

/// <summary>One connection of <see cref="WebSocketReportTransport"/>: a receive loop and a writer, at most one of each.</summary>
internal sealed class ReportStreamSocket : IReportStreamSocket
{
    private const int Live = 0;
    private const int EndedByOwner = 1;
    private const int EndedByConnection = 2;
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(1);
    private static readonly byte[] CloseMarker = [];

    private readonly object _gate = new();
    private readonly int _maxMessageBytes;
    private readonly Action<ReportStreamEvent> _events;
    private readonly ClientWebSocket _socket = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(ReportStreamSettings.Fields[1].High + 2)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private int _state;

    public ReportStreamSocket(long generation, int maxMessageBytes, Action<ReportStreamEvent> events)
    {
        Generation = generation;
        _maxMessageBytes = maxMessageBytes;
        _events = events;
    }

    public long Generation { get; }

    public bool Write(string message)
    {
        var data = Encoding.UTF8.GetBytes(message);
        return Volatile.Read(ref _state) == Live && data.Length > 0 && data.Length <= _maxMessageBytes && _outgoing.Writer.TryWrite(data);
    }

    public void Close()
    {
        if (EndBy(EndedByOwner))
        {
            _outgoing.Writer.TryWrite(CloseMarker);
            _outgoing.Writer.TryComplete();
            _ = AbortLater();
        }
    }

    public void Abort()
    {
        EndBy(EndedByOwner);
        Stop();
    }

    internal void Start(ReportRequest handshake, HttpMessageHandler handler, TimeSpan keepAliveInterval, TimeSpan keepAliveTimeout)
    {
        if (!WebSocketReportTransport.IsAllowed(handshake.Url))
        {
            ReportLog.Write("websocket refused a URL that is neither wss nor loopback ws");
            handler.Dispose();
            End(1006);
            return;
        }

        foreach (var (name, value) in handshake.Headers)
        {
            if (string.Equals(name, ReportWebSocketProtocol.SubprotocolHeader, StringComparison.OrdinalIgnoreCase))
            {
                _socket.Options.AddSubProtocol(value);
            }
            else if (!name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && !string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                _socket.Options.SetRequestHeader(name, value);
            }
        }

        _socket.Options.KeepAliveInterval = keepAliveInterval;
        _socket.Options.KeepAliveTimeout = keepAliveTimeout;
        _ = Task.Run(() => RunAsync(handshake, new ReportStreamUpgradeHandler(handler)));
    }

    private async Task RunAsync(ReportRequest handshake, ReportStreamUpgradeHandler upgrade)
    {
        var token = _stop.Token;
        using var invoker = new HttpMessageInvoker(upgrade, disposeHandler: true);
        try
        {
            try
            {
                await _socket.ConnectAsync(handshake.Url, invoker, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException or HttpRequestException or IOException or OperationCanceledException)
            {
                if (upgrade.Refused)
                {
                    var status = upgrade.Status!.Value;
                    ReportLog.Write($"websocket {handshake} -> {status} {upgrade.Body.Length} bytes");
                    Emit(new ReportStreamEvent.Rejected(Generation, ReportStreamRejection.Classify(status, upgrade.Body, upgrade.RetryAfter)), ending: true);
                }
                else
                {
                    // A 101 that the client library refused (a subprotocol it did not offer, a wrong accept key) is a protocol error.
                    var code = upgrade.Status is not null ? 1002 : 1006;
                    ReportLog.Write($"websocket {handshake} failed: {(code == 1002 ? "invalid upgrade" : e.GetType().Name)}");
                    End(code);
                }

                return;
            }

            if (_socket.SubProtocol != ReportWebSocketProtocol.Subprotocol)
            {
                ReportLog.Write($"websocket {handshake} -> 101 without the subprotocol");
                await CloseWith(WebSocketCloseStatus.ProtocolError).ConfigureAwait(false);
                End(1002);
                return;
            }

            ReportLog.Write($"websocket {handshake} -> 101");
            Emit(new ReportStreamEvent.Opened(Generation), ending: false);
            _ = Task.Run(WriteAsync);
            await ReceiveAsync(token).ConfigureAwait(false);
        }
        finally
        {
            Stop();
        }
    }

    private async Task ReceiveAsync(CancellationToken token)
    {
        var buffer = new byte[_maxMessageBytes];
        try
        {
            while (true)
            {
                var count = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (count == buffer.Length)
                    {
                        await Invalid("too large").ConfigureAwait(false);
                        return;
                    }

                    result = await _socket.ReceiveAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        var code = (int?)_socket.CloseStatus ?? 1005;
                        ReportLog.Write($"websocket closed by the service with {code}");
                        await CloseWith(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
                        End(code);
                        return;
                    }

                    count += result.Count;
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await Invalid("binary").ConfigureAwait(false);
                    return;
                }

                switch (ReportServiceMessage.Parse(buffer.AsSpan(0, count), _maxMessageBytes))
                {
                    case null:
                        await Invalid("invalid message").ConfigureAwait(false);
                        return;
                    case ReportServiceMessage.Other:
                        break;
                    case var message:
                        Emit(new ReportStreamEvent.Message(Generation, message), ending: false);
                        break;
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (Volatile.Read(ref _state) == Live)
            {
                ReportLog.Write($"websocket ended: {(e is WebSocketException socket ? socket.WebSocketErrorCode.ToString() : e.GetType().Name)}");
            }

            End(1006);
        }
    }

    private async Task WriteAsync()
    {
        var token = _stop.Token;
        try
        {
            await foreach (var data in _outgoing.Reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                if (ReferenceEquals(data, CloseMarker))
                {
                    await CloseWith(WebSocketCloseStatus.NormalClosure).ConfigureAwait(false);
                    return;
                }

                await _socket.SendAsync(data, WebSocketMessageType.Text, endOfMessage: true, token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            End(1006);
        }
    }

    /// <summary>An invalid or binary message from the service counts as <c>close 1002</c> (METRICS 11.9, Messages).</summary>
    private async Task Invalid(string what)
    {
        ReportLog.Write($"websocket received an unusable message ({what})");
        await CloseWith(WebSocketCloseStatus.ProtocolError).ConfigureAwait(false);
        End(1002);
    }

    /// <summary>Sends a close frame when the state allows one, waiting at most a second.</summary>
    private async Task CloseWith(WebSocketCloseStatus status)
    {
        if (_socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        using var grace = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        grace.CancelAfter(CloseGrace);
        try
        {
            await _socket.CloseOutputAsync(status, null, grace.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException
            or InvalidOperationException)
        {
        }
    }

    private async Task AbortLater()
    {
        try
        {
            await Task.Delay(CloseGrace + CloseGrace, _stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Stop();
    }

    /// <summary>The connection ended by itself: the owner hears it once, unless it ended the connection first.</summary>
    private void End(int code)
    {
        Emit(new ReportStreamEvent.Closed(Generation, code), ending: true);
        Stop();
    }

    private bool EndBy(int state)
    {
        lock (_gate)
        {
            if (_state != Live)
            {
                return false;
            }

            Volatile.Write(ref _state, state);
            return true;
        }
    }

    /// <summary>Events leave one at a time and never after the connection ended.</summary>
    private void Emit(ReportStreamEvent e, bool ending)
    {
        lock (_gate)
        {
            if (_state != Live)
            {
                return;
            }

            if (ending)
            {
                Volatile.Write(ref _state, EndedByConnection);
            }

            try
            {
                _events(e);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                ReportLog.Write($"websocket event handler failed: {ex.GetType().Name}");
            }
        }
    }

    private void Stop()
    {
        _outgoing.Writer.TryComplete();
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _socket.Abort();
        _socket.Dispose();
    }
}
