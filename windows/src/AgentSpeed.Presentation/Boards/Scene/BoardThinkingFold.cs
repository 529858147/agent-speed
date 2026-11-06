using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// 思考's fold on the sub-gauge (DESIGN 2c, Thinking hidden at zero): hiding, it fades out in place (0.25 s) while what is below
/// it glides up and the sub-gauge glides to its new centred place (smoothstep, 0.3 s); coming back runs the same in reverse from
/// wherever it is. Reduce Motion: at once.
/// </summary>
public sealed class BoardThinkingFold
{
    public const double FadeSeconds = 0.25;
    public const double GlideSeconds = 0.3;

    private bool _hidden;
    private double _start = double.NegativeInfinity;
    private double _fromAway;
    private double _fromAlpha = 1;

    public bool Hidden => _hidden;

    /// <summary>Hides or shows at <paramref name="now"/>.</summary>
    public void Set(bool hidden, double now, bool animate)
    {
        if (hidden == _hidden)
        {
            return;
        }

        _fromAway = animate ? Away(now) : hidden ? 1 : 0;
        _fromAlpha = animate ? Alpha(now) : hidden ? 0 : 1;
        _hidden = hidden;
        _start = animate ? now : double.NegativeInfinity;
    }

    public void Settle() => _start = double.NegativeInfinity;

    /// <summary>How far the stack has glided to its place without 思考, eased: 0 with it, 1 without.</summary>
    public double Glide(double now) => Easing.SmoothStep(0, 1, Away(now));

    /// <summary>思考's opacity.</summary>
    public double Alpha(double now)
    {
        var target = _hidden ? 0.0 : 1.0;
        var t = Math.Clamp((now - _start) / FadeSeconds, 0, 1);
        return _fromAlpha + (target - _fromAlpha) * Easing.OutQuad(t);
    }

    /// <summary>The fade or the glide runs (60 fps, like a row move).</summary>
    public bool Moving(double now) => now - _start < GlideSeconds;

    private double Away(double now)
    {
        var target = _hidden ? 1.0 : 0.0;
        var t = Math.Clamp((now - _start) / GlideSeconds, 0, 1);
        return _fromAway + (target - _fromAway) * t;
    }
}
