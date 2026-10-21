using AgentSpeed.Core.Gemini;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Monitoring;

internal enum RootFamily
{
    Codex,
    Claude,
    DeepSeek,
    Gemini,

    /// <summary>One SQLite database instead of transcript files (METRICS 5.2).</summary>
    OpenCode,
}

/// <summary>Per-family file rules: names, parser and the sources it can produce (METRICS 2 and 4).</summary>
internal static class Families
{
    /// <summary>Families read from transcript files, in a stable order.</summary>
    public static readonly RootFamily[] FileFamilies = [RootFamily.Codex, RootFamily.Claude, RootFamily.DeepSeek, RootFamily.Gemini];

    public static TranscriptNaming Naming(RootFamily family) => family switch
    {
        RootFamily.Codex => TranscriptNaming.CodexRollout,
        RootFamily.DeepSeek => TranscriptNaming.DshSession,
        RootFamily.Gemini => TranscriptNaming.GeminiSession,
        RootFamily.OpenCode => TranscriptNaming.OpenCodeDatabase,
        _ => TranscriptNaming.Jsonl,
    };

    public static TranscriptParser CreateParser(RootFamily family, string path, IDeduper deduper) => family switch
    {
        RootFamily.Codex => new CodexParser(path, deduper),
        RootFamily.DeepSeek => new DshParser(path, deduper),
        RootFamily.Gemini => new GeminiParser(path, deduper),
        RootFamily.Claude => new ClaudeParser(path, deduper),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>True when any source of the family is enabled.</summary>
    public static bool IsEnabled(RootFamily family, IReadOnlySet<SourceKind> enabled) => family switch
    {
        RootFamily.Codex => enabled.Contains(SourceKind.Codex),
        RootFamily.Claude => enabled.Contains(SourceKind.ClaudeCode) || enabled.Contains(SourceKind.ClaudeDesktop),
        RootFamily.DeepSeek => enabled.Contains(SourceKind.DeepSeek),
        RootFamily.Gemini => enabled.Contains(SourceKind.Gemini),
        RootFamily.OpenCode => enabled.Contains(SourceKind.OpenCode),
        _ => false,
    };

    public static RootFamily Of(SourceKind source) => source switch
    {
        SourceKind.Codex => RootFamily.Codex,
        SourceKind.DeepSeek => RootFamily.DeepSeek,
        SourceKind.Gemini => RootFamily.Gemini,
        SourceKind.OpenCode => RootFamily.OpenCode,
        _ => RootFamily.Claude,
    };
}

/// <summary>One root folder of a source family (Codex sessions, Claude projects shared by Code and Desktop, …).</summary>
internal sealed class SourceRoot(RootFamily family, string path, bool enabled)
{
    public RootFamily Family { get; } = family;

    public TranscriptNaming Naming => Families.Naming(Family);

    public string Path { get; } = path;

    public bool Enabled { get; } = enabled;

    public bool Available { get; set; }

    public bool Seeded { get; set; }
}

/// <summary>A discovered transcript: its tailer, its parser and what was last observed on disk.</summary>
internal sealed class TrackedFile
{
    public TrackedFile(SourceRoot root, string path, IDeduper deduper)
    {
        Root = root;
        Tailer = TranscriptTailer.For(path);
        Parser = Families.CreateParser(root.Family, path, deduper);
    }

    public SourceRoot Root { get; }

    public string Path => Tailer.Path;

    public TranscriptTailer Tailer { get; }

    public TranscriptParser Parser { get; }

    public long KnownLength { get; set; } = -1;

    public DateTime KnownWriteUtc { get; set; }

    public int PushedVersion { get; set; } = -1;

    public bool Unreadable { get; set; }
}
