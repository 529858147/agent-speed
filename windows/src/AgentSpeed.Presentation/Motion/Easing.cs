namespace AgentSpeed.Presentation.Motion;

public static class Easing
{
    public static double Clamp01(double t) => double.IsFinite(t) ? Math.Clamp(t, 0, 1) : 0;

    public static double OutCubic(double t)
    {
        t = 1 - Clamp01(t);
        return 1 - t * t * t;
    }

    public static double OutQuad(double t)
    {
        t = Clamp01(t);
        return 1 - (1 - t) * (1 - t);
    }

    /// <summary>CSS <c>cubic-bezier(x1, y1, x2, y2)</c> at progress <paramref name="t"/> (Newton steps, then bisection).</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double t)
    {
        t = Clamp01(t);
        static double Curve(double a, double b, double u) => ((1 - 3 * b + 3 * a) * u + (3 * b - 6 * a)) * u * u + 3 * a * u;
        var u = t;
        for (var i = 0; i < 6; i++)
        {
            var error = Curve(x1, x2, u) - t;
            var slope = 3 * (1 - 3 * x2 + 3 * x1) * u * u + 2 * (3 * x2 - 6 * x1) * u + 3 * x1;
            if (Math.Abs(error) < 1e-6)
            {
                return Curve(y1, y2, u);
            }

            if (Math.Abs(slope) < 1e-6)
            {
                break;
            }

            u = Math.Clamp(u - error / slope, 0, 1);
        }

        double lo = 0, hi = 1;
        u = t;
        for (var i = 0; i < 30; i++)
        {
            var x = Curve(x1, x2, u);
            if (Math.Abs(x - t) < 1e-6)
            {
                break;
            }

            if (x < t)
            {
                lo = u;
            }
            else
            {
                hi = u;
            }

            u = (lo + hi) / 2;
        }

        return Curve(y1, y2, u);
    }

    public static double InOutSine(double t) => -(Math.Cos(Math.PI * Clamp01(t)) - 1) / 2;

    public static double OutBack(double t, double overshoot = 1.70158)
    {
        t = Clamp01(t) - 1;
        return t * t * ((overshoot + 1) * t + overshoot) + 1;
    }

    public static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3 - 2 * t);
    }

    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
