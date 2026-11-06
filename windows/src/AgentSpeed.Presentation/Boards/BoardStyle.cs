namespace AgentSpeed.Presentation.Boards;

/// <summary>The look of a leaderboard board (DESIGN 2c): 流光玻璃, 計時塔 or 霓虹座艙.</summary>
public enum BoardStyle
{
    Glass,
    Timing,
    Neon,
}

/// <summary>Persisted keys of the board styles.</summary>
public static class BoardStyles
{
    /// <summary>The style of the first board, and of <c>boards.add</c> when no board was used yet.</summary>
    public const BoardStyle Default = BoardStyle.Neon;

    public static IReadOnlyList<BoardStyle> All { get; } = [BoardStyle.Glass, BoardStyle.Timing, BoardStyle.Neon];

    /// <summary>The persisted key (<c>glass</c>, <c>timing</c>, <c>neon</c>), also the suffix of the <c>boards.style.*</c> strings.</summary>
    public static string Key(BoardStyle style) => style switch
    {
        BoardStyle.Timing => "timing",
        BoardStyle.Neon => "neon",
        _ => "glass",
    };

    /// <summary>Unknown or missing keys are <see cref="BoardStyle.Glass"/>.</summary>
    public static BoardStyle Parse(string? key) => key switch
    {
        "timing" => BoardStyle.Timing,
        "neon" => BoardStyle.Neon,
        _ => BoardStyle.Glass,
    };
}
