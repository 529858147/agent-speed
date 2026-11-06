import AgentSpeedOrbModel
import AppKit
import Darwin

/// `--board-bench DIR`: puts one real board per style (panel, cached content layers, effects on the shared frame
/// clock) in the top-right corner of the main display, feeds it from `BoardSimulation` (`team`) and measures the
/// DESIGN 2c scenario over a window per state: idle → climb (cruise) → redline with bursts → overtakes → new leader,
/// then hidden and occluded. Reports process CPU, frames per second, the time share of each frame class, CPU and GPU
/// time per frame, and the main thread's CPU per engine tick (content layout, rasterization and layer updates). Writes `DIR/board-bench.txt` and quits. Use a release build.
///
/// `AGENT_SPEED_BENCH_STYLES` (comma separated), `AGENT_SPEED_BOARD_SIZE` (S, M, L; default M),
/// `AGENT_SPEED_BOARD_LEVEL` (off, soft, standard, max), `AGENT_SPEED_BENCH_SECONDS` (10),
/// `AGENT_SPEED_BENCH_STATES` (comma separated state names).
@MainActor
final class BoardBenchmark {
    enum Check {
        case shown, hidden, occluded
    }

    enum Event {
        case none, overtakes, newLeader
    }

    struct State {
        let name: String
        /// Total speed as a fraction of the redline (0: every agent idle), ramping to `rampTo` over the window.
        let fraction: Double
        var rampTo: Double?
        /// The simulation's own events (overtakes, punches, peaks); off measures the pure frame class.
        var simEvents = true
        var event = Event.none
        var check = Check.shown
        /// CPU budget (DESIGN 2c, macOS, % of one core).
        let budget: Double
    }

    struct Row {
        let style: BoardStyle
        let state: State
        let cpu: Double
        let fps: Double
        /// Share of the window spent in 60, 30 and no-frame classes.
        let classes: (Double, Double, Double)
        let cpuMicros: Double
        let gpuMillis: Double
        /// Main-thread CPU per engine tick for the content (layout, rasterization, layer updates) and effects events.
        let tickMicros: Double
    }

    static let states = [
        State(name: "idle", fraction: 0, budget: 0.3),
        State(name: "cruise", fraction: 0.1, simEvents: false, budget: 3),
        State(name: "climb", fraction: 0.1, rampTo: 0.9, budget: 6),
        State(name: "redline", fraction: 0.9, event: .overtakes, budget: 6),
        State(name: "overtakes", fraction: 0.45, event: .overtakes, budget: 6),
        State(name: "newleader", fraction: 0.7, event: .newLeader, budget: 6),
        State(name: "hidden", fraction: 0.9, event: .overtakes, check: .hidden, budget: 0.3),
        State(name: "occluded", fraction: 0.9, event: .overtakes, check: .occluded, budget: 0.3),
    ]

    private static let redline = 2000.0
    private static var current: BoardBenchmark?

    private let directory: URL
    private let seconds: Double
    private var lines: [String] = []
    private var rows: [Row] = []
    private var board: BoardWindowController?
    private var feed: BoardSimFeed?
    private var state = BoardBenchmark.states[0]
    private var stateStart = Date()
    private var scale = 1.0
    private var ticks = 0
    private var tickNanoseconds: UInt64 = 0
    private var tickCount = 0

    static func start(into directory: URL) {
        let bench = BoardBenchmark(directory: directory)
        current = bench
        Task { @MainActor in
            await bench.run()
            NSApp.terminate(nil)
        }
    }

    private init(directory: URL) {
        self.directory = directory
        seconds = ProcessInfo.processInfo.environment["AGENT_SPEED_BENCH_SECONDS"].flatMap(Double.init) ?? 10
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    }

    // MARK: Run

    private func run() async {
        #if DEBUG
        lines.append("build: debug (use `swift build -c release` for representative numbers)")
        #else
        lines.append("build: release")
        #endif
        let environment = ProcessInfo.processInfo.environment
        let screen = NSScreen.main ?? NSScreen.screens.first
        let size = environment["AGENT_SPEED_BOARD_SIZE"].flatMap(BoardSize.init(rawValue:)) ?? .medium
        let level = environment["AGENT_SPEED_BOARD_LEVEL"].flatMap(BoardEffectsIntensity.init(rawValue:)) ?? .standard
        lines.append(String(format: "display: %d Hz, %.0f× backing scale; board %@ at %@; CPU = user + system time of the whole process over %.0f s",
                            screen?.maximumFramesPerSecond ?? 60, screen?.backingScaleFactor ?? 2, size.rawValue,
                            level.rawValue, seconds))
        let styles = environment["AGENT_SPEED_BENCH_STYLES"].map { $0.split(separator: ",").compactMap { BoardStyle(rawValue: String($0)) } }
            ?? BoardStyle.allCases
        let only = environment["AGENT_SPEED_BENCH_STATES"]?.split(separator: ",").map(String.init)
        let states = Self.states.filter { only?.contains($0.name) ?? true }
        let footprint = Self.footprint()
        for style in styles {
            await run(style: style, size: size, level: level, states: states)
        }
        lines.append(String(format: "memory: process footprint %.1f MB before the boards, %.1f MB after", footprint, Self.footprint()))
        lines.append("")
        writeReport()
    }

