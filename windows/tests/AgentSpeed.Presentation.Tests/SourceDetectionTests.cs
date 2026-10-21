using AgentSpeed.Core;
using AgentSpeed.Presentation.Layout;

namespace AgentSpeed.Presentation.Tests;

public sealed class SourceDetectionTests
{
    [Theory]
    [InlineData(true, true, 0, 0, true)]
    [InlineData(false, true, 0, 0, true)]
    [InlineData(true, false, 0, 0, false)]
    [InlineData(false, false, 0, 0, false)]
    [InlineData(true, false, 2, 0, true)]
    [InlineData(true, false, 0, 5, true)]
    public void OnlyDetectedSourcesAreListed(bool enabled, bool available, int sessions, long requests, bool listed)
    {
        var status = new SourceStatus(
            SourceKind.DeepSeek, enabled, "/x/.dsh/sessions", available, sessions, Rate.Zero,
            new TokenTotals(Requests: requests), 0);

        Assert.Equal(listed, SourceDetection.IsDetected(status));
    }

    [Fact]
    public void NullStatusIsRejected() => Assert.Throws<ArgumentNullException>(() => SourceDetection.IsDetected(null!));
}
