namespace AgentSpeed.Probe;

internal enum ProbeMode
{
    DumpToday,
    Watch,
}

internal sealed record ProbeOptions(ProbeMode Mode, int WatchSeconds, string? CodexRoot, string? ClaudeRoot)
{
    public const string Usage = "usage: AgentSpeed.Probe (--dump-today | --watch N) [--codex PATH] [--claude PATH]";

    public static ProbeOptions? Parse(string[] args, out string? error)
    {
        error = null;
        ProbeMode? mode = null;
        var seconds = 0;
        string? codex = null, claude = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump-today":
                    mode = ProbeMode.DumpToday;
                    break;
                case "--watch" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0:
                    mode = ProbeMode.Watch;
                    seconds = n;
                    i++;
                    break;
                case "--codex" when i + 1 < args.Length:
                    codex = args[++i];
                    break;
                case "--claude" when i + 1 < args.Length:
                    claude = args[++i];
                    break;
                default:
                    error = $"unknown or incomplete argument: {args[i]}";
                    return null;
            }
        }

        if (mode is null)
        {
            error = "missing --dump-today or --watch N";
            return null;
        }

        return new ProbeOptions(mode.Value, seconds, codex, claude);
    }
}
