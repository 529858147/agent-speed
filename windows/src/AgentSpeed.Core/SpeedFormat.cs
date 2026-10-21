using System.Globalization;

namespace AgentSpeed.Core;

/// <summary>Compact number formatting shared by every surface (METRICS section 7).</summary>
public static class SpeedFormat
{
    public static string Speed(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            return "0";
        }

        if (value < 10)
        {
            var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
            return rounded >= 10 ? "10" : Fixed(rounded, 1);
        }

        if (value < 1000)
        {
            var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
            return rounded >= 1000 ? "1.00k" : ((long)rounded).ToString(CultureInfo.InvariantCulture);
        }

        return Compact(value);
    }

    public static string Tokens(long value)
    {
        if (value <= 0)
        {
            return "0";
        }

        return value < 1000 ? value.ToString(CultureInfo.InvariantCulture) : Compact(value);
    }

    private static string Compact(double value)
    {
        var thousands = value / 1000;
        if (Round(thousands, 2) < 10)
        {
            return Fixed(thousands, 2) + "k";
        }

        if (Round(thousands, 1) < 100)
        {
            return Fixed(thousands, 1) + "k";
        }

        if (Round(thousands, 0) < 1000)
        {
            return Fixed(thousands, 0) + "k";
        }

        var millions = value / 1_000_000;
        if (Round(millions, 2) < 10)
        {
            return Fixed(millions, 2) + "M";
        }

        return Round(millions, 1) < 100 ? Fixed(millions, 1) + "M" : Fixed(millions, 0) + "M";
    }

    private static double Round(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);

    private static string Fixed(double value, int digits) =>
        Round(value, digits).ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
}
