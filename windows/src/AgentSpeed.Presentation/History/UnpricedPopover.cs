using AgentSpeed.Core;
using AgentSpeed.Core.Unpriced;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.History;

/// <summary>One row of the unknown-prices popover (DESIGN 9 <i>Unknown prices</i>).</summary>
/// <param name="Model">The model as logged, or <c>cost.noModelName</c> when <paramref name="IsUnnamed"/>.</param>
/// <param name="Reason">The chip: <c>cost.reason.&lt;reason&gt;</c>.</param>
/// <param name="ReasonHint">The chip's tooltip: the reason's hint, for <c>unpriceable</c> followed by the table's rule.</param>
/// <param name="Via"><c>history.unpricedModels.via</c> with the provider, else empty.</param>
/// <param name="FastLabel"><c>speed.value.fast</c> when the facts were Fast (drawn after a bolt), else empty.</param>
/// <param name="Lookup"><c>history.unpricedModels.lookup</c> with the canonical key when it differs from the model, else empty.</param>
/// <param name="Tooltip">The whole second line plus the days seen.</param>
/// <param name="AccessibleValue">Reason, provider, Fast, lookup key, requests and days seen, for screen readers.</param>
public sealed record UnpricedRowView(
    string Model,
    bool IsUnnamed,
    string Requests,
    string Reason,
    string ReasonHint,
    string Via,
    string FastLabel,
    string Lookup,
    string Tooltip,
    string AccessibleValue)
{
    /// <summary>Joined by <c>·</c> after the chip: provider, Fast, the lookup key.</summary>
    public IReadOnlyList<string> Details => [.. new[] { Via, FastLabel, Lookup }.Where(d => d.Length > 0)];

    public bool Fast => FastLabel.Length > 0;
}

/// <summary>One source of the popover: its rows (at most <see cref="UnpricedPopover.MaxRows"/>) and the rest.</summary>
/// <param name="More"><c>history.unpricedModels.more</c> when rows were left out, else empty.</param>
public sealed record UnpricedGroupView(
    SourceKind Source,
    string Name,
    string Requests,
    string AccessibleName,
    IReadOnlyList<UnpricedRowView> Rows,
    string More);

/// <summary>
/// The unknown-prices popover of the History window (DESIGN 9): the count of the view's unpriced requests, the source
/// groups with their rows, the requests the current table prices now, all in the selected language.
/// </summary>
public sealed record UnpricedPopover(string Count, IReadOnlyList<UnpricedGroupView> Groups, string PricedNow)
{
    public const int MaxRows = 20;

    private const string Separator = " · ";

    public static UnpricedPopover Empty { get; } = new(string.Empty, [], string.Empty);

    public static UnpricedPopover Build(UnpricedView view, Localizer loc)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(loc);
        var groups = new List<UnpricedGroupView>();
        foreach (var group in view.Groups)
        {
            if (group.Rows.Count == 0)
            {
                continue;
            }

            var name = loc[DisplayText.SourceKey(group.Source)];
            var requests = group.RowRequests;
            var rest = group.Rows.Skip(MaxRows).ToArray();
            groups.Add(new UnpricedGroupView(
                group.Source,
                name,
                HistoryText.FullNumber(requests, loc),
                HistoryText.JoinParts([name, loc.Format("history.unpricedDay", requests)], loc),
                [.. group.Rows.Take(MaxRows).Select(r => Row(r, loc))],
                rest.Length > 0 ? loc.Format("history.unpricedModels.more", rest.Length, rest.Sum(r => r.Requests)) : string.Empty));
        }

