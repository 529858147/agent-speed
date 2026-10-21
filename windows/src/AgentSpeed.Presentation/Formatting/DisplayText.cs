using AgentSpeed.Core;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Formatting;

/// <summary>User-visible text built from live data. Never includes conversation content.</summary>
public static class DisplayText
{
    public static string TierKey(SpeedTier tier) => tier switch
    {
        SpeedTier.Cruise => "tier.cruise",
        SpeedTier.Boost => "tier.boost",
        SpeedTier.Overdrive => "tier.overdrive",
        SpeedTier.Redline => "tier.redline",
        _ => "tier.idle",
    };

    public static string TierName(SpeedTier tier, Localizer localizer) => localizer[TierKey(tier)];

    /// <summary>Tier label as drawn on the orb: uppercase for Latin scripts.</summary>
    public static string TierLabel(SpeedTier tier, Localizer localizer)
    {
        var name = TierName(tier, localizer);
        return localizer.UsesLatinScript ? name.ToUpper(localizer.Culture) : name;
    }

    public static string SourceKey(SourceKind source) => source switch
    {
        SourceKind.Codex => "source.codex",
        SourceKind.ClaudeCode => "source.claudeCode",
        _ => "source.claudeDesktop",
    };

    public static string PhaseKey(AgentPhase phase) => phase switch
    {
        AgentPhase.Generating => "phase.generating",
        AgentPhase.Tools => "phase.tools",
        _ => "phase.idle",
    };

    /// <summary><c>128 tok/s · 加速</c></summary>
    public static string SpeedLine(double value, SpeedTier tier, Localizer localizer) =>
        $"{SpeedFormat.Speed(value)} {localizer["unit.tokS"]} · {TierName(tier, localizer)}";

    /// <summary><c>每秒 128 個 token，加速</c></summary>
    public static string Accessibility(double value, SpeedTier tier, Localizer localizer) =>
        localizer.Format("a11y.orb", SpeedFormat.Speed(value), TierName(tier, localizer));

    /// <summary><c>今日 1.2M · 3 個 agent</c></summary>
    public static string TodaySummary(SpeedSnapshot snapshot, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return localizer.Format("today.summary", SpeedFormat.Tokens(snapshot.Today.Output), snapshot.Agents.Count);
    }

    /// <summary><c>生成中 3 · 工具 1</c></summary>
    public static string AgentContext(SpeedSnapshot snapshot, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return localizer.Format("agents.context", snapshot.GeneratingCount, snapshot.ToolsCount);
    }

    public static string TimeAgo(TimeSpan elapsed, Localizer localizer)
    {
        if (elapsed < TimeSpan.FromSeconds(5))
        {
            return localizer["time.justNow"];
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return localizer.Format("time.secondsAgo", (int)elapsed.TotalSeconds);
        }

        return localizer.Format("time.minutesAgo", (int)elapsed.TotalMinutes);
    }

    public static string Warning(MonitorWarning warning, Localizer localizer) => warning switch
    {
        MonitorWarning.PartialHistory => localizer["warning.partialHistory"],
        MonitorWarning.UnreadableFiles u => localizer.Format("warning.unreadable", u.Count),
        MonitorWarning.MalformedLines m => localizer.Format("warning.malformed", m.Count),
        MonitorWarning.UnfinalizedUsage u => localizer.Format("warning.unfinalized", u.Count),
        _ => string.Empty,
    };
}
