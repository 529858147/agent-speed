using System.Globalization;
using System.Text;
using AgentSpeed.Core.History;
using AgentSpeed.Core.Pricing;

namespace AgentSpeed.Core.Unpriced;

/// <summary>The build that writes the text: its version, <c>windows</c> and the OS (METRICS 9.10 <i>Text</i>).</summary>
public sealed record UnpricedApp(string Version, string Platform, string Os)
{
    /// <summary>This Windows build: <c>windows-11</c> from build 22000, else <c>windows-10</c>.</summary>
    public static UnpricedApp Windows(string version) => new(version, "windows", CurrentOs());

    private static string CurrentOs()
    {
        var version = Environment.OSVersion.Version;
        if (OperatingSystem.IsWindows())
        {
            return version.Build >= 22000 ? "windows-11" : "windows-10";
        }

        return OperatingSystem.IsMacOS()
            ? "macos-" + version.Major.ToString(CultureInfo.InvariantCulture)
            : Environment.OSVersion.Platform.ToString().ToLowerInvariant();
    }
}

/// <summary>What a view covers, for the <c>range</c> line.</summary>
/// <param name="View"><c>today</c>, <c>yesterday</c>, <c>7d</c>, <c>30d</c>, <c>90d</c>, <c>year</c>, <c>all</c>, <c>custom</c> or <c>live</c>.</param>
public sealed record UnpricedRange(string View, string First, string Last, SourceKind? Source = null, SourceSet Excluded = default);

/// <summary>
/// The copied diagnostics text (METRICS 9.10 <i>Text</i>, format 1): UTF-8, LF line ends, byte-identical with
/// <c>spec/tools/unpriced.py</c>. It holds model ids, providers, the table's rule texts, dates and counts only; path-like
/// values are redacted.
/// </summary>
public static class UnpricedText
{
    public const int Format = 1;
    public const int MaxScalars = 120;

    public static string Render(UnpricedView view, UnpricedApp app, PriceTable current, UnpricedRange range)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(range);
        var text = new StringBuilder();
        Line(text, string.Create(CultureInfo.InvariantCulture, $"Agent Speed unpriced diagnostics {Format}"));
        Line(text, string.Create(CultureInfo.InvariantCulture, $"app version={app.Version} platform={app.Platform} os={app.Os}"));
        Line(text, string.Create(CultureInfo.InvariantCulture, $"prices version={current.Version} updated={current.Updated}"));
        var excluded = range.Excluded.IsEmpty ? "none" : string.Join(",", range.Excluded.Select(SourceNames.Of));
        Line(text, string.Create(CultureInfo.InvariantCulture, $"range view={range.View} first={range.First} last={range.Last} days={Days(range.First, range.Last)} " +
            $"source={(range.Source is { } source ? SourceNames.Of(source) : "all")} excluded={excluded}"));
        foreach (var group in view.Groups)
        {
            var id = SourceNames.Of(group.Source);
            Line(text, string.Create(CultureInfo.InvariantCulture, $"source id={id} requests={group.Requests} unpriced={group.Unpriced} pricedNow={group.PricedNow} " +
                $"noCostData={group.NoCostData} derived={group.Derived} rows={group.Rows.Count}"));
            foreach (var row in group.Rows)
            {
                Line(text, string.Create(CultureInfo.InvariantCulture, $"row source={id} reason={UnpricedReasons.Name(row.Reason)} requests={row.Requests} first={row.First} " +
                    $"last={row.Last} fast={(row.Fact.Fast ? "true" : "false")} model={Quote(row.Fact.Model)} " +
                    $"provider={Quote(row.Fact.Provider)} canonical={Quote(row.Diagnosis.Canonical)} " +
                    $"rule={Quote(row.Diagnosis.Rule, redact: false)}"));
            }
        }

        Line(text, string.Create(CultureInfo.InvariantCulture, $"total requests={view.Requests} unpriced={view.Unpriced} pricedNow={view.PricedNow} " +
            $"noCostData={view.NoCostData} derived={view.Derived} rows={view.RowCount}"));
        return text.ToString();
    }

    /// <summary>
    /// A text value: <c>null</c>, or the value in double quotes with lone surrogates replaced, a path-like value as
    /// <c>&lt;path&gt;</c> (unless <paramref name="redact"/> is false), more than 120 scalars cut with <c>…</c>, and
    /// quotes, backslashes and control characters escaped.
    /// </summary>
    public static string Quote(string? value, bool redact = true)
    {
        if (value is null)
        {
            return "null";
        }

        var scalars = new List<int>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                scalars.Add(char.ConvertToUtf32(c, value[++i]));
            }
            else
            {
                scalars.Add(char.IsSurrogate(c) ? 0xFFFD : c);
            }
        }

        if (redact && IsPathLike(scalars))
        {
            scalars = ['<', 'p', 'a', 't', 'h', '>'];
        }

        var cut = scalars.Count > MaxScalars;
        var builder = new StringBuilder(value.Length + 2).Append('"');
        for (var i = 0; i < Math.Min(scalars.Count, MaxScalars); i++)
        {
            Escape(builder, scalars[i]);
        }

        if (cut)
        {
            builder.Append('…');
        }

        return builder.Append('"').ToString();
    }

    private static void Escape(StringBuilder builder, int scalar)
    {
        switch (scalar)
        {
            case '"':
                builder.Append("\\\"");
                return;
            case '\\':
                builder.Append("\\\\");
                return;
            case '\b':
                builder.Append("\\b");
                return;
            case '\t':
                builder.Append("\\t");
                return;
            case '\n':
                builder.Append("\\n");
                return;
            case '\f':
                builder.Append("\\f");
                return;
            case '\r':
                builder.Append("\\r");
                return;
        }

        if (scalar < 0x20 || scalar is >= 0x7F and <= 0x9F or 0x2028 or 0x2029)
        {
            builder.Append("\\u").Append(scalar.ToString("x4", CultureInfo.InvariantCulture));
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(scalar));
        }
    }

    /// <summary>
    /// Starts with <c>/</c>, <c>\</c>, <c>~</c> or <c>file:</c>, or with a letter, <c>:</c> and a slash, or contains
    /// <c>/users/</c>, <c>\users\</c> or <c>/home/</c> (ASCII letters compared in lower case).
    /// </summary>
    private static bool IsPathLike(List<int> scalars)
    {
        var low = new StringBuilder(scalars.Count);
        foreach (var scalar in scalars)
        {
            // Only ASCII matters for the patterns; anything else stays a non-matching placeholder.
            low.Append(scalar is >= 'A' and <= 'Z' ? (char)(scalar + 32) : scalar < 0x80 ? (char)scalar : '￿');
        }

        var text = low.ToString();
        return text.StartsWith('/') || text.StartsWith('\\') || text.StartsWith('~') || text.StartsWith("file:", StringComparison.Ordinal)
            || (text.Length >= 3 && text[0] is >= 'a' and <= 'z' && text[1] == ':' && text[2] is '/' or '\\')
            || text.Contains("/users/", StringComparison.Ordinal) || text.Contains("\\users\\", StringComparison.Ordinal)
            || text.Contains("/home/", StringComparison.Ordinal);
    }

    private static int Days(string first, string last) =>
        DateOnly.ParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber
        - DateOnly.ParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber + 1;

    private static void Line(StringBuilder text, string line) => text.Append(line).Append('\n');
}
