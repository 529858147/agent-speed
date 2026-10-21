using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Monitoring;

internal enum RootFamily
{
    Codex,
    Claude,
}

/// <summary>One transcript root folder (Codex sessions, or Claude projects shared by Code and Desktop).</summary>
internal sealed class SourceRoot(RootFamily family, string path, bool enabled)
{
    public RootFamily Family { get; } = family;

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
        Tailer = new FileTailer(path);
        Parser = root.Family == RootFamily.Codex
            ? new CodexParser(path, deduper)
            : new ClaudeParser(path, deduper);
    }

    public SourceRoot Root { get; }

    public string Path => Tailer.Path;

    public FileTailer Tailer { get; }

    public TranscriptParser Parser { get; }

    public long KnownLength { get; set; } = -1;

    public DateTime KnownWriteUtc { get; set; }

    public int PushedVersion { get; set; } = -1;

    public bool Unreadable { get; set; }
}
