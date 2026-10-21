using System.Text.Json;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests;

public class SourceAndWindowSettingsTests
{
    private static readonly string OpenCode = Path.Combine(Path.GetTempPath(), "opencode");
    private static readonly string Gemini = Path.Combine(Path.GetTempPath(), ".gemini", "tmp");

    [Fact]
    public void EverySourceKindHasItsOwnToggle()
    {
        foreach (var kind in Enum.GetValues<SourceKind>())
        {
            Assert.True(AppSettings.Default.IsSourceEnabled(kind), $"{kind} should default to on");
            var off = AppSettings.Default.WithSourceEnabled(kind, false);
            Assert.False(off.IsSourceEnabled(kind));
            Assert.All(Enum.GetValues<SourceKind>().Where(k => k != kind), other => Assert.True(off.IsSourceEnabled(other)));
            Assert.True(off.WithSourceEnabled(kind, true).IsSourceEnabled(kind));
            Assert.DoesNotContain(kind, off.ToMonitorConfiguration().EnabledSources);
        }

        Assert.True(AppSettings.Default.ToMonitorConfiguration().EnabledSources.SetEquals(Enum.GetValues<SourceKind>()));
    }

    [Fact]
    public void OpenCodeAndGeminiFoldersReachTheMonitorAndRoundTrip()
    {
        var settings = AppSettings.Default.WithSourceEnabled(SourceKind.OpenCode, false) with { OpenCodeRoot = OpenCode, GeminiRoot = Gemini };
        var config = settings.ToMonitorConfiguration();
        Assert.Equal(OpenCode, config.OpenCodeRoot);
        Assert.Equal(Gemini, config.GeminiRoot);
        Assert.NotEqual(AppSettings.Default.ToMonitorConfiguration(), (AppSettings.Default with { GeminiRoot = Gemini }).ToMonitorConfiguration());

        var json = SettingsSerializer.Serialize(settings);
        using (var document = JsonDocument.Parse(json))
        {
            var sources = document.RootElement.GetProperty("sources");
            Assert.False(sources.GetProperty("opencode").GetBoolean());
            Assert.True(sources.GetProperty("gemini").GetBoolean());
            Assert.Equal(OpenCode, sources.GetProperty("opencodeRoot").GetString());
            Assert.Equal(Gemini, sources.GetProperty("geminiRoot").GetString());
        }

        var back = SettingsSerializer.Deserialize(json);
        Assert.False(back.OpenCodeEnabled);
        Assert.True(back.GeminiEnabled);
        Assert.Equal(OpenCode, back.OpenCodeRoot);
        Assert.Equal(Gemini, back.GeminiRoot);
    }

    [Fact]
    public void OlderFilesKeepOpenCodeAndGeminiOnWithDefaultFolders()
    {
        var s = SettingsSerializer.Deserialize("""{ "version": 1, "sources": { "codex": true, "deepseek": false } }""");
        Assert.True(s.OpenCodeEnabled);
        Assert.True(s.GeminiEnabled);
        Assert.Null(s.OpenCodeRoot);
        Assert.Null(s.GeminiRoot);
        Assert.Null(s.HistoryWindow);
    }

    [Theory]
    [InlineData("""{ "sources": { "opencode": "no", "gemini": 0, "opencodeRoot": 5, "geminiRoot": "relative/tmp" } }""")]
    [InlineData("""{ "sources": { "opencode": null, "gemini": [], "opencodeRoot": null, "geminiRoot": "" } }""")]
    public void InvalidSourceValuesFallBackPerField(string json)
    {
        var s = SettingsSerializer.Deserialize(json);
        Assert.True(s.OpenCodeEnabled);
        Assert.True(s.GeminiEnabled);
        Assert.Null(s.OpenCodeRoot);
        Assert.Null(s.GeminiRoot);
    }

    [Fact]
    public void HistoryWindowFrameRoundTrips()
    {
        var frame = new WindowFrame(-1920, 40.5, 920, 680, Maximized: false);
        var json = SettingsSerializer.Serialize(AppSettings.Default with { HistoryWindow = frame });
        using (var document = JsonDocument.Parse(json))
        {
            var window = document.RootElement.GetProperty("history").GetProperty("window");
            Assert.Equal(-1920, window.GetProperty("left").GetDouble());
            Assert.Equal(680, window.GetProperty("height").GetDouble());
            Assert.False(window.GetProperty("maximized").GetBoolean());
        }

        Assert.Equal(frame, SettingsSerializer.Deserialize(json).HistoryWindow);

        using var defaults = JsonDocument.Parse(SettingsSerializer.Serialize(AppSettings.Default));
        Assert.Equal(JsonValueKind.Null, defaults.RootElement.GetProperty("history").GetProperty("window").ValueKind);
        Assert.Null(SettingsSerializer.Deserialize(SettingsSerializer.Serialize(AppSettings.Default)).HistoryWindow);
    }

    [Theory]
    [InlineData(0, 0, 719, 680)]
    [InlineData(0, 0, 920, 539)]
    [InlineData(0, 0, 20_000, 680)]
    [InlineData(0, 0, 920, 20_000)]
    [InlineData(100_001, 0, 920, 680)]
    [InlineData(0, -100_001, 920, 680)]
    [InlineData(double.NaN, 0, 920, 680)]
    [InlineData(0, double.PositiveInfinity, 920, 680)]
    [InlineData(0, 0, double.NaN, 680)]
    public void InvalidHistoryFramesAreDropped(double left, double top, double width, double height)
    {
        var settings = AppSettings.Default with { HistoryWindow = new WindowFrame(left, top, width, height, false) };
        Assert.Null(settings.Validated().HistoryWindow);
        Assert.Null(SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings)).HistoryWindow);
    }

    [Theory]
    [InlineData(720, 540)]
    [InlineData(16_384, 16_384)]
    public void HistoryFrameLimitsAreInclusive(double width, double height)
    {
        var frame = new WindowFrame(-100_000, 100_000, width, height, true);
        Assert.Equal(frame, (AppSettings.Default with { HistoryWindow = frame }).Validated().HistoryWindow);
    }

    [Theory]
    [InlineData("""{ "history": { "window": { "left": 10, "top": 10, "width": 920 } } }""")]
    [InlineData("""{ "history": { "window": { "left": "10", "top": 10, "width": 920, "height": 680 } } }""")]
    [InlineData("""{ "history": { "window": [] } }""")]
    [InlineData("""{ "history": { "window": null } }""")]
    [InlineData("""{ "history": [] }""")]
    [InlineData("""{ "history": 3 }""")]
    public void MalformedHistoryFramesAreIgnored(string json)
    {
        Assert.Null(SettingsSerializer.Deserialize(json).HistoryWindow);
    }

    [Fact]
    public void MissingMaximizedDefaultsToFalse()
    {
        var s = SettingsSerializer.Deserialize("""{ "history": { "window": { "left": 10, "top": 20, "width": 900, "height": 600 } } }""");
        Assert.Equal(new WindowFrame(10, 20, 900, 600, false), s.HistoryWindow);
    }
}
