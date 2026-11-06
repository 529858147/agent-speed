namespace AgentSpeed.Presentation.Boards;

/// <summary>A board's size (DESIGN 2c): S 280, M 340 (default), L 420 pt/DIP square.</summary>
public enum BoardSize
{
    S,
    M,
    L,
}

/// <summary>Persisted keys, sides and content limits of the board sizes.</summary>
public static class BoardSizes
{
    public static IReadOnlyList<BoardSize> All { get; } = [BoardSize.S, BoardSize.M, BoardSize.L];

    public static string Key(BoardSize size) => size switch
    {
        BoardSize.S => "S",
        BoardSize.L => "L",
        _ => "M",
    };

    /// <summary>Unknown or missing keys are <see cref="BoardSize.M"/>.</summary>
    public static BoardSize Parse(string? key) => key switch
    {
        "S" => BoardSize.S,
        "L" => BoardSize.L,
        _ => BoardSize.M,
    };

    /// <summary>The side of the rounded square in pt/DIP.</summary>
    public static double Side(BoardSize size) => size switch
    {
        BoardSize.S => 280,
        BoardSize.L => 420,
        _ => 340,
    };

    /// <summary>
    /// How many groups the ranking shows at most and how many sub rows each rank may get (DESIGN 2c, Layout per size):
    /// S 4 groups and no sub rows; M 6, two sub rows for #1 and one for #2 and #3; L 8, three for #1–#3 and two for #4–#5.
    /// </summary>
    public static BoardSizeLimits Limits(BoardSize size) => size switch
    {
        BoardSize.S => new BoardSizeLimits(4, []),
        BoardSize.L => new BoardSizeLimits(8, [3, 3, 3, 2, 2]),
        _ => new BoardSizeLimits(6, [2, 1, 1]),
    };
}

/// <param name="Groups">Groups shown at most.</param>
/// <param name="SubRows">Sub rows wanted per rank, first place first; ranks beyond the list get none.</param>
public sealed record BoardSizeLimits(int Groups, IReadOnlyList<int> SubRows)
{
    /// <summary>Sub rows an expanded group shows at most (DESIGN 2c: up to 8 of its sub-agents).</summary>
    public const int ExpandedSubRows = 8;

    /// <summary>Sub rows wanted for the group at <paramref name="index"/> (0 = first place).</summary>
    public int SubRowsAt(int index) => index >= 0 && index < SubRows.Count ? SubRows[index] : 0;
}
