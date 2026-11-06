using System.Text.Json;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Work;

/// <summary>
/// The tool fields of a Codex <c>response_item</c> payload (METRICS 6.10): <c>function_call</c> / <c>custom_tool_call</c>
/// (<c>call_id</c>, <c>name</c>, <c>arguments</c> / <c>input</c>) and their <c>…_output</c> (<c>call_id</c>, <c>output</c>).
/// </summary>
internal struct CodexTools
{
    private Item _item;
    private string? _callId;
    private string? _name;
    private ulong? _arguments;
    private ulong? _input;
    private bool? _failed;

    private enum Item : byte
    {
        None,
        FunctionCall,
        CustomCall,
        Output,
    }

    /// <summary>A cheap test on the raw line before any of its tool fields are read.</summary>
    public static bool MayHaveTools(ReadOnlySpan<byte> line) =>
        line.IndexOf("\"function_call"u8) >= 0 || line.IndexOf("\"custom_tool_call"u8) >= 0;

    /// <summary>Reads one payload member when it is a tool field (the reader is on its name); false otherwise.</summary>
    public bool TryRead(ref Utf8JsonReader reader, ReadOnlySpan<byte> line)
    {
        if (reader.ValueTextEquals("call_id"u8))
        {
            reader.Read();
            _callId = JsonScan.ReadToken(ref reader);
        }
        else if (reader.ValueTextEquals("name"u8))
        {
            reader.Read();
            _name = JsonScan.ReadToken(ref reader);
        }
        else if (reader.ValueTextEquals("arguments"u8))
        {
            reader.Read();
            _arguments = ActionDigest.ReadInput(ref reader, line);
        }
        else if (reader.ValueTextEquals("input"u8))
        {
            reader.Read();
            _input = ActionDigest.ReadInput(ref reader, line);
        }
        else if (reader.ValueTextEquals("output"u8))
        {
            reader.Read();
            try
            {
                _failed = ReadFailed(ref reader);
            }
            catch (InvalidOperationException)
            {
                // An escape that does not decode: the output says nothing, the line stays valid.
                _failed = null;
                JsonScan.SkipValue(ref reader);
            }
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>Notes the payload <c>type</c> (the reader is on its value).</summary>
    public void ReadType(ref Utf8JsonReader reader) => _item = reader.TokenType != JsonTokenType.String ? Item.None
        : reader.ValueTextEquals("function_call"u8) ? Item.FunctionCall
        : reader.ValueTextEquals("custom_tool_call"u8) ? Item.CustomCall
        : reader.ValueTextEquals("function_call_output"u8) || reader.ValueTextEquals("custom_tool_call_output"u8) ? Item.Output
        : Item.None;

    public readonly LineTools ToLineTools() => _callId is not { } id ? default : _item switch
    {
        Item.FunctionCall => LineTools.Of(new ToolCall(id, _name, _arguments ?? ActionDigest.Empty)),
        Item.CustomCall => LineTools.Of(new ToolCall(id, _name, _input ?? ActionDigest.Empty)),
        Item.Output => LineTools.Of(new ToolOutcome(id, _failed)),
        _ => default,
    };

    /// <summary>
    /// The exec output's verdict: its leading JSON object has an integer <c>exit_code</c> ≠ 0. The output is that text, or
    /// content items (an array, or a string holding one) whose first <c>input_text</c> carries it. Null when it does not say.
    /// </summary>
    private static bool? ReadFailed(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return FirstInputText(ref reader) is { } text ? LeadingExitCodeFailed(text) : null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            JsonScan.SkipValue(ref reader);
            return null;
        }

        var output = Unescape(ref reader);
        var trimmed = TrimStart(output);
        if (trimmed.IsEmpty || trimmed[0] != (byte)'[')
        {
            return LeadingExitCodeFailed(output);
        }

        try
        {
            var items = new Utf8JsonReader(trimmed, JsonScan.Options);
            items.Read();
            var text = FirstInputText(ref items);
            return items.Read() ? null : text is null ? null : LeadingExitCodeFailed(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The <c>text</c> of the first <c>input_text</c> item of an array (the reader is on it; it ends on its EndArray).</summary>
    private static byte[]? FirstInputText(ref Utf8JsonReader reader)
    {
        byte[]? found = null;
        var matched = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (matched || reader.TokenType != JsonTokenType.StartObject)
            {
                JsonScan.SkipValue(ref reader);
                continue;
            }

            var inputText = false;
            byte[]? text = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    inputText = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("input_text"u8);
                }
                else if (reader.ValueTextEquals("text"u8))
                {
                    reader.Read();
                    text = reader.TokenType == JsonTokenType.String ? Unescape(ref reader) : null;
                }
                else
                {
                    reader.Read();
                }

                JsonScan.SkipValue(ref reader);
            }

            if (inputText)
            {
                matched = true;
                found = text;
            }
        }

        return found;
    }

    private static bool? LeadingExitCodeFailed(ReadOnlySpan<byte> text)
    {
        var trimmed = TrimStart(text);
        if (trimmed.IsEmpty || trimmed[0] != (byte)'{')
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(trimmed, JsonScan.Options);
            reader.Read();
            bool? failed = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isExitCode = reader.ValueTextEquals("exit_code"u8);
                reader.Read();
                if (isExitCode)
                {
                    failed = IsIntegerLiteral(reader) ? !IsZero(reader.ValueSpan) : null;
                }

                JsonScan.SkipValue(ref reader);
            }

            return reader.TokenType == JsonTokenType.EndObject ? failed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsIntegerLiteral(in Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number && reader.ValueSpan.IndexOfAny(".eE"u8) < 0;

    private static bool IsZero(ReadOnlySpan<byte> literal) => literal.SequenceEqual("0"u8) || literal.SequenceEqual("-0"u8);

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> text) => text.TrimStart(" \t\r\n\v\f"u8);

    private static byte[] Unescape(ref Utf8JsonReader reader)
    {
        if (!reader.ValueIsEscaped)
        {
            return reader.ValueSpan.ToArray();
        }

        var buffer = new byte[reader.ValueSpan.Length];
        return buffer.AsSpan(0, reader.CopyString(buffer)).ToArray();
    }
}
