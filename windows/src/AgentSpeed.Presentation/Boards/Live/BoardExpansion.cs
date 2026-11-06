namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>
/// The group whose sub-agents a board shows in the room of the others (DESIGN 2c, Layout per size): a click on a group
/// toggles it (the others collapse); it ends after 60 s without interaction with the board.
/// </summary>
public sealed class BoardExpansion
{
    public const double Seconds = 60;

    private string? _group;
    private double _until = double.NegativeInfinity;

    /// <summary>When the expansion ends unless the board is touched again; null when nothing is expanded.</summary>
    public double? Deadline => _group is null ? null : _until;

    public string? Current(double now) => _group is not null && now < _until ? _group : null;

    /// <summary>A click on a group row; returns the group now expanded (null when the click collapsed it).</summary>
    public string? Toggle(string groupId, double now)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);
        if (string.Equals(Current(now), groupId, StringComparison.Ordinal))
        {
            Clear();
        }
        else
        {
            _group = groupId;
            _until = now + Seconds;
        }

        return Current(now);
    }

    /// <summary>Any interaction with the board (hover, click) restarts the 60 s.</summary>
    public void Touch(double now)
    {
        if (Current(now) is not null)
        {
            _until = now + Seconds;
        }
    }

    /// <summary>Ends a timed-out expansion; true when it just ended.</summary>
    public bool Expire(double now)
    {
        if (_group is null || now < _until)
        {
            return false;
        }

        Clear();
        return true;
    }

    public void Clear()
    {
        _group = null;
        _until = double.NegativeInfinity;
    }
}
