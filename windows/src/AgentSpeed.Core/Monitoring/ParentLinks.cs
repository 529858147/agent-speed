using AgentSpeed.Core.Gemini;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Monitoring;

/// <summary>How a transcript names its parent (METRICS 6.7).</summary>
internal enum LinkKind : byte
{
    /// <summary>Claude: by the <c>subagents</c> folder layout.</summary>
    Path,

    /// <summary>Codex: by parent thread id.</summary>
    CodexThread,

    /// <summary>DeepSeek Harness: by parent session id.</summary>
    DshSession,

    /// <summary>OpenCode: by <c>session_v2.parent_id</c>.</summary>
    OpenCodeSession,

    /// <summary>Gemini CLI: by the sanitized main session id inside the same <c>chats</c> folder.</summary>
    GeminiSession,
}

/// <summary>What one transcript says about its parent (METRICS 6.7).</summary>
/// <param name="ThreadId">The id other transcripts use to name this one as their parent.</param>
/// <param name="Rank">Preferred first among several candidates with the same <paramref name="ThreadId"/> when lower.</param>
internal readonly record struct AgentLink(
    string Id, LinkKind Kind, string? ThreadId, string? ParentThreadId, long? CreatedMs, int Rank = 0)
{
    public static AgentLink Of(TranscriptParser parser) => parser switch
    {
        CodexParser codex => new AgentLink(codex.AgentId, LinkKind.CodexThread, codex.MetaThreadId, codex.ParentThreadId, codex.CreatedMs),
        DshParser dsh => new AgentLink(dsh.AgentId, LinkKind.DshSession, dsh.SessionId, dsh.ParentSession, dsh.CreatedMs),
        GeminiParser gemini => OfGemini(gemini),
        _ => new AgentLink(parser.AgentId, LinkKind.Path, null, null, null),
    };

    // Main sessions are keyed by their chats folder plus sanitized session id; sub-agents name <chats>/<dir>.
    private static AgentLink OfGemini(GeminiParser gemini)
    {
        var path = gemini.AgentId;
        var thread = gemini.IsMainSession && gemini.SessionId is { Length: > 0 } session && FolderOf(path, 1) is { } chats
            ? chats + "\n" + GeminiPaths.Sanitize(session)
            : null;
        var parent = gemini.ParentDirectory is { Length: > 0 } directory && FolderOf(path, 2) is { } parentChats
            ? parentChats + "\n" + directory
            : null;
        return new AgentLink(path, LinkKind.GeminiSession, thread, parent, gemini.StartTimeMs ?? 0, gemini.IsDocument ? 1 : 0);
    }

    private static string? FolderOf(string path, int levels)
    {
        var end = path.Length;
        for (var i = 0; i < levels; i++)
        {
            end = path.AsSpan(0, end).LastIndexOfAny('/', '\\');
            if (end < 0)
            {
                return null;
            }
        }

        return path[..end];
    }
}

/// <summary>
/// Resolves parent agent ids (METRICS 6.7): Claude by path, Codex by parent thread, dsh, OpenCode and Gemini by parent
/// session.
/// </summary>
internal static class ParentLinks
{
    private const string SubagentsFolder = "subagents";
    private const string TranscriptExtension = ".jsonl";

    /// <summary>Maps each agent that has a parent to that parent's id; self references and cycles are dropped.</summary>
    /// <param name="pathComparer">How transcript paths compare on this file system.</param>
    public static Dictionary<string, string> Resolve(IReadOnlyCollection<AgentLink> agents, StringComparer pathComparer)
    {
        var byPath = new Dictionary<string, string>(agents.Count, pathComparer);
        var byThread = new Dictionary<(LinkKind, string), AgentLink>();
        foreach (var agent in agents)
        {
            byPath.TryAdd(agent.Id, agent.Id);
            if (agent.Kind != LinkKind.Path && agent.ThreadId is { Length: > 0 } thread
                && (!byThread.TryGetValue((agent.Kind, thread), out var known) || CreatedBefore(agent, known)))
            {
                byThread[(agent.Kind, thread)] = agent;
            }
        }

        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            string? parent = null;
            if (agent.Kind != LinkKind.Path)
            {
                if (agent.ParentThreadId is { Length: > 0 } thread && byThread.TryGetValue((agent.Kind, thread), out var owner))
                {
                    parent = owner.Id;
                }
            }
            else if (ClaudeParentPath(agent.Id) is { } candidate && byPath.TryGetValue(candidate, out var tracked))
            {
                parent = tracked;
            }

            if (parent is not null && !string.Equals(parent, agent.Id, StringComparison.Ordinal))
            {
                parents[agent.Id] = parent;
            }
        }

        DropCycles(parents);
        return parents;
    }

    /// <summary><c>dir/session/subagents/name.jsonl</c> → <c>dir/session.jsonl</c>, with either separator.</summary>
    public static string? ClaudeParentPath(string path)
    {
        var span = path.AsSpan();
        var name = span.LastIndexOfAny('/', '\\');
        if (name < 0)
        {
            return null;
        }

        var folder = span[..name];
        var start = folder.LastIndexOfAny('/', '\\');
        return start >= 0 && folder[(start + 1)..].SequenceEqual(SubagentsFolder)
            ? string.Concat(folder[..start], TranscriptExtension)
            : null;
    }

    private static bool CreatedBefore(in AgentLink a, in AgentLink b)
    {
        if (a.Rank != b.Rank)
        {
            return a.Rank < b.Rank;
        }

        var byTime = (a.CreatedMs ?? long.MaxValue).CompareTo(b.CreatedMs ?? long.MaxValue);
        return byTime != 0 ? byTime < 0 : string.CompareOrdinal(a.Id, b.Id) < 0;
    }

    // In ordinal id order, drops the link of each agent whose chain of parents still loops.
    private static void DropCycles(Dictionary<string, string> parents)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in parents.Keys.Order(StringComparer.Ordinal).ToList())
        {
            seen.Clear();
            string? current = id;
            while (current is not null && seen.Add(current))
            {
                current = parents.GetValueOrDefault(current);
            }

            if (current is not null)
            {
                parents.Remove(id);
            }
        }
    }
}
