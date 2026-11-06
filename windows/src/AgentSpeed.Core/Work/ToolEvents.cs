namespace AgentSpeed.Core.Work;

/// <summary>A tool call read from a line (METRICS 6.10): its id, tool name and input digest, never the input.</summary>
/// <param name="Tool">Null when the file names no tool: the call is not an action.</param>
internal readonly record struct ToolCall(string Id, string? Tool, ulong Digest);

/// <summary>A tool result read from a line, matched to its call by id; <paramref name="Failed"/> is null when the format does not say.</summary>
internal readonly record struct ToolOutcome(string Id, bool? Failed);

/// <summary>The tool calls and results one line carries, in line order.</summary>
internal readonly record struct LineTools(ToolCall[]? Calls = null, ToolOutcome[]? Outcomes = null)
{
    public static LineTools Of(ToolCall call) => new([call]);

    public static LineTools Of(ToolOutcome outcome) => new(Outcomes: [outcome]);

    public static LineTools Of(List<ToolCall>? calls, List<ToolOutcome>? outcomes) =>
        new(calls is { Count: > 0 } ? [.. calls] : null, outcomes is { Count: > 0 } ? [.. outcomes] : null);
}
