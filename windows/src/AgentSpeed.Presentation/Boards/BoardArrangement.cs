using System.Collections.Immutable;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Settings.Boards;
using AgentSpeed.Presentation.Settings.Orbs;

namespace AgentSpeed.Presentation.Boards;

/// <summary>Where a board's square is: the display key, the center and half the side in physical pixels.</summary>
public readonly record struct BoardLocation(string Display, PointD Center, double Half)
{
    public BoardObstacle Obstacle => BoardObstacle.Square(Center, Half);
}

/// <summary>The updated document and the locations of the boards an operation placed.</summary>
public sealed record BoardLayoutResult(BoardsDocument Document, string Arrangement, ImmutableDictionary<string, BoardLocation> Locations);

/// <summary>
/// Positions of the boards across display arrangements (DESIGN 2c Data model, as the orbs' in 2b): a board's record of
/// this arrangement; else its record from the most recently used arrangement whose display is still connected; else its
/// latest record moved to the main display; else the top-right default. Boards and visible orbs never overlap: a board
/// that would slides to the nearest free spot, in list order. Pure; <c>orbs</c> are the orbs' discs in physical pixels.
/// </summary>
public static class BoardArrangement
{
    public static BoardLayoutResult Resolve(BoardsDocument document, IReadOnlyList<OrbDisplay> displays, IReadOnlyList<OrbDisc> orbs)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(orbs);
        var connected = Connected(displays);
        if (connected.Count == 0)
        {
            return new BoardLayoutResult(document, string.Empty, ImmutableDictionary<string, BoardLocation>.Empty);
        }