    private func run(style: BoardStyle, size: BoardSize, level: BoardEffectsIntensity, states: [State]) async {
        let screen = NSScreen.main ?? NSScreen.screens.first
        let visible = screen?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
        let side = CGFloat(size.side)
        let center = CGPoint(x: visible.maxX - side / 2 - 24, y: visible.maxY - side / 2 - 24)
        let board = BoardWindowController(config: BoardConfig(id: "bench-\(style.rawValue)", style: style, size: size,
                                                              clickThrough: true),
                                          center: center, dark: true)
        board.driver.update { $0.setIntensity(level) }
        board.host.content.scale = screen?.backingScaleFactor ?? 2
        var simulation = BoardSimulation(scenario: "team")
        simulation.paused = true
        let feed = BoardSimFeed(simulation: simulation, style: style, size: size, dark: true, l10n: BoardSimFeed.harnessLocalizer(),
                                redline: Self.redline)
        board.contentProvider = { feed.takeItems() }
        board.show()
        self.board = board
        self.feed = feed
        scale = Self.unitSpeed(simulation)
        let timer = Timer.scheduledTimer(withTimeInterval: simulation.tick, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.step() }
        }
        defer { timer.invalidate() }

        for state in states {
            feed.simulation.paused = state.fraction == 0
            self.state = state
            stateStart = .distantFuture
            setSpeed()
            var occluder: NSWindow?
            switch state.check {
            case .shown: break
            case .hidden: board.hide()
            case .occluded: occluder = Self.occluder(over: board.effectsPanel.frame)
            }
            // The live speed is a 3 s window: let it reach the state, and idle let every effect end.
            try? await Task.sleep(for: .seconds(state.fraction == 0 ? 6 : 4))
            stateStart = Date()
            await measure(style: style, state: state, board: board)
            occluder?.orderOut(nil)
            if state.check == .hidden { board.show() }
        }
        board.close()
        self.board = nil
        self.feed = nil
        try? await Task.sleep(for: .seconds(0.6))
    }

    private func measure(style: BoardStyle, state: State, board: BoardWindowController) async {
        let canvases = [board.driver.overlay]
        for canvas in canvases {
            canvas.resetStats()
            _ = canvas.encoder?.gpuTime.drain()
        }
        var samples = (0, 0, 0)
        tickNanoseconds = 0
        tickCount = 0
        let cpuStart = Self.cpuTime()
        let wallStart = Date()
        while Date().timeIntervalSince(wallStart) < seconds {
            switch board.driver.canRender ? board.driver.effects.frameClass : .none {
            case .fps60: samples.0 += 1
            case .fps30, .fps15: samples.1 += 1
            case .none: samples.2 += 1
            }
            try? await Task.sleep(for: .seconds(0.1))
        }
        let wall = Date().timeIntervalSince(wallStart)
        let cpu = (Self.cpuTime() - cpuStart) / wall * 100
        let frames = board.driver.overlay.framesDrawn
        let nanoseconds = canvases.reduce(UInt64(0)) { $0 + $1.cpuNanoseconds }
        var gpuSeconds = 0.0
        for canvas in canvases {
            if let (seconds, _) = canvas.encoder?.gpuTime.drain() { gpuSeconds += seconds }
        }
        let total = Double(max(1, samples.0 + samples.1 + samples.2))
        let row = Row(style: style, state: state, cpu: cpu, fps: Double(frames) / wall,
                      classes: (Double(samples.0) / total, Double(samples.1) / total, Double(samples.2) / total),
                      cpuMicros: frames > 0 ? Double(nanoseconds) / Double(frames) / 1000 : 0,
                      gpuMillis: frames > 0 ? gpuSeconds / Double(frames) * 1000 : 0,
                      tickMicros: tickCount > 0 ? Double(tickNanoseconds) / Double(tickCount) / 1000 : 0)
        rows.append(row)
        print(Self.format(row))
    }

    /// One engine tick: the simulation, the content layers and the effects, plus the state's scripted events.
    private func step() {
        guard let board, let feed else { return }
        setSpeed()
        // A tick that shows the same content as the last lays out nothing (an idle board).
        let changed = feed.step()
        let start = Self.threadNanoseconds()
        if changed {
            board.contentChanged()
            ticks += 1
        }
        defer {
            tickNanoseconds += Self.threadNanoseconds() - start
            tickCount += 1
        }
        let event = state.event
        let simEvents = state.simEvents
        let ticks = self.ticks
        let rendering = board.isRendering
        board.driver.update { fx in
            feed.play(on: &fx, events: rendering && simEvents)
            guard rendering else { return }
            let rows = feed.snapshot.groups.compactMap { feed.layout.groupRows[$0.id] }
            guard rows.count >= 3 else { return }
            let color = RGBColor(hex: 0x34D399).simd
            switch event {
            case .none: break
            case .overtakes:
                guard ticks % 4 == 0 else { return }
                let index = 1 + (ticks / 4) % 2
                fx.overtake(row: rows[index].row, rank: rows[index].rank, color: color, passedRankY: rows[index - 1].rank.y)
            case .newLeader:
                guard ticks % 8 == 0 else { return }
                fx.newLeader(row: rows[0].row, rank: rows[0].rank, color: color, passedRankY: rows[1].rank.y)
            }
        }
    }

    /// The state's speed; a ramp climbs over the measuring window.
    private func setSpeed() {
        guard let feed else { return }
        var fraction = state.fraction
        if let to = state.rampTo {
            let u = min(max(Date().timeIntervalSince(stateStart) / seconds, 0), 1)
            fraction += (to - fraction) * u
        }
        feed.simulation.speedScale = fraction * Self.redline / scale
    }

    /// The simulation's mean total speed at `speedScale` 1 over a minute of ticks (to scale a state's fraction).
    private static func unitSpeed(_ simulation: BoardSimulation) -> Double {
        var copy = simulation
        copy.paused = false
        copy.speedScale = 1
        var sum = 0.0
        for _ in 0..<120 {
            copy.advance()
            sum += copy.agents.reduce(0) { $0 + $1.live }
        }
        return max(1, sum / 120)
    }

    /// An opaque window over the board and its effects, so the system reports them fully occluded.
    private static func occluder(over frame: CGRect) -> NSWindow {
        let rect = frame.insetBy(dx: -8, dy: -8)
        let window = NSWindow(contentRect: rect, styleMask: [.borderless], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false
        window.ignoresMouseEvents = true
        window.isOpaque = true
        window.backgroundColor = NSColor(srgbRed: 0.16, green: 0.2, blue: 0.27, alpha: 1)
        window.level = .statusBar
        window.hasShadow = false
        window.orderFrontRegardless()
        return window
    }

    // MARK: Report

    private static func format(_ row: Row) -> String {
        let pass = row.cpu <= row.state.budget ? "ok" : "OVER"
        let gpu = row.gpuMillis > 1.5 ? " GPU OVER" : ""
        let classes = String(format: "60:%3.0f%% 30:%3.0f%% none:%3.0f%%", row.classes.0 * 100, row.classes.1 * 100,
                             row.classes.2 * 100)
        return String(format: "%-7@ %-10@ CPU %6.2f %%  ≤ %3.1f  %-4@  %5.1f fps  [%@]  %6.0f µs/frame  %5.2f ms GPU  %6.0f µs/tick%@",
                      row.style.rawValue, row.state.name, row.cpu, row.state.budget, pass, row.fps, classes,
                      row.cpuMicros, row.gpuMillis, row.tickMicros, gpu)
    }

    private func writeReport() {
        var text = lines.joined(separator: "\n") + "\n"
        text += "Budget (DESIGN 2c, macOS, M, Standard): idle ≤ 0.3 %, cruise ≤ 3 %, redline with bursts ≤ 6 %, GPU ≤ 1.5 ms per frame at 2×; hidden and occluded render nothing\n\n"
        text += rows.map(Self.format).joined(separator: "\n") + "\n"
        try? text.write(to: directory.appendingPathComponent("board-bench.txt"), atomically: true, encoding: .utf8)
        print(text, terminator: "")
    }

    /// User + system CPU seconds of this process.
    private static func cpuTime() -> Double {
        var usage = rusage()
        getrusage(RUSAGE_SELF, &usage)
        func seconds(_ t: timeval) -> Double { Double(t.tv_sec) + Double(t.tv_usec) / 1e6 }
        return seconds(usage.ru_utime) + seconds(usage.ru_stime)
    }

    /// CPU time of the calling thread.
    private static func threadNanoseconds() -> UInt64 {
        clock_gettime_nsec_np(CLOCK_THREAD_CPUTIME_ID)
    }

    /// The process's physical footprint in MB.
    private static func footprint() -> Double {
        var info = task_vm_info_data_t()
        var count = mach_msg_type_number_t(MemoryLayout<task_vm_info_data_t>.size / MemoryLayout<natural_t>.size)
        let result = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { task_info(mach_task_self_, task_flavor_t(TASK_VM_INFO), $0, &count) }
        }
        return result == KERN_SUCCESS ? Double(info.phys_footprint) / 1_048_576 : 0
    }
}
