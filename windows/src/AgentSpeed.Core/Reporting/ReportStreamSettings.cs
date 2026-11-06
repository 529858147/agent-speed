using System.Text.Json;

namespace AgentSpeed.Core.Reporting;

/// <summary>
/// The stream settings of a discovery document's <c>websocket</c> object (METRICS 11.9, Availability). Unlike <c>limits</c>
/// they are never clamped: a field of another type or out of its range makes the whole object invalid.
/// </summary>
public sealed record ReportStreamSettings(
    int MinIntervalMs = 50,
    int MaxInFlight = 8,
    int AckTimeoutMs = 2000,
    int MaxMessageBytes = 65_536,
    int PingIntervalSeconds = 20,
    int PongTimeoutSeconds = 10)
{
    /// <summary>The fields in document order with their ranges.</summary>
    public static IReadOnlyList<(string Name, int Low, int High)> Fields { get; } =
    [
        ("minIntervalMs", 10, 1000), ("maxInFlight", 1, 64), ("ackTimeoutMs", 500, 10_000),
        ("maxMessageBytes", 32_768, 65_536), ("pingIntervalSeconds", 10, 60), ("pongTimeoutSeconds", 5, 30),
    ];

    public static ReportStreamSettings Default { get; } = new();

    /// <summary>The values in the order of <see cref="Fields"/>.</summary>
    public IReadOnlyList<int> Values => [MinIntervalMs, MaxInFlight, AckTimeoutMs, MaxMessageBytes, PingIntervalSeconds, PongTimeoutSeconds];

    /// <summary>The object as the client stores it: it parses back to the same settings.</summary>
    public ReportJson Json => ReportJson.Object(
        new[] { ("path", ReportJson.Of(ReportWebSocketProtocol.Path)), ("subprotocol", ReportJson.Of(ReportWebSocketProtocol.Subprotocol)) }
            .Concat(Fields.Select((entry, index) => (entry.Name, ReportJson.Of(Values[index])))));

    /// <summary>The settings of a <c>websocket</c> object, or null when it is not a valid one.</summary>
    public static ReportStreamSettings? Parse(JsonElement websocket)
    {
        if (ReportStrictJson.Member(websocket, "path") is not { } path || ReportStrictJson.Text(path) != ReportWebSocketProtocol.Path
            || ReportStrictJson.Member(websocket, "subprotocol") is not { } subprotocol
            || ReportStrictJson.Text(subprotocol) != ReportWebSocketProtocol.Subprotocol)
        {
            return null;
        }

        var values = Default.Values.ToArray();
        for (var index = 0; index < Fields.Count; index++)
        {
            var (name, low, high) = Fields[index];
            if (!websocket.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (ReportStrictJson.Integer(value) is not { } number || number < low || number > high)
            {
                return null;
            }

            values[index] = (int)number;
        }

        var settings = FromValues(values);
        return settings.PongTimeoutSeconds < settings.PingIntervalSeconds ? settings : null;
    }

    /// <summary>Six values in the order of <see cref="Fields"/>, saturated to the <see cref="int"/> range (a <c>ready</c> may carry any integer).</summary>
    public static ReportStreamSettings FromValues(IReadOnlyList<long> values) =>
        FromValues(values.Select(v => (int)Math.Clamp(v, int.MinValue, int.MaxValue)).ToArray());

    private static ReportStreamSettings FromValues(int[] values) => new(values[0], values[1], values[2], values[3], values[4], values[5]);
}
