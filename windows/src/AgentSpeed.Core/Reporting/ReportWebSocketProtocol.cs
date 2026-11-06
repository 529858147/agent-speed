using System.Text;

namespace AgentSpeed.Core.Reporting;

/// <summary>The WebSocket transport's fixed names, URL, signed handshake and <c>report</c> message (METRICS 11.9).</summary>
public static class ReportWebSocketProtocol
{
    public const string Path = "/v1/reports/ws";
    public const string Subprotocol = "agent-speed-report.v1";
    public const string Extension = "reportWebSocket";
    public const string SubprotocolHeader = "Sec-WebSocket-Protocol";

    /// <summary>The four headers of a v1 signature, in the order the message writes them.</summary>
    public static IReadOnlyList<string> SignatureHeaders { get; } =
        [ReportCanonical.DeviceHeader, ReportCanonical.TimestampHeader, ReportCanonical.NonceHeader, ReportCanonical.SignatureHeader];

    /// <summary><c>api</c> with <c>https</c> → <c>wss</c> (<c>http</c> → <c>ws</c> only for a loopback test service), plus the path.</summary>
    public static Uri Url(ReportServiceUrl api) =>
        new((api.IsLoopbackHttp ? "ws://" : "wss://") + api.Text[(api.Scheme.Length + 3)..] + Path, UriKind.Absolute);

    /// <summary>The upgrade request: the v1 signature over <c>GET</c>, the WebSocket path and an empty body, plus the subprotocol.</summary>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key cannot sign.</exception>
    public static ReportRequest Handshake(ReportRequestBuilder builder, ReportServiceUrl api, IReportSigner signer, long timestamp, string? nonce = null)
    {
        var request = builder.Make("GET", Url(api), [], signer, timestamp, nonce);
        return request with { Headers = [.. request.Headers, new(SubprotocolHeader, Subprotocol)] };
    }

    /// <summary>
    /// One <c>report</c> message: the four signature headers of the report's v1 <c>POST</c> (names in any case) and the body
    /// as a JSON string, with only <c>"</c>, <c>\</c> and control characters escaped.
    /// </summary>
    public static string ReportMessage(IReadOnlyList<KeyValuePair<string, string>> headers, ReadOnlySpan<byte> body)
    {
        var text = new StringBuilder(body.Length + 512);
        text.Append("{\"type\":\"report\",\"headers\":{");
        for (var index = 0; index < SignatureHeaders.Count; index++)
        {
            var name = SignatureHeaders[index];
            var value = headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;
            text.Append(index > 0 ? "," : string.Empty).Append(ReportJson.Quoted(name)).Append(':').Append(ReportJson.Quoted(value));
        }

        return text.Append("},\"body\":").Append(ReportJson.Quoted(Encoding.UTF8.GetString(body))).Append('}').ToString();
    }

    /// <summary>
    /// The UTF-8 size of the <c>report</c> message of <paramref name="body"/>: the signature headers have fixed lengths (a device
    /// id, ten timestamp digits, a 16-byte nonce and a 64-byte signature), so only the body's escaping varies.
    /// </summary>
    public static int MessageBytes(ReadOnlySpan<byte> body)
    {
        var size = MessageOverhead;
        foreach (var b in body)
        {
            size += b switch
            {
                (byte)'"' or (byte)'\\' or (byte)'\b' or (byte)'\f' or (byte)'\n' or (byte)'\r' or (byte)'\t' => 2,
                < 0x20 => 6,
                _ => 1,
            };
        }

        return size;
    }

    private static readonly int MessageOverhead = ReportMessage(
    [
        new(ReportCanonical.DeviceHeader, new string('0', 36)), new(ReportCanonical.TimestampHeader, new string('0', 10)),
        new(ReportCanonical.NonceHeader, new string('0', 22)), new(ReportCanonical.SignatureHeader, new string('0', 86)),
    ], []).Length;

    /// <summary>
    /// The <c>realtime</c> object:<c>mixed</c> when the report carries traffic with status <c>ok</c>, else <c>log</c>;
    /// <paramref name="sentAtMs"/> is <c>sentAt</c> on the corrected clock.
    /// </summary>
    public static ReportJson Realtime(bool mixed, long sentAtMs, long? observationAgeMs) => ReportJson.Object(
        ("sourceMode", ReportJson.Of(mixed ? "mixed" : "log")),
        ("observedAt", observationAgeMs is { } age ? ReportJson.Of(ReportInstant.Format(sentAtMs - age)) : ReportJson.Null),
        ("observationAgeMs", ReportJson.OfOptional(observationAgeMs)),
        ("sourceResolutionMs", mixed ? ReportJson.Null : ReportJson.Of(1000)));
}
