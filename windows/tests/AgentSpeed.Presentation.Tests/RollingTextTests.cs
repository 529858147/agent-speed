using AgentSpeed.Presentation.Orb;

namespace AgentSpeed.Presentation.Tests;

public class RollingTextTests
{
    [Fact]
    public void StartsEmpty()
    {
        var text = new RollingText();
        Assert.Equal(string.Empty, text.Current);
        Assert.Equal(0, text.SlotCount);
        Assert.False(text.IsAnimating(0));
        Assert.Equal(new RollingSlot(null, null, 1, 1), text.Slot(0, 0));
    }

    [Fact]
    public void SlotsAreAlignedFromTheRight()
    {
        var text = new RollingText();
        text.Set("99", 1, 0, animate: false);
        text.Set("100", 1, 10, animate: true);

        Assert.Equal(3, text.SlotCount);
        var units = text.Slot(0, 10);
        Assert.Equal('0', units.Current);
        Assert.Equal('9', units.Previous);
        var hundreds = text.Slot(2, 10);
        Assert.Equal('1', hundreds.Current);
        Assert.Null(hundreds.Previous);
        Assert.True(hundreds.IsChanging);
        Assert.Null(text.Slot(3, 10).Current);
    }

    [Fact]
    public void UnchangedCharactersDoNotRoll()
    {
        var text = new RollingText();
        text.Set("128", 1, 0, animate: false);
        text.Set("138", 1, 5, animate: true);

        var units = text.Slot(0, 5);
        Assert.Equal(1, units.Progress);
        Assert.False(units.IsChanging);

        var tens = text.Slot(1, 5);
        Assert.Equal('3', tens.Current);
        Assert.Equal('2', tens.Previous);
        Assert.True(tens.IsChanging);

        Assert.False(text.Slot(2, 5).IsChanging);
    }

    [Fact]
    public void SlotsRollRightMostFirstWithStagger()
    {
        var text = new RollingText();
        text.Set("111", 1, 0, animate: false);
        text.Set("222", 1, 1, animate: true);

        Assert.Equal(0, text.Slot(0, 1).Progress);
        Assert.Equal(0.5, text.Slot(0, 1 + RollingText.Duration / 2).Progress, 9);
        Assert.True(text.Slot(1, 1 + RollingText.Duration / 2).Progress < text.Slot(0, 1 + RollingText.Duration / 2).Progress);

        Assert.Equal(1, text.Slot(0, 1 + RollingText.Duration).Progress, 9);
        Assert.True(text.Slot(2, 1 + RollingText.Duration).IsChanging);
        Assert.False(text.Slot(2, 1 + RollingText.Duration + 2 * RollingText.Stagger + 1e-9).IsChanging);
    }

    [Fact]
    public void IsAnimatingCoversDurationPlusStagger()
    {
        var text = new RollingText();
        text.Set("1", 1, 0, animate: false);
        text.Set("1.5", 1, 2, animate: true);

        Assert.True(text.IsAnimating(2));
        Assert.True(text.IsAnimating(2 + RollingText.Duration + 2 * RollingText.Stagger));
        Assert.False(text.IsAnimating(2 + RollingText.Duration + 3 * RollingText.Stagger + 1e-9));
    }

    [Fact]
    public void NonAnimatedSetSettlesImmediately()
    {
        var text = new RollingText();
        text.Set("42", 1, 0, animate: false);
        text.Set("43", -1, 0, animate: false);
        Assert.Equal("43", text.Current);
        Assert.False(text.IsAnimating(0));
        Assert.False(text.Slot(0, 0).IsChanging);
        Assert.Equal(-1, text.Slot(0, 0).Direction);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, -1)]
    public void DirectionIsNormalized(int direction, int expected)
    {
        var text = new RollingText();
        text.Set("1", direction, 0, animate: true);
        Assert.Equal(expected, text.Slot(0, 0).Direction);
    }

    [Fact]
    public void SettingTheSameTextDoesNotRestart()
    {
        var text = new RollingText();
        text.Set("7", 1, 0, animate: true);
        text.Set("7", 1, 100, animate: true);
        Assert.Equal(1, text.Slot(0, RollingText.Duration).Progress);
        Assert.False(text.IsAnimating(100));
    }

    [Fact]
    public void NullTextThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new RollingText().Set(null!, 1, 0, animate: true));
    }
}
