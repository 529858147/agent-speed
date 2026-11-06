using System.Globalization;

namespace AgentSpeed.Presentation.Orb.Gpu;

/// <summary>
/// Strings of the glyph atlas that rasterized to no ink although they have something to show. WPF's
/// <c>RenderTargetBitmap</c> renders through the render thread and can come back empty while the session's display state
/// changes (dotnet/wpf#3765; issue #31: a secondary display's orb kept only the digits rasterized afterwards). Such a
/// result is never kept, or an idle orb would show the hole until the atlas next fills up: each one is retried after a
/// delay that doubles from <see cref="FirstDelay"/> to <see cref="MaxDelay"/> seconds while it stays blank.
/// </summary>
public sealed class OrbBlankGlyphs
{
    public const double FirstDelay = 0.25;
    public const double MaxDelay = 8;

    private readonly Dictionary<(OrbTextKey Key, int Scale), (double RetryAt, double Delay)> _waiting = new();
    private readonly Func<double> _now;

    /// <param name="now">A monotonic clock in seconds.</param>
    public OrbBlankGlyphs(Func<double> now)
    {
        _now = now ?? throw new ArgumentNullException(nameof(now));
    }

    /// <summary>Strings waiting for a retry.</summary>
    public int Count => _waiting.Count;

    /// <summary>
    /// Seconds until the next retry falls due, or null when none is ahead. A request answered with nothing always leaves
    /// its string due later, so an orb that missed one wakes no later than its own retry.
    /// </summary>
    public double? SecondsToRetry
    {
        get
        {
            var now = _now();
            double? earliest = null;
            foreach (var (retryAt, _) in _waiting.Values)
            {
                if (retryAt > now && (earliest is null || retryAt < earliest))
                {
                    earliest = retryAt;
                }
            }

            return earliest - now;
        }
    }

    /// <summary>True while the string rasterized blank at this scale and its retry is not due yet.</summary>
    public bool IsWaiting(in OrbTextKey key, int scale) => _waiting.TryGetValue((key, scale), out var entry) && _now() < entry.RetryAt;

    /// <summary>The string rasterized blank at this scale: retry after the next delay.</summary>
    public void Failed(in OrbTextKey key, int scale)
    {
        var delay = _waiting.TryGetValue((key, scale), out var entry) ? Math.Min(MaxDelay, entry.Delay * 2) : FirstDelay;
        _waiting[(key, scale)] = (_now() + delay, delay);
    }

    /// <summary>The string rasterized with ink at this scale.</summary>
    public void Succeeded(in OrbTextKey key, int scale) => _waiting.Remove((key, scale));

    public void Clear() => _waiting.Clear();

    /// <summary>Whether a string draws something: any character besides spaces, controls and format characters.</summary>
    public static bool ExpectsInk(string text)
    {
        foreach (var ch in text)
        {
            if (!char.IsWhiteSpace(ch) && !char.IsControl(ch) && CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.Format)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether premultiplied BGRA pixels have any coverage.</summary>
    public static bool HasInk(ReadOnlySpan<byte> bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                return true;
            }
        }

        return false;
    }
}
