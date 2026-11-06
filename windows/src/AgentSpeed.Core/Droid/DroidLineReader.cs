using System.Text.Json;
using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Work;

namespace AgentSpeed.Core.Droid;

/// <summary>
/// Pure, thread-safe extraction of <see cref="DroidLine"/> (METRICS 4.16). Duplicate keys resolve like Python's
/// <c>json.loads</c>: the last occurrence wins.
/// </summary>
internal static class DroidLineReader
{
    public static DroidLine Read(ReadOnlySpan<byte> line, long offset, int length)
    {
        var result = Read(line);
        result.Offset = offset;
        result.Length = length;
        return result;
    }

    public static DroidLine Read(ReadOnlySpan<byte> line)
    {
        if (JsonScan.IsBlank(line))
        {
            return new DroidLine { Status = LineStatus.Blank };
        }

        // Like json.loads on the line's bytes: invalid UTF-8 anywhere makes it malformed; a leading BOM is accepted.
        if (!System.Text.Unicode.Utf8.IsValid(line))
        {
            return Malformed();
        }

        if (line.StartsWith("﻿"u8))
        {
            line = line[3..];
        }

        var result = default(DroidLine);
        var type = DroidLineKind.Other;
        var hasMessage = false;
        try
        {
            var reader = new Utf8JsonReader(line, JsonScan.Options);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return Malformed();
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    type = reader.TokenType != JsonTokenType.String ? DroidLineKind.Other
                        : reader.ValueTextEquals("message"u8) ? DroidLineKind.Message
                        : reader.ValueTextEquals("session_start"u8) ? DroidLineKind.SessionStart
                        : reader.ValueTextEquals("compaction_state"u8) ? DroidLineKind.Compaction
                        : reader.ValueTextEquals("agent_turn_outcome"u8) ? DroidLineKind.TurnOutcome
                        : DroidLineKind.Other;
                    JsonScan.SkipValue(ref reader);
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    result.Id = JsonScan.ReadString(ref reader);
                }
                else if (reader.ValueTextEquals("timestamp"u8))
                {
                    reader.Read();
                    result.TimestampMs = JsonScan.TryReadTimestamp(ref reader, out var ms) ? ms : null;
                }
                else if (reader.ValueTextEquals("message"u8))
                {
                    reader.Read();
                    hasMessage = reader.TokenType == JsonTokenType.StartObject;
                    ClearMessage(ref result);
                    if (hasMessage)
                    {
                        ReadMessage(ref reader, ref result, line);
                    }
                    else
                    {
                        JsonScan.SkipValue(ref reader);
                    }
                }
                else if (reader.ValueTextEquals("cwd"u8))
                {
                    reader.Read();
                    result.Cwd = JsonScan.ReadToken(ref reader);
                }
                else if (reader.ValueTextEquals("parent"u8))
                {
                    reader.Read();
                    result.Parent = SessionId(JsonScan.ReadToken(ref reader));
                }
                else if (reader.ValueTextEquals("callingSessionId"u8))
                {
                    reader.Read();
                    result.CallingSessionId = SessionId(JsonScan.ReadToken(ref reader));
                }
                else
                {
                    reader.Read();
                    JsonScan.SkipValue(ref reader);
                }
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

        result.Kind = type == DroidLineKind.Message && !hasMessage ? DroidLineKind.Other : type;

        // Calls are the assistant's, results the user's (METRICS 6.10).
        result.Tools = result.Kind != DroidLineKind.Message ? default : new LineTools(
            result.Role == DroidRole.Assistant ? result.Tools.Calls : null,
            result.Role == DroidRole.User ? result.Tools.Outcomes : null);
        return result;
    }

    private static DroidLine Malformed() => new() { Status = LineStatus.Malformed };

    private static string? SessionId(string? value) => value is not null && DroidPaths.IsSessionId(value) ? value : null;

    private static void ClearMessage(ref DroidLine result)
    {
        result.Role = DroidRole.Other;
        result.UserOnly = false;
        result.ModelId = null;
        result.Effort = null;
        result.HasToolUse = false;
        result.Tools = default;
    }

    private static void ReadMessage(ref Utf8JsonReader reader, ref DroidLine result, ReadOnlySpan<byte> line)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("role"u8))
            {
                reader.Read();
                result.Role = reader.TokenType != JsonTokenType.String ? DroidRole.Other
                    : reader.ValueTextEquals("user"u8) ? DroidRole.User
                    : reader.ValueTextEquals("assistant"u8) ? DroidRole.Assistant
                    : DroidRole.Other;
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("visibility"u8))
            {
                reader.Read();
                result.UserOnly = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("user_only"u8);
                JsonScan.SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("modelId"u8))
            {
                reader.Read();
                result.ModelId = JsonScan.ReadToken(ref reader)?.Trim() is { Length: > 0 } model ? model : null;
            }
            else if (reader.ValueTextEquals("reasoningEffort"u8))
            {
                reader.Read();
                result.Effort = JsonScan.ReadToken(ref reader);
            }
            else if (reader.ValueTextEquals("content"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    (result.HasToolUse, result.Tools) = ReadContent(ref reader, line);
                }
                else
                {
                    result.HasToolUse = false;
                    result.Tools = default;
                    JsonScan.SkipValue(ref reader);
                }
            }
            else
            {
                reader.Read();
                JsonScan.SkipValue(ref reader);
            }
        }
    }

    /// <summary>
    /// Whether the content holds a <c>tool_use</c> block, and its <c>tool_use</c> calls and <c>tool_result</c> results
    /// (METRICS 6.10: <c>input</c> only as its digest; failed when <c>is_error</c> is <c>true</c>). Leaves the reader on the
    /// array's EndArray.
    /// </summary>
    private static (bool ToolUse, LineTools Tools) ReadContent(ref Utf8JsonReader reader, ReadOnlySpan<byte> line)
    {
        var found = false;
        List<ToolCall>? calls = null;
        List<ToolOutcome>? outcomes = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                JsonScan.SkipValue(ref reader);
                continue;
            }

            bool toolUse = false, toolResult = false, isError = false;
            string? id = null, name = null, toolUseId = null;
            var input = ActionDigest.Empty;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    toolUse = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("tool_use"u8);
                    toolResult = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("tool_result"u8);
                    JsonScan.SkipValue(ref reader);
                }
                else if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    id = JsonScan.ReadToken(ref reader);
                }
                else if (reader.ValueTextEquals("name"u8))
                {
                    reader.Read();
                    name = JsonScan.ReadToken(ref reader);
                }
                else if (reader.ValueTextEquals("input"u8))
                {
                    reader.Read();
                    input = ActionDigest.ReadInput(ref reader, line);
                }
                else if (reader.ValueTextEquals("tool_use_id"u8))
                {
                    reader.Read();
                    toolUseId = JsonScan.ReadToken(ref reader);
                }
                else if (reader.ValueTextEquals("is_error"u8))
                {
                    reader.Read();
                    isError = reader.TokenType == JsonTokenType.True;
                    JsonScan.SkipValue(ref reader);
                }
                else
                {
                    reader.Read();
                    JsonScan.SkipValue(ref reader);
                }
            }

            found |= toolUse;
            if (toolUse && id is not null)
            {
                (calls ??= []).Add(new ToolCall(id, name, input));
            }
            else if (toolResult && toolUseId is not null)
            {
                (outcomes ??= []).Add(new ToolOutcome(toolUseId, isError));
            }
        }

        return (found, LineTools.Of(calls, outcomes));
    }
}
