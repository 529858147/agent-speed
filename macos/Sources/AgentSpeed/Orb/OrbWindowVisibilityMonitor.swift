import AppKit

/// Reports whether the system currently lets the panel be seen: not fully occluded, displays awake and the user
/// session active. The renderer pauses whenever this is false.
@MainActor
final class OrbWindowVisibilityMonitor {
    var onChange: (() -> Void)?

    private(set) var isOccluded = false
    private(set) var screensAsleep = false
    private(set) var sessionInactive = false

    private weak var window: NSWindow?
    nonisolated(unsafe) private var tokens: [(NotificationCenter, NSObjectProtocol)] = []

    init(window: NSWindow) {
        self.window = window
        let center = NotificationCenter.default
        let workspace = NSWorkspace.shared.notificationCenter
        observe(center, NSWindow.didChangeOcclusionStateNotification, object: window) { monitor in
            guard let window = monitor.window else { return }
            monitor.isOccluded = window.isVisible && !window.occlusionState.contains(.visible)
        }
        observe(workspace, NSWorkspace.screensDidSleepNotification) { $0.screensAsleep = true }
        observe(workspace, NSWorkspace.screensDidWakeNotification) { $0.screensAsleep = false }
        observe(workspace, NSWorkspace.sessionDidResignActiveNotification) { $0.sessionInactive = true }
        observe(workspace, NSWorkspace.sessionDidBecomeActiveNotification) { $0.sessionInactive = false }
    }

    deinit {
        for (center, token) in tokens { center.removeObserver(token) }
    }

    var allowsVisibility: Bool { !isOccluded && !screensAsleep && !sessionInactive }

    private func observe(
        _ center: NotificationCenter,
        _ name: Notification.Name,
        object: AnyObject? = nil,
        update: @escaping @MainActor (OrbWindowVisibilityMonitor) -> Void
    ) {
        let token = center.addObserver(forName: name, object: object, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                let before = self.allowsVisibility
                update(self)
                if before != self.allowsVisibility { self.onChange?() }
            }
        }
        tokens.append((center, token))
    }
}
