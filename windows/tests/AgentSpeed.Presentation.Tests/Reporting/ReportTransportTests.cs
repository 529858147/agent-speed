using System.Text.Json;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Presentation.Reporting;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Tests.Reporting;

/// <summary>The per-service transport choice (DESIGN 11, item 2b) and what the Transport row says under its control.</summary>
public class ReportTransportTests
{
    private const string StreamingDocument = """
        {"protocol":"agent-speed-report","versions":[1],
         "service":{"name":"Live board","terms":"https://example.com/terms","privacy":"https://example.com/privacy"},
         "api":"https://api.example.com/agent-speed",
         "extensions":{"reportWebSocket":1},
         "websocket":{"path":"/v1/reports/ws","subprotocol":"agent-speed-report.v1","minIntervalMs":MIN}}
        """;

    private const string PlainDocument = """
        {"protocol":"agent-speed-report","versions":[1],
         "service":{"name":"Live board","terms":"https://example.com/terms","privacy":"https://example.com/privacy"},
         "api":"https://api.example.com/agent-speed"}
        """;

    private static readonly ReportServiceUrl Radar = ReportSettingsTests.Service("https://deng.codexradar.com");
    private static readonly ReportServiceUrl Board = ReportSettingsTests.Service("https://board.example.com");

    private static ReportDiscovery Streaming(int minIntervalMs) =>
        ReportSettingsTests.Document(StreamingDocument.Replace("MIN", minIntervalMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal), "https://board.example.com");

    private static ReportPreferences Read(string reportJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        return ReportSettingsJson.Read(document.RootElement.Clone());
    }

    private static ReportPreferences RoundTrip(ReportPreferences report) =>
        SettingsSerializer.Deserialize(SettingsSerializer.Serialize(AppSettings.Default with { Report = report })).Report;

    /// <summary>The same service's document before it offered WebSocket.</summary>
    private static ReportDiscovery Plain => ReportSettingsTests.Document(PlainDocument, "https://board.example.com");

    private static ReportDiscovery Radar2026 => ReportSettingsTests.Document(ReportSettingsTests.CodexRadarDocument, "https://deng.codexradar.com");

