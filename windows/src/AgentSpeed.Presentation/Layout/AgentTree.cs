using AgentSpeed.Core;

namespace AgentSpeed.Presentation.Layout;

/// <summary>What a row draws in one connector column.</summary>
public enum TreeGuide : byte
{
    None,

    /// <summary>A line passing by on its way to a later sibling.</summary>
    Through,

    /// <summary>A line passing by with a branch into this row.</summary>
    Branch,

    /// <summary>A line ending in a branch into this row (the last sibling).</summary>
    Last,
}

/// <summary>Connector columns of one row, outermost first; levels past the indent cap share <see cref="Deeper"/>.</summary>
public readonly record struct TreeGuides(TreeGuide Level1, TreeGuide Level2, TreeGuide Deeper)
{
    public bool IsEmpty => this == default;

    internal TreeGuides With(int level, TreeGuide guide) => level switch
    {
        1 => this with { Level1 = Merge(Level1, guide) },
        2 => this with { Level2 = Merge(Level2, guide) },
        _ => this with { Deeper = Merge(Deeper, guide) },
    };

    private static TreeGuide Merge(TreeGuide a, TreeGuide b) =>
        a == TreeGuide.None || a == b ? b : b == TreeGuide.None ? a : TreeGuide.Branch;
}

/// <param name="Depth">Depth among the shown rows; 0 for a root, even when the agent is a sub-agent.</param>
public sealed record AgentTreeRow(AgentStatus Agent, int Depth, TreeGuides Guides)
{
    /// <summary>Visual indent in levels, capped at <see cref="AgentTree.MaxIndent"/>.</summary>
    public int Indent => Math.Min(Depth, AgentTree.MaxIndent);
}

/// <param name="Hidden">Agents left out by the row limit.</param>
public sealed record AgentTreeLayout(IReadOnlyList<AgentTreeRow> Rows, int Hidden);

/// <summary>
/// Shows the engine's grouped agent list (METRICS 6.7) as an indented tree without reordering it. An agent whose
/// parent is not listed is a root; one whose parent row did not fit is left out with the rest.
/// </summary>
public static class AgentTree
{
    public const int MaxIndent = 2;

    public static AgentTreeLayout Arrange(IReadOnlyList<AgentStatus> agents, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(agents);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            listed.Add(agent.Id);
        }

        var shown = new Dictionary<string, int>(StringComparer.Ordinal);
        var rows = new List<(AgentStatus Agent, int Parent, int Depth)>();
        foreach (var agent in agents)
        {
            if (rows.Count >= maxRows)
            {
                break;
            }

            var parent = -1;
            if (agent.ParentId is { } parentId && listed.Contains(parentId)
                && !string.Equals(parentId, agent.Id, StringComparison.Ordinal)
                && !shown.TryGetValue(parentId, out parent))
            {
                continue;
            }

            if (shown.TryAdd(agent.Id, rows.Count))
            {
                rows.Add((agent, parent, parent < 0 ? 0 : rows[parent].Depth + 1));
            }
        }

        var hasLaterSibling = new bool[rows.Count];
        var parentsSeen = new HashSet<int>();
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            hasLaterSibling[i] = !parentsSeen.Add(rows[i].Parent);
        }

        var result = new AgentTreeRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var guides = default(TreeGuides);
            var node = i;
            for (var level = rows[i].Depth; level >= 1; level--)
            {
                var guide = node == i
                    ? hasLaterSibling[node] ? TreeGuide.Branch : TreeGuide.Last
                    : hasLaterSibling[node] ? TreeGuide.Through : TreeGuide.None;
                guides = guides.With(level, guide);
                node = rows[node].Parent;
            }

            result[i] = new AgentTreeRow(rows[i].Agent, rows[i].Depth, guides);
        }

        return new AgentTreeLayout(result, agents.Count - result.Length);
    }
}
