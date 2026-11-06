namespace AgentSpeed.Presentation.Boards;

/// <summary>One group as the ranking sees it on an engine tick: its live value and latest activity (Unix ms).</summary>
public readonly record struct BoardRankInput(string Id, double Live, long LastActiveMs);

public enum BoardRankEventKind
{
    Overtake,
    NewLeader,
}

/// <summary>A pass on one tick: the climber's rank before the passes of that tick (<see cref="From"/>) and after (<see cref="To"/>), 1-based.</summary>
public readonly record struct BoardRankEvent(BoardRankEventKind Kind, string Id, int From, int To)
{
    /// <summary>Places gained (the <c>▲n</c> chip).</summary>
    public int Gained => From - To;
}

/// <summary>The order of a tick, first place first, and the passes it contains.</summary>
public sealed record BoardRankResult(IReadOnlyList<string> Order, IReadOnlyList<BoardRankEvent> Events);

/// <summary>
/// The board's stable ranking (DESIGN 2c, Ranking stability; reference <c>spec/tools/board_ranking.py</c>, cases
/// <c>spec/fixtures/board-ranking-cases.json</c>). A group passes the group above it only after leading it by more than
/// 10 % + 1 tok/s on two consecutive ticks, or at once when it leads by more than 2× + 20; groups that are both below
/// 0.05 order by latest activity. New groups enter at the place of their value without an event; groups that leave are
/// dropped. Only passing emits an event.
/// </summary>
public sealed class BoardRanking
{
    /// <summary>Below this a group counts as idle (both idle: latest activity decides).</summary>
    public const double Idle = 0.05;

    private List<string> _order = [];
    private Dictionary<(string Challenger, string Defender), int> _pressure = [];
    private Dictionary<(string Challenger, string Defender), int> _spare = [];

    /// <summary>The order after the last tick, first place first.</summary>
    public IReadOnlyList<string> Order => _order;

    /// <summary>Forgets the order (a board whose sources changed starts over).</summary>
    public void Reset()
    {
        _order = [];
        _pressure.Clear();
    }

    public static bool Leads(in BoardRankInput a, in BoardRankInput b)
    {
        if (a.Live < Idle && b.Live < Idle)
        {
            return a.LastActiveMs > b.LastActiveMs + 1000;
        }

        return a.Live > b.Live * 1.1 + 1;
    }

    public static bool Overwhelms(in BoardRankInput a, in BoardRankInput b) => a.Live > b.Live * 2 + 20;

    public BoardRankResult Tick(IReadOnlyList<BoardRankInput> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var byId = new Dictionary<string, BoardRankInput>(groups.Count, StringComparer.Ordinal);
        var unique = new List<BoardRankInput>(groups.Count);
        foreach (var group in groups)
        {
            if (group.Id is not null && byId.TryAdd(group.Id, group))
            {
                unique.Add(group);
            }
        }

        var pressure = _spare;
        pressure.Clear();
        foreach (var a in unique)
        {
            foreach (var b in unique)
            {
                if (!string.Equals(a.Id, b.Id, StringComparison.Ordinal) && Leads(a, b))
                {
                    pressure[(a.Id, b.Id)] = _pressure.GetValueOrDefault((a.Id, b.Id)) + 1;
                }
            }
        }

        _spare = _pressure;
        _pressure = pressure;

        var order = new List<string>(unique.Count);
        foreach (var id in _order)
        {
            if (byId.ContainsKey(id))
            {
                order.Add(id);
            }
        }

        var known = new HashSet<string>(order, StringComparer.Ordinal);
        var entering = unique.Where(g => !known.Contains(g.Id))
            .OrderByDescending(g => g.Live)
            .ThenByDescending(g => g.LastActiveMs)
            .ThenBy(g => g.Id, StringComparer.Ordinal);
        foreach (var group in entering)
        {
            var at = order.FindIndex(id => byId[id].Live < group.Live);
            order.Insert(at < 0 ? order.Count : at, group.Id);
        }

        var baseline = new Dictionary<string, int>(order.Count, StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
        {
            baseline[order[i]] = i + 1;
        }

        for (var i = 1; i < order.Count; i++)
        {
            for (var j = i; j > 0; j--)
            {
                var a = byId[order[j]];
                var b = byId[order[j - 1]];
                if (_pressure.GetValueOrDefault((a.Id, b.Id)) < 2 && !Overwhelms(a, b))
                {
                    break;
                }

                (order[j - 1], order[j]) = (order[j], order[j - 1]);
            }
        }

        var events = new List<BoardRankEvent>();
        for (var i = 0; i < order.Count; i++)
        {
            var rank = i + 1;
            var before = baseline[order[i]];
            if (rank < before)
            {
                events.Add(new BoardRankEvent(rank == 1 ? BoardRankEventKind.NewLeader : BoardRankEventKind.Overtake, order[i], before, rank));
            }
        }

        _order = order;
        return new BoardRankResult(order.ToArray(), events);
    }
}
