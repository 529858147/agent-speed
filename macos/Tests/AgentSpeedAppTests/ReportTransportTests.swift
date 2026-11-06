import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 11 item 2b and METRICS 11.9: WebSocket is the default transport, only an HTTP choice is stored, a service
/// without the capability reports over HTTP meanwhile, and a document that adds or drops it changes the transport.
@Suite("Report transport")
@MainActor
struct ReportTransportTests {
    static let service = ReportController.previewService
    static let other = try! ReportServiceURL.parse("https://other.example").get()
    static let withWebSocket = ReportController.discovery(
        websocket: #"{"path":"/v1/reports/ws","subprotocol":"agent-speed-report.v1"}"#)
    static let withoutWebSocket = ReportController.previewDiscovery

    // MARK: - The stored choice

    @Test func webSocketIsTheDefault() {
        #expect(ReportPreferences().transport(for: Self.service) == .websocket)
        #expect(ReportPreferences().transport(for: nil) == .websocket)
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withWebSocket)
        #expect(rig.report.transport == .websocket)
        #expect(rig.report.effectiveTransport == .websocket)
        #expect(!rig.report.webSocketUnavailable)
    }

    @Test func anHTTPChoiceIsStoredAndSurvivesEncoding() throws {
        var preferences = ReportPreferences()
        preferences.setTransport(.http, for: Self.service.origin)
        let decoded = try JSONDecoder().decode(ReportPreferences.self, from: JSONEncoder().encode(preferences))
        #expect(decoded.httpServices == [Self.service.origin])
        #expect(decoded.transport(for: Self.service) == .http)
        #expect(decoded.transport(for: Self.other) == .websocket)

        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withWebSocket)
        rig.report.setTransport(.http)
        #expect(ReportPreferences.load(from: rig.defaults).transport(for: Self.service) == .http)
        rig.report.setTransport(.websocket)
        #expect(ReportPreferences.load(from: rig.defaults).httpServices.isEmpty, "WebSocket is never stored")
    }

    @Test func atMostSixteenHTTPChoicesAreKeptTheLatestLast() {
        var preferences = ReportPreferences()
        for index in 0...16 { preferences.setTransport(.http, for: "https://s\(index).example") }
        #expect(preferences.httpServices.count == ReportPreferences.maxHTTPServices)
        #expect(preferences.httpServices.first == "https://s1.example")
        #expect(preferences.httpServices.last == "https://s16.example")
        preferences.setTransport(.http, for: "https://s5.example")
        #expect(preferences.httpServices.last == "https://s5.example")
        #expect(preferences.httpServices.count == ReportPreferences.maxHTTPServices)
    }

    /// An earlier build kept the WebSocket origins (`webSocketServices`, HTTP for the rest) and an `ETag` per identity.
    @Test func settingsFromAnEarlierBuildDecodeAndTheirServicesUseWebSocket() throws {
        let now = Date()
        func legacyIdentity(_ service: ReportServiceURL) throws -> [String: Any] {
            let identity = ReportIdentity(
                service: service, deviceId: ReportCanonical.newUUID(), username: "racer-old", profileURL: nil,
                pin: ReportConsentPin(Self.withoutWebSocket), discovery: Self.withoutWebSocket, discoveryFetchedAt: now,
                keyCreatedAt: now, lastUsedAt: now)
            var object = try #require(
                try JSONSerialization.jsonObject(with: JSONEncoder().encode(identity)) as? [String: Any])
            object["etag"] = "\"v1\""
            return object
        }
        let legacy: [String: Any] = [
            "enabled": true, "preset": "custom", "customURL": Self.service.string,
            "sections": ["sources", "models", "cost", "traffic"],
            "identities": [try legacyIdentity(Self.service), try legacyIdentity(Self.other)],
            "webSocketServices": [Self.other.origin],
        ]
        let rig = Rig()
        defer { rig.tearDown() }
        rig.defaults.set(try JSONSerialization.data(withJSONObject: legacy), forKey: ReportPreferences.storageKey)
        let preferences = ReportPreferences.load(from: rig.defaults)
        #expect(preferences.enabled)
        #expect(preferences.identities.map(\.id) == [Self.service.origin, Self.other.origin])
        #expect(preferences.identities.allSatisfy { $0.isRegistered && $0.discovery == Self.withoutWebSocket })
        #expect(preferences.httpServices.isEmpty)
        #expect(preferences.transport(for: Self.service) == .websocket, "HTTP by default in the earlier build")
        #expect(preferences.transport(for: Self.other) == .websocket)

        let saved = try #require(
            try JSONSerialization.jsonObject(with: JSONEncoder().encode(preferences)) as? [String: Any])
        #expect(saved["webSocketServices"] == nil)
        let identities = try #require(saved["identities"] as? [[String: Any]])
        #expect(identities.allSatisfy { $0["etag"] == nil })
    }

    // MARK: - The effective transport

    @Test func webSocketWithoutTheCapabilityReportsOverHTTP() async throws {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withoutWebSocket)
        #expect(rig.report.transport == .websocket)
        #expect(rig.report.effectiveTransport == .http)
        #expect(rig.report.webSocketUnavailable)

        rig.busyTick()
        #expect(rig.report.run != nil, "the HTTP scheduler runs")
        #expect(rig.report.stream == nil)
        #expect(rig.report.mode == .active)
        #expect(rig.report.streamIssue == nil, "no WebSocket status line while falling back")

        rig.report.pause(for: 3600)
        try await rig.waitUntil { rig.report.lastDeliveryAt != nil }
        let request = try #require(rig.transport.requests.first)
        #expect(request.method == "POST")
        #expect(request.url.path.hasSuffix(ReportEndpoint.reports))
        rig.report.resume()
        guard case .reporting = rig.report.status else {
            Issue.record("the status line is HTTP's, got \(rig.report.status)")
            return
        }
        #expect(rig.stream.links == 0, "no WebSocket connection is attempted")
    }

    @Test func aRefreshedDocumentThatAddsTheCapabilitySwitchesTheRunToWebSocket() async throws {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withoutWebSocket)
        rig.busyTick()
        #expect(rig.report.run != nil)

        rig.report.applyDiscovery(.document(Self.withWebSocket), service: Self.service)
        #expect(rig.report.identity?.discovery?.websocket != nil)
        #expect(rig.report.effectiveTransport == .websocket)
        #expect(rig.report.run == nil, "the HTTP run stopped")
        #expect(rig.report.stream != nil, "a WebSocket run started")
        try await rig.waitUntil { rig.stream.links > 0 }

        rig.report.applyDiscovery(.document(Self.withoutWebSocket), service: Self.service)
        #expect(rig.report.effectiveTransport == .http)
        #expect(rig.report.stream == nil, "a document that drops the capability ends the WebSocket run")
        #expect(rig.report.run != nil, "and HTTP takes over")
    }

    /// The run is picked by the stored document, and one that changes under a running transport replaces it at the
    /// next tick.
    @Test func aStoredDocumentPicksTheRunAndAChangeUnderItSwitchesAtTheNextTick() {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withWebSocket)
        rig.busyTick()
        #expect(rig.report.stream != nil && rig.report.run == nil)
        rig.report.updateIdentity(Self.service.origin) { $0.discovery = Self.withoutWebSocket }
        rig.busyTick()
        #expect(rig.report.run != nil && rig.report.stream == nil)
        rig.report.updateIdentity(Self.service.origin) { $0.discovery = Self.withWebSocket }
        rig.busyTick()
        #expect(rig.report.stream != nil && rig.report.run == nil)
    }

    @Test func choosingHTTPEndsTheWebSocketRunAtOnce() {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withWebSocket)
        rig.busyTick()
        #expect(rig.report.stream != nil)
        rig.report.setTransport(.http)
        #expect(rig.report.stream == nil)
        #expect(rig.report.run != nil)
        rig.report.setTransport(.websocket)
        #expect(rig.report.run == nil)
        #expect(rig.report.stream != nil)
    }

    /// The effective transport stays HTTP, so the HTTP run is not interrupted.
    @Test func choosingWebSocketOnAServiceWithoutItKeepsTheHTTPRun() throws {
        let rig = Rig()
        defer { rig.tearDown() }
        rig.register(Self.withoutWebSocket, choosing: .http)
        rig.busyTick()
        let run = try #require(rig.report.run?.id)
        rig.report.setTransport(.websocket)
        #expect(rig.report.transport == .websocket)
        #expect(rig.report.run?.id == run)
        #expect(rig.report.stream == nil)
    }
}

