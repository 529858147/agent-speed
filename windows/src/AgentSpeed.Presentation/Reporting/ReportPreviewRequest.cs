using System.Globalization;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Presentation.Reporting;

/// <summary>
/// The request line and headers the Preview window shows above the body (DESIGN 11, Preview window): the nonce, the signature
/// and the WebSocket key as <c>…</c>, the API key as <c>Bearer ••••</c>. HTTP shows the report's <c>POST</c>; WebSocket the
/// connection's signed upgrade (<c>GET wss://…/v1/reports/ws</c>), since its reports travel inside that connection.
/// </summary>
public sealed record ReportPreviewRequest(string RequestLine, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    public const string Hidden = "…";
    public const string MaskedBearer = "Bearer ••••";

    /// <param name="url">The reports endpoint.</param>
    public static ReportPreviewRequest Http(Uri url, string userAgent, string deviceId, long timestamp, int contentLength, bool hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(url);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Host", ReportUrls.Authority(url)),
            new("User-Agent", userAgent),
            new("Accept", "application/json"),
            new("Content-Type", "application/json"),
            new("Content-Length", contentLength.ToString(CultureInfo.InvariantCulture)),
        };
        AddSignature(headers, deviceId, timestamp, hasApiKey);
        return new ReportPreviewRequest($"POST {ReportUrls.RequestPath(url)} HTTP/1.1", headers);
    }

    /// <param name="url">The connection URL (<see cref="ReportWebSocketProtocol.Url"/>).</param>
    public static ReportPreviewRequest WebSocket(Uri url, string userAgent, string deviceId, long timestamp, bool hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(url);
        var headers = new List<KeyValuePair<string, string>>
        {
            new("Host", ReportUrls.Authority(url)),
            new("Upgrade", "websocket"),
            new("Connection", "Upgrade"),
            new("Sec-WebSocket-Version", "13"),
            new("Sec-WebSocket-Key", Hidden),
            new(ReportWebSocketProtocol.SubprotocolHeader, ReportWebSocketProtocol.Subprotocol),
            new("User-Agent", userAgent),
            new("Accept", "application/json"),
        };
        AddSignature(headers, deviceId, timestamp, hasApiKey);
        return new ReportPreviewRequest($"GET {url.AbsoluteUri}", headers);
    }

    private static void AddSignature(List<KeyValuePair<string, string>> headers, string deviceId, long timestamp, bool hasApiKey)
    {
        headers.Add(new(ReportCanonical.DeviceHeader, deviceId));
        headers.Add(new(ReportCanonical.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture)));
        headers.Add(new(ReportCanonical.NonceHeader, Hidden));
        headers.Add(new(ReportCanonical.SignatureHeader, Hidden));
        if (hasApiKey)
        {
            headers.Add(new("Authorization", MaskedBearer));
        }
    }
}