        return new UnpricedPopover(
            HistoryText.FullNumber(view.Unpriced, loc),
            groups,
            view.PricedNow > 0 ? loc.Format("history.unpricedModels.pricedNow", view.PricedNow) : string.Empty);
    }

    /// <summary>
    /// The History window's view (METRICS 9.10 <i>Rows</i>): its stored days, today included, in <paramref name="range"/>
    /// for its source and without the sources it excludes as subscriptions, with reasons under <paramref name="current"/>.
    /// </summary>
    public static UnpricedView ViewOf(IEnumerable<DailyUsage> usage, DayRange range, SourceKind? source, SourceSet excluded,
        Core.Pricing.PriceTable current)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return UnpricedView.Build(usage.Where(d => d is not null).Select(UnpricedPart.Of), current, Label(range.Start), Label(range.End),
            source, excluded);
    }

    /// <summary>The copied text of the History window's view (METRICS 9.10 <i>Text</i>).</summary>
    public static string CopyText(UnpricedView view, UnpricedApp app, Core.Pricing.PriceTable current, HistoryRangeKind kind, DayRange range,
        SourceKind? source, SourceSet excluded) =>
        UnpricedText.Render(view, app, current, new UnpricedRange(ViewName(kind), Label(range.Start), Label(range.End), source, excluded));

    /// <summary>The copied text's <c>view</c> for a range of the History window.</summary>
    public static string ViewName(HistoryRangeKind kind) => kind switch
    {
        HistoryRangeKind.Today => "today",
        HistoryRangeKind.Yesterday => "yesterday",
        HistoryRangeKind.Days7 => "7d",
        HistoryRangeKind.Days30 => "30d",
        HistoryRangeKind.Days90 => "90d",
        HistoryRangeKind.Year => "year",
        HistoryRangeKind.All => "all",
        _ => "custom",
    };

    /// <summary>The label of a reason's chip.</summary>
    public static string ReasonLabel(UnpricedReason reason, Localizer loc) => loc["cost.reason." + UnpricedReasons.Name(reason)];

    /// <summary>The reason's hint.</summary>
    public static string ReasonHint(UnpricedReason reason, Localizer loc) => loc["cost.reason." + UnpricedReasons.Name(reason) + ".hint"];

    private static string Label(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static UnpricedRowView Row(UnpricedRow row, Localizer loc)
    {
        var unnamed = row.Fact.Model.Length == 0;
        var model = unnamed ? loc["cost.noModelName"] : row.Fact.Model;
        var reason = ReasonLabel(row.Reason, loc);
        var hint = ReasonHint(row.Reason, loc);
        if (row.Diagnosis.Rule is { Length: > 0 } rule)
        {
            hint += Environment.NewLine + rule;
        }

        var via = row.Fact.Provider is { } provider ? loc.Format("history.unpricedModels.via", provider) : string.Empty;
        var fast = row.Fact.Fast ? loc["speed.value.fast"] : string.Empty;
        var lookup = row.Diagnosis.Canonical is { } canonical && !string.Equals(canonical, row.Fact.Model, StringComparison.Ordinal)
            ? loc.Format("history.unpricedModels.lookup", canonical)
            : string.Empty;
        var details = new[] { via, fast, lookup }.Where(d => d.Length > 0).ToArray();
        var requests = HistoryText.FullNumber(row.Requests, loc);
        var seen = loc.Format("history.unpricedModels.seen", Seen(row.First, row.Last, loc));
        var line = string.Join(Separator, details.Prepend(reason));
        var accessible = HistoryText.JoinParts(
            [reason, .. details, HistoryText.Labelled(loc["history.table.requests"], requests, loc), seen], loc);
        return new UnpricedRowView(model, unnamed, requests, reason, hint, via, fast, lookup, line + Environment.NewLine + seen, accessible);
    }

    /// <summary>The first and last day in the short date format, one date when equal.</summary>
    private static string Seen(string first, string last, Localizer loc)
    {
        if (!HistoryRanges.TryParseDay(first, out var start) || !HistoryRanges.TryParseDay(last, out var end))
        {
            return first == last ? first : first + " – " + last;
        }

        return start == end ? HistoryText.Date(start, loc) : HistoryText.DateSpan(start, end, loc);
    }
}
