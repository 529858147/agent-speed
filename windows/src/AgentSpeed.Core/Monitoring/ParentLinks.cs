using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Monitoring;

/// <summary>What one transcript says about its parent (METRICS 6.7).</summary>
internal readonly record struct AgentLink(string Id, bool IsCodex, string? ThreadId, string? ParentThreadId, long? CreatedMs)
{
    public static AgentLink Of(TranscriptParser parser) => parser is CodexParser codex
        ? new AgentLink(codex.AgentId, true, codex.MetaThreadId, codex.ParentThreadId, codex.CreatedMs)
        : new AgentLink(parser.AgentId, false, null, null, null);
}

/// <summary>Resolves parent agent ids (METRICS 6.7): Claude by path, Codex by parent thread id.</summary>
internal static class ParentLinks
{
    private const string SubagentsFolder = "subagents";
    private const string TranscriptExtension = ".jsonl";

    /// <summary>Maps each agent that has a parent to that parent's id; self references and cycles are dropped.</summary>
    /// <param name="pathComparer">How transcript paths compare on this file system.</param>
    public static Dictionary<string, string> Resolve(IReadOnlyCollection<AgentLink> agents, StringComparer pathComparer)
    {
        var byPath = new Dictionary<string, string>(agents.Count, pathComparer);
        var byThread = new Dictionary<string, AgentLink>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            byPath.TryAdd(agent.Id, agent.Id);
            if (agent.IsCodex && agent.ThreadId is { Length: > 0 } thread
                && (!byThread.TryGetValue(thread, out var known) || CreatedBefore(agent, known)))
            {
                byThread[thread] = agent;
            }
        }

        var parents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            string? parent = null;
            if (agent.IsCodex)
            {
                if (agent.ParentThreadId is { Length: > 0 } thread && byThread.TryGetValue(thread, out var owner))
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
