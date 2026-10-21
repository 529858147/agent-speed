namespace AgentSpeed.Presentation.Orb;

public readonly record struct RollingSlot(char? Current, char? Previous, double Progress, int Direction)
{
    public bool IsChanging => Progress < 1 && Current != Previous;
}

/// <summary>
/// Odometer-style text transition. Slots are aligned from the right so units stay in place; each changed slot
/// rolls up (value rising) or down (value falling), right-most first.
/// </summary>
public sealed class RollingText
{
    public const double Duration = 0.22;
    public const double Stagger = 0.025;

    private string _current = string.Empty;
    private string _previous = string.Empty;
    private double _changedAt = double.NegativeInfinity;
    private int _direction = 1;

    public string Current => _current;

    public int SlotCount => Math.Max(_current.Length, _previous.Length);

    public void Set(string text, int direction, double nowSeconds, bool animate)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text == _current)
        {
            return;
        }

        _previous = animate ? _current : text;
        _current = text;
        _direction = direction >= 0 ? 1 : -1;
        _changedAt = animate ? nowSeconds : double.NegativeInfinity;
    }

    public RollingSlot Slot(int indexFromRight, double nowSeconds)
    {
        var current = CharFromRight(_current, indexFromRight);
        var previous = CharFromRight(_previous, indexFromRight);
        if (current == previous)
        {
            return new RollingSlot(current, previous, 1, _direction);
        }

        var t = (nowSeconds - _changedAt - indexFromRight * Stagger) / Duration;
        return new RollingSlot(current, previous, Math.Clamp(t, 0, 1), _direction);
    }

    public bool IsAnimating(double nowSeconds) =>
        _previous != _current && nowSeconds - _changedAt < Duration + Stagger * SlotCount;

    private static char? CharFromRight(string text, int indexFromRight)
    {
        var index = text.Length - 1 - indexFromRight;
        return index >= 0 && index < text.Length ? text[index] : null;
    }
}
