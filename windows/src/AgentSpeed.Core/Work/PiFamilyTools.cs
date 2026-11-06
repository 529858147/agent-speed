using System.Text.Json;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Work;

/// <summary>
/// Tool calls and results of the omp and Pi session format (METRICS 6.10): assistant <c>toolCall</c> content blocks
/// (<c>id</c>, <c>name</c>, <c>arguments</c>) and <c>toolResult</c> messages (<c>toolCallId</c>, <c>isError</c>).
/// </summary>
internal static class PiFamilyTools
{
    /// <summary>The <c>toolCall</c> blocks of a content array (the reader is on it; it ends on its EndArray).</summary>
    public static ToolCall[]? ReadCalls(ref Utf8JsonReader reader, ReadOnlySpan<byte> line)
    {
        List<ToolCall>? calls = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                JsonScan.SkipValue(ref reader);
                continue;
            }

            var isToolCall = false;
            string? id = null;
            string? name = null;
            var digest = ActionDigest.Empty;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("type"u8))
                {
                    reader.Read();
                    isToolCall = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("toolCall"u8);
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
                else if (reader.ValueTextEquals("arguments"u8))
                {
                    reader.Read();
                    digest = ActionDigest.ReadInput(ref reader, line);
                }
                else
                {
                    reader.Read();
                    JsonScan.SkipValue(ref reader);
                }
            }

            if (isToolCall && id is not null)
            {
                (calls ??= []).Add(new ToolCall(id, name, digest));
            }
        }

        return calls?.ToArray();
    }

    /// <summary>The distinct ids of <paramref name="calls"/>, in order.</summary>
    public static string[]? Ids(ToolCall[]? calls) => calls?.Select(c => c.Id).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>A <c>toolResult</c> message: failed exactly when <c>isError</c> is <c>true</c>.</summary>
    public static LineTools Result(string? toolCallId, bool isError) =>
        toolCallId is null ? default : LineTools.Of(new ToolOutcome(toolCallId, isError));
}
