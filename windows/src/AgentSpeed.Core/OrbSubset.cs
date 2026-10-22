namespace AgentSpeed.Core;

/// <summary>Whether a floating orb's sources can show anything (DESIGN 2b, "Unavailable sources").</summary>
public enum SubsetState
{
    /// <summary>At least one source is available.</summary>
    Available,

    /// <summary>Every source is disabled.</summary>
    AllDisabled,

    /// <summary>Every source is disabled or not detected, and at least one is not detected.</summary>
    AllNotFound,
}

/// <summary>What a floating orb tracking a source set shows for one snapshot (METRICS 6.8).</summary>
/// <param name="Live">Sum of the available members' live rates.</param>
/// <param name="EstimatedShare">Estimated share of <c>Live.Output</c> (0 when it is 0).</param>
/// <param name="Agents">The agents list filtered to the available members, in snapshot order; a parent outside the set is cleared.</param>
/// <param name="GeneratingSources">The source of each generating agent, in <paramref name="Agents"/> order (the orb's agent dots).</param>
/// <param name="Today">Sum of the available members' today totals.</param>
/// <param name="MeasuredPeakOutput">Today's measured output peak of the set; null while the snapshot does not report it.</param>
/// <param name="Unavailable">Members that are disabled or not detected, in source order.</param>
public sealed record SubsetReading(
    Rate Live,
    double EstimatedShare,
    IReadOnlyList<AgentStatus> Agents,
    IReadOnlyList<SourceKind> GeneratingSources,
    TokenTotals Today,
    double? MeasuredPeakOutput,
    IReadOnlyList<SourceKind> Unavailable,
    SubsetState State);

/// <summary>Per-orb views of a snapshot (METRICS 6.8, DESIGN 2b).</summary>
public static class OrbSubset
{
    /// <summary>
    /// The reading of an orb tracking <paramref name="sources"/>; null (all sources) returns exactly the snapshot's own
    /// values. A member the snapshot does not list counts as available without data; a set without any known source is
    /// <see cref="SubsetState.AllNotFound"/>.
    /// </summary>
    public static SubsetReading Reading(SpeedSnapshot snapshot, SourceSet? sources)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (sources is not { } set)
        {
            return new SubsetReading(
                snapshot.Live,
                snapshot.LiveEstimatedShare,
                snapshot.Agents,
                GeneratingSources(snapshot.Agents),
                snapshot.Today,
                snapshot.PeakToday.Output,
                [],
                SubsetState.Available);
        }

        var included = set.Intersect(SourceSet.Everything);
        var unavailable = SourceSet.Empty;
        var anyEnabled = false;
        double liveOutput = 0, liveTotal = 0, estimated = 0;
        var today = TokenTotals.Zero;
        foreach (var status in snapshot.Sources)
        {
            if (!set.Contains(status.Kind))
            {
                continue;
            }

            anyEnabled |= status.Enabled;
            if (IsUnavailable(status))
            {
                unavailable = unavailable.With(status.Kind);
                included = included.Without(status.Kind);
                continue;
            }

            liveOutput += status.Live.Output;
            liveTotal += status.Live.Total;
            estimated += status.LiveEstimated.Output;
            today = Add(today, status.Today);
        }

        var state = !included.IsEmpty ? SubsetState.Available
            : anyEnabled || unavailable.IsEmpty ? SubsetState.AllNotFound
            : SubsetState.AllDisabled;
        var agents = Filter(snapshot.Agents, included);
        return new SubsetReading(
            new Rate(liveOutput, liveTotal),
            liveOutput > 0 ? estimated / liveOutput : 0,
            agents,
            GeneratingSources(agents),
            today,
            snapshot.SubsetPeaks.TryGetValue(set, out var peak) ? peak : null,
            [.. unavailable],
            state);
    }

    /// <summary>
    /// Whether an orb tracking <paramref name="sources"/> receives <paramref name="speedEvent"/>: completions of its own
    /// sources (all for null), and the all-sources new peak only for an all-sources orb (subset orbs use
    /// <see cref="SubsetPeakTracker"/>).
    /// </summary>
    public static bool Accepts(SpeedEvent speedEvent, SourceSet? sources) => speedEvent switch
    {
        SpeedEvent.ResponseCompleted completed => sources?.Contains(completed.Response.Source) ?? true,
        SpeedEvent.NewPeak => sources is null,
        _ => false,
    };

    /// <summary>Disabled, or not detected: no readable root and no data today.</summary>
    public static bool IsUnavailable(SourceStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return !status.Enabled || (!status.Available && status.Today.Requests == 0 && status.Live == Rate.Zero);
    }

    private static List<AgentStatus> Filter(IReadOnlyList<AgentStatus> agents, SourceSet sources)
    {
        var kept = new List<AgentStatus>(agents.Count);
        HashSet<string>? ids = null;
        foreach (var agent in agents)
        {
            if (sources.Contains(agent.Source))
            {
                kept.Add(agent);
                (ids ??= new HashSet<string>(StringComparer.Ordinal)).Add(agent.Id);
            }
        }

        for (var i = 0; i < kept.Count; i++)
        {
            if (kept[i].ParentId is { } parent && !ids!.Contains(parent))
            {
                kept[i] = kept[i] with { ParentId = null };
            }
        }

        return kept;
    }

    private static List<SourceKind> GeneratingSources(IReadOnlyList<AgentStatus> agents)
    {
        var sources = new List<SourceKind>();
        foreach (var agent in agents)
        {
            if (agent.Phase == AgentPhase.Generating)
            {
                sources.Add(agent.Source);
            }
        }

        return sources;
    }

    private static TokenTotals Add(TokenTotals a, TokenTotals b) => new(
        a.Requests + b.Requests,
        a.Input + b.Input,
        a.CachedInput + b.CachedInput,
        a.CacheWriteInput + b.CacheWriteInput,
        a.Output + b.Output,
        a.Reasoning + b.Reasoning);
}
