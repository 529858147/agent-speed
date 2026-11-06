import AgentSpeedOrbModel
import AppKit

/// `--board-demo [glass|timing|neon] [S|M|L]`: one real board (panel, content layers, effects on the shared clock) in
/// the top-right corner of the main display, fed by `BoardSimulation` every engine tick through the style's content builder. Drag it
/// around; quit with ⌘Q or after `AGENT_SPEED_DEMO_SECONDS`.
///
/// `AGENT_SPEED_BOARD_SCENARIO` (swarm, duel, team, calm; default swarm), `AGENT_SPEED_BOARD_LEVEL`
/// (off, soft, standard, max), `AGENT_SPEED_BOARD_SPEED` (speed multiplier), `AGENT_SPEED_DEMO_LIGHT=1`,
/// `AGENT_SPEED_REDUCE_MOTION=1`, `AGENT_SPEED_BOARD_LANG` (zh-Hant, zh-Hans, en, ja), `AGENT_SPEED_DEMO_TRACE=1` (prints
/// the content animations running after each tick).
@MainActor
enum BoardDemoHarness {
    private static var board: BoardWindowController?
    private static var feed: BoardSimFeed?
    private static var timer: Timer?

    static func start(_ options: BoardDemoOptions) {
        let environment = ProcessInfo.processInfo.environment
        let style = options.style.flatMap(BoardStyle.init(rawValue:)) ?? .neon
        let size = options.size.flatMap(BoardSize.init(rawValue:)) ?? .medium
        let dark = environment["AGENT_SPEED_DEMO_LIGHT"] != "1"
        let screen = NSScreen.main ?? NSScreen.screens.first
        let visible = screen?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
        let side = CGFloat(size.side)
        let center = CGPoint(x: visible.maxX - side / 2 - 24, y: visible.maxY - side / 2 - 24)

        let board = BoardWindowController(config: BoardConfig(id: "demo", style: style, size: size), center: center, dark: dark)
        board.reduceMotion = environment["AGENT_SPEED_REDUCE_MOTION"] == "1"
        let level = environment["AGENT_SPEED_BOARD_LEVEL"].flatMap(BoardEffectsIntensity.init(rawValue:)) ?? .standard
        board.driver.update { $0.setIntensity(level) }
        board.host.content.scale = screen?.backingScaleFactor ?? 2

        var simulation = BoardSimulation(scenario: environment["AGENT_SPEED_BOARD_SCENARIO"] ?? "swarm")
        simulation.speedScale = environment["AGENT_SPEED_BOARD_SPEED"].flatMap(Double.init) ?? 1
        let feed = BoardSimFeed(simulation: simulation, style: style, size: size, dark: dark, l10n: BoardSimFeed.harnessLocalizer(),
                                highContrast: NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast)
        board.contentProvider = { feed.takeItems() }
        board.show()
        self.board = board
        self.feed = feed

        timer = Timer.scheduledTimer(withTimeInterval: simulation.tick, repeats: true) { _ in
            MainActor.assumeIsolated { step() }
        }
        if let seconds = environment["AGENT_SPEED_DEMO_SECONDS"].flatMap(Double.init) {
            DispatchQueue.main.asyncAfter(deadline: .now() + seconds) { NSApp.terminate(nil) }
        }
    }

    private static func step() {
        guard let board, let feed else { return }
        feed.step()
        board.contentChanged()
        board.driver.update { feed.play(on: &$0, events: board.isRendering) }
        if ProcessInfo.processInfo.environment["AGENT_SPEED_DEMO_TRACE"] == "1" { trace(board.host.content.layer) }
    }

    /// One line per tick: how many content layers run each animation key.
    private static func trace(_ root: CALayer) {
        var counts: [String: Int] = [:]
        func visit(_ layer: CALayer) {
            for key in layer.animationKeys() ?? [] { counts[key, default: 0] += 1 }
            layer.sublayers?.forEach(visit)
        }
        visit(root)
        print("content", counts.sorted { $0.key < $1.key }.map { "\($0.key)=\($0.value)" }.joined(separator: " "))
    }
}