/// A controller on its own settings suite and folder, with transports that never leave the process.
@MainActor
private struct Rig {
    let suite = "AgentSpeedReportTransportTests-\(UUID().uuidString)"
    let defaults: UserDefaults
    let folder: URL
    let transport = RecordingTransport()
    let stream = SilentStreamTransport()
    let report: ReportController

    init() {
        let defaults = UserDefaults(suiteName: suite)!
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(suite, isDirectory: true)
        self.defaults = defaults
        self.folder = folder
        report = ReportController(
            settings: AppSettings(defaults: defaults), store: SpeedStore(), traffic: TrafficStore(),
            priceBook: PriceBook(), appData: folder, transport: transport, streamTransport: stream, version: "1.4.1")
    }

    func tearDown() {
        report.shutdown()
        UserDefaults.standard.removePersistentDomain(forName: suite)
        try? FileManager.default.removeItem(at: folder)
    }

    /// Registered with the example service on `document`, reporting on, a software key; nothing runs yet.
    func register(_ document: ReportDiscovery, choosing transport: ReportTransportKind = .websocket) {
        let now = Date()
        var preferences = ReportPreferences()
        preferences.enabled = true
        preferences.preset = .custom
        preferences.customURL = ReportController.previewService.string
        preferences.invite = .accepted
        preferences.identities = [ReportIdentity(
            service: ReportController.previewService, deviceId: ReportCanonical.newUUID(), username: "racer-test",
            profileURL: nil, pin: ReportConsentPin(document), discovery: document, discoveryFetchedAt: now,
            keyCreatedAt: now, lastUsedAt: now)]
        preferences.setTransport(transport, for: ReportController.previewService.origin)
        report.preferences = preferences
        report.signer = SoftwareReportSigner()
    }

