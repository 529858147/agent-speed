using System.Globalization;

namespace AgentSpeed.Core.Tests;

public sealed class SpeedFormatTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(4.24, "4.2")]
    [InlineData(9.96, "10")]
    [InlineData(128.4, "128")]
    [InlineData(999.6, "1.00k")]
    [InlineData(1284, "1.28k")]
    [InlineData(9999, "10.0k")]
    [InlineData(12840, "12.8k")]
    [InlineData(128400, "128k")]
    [InlineData(999700, "1.00M")]
    [InlineData(1284000, "1.28M")]
    public void FormatsSpeed(double value, string expected)
    {
        Assert.Equal(expected, SpeedFormat.Speed(value));
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1200000, "1.20M")]
    [InlineData(384000, "384k")]
    [InlineData(1000, "1.00k")]
    [InlineData(12800, "12.8k")]
    public void FormatsTokens(long value, string expected)
    {
        Assert.Equal(expected, SpeedFormat.Tokens(value));
    }

    [Fact]
    public void FormattingIgnoresCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("4.2", SpeedFormat.Speed(4.24));
            Assert.Equal("1.28k", SpeedFormat.Speed(1284));
            Assert.Equal("1.20M", SpeedFormat.Tokens(1_200_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
