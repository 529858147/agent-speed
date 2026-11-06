using System.Collections.Immutable;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Settings.Boards;

/// <summary>
/// Every leaderboard board and where each one sits (DESIGN 2c, persisted next to the orbs as the settings value
/// <c>boards</c>). Immutable with value equality; positions use the orbs' arrangement keys and records, most recently
/// used arrangement first, at most 16. A fresh install and an upgrade have no board.
/// </summary>
public sealed record BoardsDocument
{
    public const int CurrentVersion = 1;
    public const int MaxBoards = 4;

    private readonly ImmutableArray<BoardDefinition> _boards = [];
    private readonly ImmutableArray<OrbLayout> _layouts = [];

    public BoardsDocument(IEnumerable<BoardDefinition>? boards = null, IEnumerable<OrbLayout>? layouts = null)
    {
        Boards = boards is null ? [] : [.. boards];
        Layouts = layouts is null ? [] : [.. layouts];
    }

    public static BoardsDocument Empty { get; } = new();

    /// <summary>List order (Settings rows, overlap resolution).</summary>
    public ImmutableArray<BoardDefinition> Boards
    {
        get => _boards;
        init => _boards = value.IsDefault ? [] : value;
    }

    /// <summary>Most recently used arrangement first.</summary>
    public ImmutableArray<OrbLayout> Layouts
    {
        get => _layouts;
        init => _layouts = value.IsDefault ? [] : value;
    }

    public int Count => Boards.Length;

    /// <summary><c>boards.add</c> is possible (fewer than four boards; otherwise it shows <c>boards.limit</c>).</summary>
    public bool CanAdd => Boards.Length < MaxBoards;

    /// <summary>
    /// The style <c>boards.add</c> uses: the last used one, i.e. that of the most recently added board, else
    /// <see cref="BoardStyles.Default"/>.
    /// </summary>
    public BoardStyle NextStyle => Boards.IsEmpty ? BoardStyles.Default : Boards[^1].Style;