    /// One engine tick with the monitor running and an agent generating.
    func busyTick() {
        var snapshot = PreviewData.snapshot(speed: 240, redline: 2000)
        snapshot.report = ReportLiveFacts(
            agents: [.claudeCode: ReportAgentCounts(generating: 1, active: 1)],
            generatingModels: [ReportModelKey(modelId: "claude-opus-5-5", source: .claudeCode): 1],
            counters: ReportCounters())
        report.handleLive(snapshot)
    }

    func waitUntil(_ condition: () -> Bool) async throws {
        let deadline = Date().addingTimeInterval(5)
        while !condition(), Date() < deadline {
            try await Task.sleep(for: .milliseconds(20))
        }
        #expect(condition())
    }
}

/// Answers every API request with `accepted` and keeps it; discovery is not fetched in these tests.
private final class RecordingTransport: ReportTransport, @unchecked Sendable {
    private let lock = NSLock()
    private var sent: [ReportRequest] = []

    var requests: [ReportRequest] { lock.withLock { sent } }

    func get(_ url: URL, userAgent: String, maxBytes: Int, timeout: TimeInterval)
        async throws(ReportTransportError) -> ReportHTTPResponse {
        ReportHTTPResponse(status: 404)
    }

    func send(_ request: ReportRequest, maxBytes: Int, timeout: TimeInterval)
        async throws(ReportTransportError) -> ReportHTTPResponse {
        lock.withLock { sent.append(request) }
        return ReportHTTPResponse(status: 200, headers: ["content-type": "application/json"],
                                  body: Data(#"{"status":"accepted"}"#.utf8))
    }
}

/// Connections that never open: the session asks for one and nothing leaves the process.
private final class SilentStreamTransport: ReportStreamTransport, @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    var links: Int { lock.withLock { count } }

    func link(
        settings: ReportStreamSettings, queue: DispatchQueue,
        onEvent: @escaping @Sendable (ReportStreamEvent) -> Void,
        onSigningFailure: @escaping @Sendable () -> Void
    ) -> any ReportStreamLink {
        lock.withLock { count += 1 }
        return SilentStreamLink()
    }
}

private final class SilentStreamLink: ReportStreamLink, @unchecked Sendable {
    func start(_ makeRequest: @escaping @Sendable () throws -> ReportRequest) {}
    func send(_ makeText: @escaping @Sendable () throws -> String) {}
    func close() {}
    func abort() {}
}
