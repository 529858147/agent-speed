namespace AgentSpeed.Core.Hermes;

/// <summary>
/// Hermes writes local wall-clock times without a zone (METRICS 4.15 Time). A wall time is converted with a zone's rules
/// like Python's <c>datetime.timestamp()</c> with <c>fold = 0</c>: a time that occurs twice is the earlier instant, a skipped
/// time takes the offset in force before the change.
/// </summary>
internal static class HermesClock
{
    private const long HourMs = 3_600_000;
    private const long ProbeMs = 14 * HourMs;
    private static readonly long MinMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
    private static readonly long MaxMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>Unix milliseconds of a wall time; null outside the years 1970–9999 or the calendar and clock ranges of 3.1.</summary>
    public static long? ToUnixMs(int year, int month, int day, int hour, int minute, int second, int millisecond, TimeZoneInfo zone)
    {
        if (year is < 1970 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour > 23 || minute > 59 || second > 59 || millisecond is < 0 or > 999)
        {
            return null;
        }

        var wall = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var before = OffsetMs(wall - ProbeMs, zone);
        var after = OffsetMs(wall + ProbeMs, zone);
        long? earliest = null;
        foreach (var offset in (ReadOnlySpan<long>)[before, after])
        {
            var candidate = wall - offset;
            if (OffsetMs(candidate, zone) == offset && (earliest is null || candidate < earliest))
            {
                earliest = candidate;
            }
        }

        return (earliest ?? wall - before) + millisecond;
    }

    /// <summary>A zone by IANA or Windows id (fixtures name IANA zones; Windows may know only its own ids).</summary>
    public static TimeZoneInfo FindZone(string id)
    {
        if (id is "UTC" or "Etc/UTC")
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId))
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
    }

    private static long OffsetMs(long unixMs, TimeZoneInfo zone)
    {
        var clamped = Math.Clamp(unixMs, MinMs, MaxMs);
        return (long)zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(clamped)).TotalMilliseconds;
    }
}