    public BoardDefinition? Find(string? id) => id is null ? null : Boards.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.Ordinal));

    public int IndexOf(string? id)
    {
        for (var i = 0; i < Boards.Length; i++)
        {
            if (string.Equals(Boards[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public OrbLayout? LayoutFor(string arrangement) =>
        Layouts.FirstOrDefault(l => string.Equals(l.Arrangement, arrangement, StringComparison.OrdinalIgnoreCase));

    public OrbPositionRecord? RecordFor(string arrangement, string boardId) =>
        LayoutFor(arrangement)?.Positions.TryGetValue(boardId, out var record) == true ? record : null;

    /// <summary>
    /// A new board (<c>boards.add</c>): the last used style, M, all sources; null at the limit. Placement is the
    /// window side's (next to the window it came from).
    /// </summary>
    public (BoardsDocument Document, BoardDefinition Board)? Add(string? id = null)
    {
        if (!CanAdd)
        {
            return null;
        }

        var board = new BoardDefinition(id ?? OrbDefinition.NewId()) { Style = NextStyle };
        return (this with { Boards = Boards.Add(board) }, board);
    }

    /// <summary>Removes a board and its positions.</summary>
    public BoardsDocument Remove(string id) => IndexOf(id) < 0 ? this : WithBoards(Boards.Where(b => b.Id != id));

    /// <summary>Replaces the board with the same id; unknown ids leave the document unchanged.</summary>
    public BoardsDocument Replace(BoardDefinition board)
    {
        ArgumentNullException.ThrowIfNull(board);
        var index = IndexOf(board.Id);
        return index < 0 ? this : this with { Boards = Boards.SetItem(index, board.Validated()) };
    }

    /// <summary>New list of boards; positions of boards that are gone are dropped.</summary>
    public BoardsDocument WithBoards(IEnumerable<BoardDefinition> boards)
    {
        ImmutableArray<BoardDefinition> list = [.. boards];
        var ids = list.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var layouts = Layouts
            .Select(l => l with { Positions = l.Positions.RemoveRange(l.Positions.Keys.Where(k => !ids.Contains(k)).ToArray()) })
            .Where(l => l.Positions.Count > 0);
        return this with { Boards = list, Layouts = [.. layouts] };
    }

    /// <summary>Stores a position in an arrangement and marks it most recently used (dropping beyond 16).</summary>
    public BoardsDocument WithPosition(string arrangement, string boardId, OrbPositionRecord record) =>
        WithPositions(arrangement, [new KeyValuePair<string, OrbPositionRecord>(boardId, record)]);

    public BoardsDocument WithPositions(string arrangement, IEnumerable<KeyValuePair<string, OrbPositionRecord>> records)
    {
        ArgumentException.ThrowIfNullOrEmpty(arrangement);
        ArgumentNullException.ThrowIfNull(records);
        var existing = LayoutFor(arrangement);
        var positions = existing?.Positions ?? ImmutableDictionary.Create<string, OrbPositionRecord>(StringComparer.Ordinal);
        foreach (var (id, record) in records)
        {
            if (Find(id) is not null && record.Validated() is { } valid)
            {
                positions = positions.SetItem(id, valid);
            }
        }

        if (positions.Count == 0)
        {
            return this;
        }

        var layout = new OrbLayout(existing?.Arrangement ?? arrangement, positions);
        return this with { Layouts = Promote(layout) };
    }

    /// <summary>Marks an arrangement as the most recently used one.</summary>
    public BoardsDocument Touch(string arrangement) =>
        LayoutFor(arrangement) is { } layout && !ReferenceEquals(Layouts[0], layout) ? this with { Layouts = Promote(layout) } : this;

    /// <summary>
    /// Every value in range: at most four boards, invalid or duplicate ids replaced by fresh UUIDs, at most 16
    /// arrangements of valid records for existing boards.
    /// </summary>
    public BoardsDocument Validated()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var boards = ImmutableArray.CreateBuilder<BoardDefinition>();
        foreach (var board in Boards.Take(MaxBoards))
        {
            if (board is null)
            {
                continue;
            }

            var clean = board.Validated();
            while (!ids.Add(clean.Id))
            {
                clean = clean with { Id = OrbDefinition.NewId() };
            }

            boards.Add(clean);
        }

        var layouts = ImmutableArray.CreateBuilder<OrbLayout>();
        var arrangements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in Layouts)
        {
            if (layouts.Count >= OrbsDocument.MaxArrangements)
            {
                break;
            }

            if (layout is null || !OrbsDocument.IsValidArrangement(layout.Arrangement) || !arrangements.Add(layout.Arrangement))
            {
                continue;
            }

            var positions = ImmutableDictionary.CreateBuilder<string, OrbPositionRecord>(StringComparer.Ordinal);
            foreach (var (id, record) in layout.Positions)
            {
                if (positions.Count < MaxBoards && ids.Contains(id) && record.Validated() is { } valid)
                {
                    positions[id] = valid;
                }
            }

            if (positions.Count > 0)
            {
                layouts.Add(new OrbLayout(layout.Arrangement, positions.ToImmutable()));
            }
        }

        return new BoardsDocument(boards.ToImmutable(), layouts.ToImmutable());
    }

    public bool Equals(BoardsDocument? other) =>
        ReferenceEquals(this, other)
        || (other is not null && Boards.SequenceEqual(other.Boards) && Layouts.SequenceEqual(other.Layouts));

    public override int GetHashCode() => HashCode.Combine(Boards.Length, Layouts.Length, Boards.Length > 0 ? Boards[0] : null);

    private ImmutableArray<OrbLayout> Promote(OrbLayout layout)
    {
        var rest = Layouts.Where(l => !string.Equals(l.Arrangement, layout.Arrangement, StringComparison.OrdinalIgnoreCase));
        return [.. new[] { layout }.Concat(rest).Take(OrbsDocument.MaxArrangements)];
    }
}
