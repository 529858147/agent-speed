using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentSpeed.Core.Work;

/// <summary>
/// The digest of a tool call's input (METRICS 6.10): the first 16 hex digits of SHA-256 over the input as the file stores
/// it — a string's UTF-8 text, an object's or array's raw JSON slice of the line. Computed while the line is read.
/// </summary>
internal static class ActionDigest
{
    /// <summary>A missing or null input hashes as the empty string.</summary>
    public static ulong Empty { get; } = Of([]);

    public static ulong Of(ReadOnlySpan<byte> utf8)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(utf8, hash);
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }

    public static string Hex(ulong digest) => digest.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Hashes the value the reader is on (a reader over <paramref name="line"/>) and moves past it. A string whose escapes
    /// do not decode (a lone surrogate) hashes as written rather than failing its line.
    /// </summary>
    public static ulong ReadInput(ref Utf8JsonReader reader, ReadOnlySpan<byte> line)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String when reader.ValueIsEscaped:
                var buffer = ArrayPool<byte>.Shared.Rent(reader.ValueSpan.Length);
                try
                {
                    return Of(buffer.AsSpan(0, reader.CopyString(buffer)));
                }
                catch (InvalidOperationException)
                {
                    return Of(reader.ValueSpan);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                var start = (int)reader.TokenStartIndex;
                reader.Skip();
                return Of(line[start..(int)reader.BytesConsumed]);
            case JsonTokenType.Null:
                return Empty;
            default:
                return Of(reader.ValueSpan);
        }
    }
}
