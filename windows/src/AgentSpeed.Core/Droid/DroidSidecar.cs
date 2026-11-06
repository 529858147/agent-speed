using System.Text.Json;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Droid;

/// <summary>What identifies one version of a sidecar: its size, modification time and file identity.</summary>
internal readonly record struct DroidSidecarSignature(long Length, long WriteTicks, FileIdentity? Identity);

internal enum DroidSidecarStatus : byte
{
    /// <summary>No regular file at the path: nothing changes.</summary>
    Missing,

    /// <summary>The same version as the last one read: nothing changes.</summary>
    Unchanged,

    /// <summary>Not a regular file, larger than 1 MiB, not UTF-8 or not a JSON object: nothing changes, the next read cycle tries again.</summary>
    Unreadable,
    Readable,
}

/// <summary>One read of a sidecar (METRICS 4.16 Sidecar).</summary>
/// <param name="Counters">The <c>tokenUsage</c> counters; null when it is not an object (droid before 0.123).</param>
/// <param name="Model">The session model: the router's pick, else <c>model</c> unless <c>auto</c>; trimmed; null when neither.</param>
/// <param name="Link">The path is a link (never followed); its time does not count as the session's.</param>
internal readonly record struct DroidSidecarRead(
    DroidSidecarStatus Status, DroidSidecarSignature? Signature = null, DroidCounters? Counters = null, string? Model = null,
    bool Link = false);

/// <summary>Reads Factory Droid's per-session settings files: whole, at most 1 MiB, opened only for the read.</summary>
internal static class DroidSidecar
{
    public const int MaxBytes = 1_048_576;

    /// <summary>
    /// Reads the sidecar at <paramref name="path"/> unless its signature equals <paramref name="known"/>. The file is opened
    /// with read, write and delete sharing (droid replaces it and keeps a <c>.bak</c>) and closed right after reading.
    /// Every result but <see cref="DroidSidecarStatus.Missing"/> has a signature: one that cannot be opened (a link, a
    /// sharing violation) is signed by its size and time alone, so a parent sidecar that stays unreadable is recognized.
    /// </summary>
    public static DroidSidecarRead Read(string path, DroidSidecarSignature? known = null)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return new DroidSidecarRead(DroidSidecarStatus.Missing);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DroidSidecarRead(DroidSidecarStatus.Missing);
        }

        try
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return new DroidSidecarRead(DroidSidecarStatus.Unreadable, StatSignature(info), Link: true);
            }

            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = RandomAccess.GetLength(handle);
            var signature = new DroidSidecarSignature(length, File.GetLastWriteTimeUtc(handle).Ticks, FileIdentity.From(handle));
            if (signature == known)
            {
                return new DroidSidecarRead(DroidSidecarStatus.Unchanged, signature);
            }

            if (length > MaxBytes)
            {
                return new DroidSidecarRead(DroidSidecarStatus.Unreadable, signature);
            }

            var buffer = new byte[(int)length + 1];
            var read = 0;
            int count;
            while (read < buffer.Length && (count = RandomAccess.Read(handle, buffer.AsSpan(read), read)) > 0)
            {
                read += count;
            }

            return read > MaxBytes ? new DroidSidecarRead(DroidSidecarStatus.Unreadable, signature) : Parse(buffer.AsSpan(0, read), signature);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new DroidSidecarRead(DroidSidecarStatus.Missing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new DroidSidecarRead(DroidSidecarStatus.Unreadable, StatSignature(info));
        }
    }

    private static DroidSidecarSignature? StatSignature(FileInfo info)
    {
        try
        {
            info.Refresh();
            return info.Exists ? new DroidSidecarSignature(info.Length, info.LastWriteTimeUtc.Ticks, null) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>A sidecar's bytes: strict UTF-8, one leading U+FEFF removed, a JSON object (METRICS 3.6).</summary>
    public static DroidSidecarRead Parse(ReadOnlySpan<byte> data, DroidSidecarSignature? signature = null)
    {
        var unreadable = new DroidSidecarRead(DroidSidecarStatus.Unreadable, signature);
        if (data.Length > MaxBytes || !System.Text.Unicode.Utf8.IsValid(data))
        {
            return unreadable;
        }

        if (data.StartsWith("﻿"u8))
        {
            data = data[3..];
            if (data.StartsWith("﻿"u8))
            {
                return unreadable;
            }
        }

        string? model = null, routed = null;
        Span<long> counters = stackalloc long[DroidCounters.FieldCount];
        var hasCounters = false;
        try
        {
            var reader = new Utf8JsonReader(data, JsonScan.Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return unreadable;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("model"u8))
                {
                    reader.Read();
                    model = JsonScan.ReadToken(ref reader);
                }
                else if (reader.ValueTextEquals("effectiveFactoryRouterModel"u8))
                {
                    reader.Read();
                    routed = null;
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                        {
                            var isModel = reader.ValueTextEquals("modelId"u8);
                            reader.Read();
                            if (isModel)
                            {
                                routed = JsonScan.ReadToken(ref reader);
                            }
                            else
                            {
                                JsonScan.SkipValue(ref reader);
                            }
                        }
                    }
                    else
                    {
                        JsonScan.SkipValue(ref reader);
                    }
                }
                else if (reader.ValueTextEquals("tokenUsage"u8))
                {
                    reader.Read();
                    counters.Clear();
                    hasCounters = reader.TokenType == JsonTokenType.StartObject;
                    if (hasCounters)
                    {
                        ReadCounters(ref reader, counters);
                    }
                    else
                    {
                        JsonScan.SkipValue(ref reader);
                    }
                }
                else
                {
                    reader.Read();
                    JsonScan.SkipValue(ref reader);
                }
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return unreadable;
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return unreadable;
        }

        return new DroidSidecarRead(
            DroidSidecarStatus.Readable,
            signature,
            hasCounters ? DroidCounters.FromFields(counters) : null,
            SessionModel(routed, model));
    }

    /// <summary><c>effectiveFactoryRouterModel.modelId</c> when not blank, else <c>model</c> when not blank and not <c>auto</c>.</summary>
    private static string? SessionModel(string? routed, string? model) =>
        routed?.Trim() is { Length: > 0 } pick ? pick
        : model?.Trim() is { Length: > 0 } chosen && !chosen.Equals("auto", StringComparison.OrdinalIgnoreCase) ? chosen
        : null;

    private static void ReadCounters(ref Utf8JsonReader reader, scoped Span<long> counters)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var field = reader.ValueTextEquals("inputTokens"u8) ? 0
                : reader.ValueTextEquals("cacheCreationTokens"u8) ? 1
                : reader.ValueTextEquals("cacheReadTokens"u8) ? 2
                : reader.ValueTextEquals("outputTokens"u8) ? 3
                : reader.ValueTextEquals("thinkingTokens"u8) ? 4
                : -1;
            reader.Read();
            if (field >= 0)
            {
                counters[field] = JsonScan.ReadCount(ref reader);
            }
            else
            {
                JsonScan.SkipValue(ref reader);
            }
        }
    }
}
