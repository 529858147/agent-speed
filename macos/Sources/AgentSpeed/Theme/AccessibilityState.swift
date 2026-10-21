import AppKit
import Observation

/// Mirrors the system display accessibility options and updates live.
@MainActor
@Observable
final class AccessibilityState {
    private(set) var reduceMotion = false
    private(set) var reduceTransparency = false
    private(set) var increaseContrast = false

    @ObservationIgnored private var observer: NSObjectProtocol?

    init() {
        refresh()
        observer = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh() }
        }
    }

    /// Effects level after applying Reduce Motion (caps at Soft).
    func effectiveLevel(_ level: EffectsLevel) -> EffectsLevel {
        reduceMotion ? min(level, .soft) : level
    }

    private func refresh() {
        let workspace = NSWorkspace.shared
        if reduceMotion != workspace.accessibilityDisplayShouldReduceMotion {
            reduceMotion = workspace.accessibilityDisplayShouldReduceMotion
        }
        if reduceTransparency != workspace.accessibilityDisplayShouldReduceTransparency {
            reduceTransparency = workspace.accessibilityDisplayShouldReduceTransparency
        }
        if increaseContrast != workspace.accessibilityDisplayShouldIncreaseContrast {
            increaseContrast = workspace.accessibilityDisplayShouldIncreaseContrast
        }
    }
}