        var arrangement = OrbArrangement.Key(connected.Values);
        var main = Main(displays, connected);
        var current = document.LayoutFor(arrangement);
        var occupied = orbs.Select(o => BoardObstacle.Disc(o.Center, o.Radius)).ToList();
        var locations = ImmutableDictionary.CreateBuilder<string, BoardLocation>(StringComparer.Ordinal);
        var written = new List<KeyValuePair<string, OrbPositionRecord>>(document.Count);
        var defaults = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var board in document.Boards.OrderBy(b => b.Visible ? 0 : 1))
        {
            var (record, display) = Pick(document, current, board.Id, connected, main);
            var half = BoardPlacement.HalfSide(board.Size, display.Scale);
            PointD center;
            if (record is { } r)
            {
                var wanted = r.ToCenter(display.Work, display.Scale, half);
                center = BoardSpace.Resolve(wanted, half, occupied, display.Work, display.Scale);
                written.Add(new(board.Id, center == wanted
                    ? r.OnDisplay(display.Key)
                    : BoardPlacement.Record(display.Key, center, board.Size, display.Work, display.Scale)));
            }
            else
            {
                defaults.TryGetValue(display.Key, out var index);
                defaults[display.Key] = index + 1;
                var start = BoardPlacement.Default(board.Size, display.Work, display.Scale, index);
                center = BoardSpace.Resolve(start, half, occupied, display.Work, display.Scale);
            }

            if (board.Visible)
            {
                occupied.Add(BoardObstacle.Square(center, half));
            }

            locations[board.Id] = new BoardLocation(display.Key, center, half);
        }

        var next = written.Count > 0 ? document.WithPositions(arrangement, written) : document.Touch(arrangement);
        return new BoardLayoutResult(next, arrangement, locations.ToImmutable());
    }

    /// <summary>Stores a board's center (a finished drag) in the current arrangement.</summary>
    public static BoardsDocument Commit(BoardsDocument document, IReadOnlyList<OrbDisplay> displays, string boardId, PointD center)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(displays);
        var connected = Connected(displays);
        if (connected.Count == 0 || document.Find(boardId) is not { } board || OrbArrangement.DisplayAt(displays, center) is not { } display)
        {
            return document;
        }

        var record = BoardPlacement.Record(display.Key, center, board.Size, display.Work, display.Scale);
        return document.WithPosition(OrbArrangement.Key(connected.Values), boardId, record);
    }

    /// <summary>
    /// Where a released board settles: its snapped center when that is clear of the other windows, else the nearest free
    /// spot on that display. <paramref name="others"/> are the other visible boards' squares and the orbs' discs.
    /// </summary>
    public static PointD Release(BoardDefinition board, IReadOnlyList<OrbDisplay> displays, IReadOnlyList<BoardObstacle> others, PointD snapped)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(others);
        if (OrbArrangement.DisplayAt(displays, snapped) is not { } display)
        {
            return snapped;
        }

        var half = BoardPlacement.HalfSide(board.Size, display.Scale);
        return BoardSpace.Resolve(snapped, half, others, display.Work, display.Scale);
    }

    /// <summary>
    /// Places a new board next to the window it came from (<paramref name="origin"/>; else the main display's default corner)
    /// clear of <paramref name="others"/>, and stores the position.
    /// </summary>
    public static BoardLayoutResult PlaceNew(
        BoardsDocument document, IReadOnlyList<OrbDisplay> displays, string newId, BoardObstacle? origin, IReadOnlyList<BoardObstacle> others)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(displays);
        var connected = Connected(displays);
        if (connected.Count == 0 || document.Find(newId) is not { } board)
        {
            return new BoardLayoutResult(document, string.Empty, ImmutableDictionary<string, BoardLocation>.Empty);
        }

        var display = origin is { } from && OrbArrangement.DisplayAt(displays, from.Center) is { } at && connected.TryGetValue(at.Key, out var known)
            ? known
            : Main(displays, connected);
        var half = BoardPlacement.HalfSide(board.Size, display.Scale);
        var fallback = BoardPlacement.Default(board.Size, display.Work, display.Scale);
        var center = BoardSpace.PlaceNew(half, origin, fallback, others, display.Work, display.Scale);
        var arrangement = OrbArrangement.Key(connected.Values);
        var record = BoardPlacement.Record(display.Key, center, board.Size, display.Work, display.Scale);
        return new BoardLayoutResult(document.WithPosition(arrangement, newId, record), arrangement,
            ImmutableDictionary<string, BoardLocation>.Empty.WithComparers(StringComparer.Ordinal).Add(newId, new BoardLocation(display.Key, center, half)));
    }

    private static (OrbPositionRecord? Record, OrbDisplay Display) Pick(
        BoardsDocument document, OrbLayout? current, string id, Dictionary<string, OrbDisplay> connected, OrbDisplay main)
    {
        if (current is not null && current.Positions.TryGetValue(id, out var own) && connected.TryGetValue(own.Display, out var ownDisplay))
        {
            return (own, ownDisplay);
        }

        OrbPositionRecord? latest = null;
        foreach (var layout in document.Layouts)
        {
            if (ReferenceEquals(layout, current) || !layout.Positions.TryGetValue(id, out var record))
            {
                continue;
            }

            if (connected.TryGetValue(record.Display, out var display))
            {
                return (record, display);
            }

            latest ??= record;
        }

        if (latest is null && current is not null && current.Positions.TryGetValue(id, out var stale))
        {
            latest = stale;
        }

        return latest is { } moved ? (moved.OnDisplay(main.Key), main) : (null, main);
    }

    private static Dictionary<string, OrbDisplay> Connected(IReadOnlyList<OrbDisplay> displays)
    {
        var map = new Dictionary<string, OrbDisplay>(StringComparer.OrdinalIgnoreCase);
        foreach (var display in displays)
        {
            if (!string.IsNullOrEmpty(display.Key) && display.Key.Length <= OrbPositionRecord.MaxDisplayKeyLength)
            {
                map.TryAdd(display.Key, display with { Scale = OrbGeometry.ValidScale(display.Scale) });
            }
        }

        return map;
    }

    private static OrbDisplay Main(IReadOnlyList<OrbDisplay> displays, Dictionary<string, OrbDisplay> connected)
    {
        foreach (var display in displays)
        {
            if (display.IsMain && connected.TryGetValue(display.Key, out var main))
            {
                return main;
            }
        }

        return connected[displays.First(d => connected.ContainsKey(d.Key ?? string.Empty)).Key];
    }
}
