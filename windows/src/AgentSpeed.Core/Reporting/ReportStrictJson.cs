using System.Globalization;
using System.Text.Json;
using System.Text.Unicode;

namespace AgentSpeed.Core.Reporting;

/// <summary>
/// Strict JSON as the WebSocket transport reads the service's messages (METRICS 11.9, Messages): UTF-8 without a byte
/// order mark, one object, no duplicate member names, no lone surrogates, no NaN, at most 32 levels and a size cap.
/// </summary>
public static class ReportStrictJson
{
    public const int MaxDepth = 32;

    /// <summary>The document of one strict JSON object, or null; the caller disposes it.</summary>
    public static JsonDocument? ParseObject(ReadOnlySpan<byte> data, int maxBytes)
    {
        if (data.Length > maxBytes || !Utf8.IsValid(data) || data is [0xEF, 0xBB, 0xBF, ..])
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            return null;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object || !IsStrict(document.RootElement))
        {
            document.Dispose();
            return null;
        }

        return document;
    }

    /// <summary>The integer of a number written without fraction or exponent, saturated at the <c>long</c> limits; null otherwise.</summary>
    public static long? Integer(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var raw = value.GetRawText();
        var digits = raw.StartsWith('-') ? raw[1..] : raw;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : raw.StartsWith('-') ? long.MinValue : long.MaxValue;
    }

    /// <summary>The text of a string, or null for another kind (or a string that is not valid UTF-16).</summary>
    public static string? Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static JsonElement? Member(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static bool IsStrict(JsonElement element)
    {
        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!names.Add(property.Name) || !IsStrict(property.Value))
                        {
                            return false;
                        }
                    }

                    return true;
                case JsonValueKind.Array:
                    return element.EnumerateArray().All(IsStrict);
                case JsonValueKind.String:
                    _ = element.GetString();
                    return true;
                default:
                    return true;
            }
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
