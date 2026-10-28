using AgentSpeed.Core;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Formatting;

public enum TaskbarActivity { Reading, Unavailable, Generating, Tools, Waiting, Idle }

/// <summary>Visible taskbar state, derived from the existing snapshot without doing any I/O.</summary>
public sealed record TaskbarReadout(string Value, bool HideUnit, bool Muted, TaskbarActivity Activity, string Status)
{
    public static TaskbarReadout From(SpeedSnapshot snapshot, SpeedMetric metric, Localizer loc)
    {
        var value = snapshot.Live.ValueFor(metric);
        var readable = !DisplayText.IsNegligibleSpeed(value);
        var activity = snapshot.State.Kind switch
        {
            MonitorStateKind.Starting => TaskbarActivity.Reading,
            MonitorStateKind.NoSources => TaskbarActivity.Unavailable,
            _ when readable => TaskbarActivity.Generating,
            _ when snapshot.GeneratingCount > 0 => TaskbarActivity.Waiting,
            _ when snapshot.ToolsCount > 0 => TaskbarActivity.Tools,
            _ when HasReadWarning(snapshot) => TaskbarActivity.Unavailable,
            _ => TaskbarActivity.Idle,
        };
        var status = loc[activity switch
        {
            TaskbarActivity.Reading => "win.taskbar.reading",
            TaskbarActivity.Unavailable => "win.taskbar.unavailable",
            TaskbarActivity.Generating => "phase.generating",
            TaskbarActivity.Tools => "win.taskbar.tools",
            TaskbarActivity.Waiting => "win.taskbar.waiting",
            _ => "phase.idle",
        }];
        var text = activity switch
        {
            TaskbarActivity.Reading or TaskbarActivity.Tools or TaskbarActivity.Waiting => status,
            TaskbarActivity.Unavailable => CostDisplay.Dash,
            TaskbarActivity.Idle => "0",
            _ => SpeedFormat.Speed(value),
        };
        return new TaskbarReadout(text,
            activity is TaskbarActivity.Reading or TaskbarActivity.Tools or TaskbarActivity.Waiting,
            activity != TaskbarActivity.Generating, activity, status);
    }

    private static bool HasReadWarning(SpeedSnapshot snapshot) => snapshot.Warnings.Any(w =>
        w is MonitorWarning.UnreadableFiles { Count: > 0 } or MonitorWarning.MalformedLines { Count: > 0 }
            or MonitorWarning.UnsupportedFormat);
}

public sealed record TaskbarDetailRow(string Label, string Value);

/// <summary>Built only while the taskbar tooltip is visible. Values follow the same cost filters as the dashboard.</summary>
public sealed record TaskbarDetails(
    string Primary, string Status, string Measurement, IReadOnlyList<TaskbarDetailRow> Sources,
    string TodayLabel, string Today, string LiveLabel, string Live,
    string SpeedScope, string CostScope, string Subscriptions, string Notice, string Hint, string Actions, string Traffic = "")
{
    public static TaskbarDetails From(SpeedSnapshot snapshot, AppSettings settings, CostDisplay cost)
    {
        var loc = cost.Localizer;
        var readout = TaskbarReadout.From(snapshot, settings.Metric, loc);
        var metric = loc[settings.Metric == SpeedMetric.Output ? "win.taskbar.output" : "win.taskbar.total"];
        var primary = readout.HideUnit ? readout.Value : $"{readout.Value} {loc["unit.tokS"]}";
        primary += $" · {metric}";
        var estimated = settings.Metric == SpeedMetric.Output ? snapshot.LiveEstimatedShare > 0
            : snapshot.Sources.Any(s => s.Enabled && s.LiveEstimated.Total > 0);
        var measurement = readout.Activity is TaskbarActivity.Reading or TaskbarActivity.Unavailable
            ? string.Empty : loc["win.taskbar.window"] + (estimated ? $" · {loc["win.taskbar.estimated"]}" : string.Empty);
        var active = loc.Format("win.taskbar.activity", snapshot.GeneratingCount, snapshot.ToolsCount);
        if (readout.Activity is not TaskbarActivity.Generating) active = $"{readout.Status} · {active}";
        var sources = snapshot.Sources.Where(s => s.Enabled && (s.ActiveAgents > 0 || !DisplayText.IsNegligibleSpeed(s.Live.ValueFor(settings.Metric))))
            .OrderByDescending(s => s.Live.ValueFor(settings.Metric)).ThenBy(s => s.Kind)
            .Select(s => new TaskbarDetailRow(loc[DisplayText.SourceKey(s.Kind)],
                $"{SpeedFormat.Speed(s.Live.ValueFor(settings.Metric))} {loc["unit.tokS"]}")).ToArray();
        var summary = TodayCostSummary.From(snapshot, cost);
        var money = cost.Live(snapshot);
        var live = readout.Activity is TaskbarActivity.Reading or TaskbarActivity.Unavailable
            ? CostDisplay.Dash : loc.Format("cost.unitPerSecond", money.InlineText);
        var costSources = summary.Parts.Select(p => p.Source).ToArray();
        var counted = costSources.Length > 0 ? Names(costSources, loc) : loc["win.taskbar.noCostSources"];
        var subscriptions = cost.Settings.Subscriptions;
        var subscriptionText = !subscriptions.Any() ? loc["win.taskbar.subscriptions.none"]
            : loc.Format(cost.Settings.IncludeSubscriptions ? "win.taskbar.subscriptions.included" : "win.taskbar.subscriptions.excluded", Names(subscriptions, loc));
        var notices = snapshot.Warnings.Select(w => DisplayText.Warning(w, loc)).Where(t => t.Length > 0).ToList();
        if (snapshot.State.Kind == MonitorStateKind.NoSources) notices.Insert(0, loc["win.taskbar.noSources"]);
        if (summary.UnpricedText is { } unpriced) notices.Add(unpriced);
        if (summary.RateLine is { } rate) notices.Add(rate);
        return new TaskbarDetails(primary, active, measurement, sources,
            loc["win.taskbar.today"], summary.AmountText, loc["win.taskbar.live"], live,
            loc["win.taskbar.speedScope"], loc.Format("win.taskbar.costScope", counted), subscriptionText,
            string.Join("\n", notices), loc["win.taskbar.costHint"], loc["win.taskbar.actions"]);
    }

    private static string Names(IEnumerable<SourceKind> sources, Localizer loc) =>
        string.Join(loc["list.separator"], sources.Select(s => loc[DisplayText.SourceKey(s)]));
}
