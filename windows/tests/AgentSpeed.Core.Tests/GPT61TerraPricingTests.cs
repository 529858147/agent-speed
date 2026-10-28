using AgentSpeed.Core.Parsing;
using AgentSpeed.Core.Pricing;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tests;

public sealed class GPT61TerraPricingTests
{
    private const long At = 1_793_059_200_000; // 2026-10-27 UTC

    [Theory]
    [InlineData("gpt-6.1-terra")]
    [InlineData("openai/gpt-6.1-terra")]
    [InlineData("gpt-6.1-terra-fast")]
    public void CanonicalAndRoutedNamesAreReportable(string model)
    {
        Assert.Equal("gpt-6.1-terra", PriceBook.Bundled.ReportModelId(model, "openai"));
    }

    [Theory]
    [InlineData(1000, 0, 0, 100, 1500)]
    [InlineData(1000, 1000, 0, 0, 50)]
    [InlineData(1000, 0, 1000, 0, 1250)]
    [InlineData(1000, 800, 100, 200, 1265)]
    [InlineData(272000, 0, 0, 2, 272010)]
    [InlineData(272001, 0, 0, 2, 544017)]
    [InlineData(272001, 272000, 0, 2, 27217)]
    [InlineData(400000, 200000, 100000, 200, 471500)]
    public void StandardAndLongContextUseSeparateTokenClasses(long input, long cached, long write, long output, long expected)
    {
        var usage = new TokenCounts(Input: input, CachedInput: cached, CacheWriteInput: write, Output: output);
        Assert.Equal(expected, PriceBook.Bundled.Cost("gpt-6.1-terra", "openai", usage, At, fast: false));
    }

    [Theory]
    [InlineData(1000, 800, 100, 200, 2530)]
    [InlineData(400000, 200000, 100000, 200, 943000)]
    public void FastFlagAndSuffixUseTheSameDoubleRate(long input, long cached, long write, long output, long expected)
    {
        var usage = new TokenCounts(Input: input, CachedInput: cached, CacheWriteInput: write, Output: output);
        Assert.Equal(expected, PriceBook.Bundled.Cost("gpt-6.1-terra", "openai", usage, At, fast: true));
        Assert.Equal(expected, PriceBook.Bundled.Cost("gpt-6.1-terra-fast", "openai", usage, At, fast: false));
    }

    [Fact]
    public void EarlierTerraAndUnrecognizedNamesRemainDistinct()
    {
        var table = PriceBook.Bundled;
        Assert.Equal("gpt-5.6-terra", table.ReportModelId("gpt-5.6-terra", "openai"));
        Assert.Equal(3200, table.Cost("gpt-5.6-terra", "openai", new TokenCounts(Input: 1000, Output: 100), At, fast: false));
        Assert.Equal(ReportModelIds.Unknown, table.ReportModelId("gpt-6.1-terra-unpublished", "openai"));
        Assert.Null(table.Cost("gpt-6.1-terra-unpublished", "openai", new TokenCounts(Input: 1000), At, fast: false));
    }
}
