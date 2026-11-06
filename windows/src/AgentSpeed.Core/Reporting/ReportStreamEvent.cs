namespace AgentSpeed.Core.Reporting;

/// <summary>
/// What one WebSocket connection tells its owner (METRICS 11.9), tagged with the connection's generation so that events of
/// an ended connection never reach the stream scheduler. A connection ends with <see cref="Rejected"/> or
/// <see cref="Closed"/> unless its owner ended it first (then nothing more arrives).
/// </summary>
public abstract record ReportStreamEvent(long Generation)
{
    /// <summary>The <c>101</c> selected <c>agent-speed-report.v1</c>.</summary>
    public sealed record Opened(long Generation) : ReportStreamEvent(Generation);

    /// <summary>A valid <c>ready</c>, <c>ack</c> or <c>error</c> (messages of another type are ignored).</summary>
    public sealed record Message(long Generation, ReportServiceMessage Value) : ReportStreamEvent(Generation);

    /// <summary>The upgrade was answered without <c>101</c>.</summary>
    public sealed record Rejected(long Generation, ReportStreamRejection Rejection) : ReportStreamEvent(Generation);

    /// <summary>
    /// The connection ended: the service's close code, <c>1006</c> for the network (a missing Pong included), <c>1002</c>
    /// for a <c>101</c> without the subprotocol or an invalid or binary message.
    /// </summary>
    public sealed record Closed(long Generation, int Code) : ReportStreamEvent(Generation);
}

/// <summary>One connection: messages are written in order; <see cref="Close"/> and <see cref="Abort"/> end it without events.</summary>
public interface IReportStreamSocket
{
    long Generation { get; }

    /// <summary>Queues one text message; false when the connection has ended or the message is over its size limit.</summary>
    bool Write(string message);

    /// <summary><c>close 1000</c> after the queued messages, then the socket goes within a second.</summary>
    void Close();

    /// <summary>Drops the socket without a close handshake.</summary>
    void Abort();
}

/// <summary>The network seam of the WebSocket transport; tests use a scripted one or the reference server.</summary>
public interface IReportStreamTransport
{
    /// <summary>
    /// Opens a connection with the signed upgrade <paramref name="handshake"/>; its events arrive in order on any thread through
    /// <paramref name="events"/>. Only <c>wss</c>, or <c>ws</c> to a loopback host, is opened; anything else ends at once with
    /// <c>1006</c>.
    /// </summary>
    IReportStreamSocket Open(ReportRequest handshake, ReportStreamSettings settings, Action<ReportStreamEvent> events);
}
