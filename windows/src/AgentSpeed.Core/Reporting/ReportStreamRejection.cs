using System.Globalization;

namespace AgentSpeed.Core.Reporting;

/// <summary>
/// A handshake answered without <c>101</c> (METRICS 11.9, Reactions): the code of its v1 error body, else the one its status
/// stands for, and the wait (<c>retryAfterSeconds</c> of the body, else the <c>Retry-After</c> header in delay seconds).
/// </summary>
/// <param name="Code">Null for a plain failure (<c>5xx</c> without a wait, a <c>3xx</c> …).</param>
public sealed record ReportStreamRejection(int Status, string? Code, int? RetryAfterSeconds)
{
    public const int MaxBodyBytes = 16_384;

    /// <summary>The v1 error body's <c>serverTime</c> (Unix seconds), for <c>timestamp_out_of_window</c> (METRICS 11.6).</summary>
    public long? ServerTime { get; init; }

    public static ReportStreamRejection Classify(int status, ReadOnlySpan<byte> body, string? retryAfter)
    {
        var header = RetryAfterHeader(retryAfter);
        string? code = null;
        int? wait = null;
        long? serverTime = null;
        if (!body.IsEmpty)
        {
            using var document = ReportStrictJson.ParseObject(body, 65_536);
            if (document is not null && ReportStrictJson.Member(document.RootElement, "error") is { ValueKind: System.Text.Json.JsonValueKind.Object } error
                && ReportStrictJson.Member(error, "code") is { } codeValue && ReportStrictJson.Text(codeValue) is { } text && ReportReply.IsCode(text))
            {
                code = text;
                wait = ReportStrictJson.Member(error, "retryAfterSeconds") is { } given && ReportStrictJson.Integer(given) is >= 1 and var seconds
                    ? (int)Math.Min(seconds, int.MaxValue)
                    : null;
                serverTime = ReportStrictJson.Member(error, "serverTime") is { } time && ReportStrictJson.Integer(time) is >= 0 and var at ? at : null;
            }
        }

        code ??= status switch
        {
            401 => "signature_invalid",
            403 => "forbidden",
            404 => "unknown_device",
            410 => "gone",
            413 => "payload_too_large",
            429 => "rate_limited",
            503 when header is not null => "rate_limited",
            >= 400 and < 500 => "bad_request",
            _ => null,
        };
        return new ReportStreamRejection(status, code, wait ?? header) { ServerTime = serverTime };
    }

    /// <summary>A <c>Retry-After</c> header in delay seconds; an HTTP date counts as absent.</summary>
    public static int? RetryAfterHeader(string? value)
    {
        var text = value?.Trim();
        return text is { Length: >= 1 and <= 9 } && text.All(char.IsAsciiDigit)
            ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;
    }
}
