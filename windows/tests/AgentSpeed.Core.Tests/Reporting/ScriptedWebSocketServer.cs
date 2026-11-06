using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>
/// A scripted RFC 6455 server on a free loopback port for one connection: it answers the upgrade as told, then runs a script
/// over the raw frames. It never answers a Ping, so keep-alive can be tested.
/// </summary>
internal sealed class ScriptedWebSocketServer : IDisposable
{
    private static readonly byte[] Guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"u8.ToArray();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly Task _run;

    /// <param name="answer">The status line and headers after the request's headers were read (null: 101 with the subprotocol).</param>
    /// <param name="script">Runs after a 101 on the connection's stream.</param>
    public ScriptedWebSocketServer(Func<IReadOnlyDictionary<string, string>, string?>? answer = null, Func<Frames, Task>? script = null)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _run = Task.Run(async () =>
        {
            using var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            var stream = client.GetStream();
            var headers = await ReadHeaders(stream).ConfigureAwait(false);
            Headers.TrySetResult(headers);
            var accept = Convert.ToBase64String(SHA1.HashData([.. Encoding.ASCII.GetBytes(headers.GetValueOrDefault("sec-websocket-key", string.Empty)), .. Guid]));
            var head = answer?.Invoke(headers)
                ?? "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Protocol: agent-speed-report.v1\r\n";
            head = head.Replace("{accept}", accept, StringComparison.Ordinal);
            if (head.StartsWith("HTTP/1.1 101", StringComparison.Ordinal) && !head.Contains("Sec-WebSocket-Accept", StringComparison.Ordinal))
            {
                head += "Sec-WebSocket-Accept: " + accept + "\r\n";
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "\r\n")).ConfigureAwait(false);
            if (head.StartsWith("HTTP/1.1 101", StringComparison.Ordinal) && script is not null)
            {
                await script(new Frames(stream)).ConfigureAwait(false);
            }
            else
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
        });
    }

    public int Port { get; }

    public Uri Url => new($"ws://127.0.0.1:{Port}/v1/reports/ws");

    /// <summary>The upgrade request's headers, names lower-case.</summary>
    public TaskCompletionSource<IReadOnlyDictionary<string, string>> Headers { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Finished => _run;

    public void Dispose()
    {
        _listener.Stop();
        _listener.Dispose();
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadHeaders(NetworkStream stream)
    {
        var data = new List<byte>();
        var one = new byte[1];
        while (data.Count < 65_536 && !(data.Count >= 4 && data[^4] == '\r' && data[^3] == '\n' && data[^2] == '\r' && data[^1] == '\n'))
        {
            if (await stream.ReadAsync(one).ConfigureAwait(false) == 0)
            {
                break;
            }

            data.Add(one[0]);
        }

        var lines = Encoding.ASCII.GetString([.. data]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1);
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
            }
        }

        return headers;
    }

    /// <summary>Unmasked server frames out, masked client frames in.</summary>
    internal sealed class Frames(NetworkStream stream)
    {
        public const int Text = 0x1;
        public const int Binary = 0x2;
        public const int Close = 0x8;
        public const int Ping = 0x9;
        public const int Pong = 0xA;

        public Task SendText(string text) => Send(Text, Encoding.UTF8.GetBytes(text));

        public Task SendClose(int code)
        {
            var payload = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)code);
            return Send(Close, payload);
        }

        public async Task Send(int opcode, byte[] payload)
        {
            var header = new List<byte> { (byte)(0x80 | opcode) };
            if (payload.Length < 126)
            {
                header.Add((byte)payload.Length);
            }
            else
            {
                header.Add(126);
                header.Add((byte)(payload.Length >> 8));
                header.Add((byte)payload.Length);
            }

            byte[] frame = [.. header, .. payload];
            await stream.WriteAsync(frame).ConfigureAwait(false);
        }

        /// <summary>The next client frame, or null when the connection ended.</summary>
        public async Task<(int Opcode, byte[] Payload)?> Receive(TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            try
            {
                var head = await Exactly(2, deadline.Token).ConfigureAwait(false);
                long size = head[1] & 0x7F;
                if (size == 126)
                {
                    size = BinaryPrimitives.ReadUInt16BigEndian(await Exactly(2, deadline.Token).ConfigureAwait(false));
                }
                else if (size == 127)
                {
                    size = (long)BinaryPrimitives.ReadUInt64BigEndian(await Exactly(8, deadline.Token).ConfigureAwait(false));
                }

                var mask = await Exactly(4, deadline.Token).ConfigureAwait(false);
                var payload = await Exactly((int)size, deadline.Token).ConfigureAwait(false);
                for (var i = 0; i < payload.Length; i++)
                {
                    payload[i] ^= mask[i % 4];
                }

                return (head[0] & 0x0F, payload);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or EndOfStreamException)
            {
                return null;
            }
        }

        private async Task<byte[]> Exactly(int count, CancellationToken token)
        {
            var buffer = new byte[count];
            await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
            return buffer;
        }
    }
}
