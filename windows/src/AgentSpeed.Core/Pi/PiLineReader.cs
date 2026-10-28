using System.Text.Json;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Pi;

/// <summary>
/// Pure, thread-safe extraction of <see cref="PiLine"/> (METRICS 4.13). Duplicate keys resolve like Python's
/// <c>json.loads</c>: the last occurrence wins.
/// </summary>
internal static class PiLineReader
{
    private const double TwoTo63 = 9_223_372_036_854_775_808.0;

    public static PiLine Read(ReadOnlySpan<byte> line)
    {
        if (JsonScan.IsBlank(line))
        {
            return new PiLine { Status = LineStatus.Blank };
        }

        // Like json.loads on the line's bytes: invalid UTF-8 anywhere, even in values never read, makes it malformed.
        if (!System.Text.Unicode.Utf8.IsValid(line))
        {
            return Malformed();
        }

        var result = default(PiLine);
        var top = default(TopLevel);
        try
        {
            var reader = new Utf8JsonReader(line, JsonScan.Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Malformed();
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                ReadTopLevelProperty(ref reader, ref result, ref top);
            }

            if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            {
                return Malformed();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return Malformed();
        }

        Classify(ref result, in top);
        return result;
    }

    private static PiLine Malformed() => new() { Status = LineStatus.Malformed };

    private static void Classify(ref PiLine result, in TopLevel top)
    {
        result.Kind = top.Type switch
        {
            TypeName.Session => PiEntryKind.Session,
            TypeName.Message => top.HasMessage ? PiEntryKind.Message : PiEntryKind.Other,
            TypeName.Usage or TypeName.Compaction or TypeName.BranchSummary when top.Call.HasUsageObject =>
                PiEntryKind.SideUsage,
            _ => PiEntryKind.Other,
        };

        if (result.Kind == PiEntryKind.Message)
        {
            var message = top.Message;
            result.Role = message.Role;
            result.Call = message.Call;
            result.StartedMs = message.StartedMs;
            result.DurationMs = message.DurationMs;
            result.Stop = message.Stop;
            result.ToolCallIds = message.ToolCallIds;
            result.ToolCallId = message.ToolCallId;
        }
        else if (result.Kind == PiEntryKind.SideUsage)
        {
            result.Call = top.Call.ToCall();
        }
    }

    private static void ReadTopLevelProperty(ref Utf8JsonReader reader, ref PiLine result, ref TopLevel top)
    {
        if (reader.ValueTextEquals("type"u8))
        {
            reader.Read();
            top.Type = reader.TokenType != JsonTokenType.String ? TypeName.Other
                : reader.ValueTextEquals("message"u8) ? TypeName.Message
                : reader.ValueTextEquals("session"u8) ? TypeName.Session
                : reader.ValueTextEquals("usage"u8) ? TypeName.Usage
                : reader.ValueTextEquals("compaction"u8) ? TypeName.Compaction
                : reader.ValueTextEquals("branch_summary"u8) ? TypeName.BranchSummary
                : TypeName.Other;
            JsonScan.SkipValue(ref reader);
        }
        else if (reader.ValueTextEquals("id"u8))
        {
            reader.Read();
            result.Id = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            JsonScan.SkipValue(ref reader);
        }
        else if (reader.ValueTextEquals("timestamp"u8))
        {
            reader.Read();
            result.TimestampMs = JsonScan.TryReadTimestamp(ref reader, out var ms) ? ms : null;
        }
        else if (reader.ValueTextEquals("cwd"u8))
        {
            reader.Read();
            result.Cwd = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            JsonScan.SkipValue(ref reader);
        }
        else if (reader.ValueTextEquals("message"u8))
        {
            reader.Read();
            top.HasMessage = reader.TokenType == JsonTokenType.StartObject;
            top.Message = top.HasMessage ? ReadMessage(ref reader) : default;
            JsonScan.SkipValue(ref reader);
        }
        else if (!TryReadCallProperty(ref reader, ref top.Call))
        {
            reader.Read();
            JsonScan.SkipValue(ref reader);
        }
    }

    // The reader is on the message object; it ends on its EndObject.
    private static Message ReadMessage(ref Utf8JsonReader reader)
    {
        var message = new Message { Stop = PiStop.Aborted };
        var call = default(CallFields);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("role"u8))
            {
                reader.Read();
                message.Role = reader.TokenType != JsonTokenType.String ? PiRole.Other
                    : reader.ValueTextEquals("assistant"u8) ? PiRole.Assistant
                    : reader.ValueTextEquals("user"u8) ? PiRole.User
                    : reader.ValueTextEquals("toolResult"u8) ? PiRole.ToolResult
                    : PiRole.Other;
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("timestamp"u8))
            {
                reader.Read();
                message.StartedMs = ReadFinite(ref reader) is { } started && started > 0 ? Truncate(started) : null;
            }
            else if (reader.ValueTextEquals("durationMs"u8))
            {
                reader.Read();
                message.DurationMs = ReadFinite(ref reader) is { } duration && duration >= 0 ? Truncate(duration) : null;
            }
            else if (reader.ValueTextEquals("stopReason"u8))
            {
                reader.Read();
                message.Stop = reader.TokenType != JsonTokenType.String ? PiStop.Aborted
                    : reader.ValueTextEquals("stop"u8) ? PiStop.Stop
                    : reader.ValueTextEquals("toolUse"u8) ? PiStop.ToolUse
                    : reader.ValueTextEquals("length"u8) ? PiStop.Length
                    : reader.ValueTextEquals("error"u8) ? PiStop.Error
                    : reader.ValueTextEquals("aborted"u8) ? PiStop.Aborted
                    : reader.ValueTextEquals("deferred"u8) ? PiStop.Deferred
                    : PiStop.Other;
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("content"u8))
            {
                reader.Read();
                message.ToolCallIds = reader.TokenType == JsonTokenType.StartArray ? ReadToolCallIds(ref reader) : null;
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("toolCallId"u8))
            {
                reader.Read();
                message.ToolCallId = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                JsonScan.SkipValue(ref reader);
            }
            else if (!TryReadCallProperty(ref reader, ref call))
            {
                reader.Read();
                JsonScan.SkipValue(ref reader);
            }
        }

