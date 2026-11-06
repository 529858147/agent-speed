using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.Reporting;

public enum ReportAckStatus
{
    Accepted,
    Duplicate,
}

/// <summary>A message of the service on the WebSocket transport, parsed strictly (METRICS 11.9, Messages).</summary>
public abstract partial record ReportServiceMessage
{
    public const long MaxSeq = 9_007_199_254_740_991;

    private ReportServiceMessage()
    {
    }

    /// <summary><c>ready</c>: the six limits in the order of <see cref="ReportStreamSettings.Fields"/>, any integers (the scheduler compares them).</summary>
    public sealed record Ready(ReportStreamSettings Limits) : ReportServiceMessage;

    public sealed record Ack(string RunId, long Seq, ReportAckStatus Status, string BodySha256, int? Rank) : ReportServiceMessage;

    /// <summary><c>error</c>: <see cref="RunId"/> and <see cref="Seq"/> are null for <c>"run": null</c>.</summary>
    public sealed record Error(string? RunId, long? Seq, int HttpStatus, string Code, int? RetryAfterSeconds, long? ServerTime, bool Fatal)
        : ReportServiceMessage;

    /// <summary>A message of another <c>type</c>: ignored.</summary>
    public sealed record Other : ReportServiceMessage;

    /// <summary>The message, or null when it is invalid (the connection then counts as <c>close 1002</c>).</summary>
    public static ReportServiceMessage? Parse(ReadOnlySpan<byte> data, int maxBytes)
    {
        using var document = ReportStrictJson.ParseObject(data, maxBytes);
        if (document is null)
        {
            return null;
        }

        var root = document.RootElement;
        return ReportStrictJson.Member(root, "type") is { } type && ReportStrictJson.Text(type) is { } kind
            ? kind switch
            {
                "ready" => ParseReady(root),
                "ack" => ParseAck(root),
                "error" => ParseError(root),
                _ => new Other(),
            }
            : null;
    }

    /// <summary>A sent-at style instant, <c>YYYY-MM-DDTHH:MM:SS.sssZ</c> (shape only).</summary>
    public static bool IsInstant(string? text) => text is not null && InstantPattern().IsMatch(text);

    private static Ready? ParseReady(JsonElement root)
    {
        if (Text(root, "protocol") != ReportDiscovery.ProtocolName || Integer(root, "version") != 1 || Integer(root, "transportVersion") != 1
            || !IsInstant(Text(root, "serverTime")))
        {
            return null;
        }

        var limits = new long[ReportStreamSettings.Fields.Count];
        for (var index = 0; index < limits.Length; index++)
        {
            if (Integer(root, ReportStreamSettings.Fields[index].Name) is not { } value)
            {
                return null;
            }

            limits[index] = value;
        }

        return new Ready(ReportStreamSettings.FromValues(limits));
    }

    private static Ack? ParseAck(JsonElement root)
    {
        if (Run(root) is not { } run)
        {
            return null;
        }

        ReportAckStatus? status = Text(root, "status") switch
        {
            "accepted" => ReportAckStatus.Accepted,
            "duplicate" => ReportAckStatus.Duplicate,
            _ => null,
        };
        var digest = Text(root, "bodySha256");
        if (status is null || digest is null || !Sha256Pattern().IsMatch(digest) || !IsInstant(Text(root, "receivedAt"))
            || !IsInstant(Text(root, "acceptedAt")) || Integer(root, "processingMs") is not (>= 0 and <= 900_000))
        {
            return null;
        }

        int? rank = null;
        if (ReportStrictJson.Member(root, "rank") is { ValueKind: not JsonValueKind.Null } given)
        {
            if (ReportStrictJson.Integer(given) is not { } value || value is < 1 or > 100_000_000)
            {
                return null;
            }

            rank = (int)value;
        }

        return new Ack(run.Id, run.Seq, status.Value, digest, rank);
    }

    private static Error? ParseError(JsonElement root)
    {
        if (ReportStrictJson.Member(root, "run") is not { } runMember)
        {
            return null;
        }

        (string Id, long Seq)? run = null;
        if (runMember.ValueKind != JsonValueKind.Null)
        {
            if (Run(root) is not { } given)
            {
                return null;
            }

            run = given;
        }

        if (Integer(root, "httpStatus") is not { } status || status is < 400 or > 599
            || ReportStrictJson.Member(root, "error") is not { ValueKind: JsonValueKind.Object } error
            || Text(error, "code") is not { } code || !CodePattern().IsMatch(code))
        {
            return null;
        }

        int? retry = null;
        if (ReportStrictJson.Member(error, "retryAfterSeconds") is { ValueKind: not JsonValueKind.Null } retryValue)
        {
            if (ReportStrictJson.Integer(retryValue) is not { } seconds || seconds is < 1 or > 86_400)
            {
                return null;
            }

            retry = (int)seconds;
        }

        long? serverTime = null;
        if (ReportStrictJson.Member(error, "serverTime") is { ValueKind: not JsonValueKind.Null } timeValue)
        {
            if (ReportStrictJson.Integer(timeValue) is not { } time || time < 0)
            {
                return null;
            }

            serverTime = time;
        }

        bool? fatal = ReportStrictJson.Member(root, "fatal") switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => null,
        };
        return fatal is { } isFatal ? new Error(run?.Id, run?.Seq, (int)status, code, retry, serverTime, isFatal) : null;
    }

    /// <summary><c>run</c>: an object with an <c>id</c> of 1–64 code points and a <c>seq</c> of at least 1.</summary>
    private static (string Id, long Seq)? Run(JsonElement root) =>
        ReportStrictJson.Member(root, "run") is { ValueKind: JsonValueKind.Object } run
        && Text(run, "id") is { } id && id.EnumerateRunes().Count() is >= 1 and <= 64
        && Integer(run, "seq") is >= 1 and <= MaxSeq and var seq
            ? (id, seq)
            : null;

    private static string? Text(JsonElement element, string name) =>
        ReportStrictJson.Member(element, name) is { } value ? ReportStrictJson.Text(value) : null;

    private static long? Integer(JsonElement element, string name) =>
        ReportStrictJson.Member(element, name) is { } value ? ReportStrictJson.Integer(value) : null;

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z\\z", RegexOptions.CultureInvariant)]
    private static partial Regex InstantPattern();

    [GeneratedRegex("^[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
