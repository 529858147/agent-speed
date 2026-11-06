namespace AgentSpeed.Core;

/// <summary>The two values agents show (EXECUTION-MODES, Reasoning effort and speed): levels, bolts and display order.</summary>
public static class EffortAndSpeed
{
    private static readonly string[] EffortOrder = ["ultracode", "ultra", "max", "xhigh", "high", "medium", "low", "minimal", "off"];
    private static readonly string[] SpeedOrder = ["ultrafast", "fast", "highspeed", "standard"];

    /// <summary>The attributed reasoning effort; null when unknown.</summary>
    public static ExecutionModeAttribution? Effort(ExecutionModes modes) => Known(modes.Attribute(ExecutionModeKind.Effort));

    /// <summary>The derived speed; null when unknown.</summary>
    public static ExecutionModeAttribution? Speed(ExecutionModes modes) => Known(modes.Attribute(ExecutionModeKind.Speed));

    /// <summary>Meter level 0–5; null for a token without a level (shown as its word only).</summary>
    public static int? Level(string effort) => effort switch
    {
        "off" => 0,
        "minimal" or "low" => 1,
        "medium" => 2,
        "high" => 3,
        "xhigh" => 4,
        "max" or "ultra" or "ultracode" => 5,
        _ => null,
    };

    /// <summary><c>ultra</c> (Codex) and <c>ultracode</c> (Claude Code) light the top rung.</summary>
    public static bool IsTopRung(string effort) => effort is "ultra" or "ultracode";

    public static int Bolts(string speed) => speed switch
    {
        "fast" or "highspeed" => 1,
        "ultrafast" => 2,
        _ => 0,
    };

    /// <summary>Known tokens by rank, other tokens in ordinal order, <c>unknown</c> last.</summary>
    public static IComparer<string> Order(ExecutionModeKind kind) => kind == ExecutionModeKind.Effort ? EffortComparer : SpeedComparer;

    private static readonly IComparer<string> EffortComparer = new RankComparer(EffortOrder);
    private static readonly IComparer<string> SpeedComparer = new RankComparer(SpeedOrder);

    private static ExecutionModeAttribution? Known(ExecutionModeAttribution value) => value.IsKnown ? value : null;

    private sealed class RankComparer(string[] ranks) : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            var byRank = Rank(x).CompareTo(Rank(y));
            return byRank != 0 ? byRank : string.CompareOrdinal(x, y);
        }

        private int Rank(string? value) => value == "unknown" ? ranks.Length + 1
            : Array.IndexOf(ranks, value) is var index and >= 0 ? index : ranks.Length;
    }
}
