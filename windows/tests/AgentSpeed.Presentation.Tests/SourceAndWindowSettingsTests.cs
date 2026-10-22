using System.Text.Json;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests;

public class SourceAndWindowSettingsTests
{
    private static readonly string OpenCode = Path.Combine(Path.GetTempPath(), "opencode");
    private static readonly string Gemini = Path.Combine(Path.GetTempPath(), ".gemini", "tmp");
    private static readonly string Omp = Path.Combine(Path.GetTempPath(), ".omp", "profiles", "work", "agent", "sessions");
    private static readonly string Antigravity = Path.Combine(Path.GetTempPath(), ".gemini", "antigravity-cli");

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

    [Fact]
    public void OmpAndAntigravityFoldersReachTheMonitorAndRoundTrip()
    {
        var settings = AppSettings.Default.WithSourceEnabled(SourceKind.Omp, false) with { OmpRoot = Omp, AntigravityRoot = Antigravity };
        var config = settings.ToMonitorConfiguration();
        Assert.DoesNotContain(SourceKind.Omp, config.EnabledSources);
        Assert.Contains(SourceKind.Antigravity, config.EnabledSources);
        Assert.Equal(Omp, config.OmpRoot);
        Assert.Equal(Antigravity, config.AntigravityRoot);
        Assert.NotEqual(AppSettings.Default.ToMonitorConfiguration(), (AppSettings.Default with { OmpRoot = Omp }).ToMonitorConfiguration());
        Assert.NotEqual(AppSettings.Default.ToMonitorConfiguration(),
            (AppSettings.Default with { AntigravityRoot = Antigravity }).ToMonitorConfiguration());

        var json = SettingsSerializer.Serialize(settings);
        using (var document = JsonDocument.Parse(json))
        {
            var sources = document.RootElement.GetProperty("sources");
            Assert.False(sources.GetProperty("omp").GetBoolean());
            Assert.True(sources.GetProperty("antigravity").GetBoolean());
            Assert.Equal(Omp, sources.GetProperty("ompRoot").GetString());
            Assert.Equal(Antigravity, sources.GetProperty("antigravityRoot").GetString());
        }

        var back = SettingsSerializer.Deserialize(json);
        Assert.False(back.OmpEnabled);
        Assert.True(back.AntigravityEnabled);
        Assert.Equal(Omp, back.OmpRoot);
        Assert.Equal(Antigravity, back.AntigravityRoot);

        using var defaults = JsonDocument.Parse(SettingsSerializer.Serialize(AppSettings.Default));
        var defaultSources = defaults.RootElement.GetProperty("sources");
        Assert.True(defaultSources.GetProperty("omp").GetBoolean());
        Assert.True(defaultSources.GetProperty("antigravity").GetBoolean());
        Assert.Equal(JsonValueKind.Null, defaultSources.GetProperty("ompRoot").ValueKind);
        Assert.Equal(JsonValueKind.Null, defaultSources.GetProperty("antigravityRoot").ValueKind);
    }

    [Fact]
    public void OlderFilesKeepOmpAndAntigravityOnWithDefaultFolders()
    {
        var s = SettingsSerializer.Deserialize("""
            { "version": 1, "sources": { "codex": true, "opencode": false, "gemini": true, "geminiRoot": null } }
            """);
        Assert.True(s.OmpEnabled);
        Assert.True(s.AntigravityEnabled);
        Assert.Null(s.OmpRoot);
        Assert.Null(s.AntigravityRoot);
        Assert.False(s.OpenCodeEnabled);

        var config = s.ToMonitorConfiguration();
        Assert.Contains(SourceKind.Omp, config.EnabledSources);
        Assert.Contains(SourceKind.Antigravity, config.EnabledSources);
        Assert.Null(config.OmpRoot);
        Assert.Null(config.AntigravityRoot);
    }

    [Fact]
    public void AChosenBackupStoreIsKeptForTheMonitorToSkip()
    {
        var backup = Path.Combine(Path.GetTempPath(), ".gemini", "antigravity-backup");
        var back = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(AppSettings.Default with { AntigravityRoot = backup }));
        Assert.Equal(backup, back.AntigravityRoot);
        Assert.Equal(backup, back.ToMonitorConfiguration().AntigravityRoot);
        Assert.Empty(LiveMonitor.AntigravityStores(backup));
        Assert.Equal([Antigravity], LiveMonitor.AntigravityStores(Antigravity));
    }

    [Theory]
    [InlineData("""
        { "sources": { "opencode": "no", "gemini": 0, "omp": "off", "antigravity": 1,
          "opencodeRoot": 5, "geminiRoot": "relative/tmp", "ompRoot": 7, "antigravityRoot": "relative/store" } }
        """)]
    [InlineData("""
        { "sources": { "opencode": null, "gemini": [], "omp": null, "antigravity": {},
          "opencodeRoot": null, "geminiRoot": "", "ompRoot": null, "antigravityRoot": "   " } }
        """)]
    public void InvalidSourceValuesFallBackPerField(string json)
    {
        var s = SettingsSerializer.Deserialize(json);
        Assert.True(s.OpenCodeEnabled);
        Assert.True(s.GeminiEnabled);
        Assert.True(s.OmpEnabled);
        Assert.True(s.AntigravityEnabled);
        Assert.Null(s.OpenCodeRoot);
        Assert.Null(s.GeminiRoot);
        Assert.Null(s.OmpRoot);
        Assert.Null(s.AntigravityRoot);
    }

    [Fact]
    public void OmpAndAntigravityPathsAreCleaned()
    {
        var s = (AppSettings.Default with { OmpRoot = $"  {Omp}  ", AntigravityRoot = $"{Antigravity}\0" }).Validated();
        Assert.Equal(Omp, s.OmpRoot);
        Assert.Null(s.AntigravityRoot);
        Assert.Null((AppSettings.Default with { OmpRoot = Path.Combine("relative", "sessions") }).Validated().OmpRoot);
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
