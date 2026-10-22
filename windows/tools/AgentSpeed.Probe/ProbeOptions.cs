using AgentSpeed.Core;
using AgentSpeed.Core.Monitoring;

namespace AgentSpeed.Probe;

internal enum ProbeMode
{
    DumpToday,
    DumpHistory,
    Watch,
}

/// <param name="GeminiRoots">Gemini CLI <c>tmp</c> folders; empty uses the defaults.</param>
/// <param name="OmpRoots">omp <c>sessions</c> folders; empty uses the default root and every named profile.</param>
/// <param name="AntigravityRoots">Antigravity store (or <c>conversations</c>) folders; empty uses the three default stores.</param>
/// <param name="StorePath">History store to use and keep; null analyses into a temporary store.</param>
/// <param name="SinceMs">Overrides the start of "today" for <c>--dump-today</c> (Unix milliseconds).</param>
internal sealed record ProbeOptions(
    ProbeMode Mode,
    int WatchSeconds,
    string? CodexRoot,
    string? ClaudeRoot,
    string? DshRoot,
    string? OpenCodeRoot,
    IReadOnlyList<string> GeminiRoots,
    bool Utc,
    string? StorePath,
    long? SinceMs = null,
    IReadOnlyList<string>? OmpRoots = null,
    IReadOnlyList<string>? AntigravityRoots = null)
{
    public const string Usage =
        "usage: AgentSpeed.Probe (--dump-today [--since UNIXMS] | --dump-history [--utc] [--store PATH] | --watch N) " +
        "[--codex PATH] [--claude PATH] [--dsh PATH] [--opencode PATH] [--gemini PATH]... [--omp PATH]... [--antigravity PATH]...";

    public MonitorConfiguration Configuration => new(
        new HashSet<SourceKind>(Enum.GetValues<SourceKind>()),
        CodexRoot,
        ClaudeRoot,
        DshRoot,
        OpenCodeRoot,
        GeminiRoots.Count > 0 ? GeminiRoots[0] : null,
        OmpRoots is { Count: > 0 } omp ? omp[0] : null,
        AntigravityRoots is { Count: > 0 } antigravity ? antigravity[0] : null);

    /// <summary>Resolved roots; several <c>--gemini</c>, <c>--omp</c> or <c>--antigravity</c> folders are all read (like the reference).</summary>
    public SourceLocations Locations
    {
        get
        {
            var resolved = SourceLocations.Resolve(Configuration);
            if (GeminiRoots.Count > 1)
            {
                resolved = resolved with { GeminiRoots = [.. GeminiRoots.Select(SourceLocations.Normalize)] };
            }

            if (OmpRoots is { Count: > 1 } omp)
            {
                resolved = resolved with { OmpRoots = SourceLocations.NormalizeAll(omp) };
            }

            if (AntigravityRoots is { Count: > 1 } antigravity)
            {
                resolved = resolved with
                {
                    AntigravityStores = SourceLocations.NormalizeAll(antigravity.SelectMany(LiveMonitor.AntigravityStores)),
                };
            }

            return resolved;
        }
    }

    public static ProbeOptions? Parse(string[] args, out string? error)
    {
        error = null;
        ProbeMode? mode = null;
        var seconds = 0;
        var utc = false;
        string? codex = null, claude = null, dsh = null, opencode = null, store = null;
        long? since = null;
        var gemini = new List<string>();
        var omp = new List<string>();
        var antigravity = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump-today":
                    mode = ProbeMode.DumpToday;
                    break;
                case "--dump-history":
                    mode = ProbeMode.DumpHistory;
                    break;
                case "--utc":
                    utc = true;
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
                case "--dsh" when i + 1 < args.Length:
                    dsh = args[++i];
                    break;
                case "--opencode" when i + 1 < args.Length:
                    opencode = args[++i];
                    break;
                case "--gemini" when i + 1 < args.Length:
                    gemini.Add(args[++i]);
                    break;
                case "--omp" when i + 1 < args.Length:
                    omp.Add(args[++i]);
                    break;
                case "--antigravity" when i + 1 < args.Length:
                    antigravity.Add(args[++i]);
                    break;
                case "--store" when i + 1 < args.Length:
                    store = args[++i];
                    break;
                case "--since" when i + 1 < args.Length && long.TryParse(args[i + 1], out var ms):
                    since = ms;
                    i++;
                    break;
                default:
                    error = $"unknown or incomplete argument: {args[i]}";
                    return null;
            }
        }

        if (mode is null)
        {
            error = "missing --dump-today, --dump-history or --watch N";
            return null;
        }

        return new ProbeOptions(mode.Value, seconds, codex, claude, dsh, opencode, gemini, utc, store, since, omp, antigravity);
    }
}
