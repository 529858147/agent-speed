using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;

namespace AgentSpeed.Presentation.Boards;

/// <summary>
/// Groups a board's agents by their root (DESIGN 2c, Content item 2): every agent belongs to the group of the topmost
/// listed ancestor reached through <see cref="AgentStatus.ParentId"/>; a sub-agent whose parent is not listed is its own
/// root. The input is the agents list of the board's sources (METRICS 6.7 filtered by 6.8, <see cref="OrbSubset"/>).
/// </summary>
public static class BoardGrouping
{
    /// <summary>
    /// The groups in the order their roots appear in <paramref name="agents"/>; the ranking orders them. Each carries its
    /// intelligence.
    /// </summary>
    public static IReadOnlyList<BoardGroup> Group(IReadOnlyList<AgentStatus> agents, SpeedMetric metric)
    {
        ArgumentNullException.ThrowIfNull(agents);
        var byId = new Dictionary<string, AgentStatus>(agents.Count, StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            byId.TryAdd(agent.Id, agent);
        }

        var roots = new List<string>();
        var members = new Dictionary<string, List<AgentStatus>>(StringComparer.Ordinal);
        foreach (var agent in agents)
        {
            if (!ReferenceEquals(byId[agent.Id], agent))
            {
                continue;
            }

            var root = RootOf(agent, byId);
            if (!members.TryGetValue(root, out var list))
            {
                members[root] = list = [];
                roots.Add(root);
            }

            if (!string.Equals(root, agent.Id, StringComparison.Ordinal))
            {
                list.Add(agent);
            }
        }

        var groups = new List<BoardGroup>(roots.Count);
        foreach (var rootId in roots)
        {
            var root = byId[rootId];
            var subs = members[rootId];
            subs.Sort(CompareSubs(metric));
            var subLive = 0.0;
            var generating = root.Phase == AgentPhase.Generating ? 1 : 0;
            var tools = root.Phase == AgentPhase.Tools ? 1 : 0;
            var lastActive = root.LastActivity.ToUnixTimeMilliseconds();
            foreach (var sub in subs)
            {
                subLive += LiveOf(sub, metric);
                generating += sub.Phase == AgentPhase.Generating ? 1 : 0;
                tools += sub.Phase == AgentPhase.Tools ? 1 : 0;
                lastActive = Math.Max(lastActive, sub.LastActivity.ToUnixTimeMilliseconds());
            }

            groups.Add(new BoardGroup(rootId, root, subs, LiveOf(root, metric) + subLive, subLive, lastActive, generating, tools)
            {
                Intelligence = AgentIntelligence.Group(root, subs),
            });
        }

        return groups;
    }

    /// <summary>An agent's live value in the metric (non-finite or negative values count as 0).</summary>
    public static double LiveOf(AgentStatus agent, SpeedMetric metric)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var value = agent.Live.ValueFor(metric);
        return double.IsFinite(value) && value > 0 ? value : 0;
    }

    /// <summary>Sub rows sort by live value, latest activity, id (DESIGN 2c, Ranking stability).</summary>
    public static Comparison<AgentStatus> CompareSubs(SpeedMetric metric) => (a, b) =>
    {
        var byLive = LiveOf(b, metric).CompareTo(LiveOf(a, metric));
        if (byLive != 0)
        {
            return byLive;
        }

        var byActivity = b.LastActivity.CompareTo(a.LastActivity);
        return byActivity != 0 ? byActivity : string.CompareOrdinal(a.Id, b.Id);
    };

    private static string RootOf(AgentStatus agent, Dictionary<string, AgentStatus> byId)
    {
        // The engine resolves cycles already (METRICS 6.7); the step limit only keeps a malformed list finite.
        var current = agent;
        for (var steps = 0; steps < byId.Count && current.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent)
            && !string.Equals(parentId, agent.Id, StringComparison.Ordinal); steps++)
        {
            current = parent;
        }

        return current.Id;
    }
}