        message.Call = call.ToCall();
        return message;
    }

    // model, provider, api and usage: on assistant messages and on side-usage entries.
    private static bool TryReadCallProperty(ref Utf8JsonReader reader, ref CallFields call)
    {
        if (reader.ValueTextEquals("model"u8))
        {
            reader.Read();
            call.Model = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            JsonScan.SkipValue(ref reader);
        }
        else if (reader.ValueTextEquals("provider"u8))
        {
            reader.Read();
            call.HasProvider = reader.TokenType == JsonTokenType.String;
            call.Provider = call.HasProvider && !reader.ValueSpan.IsEmpty ? reader.GetString() : null;
            JsonScan.SkipValue(ref reader);
        }
        else if (reader.ValueTextEquals("api"u8))
        {
            reader.Read();
            call.HasApi = reader.TokenType == JsonTokenType.String;
            JsonScan.SkipValue(ref reader);
        }
        else if (reader.ValueTextEquals("usage"u8))
        {
            reader.Read();
            call.HasUsageObject = reader.TokenType == JsonTokenType.StartObject;
            call.Usage = call.HasUsageObject ? ReadUsage(ref reader) : default;
            JsonScan.SkipValue(ref reader);
        }
        else
        {
            return false;
        }

        return true;
    }

    // The reader is on the usage object; it ends on its EndObject.
    private static Usage ReadUsage(ref Utf8JsonReader reader)
    {
        var usage = default(Usage);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("input"u8))
            {
                reader.Read();
                usage.Input = JsonScan.ReadCount(ref reader);
            }
            else if (reader.ValueTextEquals("output"u8))
            {
                reader.Read();
                usage.Output = JsonScan.ReadCount(ref reader);
            }
            else if (reader.ValueTextEquals("cacheRead"u8))
            {
                reader.Read();
                usage.CacheRead = JsonScan.ReadCount(ref reader);
            }
            else if (reader.ValueTextEquals("cacheWrite"u8))
            {
                reader.Read();
                usage.CacheWrite = JsonScan.ReadCount(ref reader);
            }
            else if (reader.ValueTextEquals("cacheWrite1h"u8))
            {
                reader.Read();
                usage.CacheWrite1h = JsonScan.ReadCount(ref reader);
            }
            else if (reader.ValueTextEquals("reasoning"u8))
            {
                reader.Read();
                usage.Reasoning = JsonScan.ReadCount(ref reader);
            }
            else
            {
                reader.Read();
                JsonScan.SkipValue(ref reader);
            }
        }

        return usage;
    }

    // The reader is on the content array; it ends on its EndArray.
    private static string[]? ReadToolCallIds(ref Utf8JsonReader reader)
    {
        List<string>? ids = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                JsonScan.SkipValue(ref reader);
                continue;
            }

            var isToolCall = false;
            string? id = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    isToolCall = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("toolCall"u8);
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    id = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                }
                else
                {
                    reader.Read();
                }

                JsonScan.SkipValue(ref reader);
            }

            if (isToolCall && id is not null && (ids ??= []).IndexOf(id) < 0)
            {
                ids.Add(id);
            }
        }

        return ids?.ToArray();
    }

    private static double? ReadFinite(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            JsonScan.SkipValue(ref reader);
            return null;
        }

        return reader.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
    }

    /// <summary>Toward zero, saturated to the <see cref="long"/> range.</summary>
    private static long Truncate(double value) =>
        value >= TwoTo63 ? long.MaxValue : value <= -TwoTo63 ? long.MinValue : (long)Math.Truncate(value);

    private enum TypeName : byte
    {
        Other,
        Session,
        Message,
        Usage,
        Compaction,
        BranchSummary,
    }

    private struct Usage
    {
        public long Input;
        public long Output;
        public long CacheRead;
        public long CacheWrite;
        public long CacheWrite1h;
        public long Reasoning;
    }

    private struct CallFields
    {
        public string? Model;
        public bool HasProvider;
        public string? Provider;
        public bool HasApi;
        public bool HasUsageObject;
        public Usage Usage;

        public readonly PiCall ToCall() => new()
        {
            Guarded = Model is not null && HasProvider && HasApi && HasUsageObject,
            Model = Model,
            Provider = Provider,
            HasUsageObject = HasUsageObject,
            HasUsage = Usage.Input > 0 || Usage.Output > 0 || Usage.CacheRead > 0 || Usage.CacheWrite > 0,
            Tokens = new TokenCounts(
                JsonScan.SaturatingAdd(JsonScan.SaturatingAdd(Usage.Input, Usage.CacheRead), Usage.CacheWrite),
                Usage.CacheRead,
                Usage.CacheWrite,
                Usage.Output,
                Usage.Reasoning,
                Usage.CacheWrite1h),
        };
    }

    private struct Message
    {
        public PiRole Role;
        public PiCall Call;
        public long? StartedMs;
        public long? DurationMs;
        public PiStop Stop;
        public string[]? ToolCallIds;
        public string? ToolCallId;
    }

    private struct TopLevel
    {
        public TypeName Type;
        public bool HasMessage;
        public Message Message;
        public CallFields Call;
    }
}
