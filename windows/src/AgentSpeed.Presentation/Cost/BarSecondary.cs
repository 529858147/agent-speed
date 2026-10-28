using AgentSpeed.Core;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Cost;

/// <summary>One line of the taskbar money block: label, number, unit.</summary>
/// <param name="Dim">Nothing priced or zero: the number takes the secondary color.</param>
public sealed record BarMoneyLine(string Label, string Number, string Unit, bool Dim);

/// <summary>
/// The taskbar cost block: a compact daily estimate or the original live-rate + daily-total pair.
/// Both use the dashboard's billable sources and preserve the lower-bound / unknown-price rules.
/// </summary>
/// <param name="Spoken"><c>a11y.bar.money</c>: read after the value by UI Automation and the tooltip.</param>
public sealed record BarSecondary(BarMoneyLine Live, BarMoneyLine Today, string Spoken)
{
    public bool Compact { get; init; }
    private static readonly BarMoneyLine HiddenLive = new(string.Empty, string.Empty, string.Empty, true);

    /// <summary>
    /// Null unless compact or secondary costs are selected alongside tokens, with a visible value.
    /// </summary>
    public static BarSecondary? From(AppSettings settings, SpeedSnapshot snapshot, CostDisplay cost)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(cost);
        if ((!settings.BarSecondary && !settings.BarCompact) || settings.Cost.LiveUnit != LiveUnit.Tokens || settings.BarMode == BarDisplayMode.GlyphOnly)
        {
            return null;
        }

        var loc = cost.Localizer;
        var summary = TodayCostSummary.From(snapshot, cost);
        var unknown = summary.Total.IsUnknown(summary.Requests);
        var atLeast = summary.Requests > 0 && summary.Total.UnpricedRequests > 0;
        if (settings.BarCompact)
        {
            var text = summary.AmountText;
            if (!unknown && !atLeast && summary.Requests > 0) text = "≈" + text;
            var compact = new BarMoneyLine(loc["bar.money.today"], text, string.Empty,
                unknown || summary.Total.CostMicroUsd == 0);
            return new BarSecondary(HiddenLive, compact, loc.Format("win.taskbar.today.spoken", summary.Spoken)) { Compact = true };
        }

        var money = cost.Live(snapshot);
        var live = new BarMoneyLine(
            loc["bar.money.now"],
            money.Unknown ? CostDisplay.Dash : (money.AtLeast ? CostDisplay.AtLeastSign : string.Empty) + money.Number,
            money.Unit,
            money.Unknown || money.Value == 0);
        var amount = cost.Amount(summary.Requests == 0 ? 0 : summary.Total.CostMicroUsd).Number;
        var today = new BarMoneyLine(
            loc["bar.money.today"],
            unknown ? CostDisplay.Dash : (atLeast ? CostDisplay.AtLeastSign : string.Empty) + amount,
            cost.Symbol,
            unknown || summary.Total.CostMicroUsd == 0);
        return new BarSecondary(live, today, loc.Format("a11y.bar.money", money.SpokenAmount, summary.Spoken));
    }
}
