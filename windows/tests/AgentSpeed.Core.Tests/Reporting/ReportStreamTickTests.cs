using AgentSpeed.Core.Reporting;
using AgentSpeed.Core.Traffic;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>
/// What a WebSocket tick hands the stream scheduler (METRICS 11.9): the changed rule — a ledger increment at the tick, or a
/// value the body carries that differs from the last built report's once rounded — and the clock correction's input.
/// </summary>
public sealed class ReportStreamTickTests
{
    private static readonly ReportModelKey Gpt = new("gpt-5.5", SourceKind.Codex);

    private static ReportSample Sample(ReportCounters counters, long epoch = 3, TrafficReportTotals? traffic = null) => ReportSample.Empty with
    {
        Facts = ReportLiveFacts.Empty with { Counters = counters, Epoch = epoch },
        Traffic = ReportTrafficSample.Off with { Status = ReportTrafficStatus.Ok, Totals = traffic ?? TrafficReportTotals.Zero },
    };

    private static TrafficReportTotals Traffic(long rx) =>
        new(1, new Dictionary<SourceKind, TrafficSplit> { [SourceKind.Codex] = new(new TrafficBytes(rx, 10), TrafficBytes.Zero) });

    [Fact]
    public void ATokenIncrementSinceThePreviousTickIsAChange()
    {
        var counters = new ReportCounters();
        counters.Add(new ReportUsage(1, 40), Gpt);
        var first = Sample(counters.Copy());
        var window = new ReportWindow(first);
        Assert.False(window.HasIncrement(first, traffic: false));

        counters.Add(new ReportUsage(0, 25), Gpt);
        var grown = Sample(counters.Copy());
        Assert.True(window.HasIncrement(grown, traffic: false));
        window.Observe(grown);
        Assert.False(window.HasIncrement(grown, traffic: false));
        Assert.False(window.HasIncrement(Sample(counters.Copy()), traffic: false));
    }

    [Fact]
    public void ACostThatArrivesLaterIsAChangeButNotActivity()
    {
        var counters = new ReportCounters();
        counters.Add(new ReportUsage(1, 40, UnpricedRequests: 1), Gpt);
        var window = new ReportWindow(Sample(counters.Copy()));
        counters.Add(new ReportUsage(CostMicroUsd: 900), Gpt);
        var priced = Sample(counters.Copy());
        Assert.True(window.HasIncrement(priced, traffic: false));
        Assert.False(window.HasTokenIncrement(priced));
    }

    [Fact]
    public void ANewEngineWithCountsIsAChange()
    {
        var window = new ReportWindow(Sample(new ReportCounters(), epoch: 3));
        Assert.False(window.HasIncrement(Sample(new ReportCounters(), epoch: 4), traffic: false));
        var counters = new ReportCounters();
        counters.Add(new ReportUsage(1, 5), Gpt);
        Assert.True(window.HasIncrement(Sample(counters, epoch: 4), traffic: false));
    }

    [Fact]
    public void AgentTrafficCountsOnlyWhenTheReportCarriesIt()
    {
        var counters = new ReportCounters();
        var window = new ReportWindow(Sample(counters, traffic: Traffic(100)));
        var moved = Sample(counters, traffic: Traffic(160));
        Assert.True(window.HasIncrement(moved, traffic: true));
        Assert.False(window.HasIncrement(moved, traffic: false));
        window.Observe(moved);
        Assert.False(window.HasIncrement(Sample(counters, traffic: Traffic(160)), traffic: true));
    }

    [Fact]
    public void ValuesAreComparedAsTheBodyRoundsThem()
    {
        var agents = new Dictionary<SourceKind, ReportAgentCounts> { [SourceKind.Codex] = new(1, 0, 2), [SourceKind.ClaudeCode] = new(0, 1, 1) };
        ReportSample Live(double output, double total, double share) => ReportSample.Empty with
        {
            Live = new Rate(output, total),
            EstimatedShare = share,
            Facts = ReportLiveFacts.Empty with { Agents = agents },
        };

        var values = ReportStreamValues.Of(Live(123.44, 4567.81, 0.12345));
        Assert.Equal(new ReportStreamValues(123.4, 4567.8, 0.123, 1, 1, 3), values);
        Assert.Equal(values, ReportStreamValues.Of(Live(123.36, 4567.76, 0.1234)));
        Assert.NotEqual(values, ReportStreamValues.Of(Live(123.46, 4567.8, 0.123)));
        Assert.Equal(new ReportStreamValues(0, 0, 0, 1, 1, 3), ReportStreamValues.Of(Live(double.NaN, -4, double.PositiveInfinity)));
    }

    [Theory]
    [InlineData("""{"error":{"code":"timestamp_out_of_window","serverTime":1792000000}}""", 1_792_000_000L)]
    [InlineData("""{"error":{"code":"timestamp_out_of_window"}}""", null)]
    [InlineData("""{"error":{"code":"timestamp_out_of_window","serverTime":-5}}""", null)]
    [InlineData("""{"error":{"code":"timestamp_out_of_window","serverTime":"1792000000"}}""", null)]
    public void ARefusedHandshakeCarriesTheServersClock(string body, long? serverTime)
    {
        var rejection = ReportStreamRejection.Classify(401, ReportSpec.Utf8(body), null);
        Assert.Equal("timestamp_out_of_window", rejection.Code);
        Assert.Equal(serverTime, rejection.ServerTime);
    }
}