    private static ReportIdentity Registered(ReportServiceUrl service, ReportDiscovery document) => new()
    {
        Service = service,
        DeviceId = "6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b",
        Username = "racer-7k3m9q",
        Pin = ReportConsentPin.Of(document),
        Discovery = document,
        DiscoveryFetchedAt = DateTimeOffset.UtcNow,
        KeyCreatedAt = DateTimeOffset.UtcNow,
        LastUsedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public void WebSocketIsTheDefaultForEveryService()
    {
        var preferences = ReportPreferences.Default;
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(Radar));
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(Board));
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(null));
        Assert.Empty(preferences.HttpServices);
    }

    [Fact]
    public void SettingsOfAnEarlierBuildLoadAndTheirServicesUseWebSocket()
    {
        // An earlier build kept the WebSocket origins and each identity's discovery ETag; neither is read any more.
        var preferences = Read($$"""
            {"enabled":true,"preset":"custom","customUrl":"https://board.example.com","sections":["sources"],
             "webSocketServices":["https://board.example.com"],
             "identities":[{"service":"https://deng.codexradar.com","deviceId":"6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b","username":"zoe",
               "discovery":{{ReportSettingsTests.CodexRadarDocument}},"discoveryFetchedAt":"2026-10-23T08:00:00Z","etag":"\"v1\"",
               "keyCreatedAt":"2026-10-23T08:00:00Z","pendingKey":false,"hasApiKey":false,"lastUsedAt":"2026-10-23T08:00:00Z"}]}
            """);
        Assert.True(preferences.Enabled);
        Assert.Empty(preferences.HttpServices);
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(Board));
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(Radar));
        var identity = Assert.Single(preferences.Identities);
        Assert.Equal("zoe", identity.Username);
        Assert.NotNull(identity.Discovery);
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero), identity.DiscoveryFetchedAt);

        var saved = SettingsSerializer.Serialize(AppSettings.Default with { Report = preferences });
        Assert.DoesNotContain("\"etag\"", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("webSocketServices", saved, StringComparison.Ordinal);
        var again = RoundTrip(preferences);
        Assert.Empty(again.HttpServices);
        Assert.Equal(ReportTransportKind.WebSocket, again.TransportOf(Radar));
        Assert.Equal("zoe", Assert.Single(again.Identities).Username);
    }

    [Fact]
    public void TheHttpChoiceBelongsToOneServiceAndCanBeUndone()
    {
        var chosen = ReportPreferences.Default.WithTransport(Board.Origin, ReportTransportKind.Http);
        Assert.Equal(ReportTransportKind.Http, chosen.TransportOf(Board));
        Assert.Equal(ReportTransportKind.WebSocket, chosen.TransportOf(Radar));
        Assert.Equal(chosen, chosen.WithTransport(Board.Origin, ReportTransportKind.Http));
        Assert.NotEqual(ReportPreferences.Default, chosen);

        var undone = chosen.WithTransport(Board.Origin, ReportTransportKind.WebSocket);
        Assert.Equal(ReportTransportKind.WebSocket, undone.TransportOf(Board));
        Assert.Equal(ReportPreferences.Default, undone);
    }

    [Fact]
    public void TheHttpChoiceSurvivesTheSettingsFile()
    {
        var chosen = ReportPreferences.Default with { Preset = ReportPreset.Custom, CustomUrl = Board.Text };
        chosen = chosen.WithTransport(Board.Origin, ReportTransportKind.Http).WithTransport(Radar.Origin, ReportTransportKind.Http);
        var saved = SettingsSerializer.Serialize(AppSettings.Default with { Report = chosen });
        Assert.Contains("\"httpServices\"", saved, StringComparison.Ordinal);

        var loaded = RoundTrip(chosen);
        Assert.Equal([Board.Origin, Radar.Origin], loaded.HttpServices.ToArray());
        Assert.Equal(ReportTransportKind.Http, loaded.TransportOf(Board));
        Assert.Equal(ReportTransportKind.Http, loaded.TransportOf(Radar));
        Assert.Equal(chosen, loaded);
    }

    [Fact]
    public void AStoredListIsCleanedAndBounded()
    {
        var origins = Enumerable.Range(0, ReportPreferences.MaxHttpServices + 4).Select(i => $"\"https://s{i}.example.com\"");
        var preferences = Read($$"""
            {"httpServices":["https://board.example.com",7,"",null,{{string.Join(',', origins)}},"https://board.example.com"]}
            """);
        Assert.Equal(ReportPreferences.MaxHttpServices, preferences.HttpServices.Length);
        Assert.Equal("https://board.example.com", preferences.HttpServices[^1]);
        Assert.Single(preferences.HttpServices, o => o == "https://board.example.com");
        Assert.DoesNotContain("https://s0.example.com", preferences.HttpServices);
        Assert.Equal(ReportTransportKind.Http, preferences.TransportOf(Board));

        Assert.Empty(Read("""{"httpServices":"https://board.example.com"}""").HttpServices);
    }

    [Fact]
    public void TheOldestChoiceGoesFirstWhenTheListIsFull()
    {
        var preferences = ReportPreferences.Default;
        for (var i = 0; i <= ReportPreferences.MaxHttpServices; i++)
        {
            preferences = preferences.WithTransport($"https://s{i}.example.com", ReportTransportKind.Http);
        }

        Assert.Equal(ReportPreferences.MaxHttpServices, preferences.HttpServices.Length);
        Assert.DoesNotContain("https://s0.example.com", preferences.HttpServices);
        Assert.Equal($"https://s{ReportPreferences.MaxHttpServices}.example.com", preferences.HttpServices[^1]);
        Assert.Equal(preferences.HttpServices, preferences.Validated().HttpServices, StringComparer.Ordinal);
    }

    [Fact]
    public void WebSocketIsEffectiveOnlyWithTheCapability()
    {
        Assert.Null(Radar2026.WebSocket);
        Assert.Equal(ReportTransportKind.WebSocket, ReportTransports.Effective(ReportTransportKind.WebSocket, Streaming(50)));
        Assert.Equal(ReportTransportKind.Http, ReportTransports.Effective(ReportTransportKind.WebSocket, Radar2026));
        Assert.Equal(ReportTransportKind.Http, ReportTransports.Effective(ReportTransportKind.WebSocket, null));
        Assert.Equal(ReportTransportKind.Http, ReportTransports.Effective(ReportTransportKind.Http, Streaming(50)));
    }

    [Fact]
    public void WebSocketWithoutTheCapabilityReportsOverHttp()
    {
        var preferences = ReportPreferences.Default with { Enabled = true, Identities = [Registered(Radar, Radar2026)] };
        Assert.Equal(ReportTransportKind.WebSocket, preferences.TransportOf(Radar));
        Assert.Equal(ReportTransportKind.Http, preferences.EffectiveTransportOf(Radar));

        // What the controller runs for an effective HTTP: the HTTP scheduler on the stored document's limits, which sends.
        var document = preferences.Identity(Radar)!.Discovery!;
        var scheduler = new ReportScheduler(document.Limits, jitter: () => 1);
        var sends = new List<int>();
        for (long t = 0; t <= 12_000; t += 500)
        {
            foreach (var action in scheduler.Tick(t, active: true, blocked: false))
            {
                if (action is ReportAction.Send send)
                {
                    sends.Add(send.Ticket.Seq);
                    scheduler.Finish(send.Ticket, new ReportAttemptOutcome.Delivered(null), t);
                }
            }
        }

        Assert.True(sends.Count >= 2, $"{sends.Count} reports sent");
        Assert.Equal(Enumerable.Range(1, sends.Count), sends);
    }

    [Fact]
    public void AChangeOfTheEffectiveTransportIsAChangeOfTransport()
    {
        var preferences = ReportPreferences.Default with { Enabled = true, Identities = [Registered(Board, Plain)] };
        var tracker = new ReportTransportTracker();
        Assert.False(tracker.Update(Board.Origin, preferences.EffectiveTransportOf(Board)));
        Assert.Equal(ReportTransportKind.Http, tracker.Current);

        // A refreshed document adds the capability: the run starts over on WebSocket.
        preferences = preferences.Updating(Board.Origin, i => i with { Discovery = Streaming(50) });
        Assert.True(tracker.Update(Board.Origin, preferences.EffectiveTransportOf(Board)));
        Assert.Equal(ReportTransportKind.WebSocket, tracker.Current);
        Assert.False(tracker.Update(Board.Origin, preferences.EffectiveTransportOf(Board)));

        // The user's HTTP choice changes it back; choosing WebSocket again on a document without it does not.
        preferences = preferences.WithTransport(Board.Origin, ReportTransportKind.Http);
        Assert.True(tracker.Update(Board.Origin, preferences.EffectiveTransportOf(Board)));
        preferences = preferences.Updating(Board.Origin, i => i with { Discovery = Plain })
            .WithTransport(Board.Origin, ReportTransportKind.WebSocket);
        Assert.False(tracker.Update(Board.Origin, preferences.EffectiveTransportOf(Board)));

        // Another service, or none, is never a change of transport.
        Assert.False(tracker.Update(Radar.Origin, ReportTransportKind.WebSocket));
        Assert.False(tracker.Update(null, ReportTransportKind.Http));
        Assert.False(tracker.Update(Radar.Origin, ReportTransportKind.WebSocket));
    }

    [Fact]
    public void HttpExplainsBothTransports()
    {
        Assert.Equal(new ReportTransportNotes("report.transport.hint", false, null), ReportTransportNotes.For(ReportTransportKind.Http, null));
        Assert.Equal(new ReportTransportNotes("report.transport.hint", false, null),
            ReportTransportNotes.For(ReportTransportKind.Http, ReportSettingsTests.Document(ReportSettingsTests.CodexRadarDocument, "https://deng.codexradar.com")));
    }

    [Fact]
    public void WebSocketSaysWhereTheDataComesFrom()
    {
        Assert.Equal(new ReportTransportNotes("report.transport.source", false, null), ReportTransportNotes.For(ReportTransportKind.WebSocket, null));
        Assert.Equal(new ReportTransportNotes("report.transport.source", false, null), ReportTransportNotes.For(ReportTransportKind.WebSocket, Streaming(50)));
    }

    [Fact]
    public void ADocumentWithoutTheCapabilityReplacesTheCaptionWithTheFallbackNote()
    {
        var radar = ReportSettingsTests.Document(ReportSettingsTests.CodexRadarDocument, "https://deng.codexradar.com");
        Assert.Null(radar.WebSocket);
        Assert.Equal(new ReportTransportNotes(null, true, null), ReportTransportNotes.For(ReportTransportKind.WebSocket, radar));
    }

    [Theory]
    [InlineData(10, null)]
    [InlineData(50, null)]
    [InlineData(51, 51)]
    [InlineData(250, 250)]
    [InlineData(1000, 1000)]
    public void AServiceSlowerThanLiveIsNamed(int minIntervalMs, int? throttled)
    {
        Assert.Equal(throttled, ReportTransportNotes.For(ReportTransportKind.WebSocket, Streaming(minIntervalMs)).ThrottledMs);
        Assert.Null(ReportTransportNotes.For(ReportTransportKind.Http, Streaming(minIntervalMs)).ThrottledMs);
    }

    [Fact]
    public void TheTransportStringsExistInEveryLanguage()
    {
        var table = Localization.Localizer.LoadEmbeddedTable();
        foreach (var language in Localization.LanguageCodes.All)
        {
            var loc = new Localization.Localizer(table, language);
            foreach (var key in new[] { "report.transport", "report.transport.http", "report.transport.websocket", "report.transport.hint",
                "report.transport.source", "report.status.wsUnsupported", "report.preview.onChange" })
            {
                Assert.NotEqual(key, loc[key]);
            }

            Assert.Contains("250", loc.Format("report.transport.throttled", 250), StringComparison.Ordinal);
        }
    }
}
