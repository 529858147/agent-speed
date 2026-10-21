using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Monitoring;

internal enum RootFamily
{
    Codex,
    Claude,
    DeepSeek,
}

/// <summary>One transcript root folder (Codex sessions, Claude projects shared by Code and Desktop, dsh sessions).</summary>
internal sealed class SourceRoot(RootFamily family, string path, bool enabled)
{
    public RootFamily Family { get; } = family;

    public TranscriptNaming Naming => Family == RootFamily.DeepSeek ? TranscriptNaming.DshSession : TranscriptNaming.Jsonl;

    public string Path { get; } = path;

    public bool Enabled { get; } = enabled;

    public bool Available { get; set; }

    public bool Seeded { get; set; }
}

/// <summary>A discovered transcript: its tailer, its parser and what was last observed on disk.</summary>
internal sealed class TrackedFile
{
    public TrackedFile(SourceRoot root, string path, Deduper deduper)
    {
        Root = root;
        Tailer = TranscriptTailer.For(path);
        Parser = root.Family switch
        {
            RootFamily.Codex => new CodexParser(path, deduper),
            RootFamily.DeepSeek => new DshParser(path, deduper),
            _ => new ClaudeParser(path, deduper),
        };
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
