using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>A board's polite announcement of a new leader (<c>a11y.board.newLeader</c>), at most once per 10 s (DESIGN 2c Accessibility).</summary>
public sealed class BoardAnnouncer
{
    public const double MinInterval = 10;

    private double _last = double.NegativeInfinity;

    /// <summary>The text to announce for this tick, or null (no new leader, or one was announced in the last 10 s).</summary>
    public string? NewLeader(BoardTick tick, double now, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(loc);
        if (now - _last < MinInterval)
        {
            return null;
        }

        foreach (var e in tick.Events)
        {
            if (e.Kind != BoardRankEventKind.NewLeader)
            {
                continue;
            }

            var leader = tick.Entries.FirstOrDefault(r => r.Kind == BoardEntryKind.Group && string.Equals(r.Key, e.Id, StringComparison.Ordinal));
            if (leader is null)
            {
                continue;
            }

            _last = now;
            return loc.Format("a11y.board.newLeader", leader.Group.Root.Project);
        }

        return null;
    }
}
