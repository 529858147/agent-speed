using System.Net;
using System.Net.Http;

namespace AgentSpeed.Core.Reporting;

/// <summary>
/// Sits between <see cref="System.Net.WebSockets.ClientWebSocket"/> and the HTTP handler of one connection: it notes the
/// upgrade's status and, when the service refused it, keeps the first 16 KiB of the v1 error body and the <c>Retry-After</c>
/// header, which the WebSocket client itself discards (METRICS 11.9, Reactions). Nothing is logged.
/// </summary>
internal sealed class ReportStreamUpgradeHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private const int ChunkSize = 4096;

    /// <summary>The upgrade's status; null before an answer arrived.</summary>
    public int? Status { get; private set; }

    /// <summary>A refused upgrade's body; empty when it was longer than <see cref="ReportStreamRejection.MaxBodyBytes"/> or unreadable.</summary>
    public byte[] Body { get; private set; } = [];

    public string? RetryAfter { get; private set; }

    /// <summary>The upgrade was answered with something other than <c>101</c>.</summary>
    public bool Refused => Status is { } status && status != (int)HttpStatusCode.SwitchingProtocols;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        Status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.SwitchingProtocols)
        {
            return response;
        }

        RetryAfter = response.Headers.NonValidated.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null;
        try
        {
            Body = await ReadBounded(response.Content, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            Body = [];
        }

        return response;
    }

    private static async Task<byte[]> ReadBounded(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > ReportStreamRejection.MaxBodyBytes)
        {
            return [];
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[ReportStreamRejection.MaxBodyBytes + 1];
            var count = 0;
            int read;
            while (count < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(count, Math.Min(ChunkSize, buffer.Length - count)),
                cancellationToken).ConfigureAwait(false)) > 0)
            {
                count += read;
            }

            return count > ReportStreamRejection.MaxBodyBytes ? [] : buffer[..count];
        }
    }
}
