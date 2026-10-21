import AppKit

/// The floating orb window: placement, drag and magnetic edge snapping, click-through outside the disc, per-display
/// persistence, visibility and idle fading, all applied live from the settings.
@MainActor
final class OrbWindowController {
    private static let hoverScale: CGFloat = 1.03

    private let model: AppModel
    private let interaction: OrbInteraction
    private let hostState: OrbHostState
    private let panel: OrbPanel
    private let host: OrbHostView
    private let animator: OrbWindowAnimator
    private let store = OrbPositionStore()
    private let pointer = OrbWindowPointerTracker()
    private let fader: OrbWindowFader
    private let visibility: OrbWindowVisibilityMonitor
    private var dimmer: OrbWindowIdleDimmer?
    private var reporter: OrbAccessibilityReporter?
    private var loops: [ObservationLoop] = []
    nonisolated(unsafe) private var screenObserver: NSObjectProtocol?

    private var drag: OrbDragSession?
    private var dragInterruptedMotion = false
    private var dragRegion: [CGRect] = []
    private var cursorPushed = false
    /// Display hosting the disc and where the disc rests on it.
    private var displayUUID: String?
    private var placementRecord: OrbPositionRecord?
    /// True when the current placement is the user's choice on `displayUUID` (and so is worth persisting).
    private var isUserPlaced = false
    private var appliedDiameter: CGFloat

    init(model: AppModel) {
        self.model = model
        let diameter = CGFloat(model.settings.orbSize)
        let interaction = OrbInteraction()
        let hostState = OrbHostState(diameter: diameter)
        let panel = OrbPanel()
        self.interaction = interaction
        self.hostState = hostState
        self.panel = panel
        appliedDiameter = diameter
        host = OrbHostView(
            content: OrbHostContent(model: model, state: hostState, interaction: interaction),
            diameter: diameter
        )
        animator = OrbWindowAnimator(center: .zero, diameter: diameter)
        fader = OrbWindowFader(panel: panel)
        visibility = OrbWindowVisibilityMonitor(window: panel)

        panel.contentView = host
        panel.ignoresMouseEvents = true
        host.delegate = self
        animator.attach(to: host)
        animator.onChange = { [weak self] state in self?.apply(state) }
        animator.onCenterSettled = { [weak self] in self?.centerSettled() }
        pointer.onMove = { [weak self] location, isPlainMove in
            guard let self else { return }
            if isPlainMove, self.drag != nil { self.finishPendingDrag() }
            self.updatePointer(at: location)
        }
        visibility.onChange = { [weak self] in self?.updateVisibleOnScreen() }

        restoreAtLaunch()
        observe()
    }

    deinit {
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
    }

    /// Persists the current position for its display right away.
    func savePosition() {
        guard isUserPlaced, let displayUUID, let placementRecord else { return }
        store.save(placementRecord, for: displayUUID)
        OrbWindowLog.debug("saved \(placementRecord) on \(displayUUID)")
    }

    /// Animates back to the default corner and forgets every saved position.
    func resetPosition() {
        store.clear()
        isUserPlaced = false
        guard let screen = Self.primaryScreen else { return }
        let center = OrbSnap.defaultCenter(radius: radius, in: screen.visibleFrame)
        commit(center, on: screen, byUser: false)
        animator.animate(center: center, motion: motion(.layout))
    }

    // MARK: Setup

    private func observe() {
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            let settings = self.model.settings
            self.panel.apply(alwaysOnTop: settings.orbAlwaysOnTop, allSpaces: settings.orbAllSpaces)
            self.fader.reorderIfShown()
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            self.resize(to: CGFloat(self.model.settings.orbSize))
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            let hideBlur = self.model.accessibility.reduceTransparency
            let opacity = CGFloat(self.model.settings.orbOpacity)
            if self.host.blurView.isHidden != hideBlur { self.host.blurView.isHidden = hideBlur }
            if self.host.blurView.alphaValue != opacity { self.host.blurView.alphaValue = opacity }
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            let title = self.model.l10n("app.name")
            if self.panel.title != title { self.panel.title = title }
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            self.setShown(self.model.settings.orbVisible)
        })
        loops.append(ObservationLoop { [weak self] in
            guard let self else { return }
            _ = self.model.settings.orbClickThrough
            self.updateTracking()
        })
        dimmer = OrbWindowIdleDimmer(model: model, interaction: interaction) { [weak self] dimmed in
            self?.fader.setDimmed(dimmed)
        }
        reporter = OrbAccessibilityReporter(model: model, host: host)
        screenObserver = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.screensChanged() }
        }
    }

    // MARK: Placement

    private var radius: CGFloat { animator.targetState.diameter / 2 }

    private var reduceMotion: Bool { model.accessibility.reduceMotion }

    /// Motion for a programmatic move: none while hidden, an ease under Reduce Motion.
    private func motion(_ preferred: OrbWindowMotion) -> OrbWindowMotion? {
        guard fader.isShown else { return nil }
        return reduceMotion ? .reduced : preferred
    }

    private static var primaryScreen: NSScreen? { NSScreen.main ?? NSScreen.screens.first }

    private static func screen(with uuid: String) -> NSScreen? {
        NSScreen.screens.first { $0.orbDisplayUUID == uuid }
    }

    private static func screen(containing center: CGPoint) -> NSScreen? {
        let screens = NSScreen.screens
        guard let index = OrbSnap.screenIndex(for: center, in: screens.map(\.orbSnapScreen)) else { return nil }
        return screens[index]
    }

    private func restoreAtLaunch() {
        if let last = store.lastDisplay, let screen = Self.screen(with: last), let record = store.record(for: last) {
            let center = record.center(radius: radius, visible: screen.visibleFrame)
            isUserPlaced = true
            commit(center, on: screen, byUser: false)
            animator.jump(center: center, diameter: appliedDiameter)
            OrbWindowLog.debug("restored \(OrbWindowLog.describe(center)) on last display \(last)")
            return
        }
        guard let screen = Self.primaryScreen else {
            animator.jump(center: .zero, diameter: appliedDiameter)
            return
        }
        let center = fallbackCenter(on: screen)
        commit(center, on: screen, byUser: false)
        animator.jump(center: center, diameter: appliedDiameter)
        OrbWindowLog.debug("placed \(OrbWindowLog.describe(center)) on \(screen.orbDisplayUUID ?? "?")")
    }

    /// The saved position on `screen`, else the default corner.
    private func fallbackCenter(on screen: NSScreen) -> CGPoint {
        if let uuid = screen.orbDisplayUUID, let record = store.record(for: uuid) {
            return record.center(radius: radius, visible: screen.visibleFrame)
        }
        return OrbSnap.defaultCenter(radius: radius, in: screen.visibleFrame)
    }

    private func commit(_ center: CGPoint, on screen: NSScreen, byUser: Bool) {
        displayUUID = screen.orbDisplayUUID
        placementRecord = OrbPositionRecord(center: center, radius: radius, visible: screen.visibleFrame)
        if byUser { isUserPlaced = true }
        if isUserPlaced { savePosition() }
    }

    private func screensChanged() {
        guard !NSScreen.screens.isEmpty, drag == nil else { return }
        OrbWindowLog.debug("screens changed: \(NSScreen.screens.map { "\($0.orbDisplayUUID ?? "?") \(OrbWindowLog.describe($0.visibleFrame))" })")
        if let last = store.lastDisplay, last != displayUUID, let screen = Self.screen(with: last), let record = store.record(for: last) {
            isUserPlaced = true
            move(to: record.center(radius: radius, visible: screen.visibleFrame), on: screen)
        } else if let uuid = displayUUID, let screen = Self.screen(with: uuid) {
            let record = placementRecord ?? OrbPositionRecord(center: animator.targetState.center, radius: radius, visible: screen.visibleFrame)
            move(to: record.center(radius: radius, visible: screen.visibleFrame), on: screen)
        } else if let screen = Self.primaryScreen {
            isUserPlaced = store.lastDisplay != nil && store.lastDisplay == screen.orbDisplayUUID
            move(to: fallbackCenter(on: screen), on: screen)
        }
    }

    private func move(to center: CGPoint, on screen: NSScreen) {
        commit(center, on: screen, byUser: false)
        guard center != animator.targetState.center else { return }
        animator.animate(center: center, motion: motion(.layout))
    }

    private func resize(to diameter: CGFloat) {
        guard diameter != appliedDiameter else { return }
        let oldRadius = appliedDiameter / 2
        appliedDiameter = diameter
        let animation: OrbWindowMotion? = fader.isShown && !reduceMotion ? .layout : nil
        animator.animate(diameter: diameter, motion: animation)
        guard drag == nil else { return }
        let current = animator.targetState.center
        guard let screen = Self.screen(containing: current) else { return }
        let center = OrbSnap.resized(current, from: oldRadius, to: diameter / 2, in: screen.visibleFrame)
        commit(center, on: screen, byUser: false)
        animator.animate(center: center, motion: animation)
    }

    private func apply(_ state: OrbWindowAnimator.State) {
        let side = OrbPanel.side(for: state.diameter)
        let scale = max(1, panel.screen?.backingScaleFactor ?? panel.backingScaleFactor)
        let origin = NSPoint(
            x: ((state.center.x - side / 2) * scale).rounded() / scale,
            y: ((state.center.y - side / 2) * scale).rounded() / scale
        )
        let frame = NSRect(origin: origin, size: NSSize(width: side, height: side))
        if panel.frame.size != frame.size {
            panel.setFrame(frame, display: true)
        } else if panel.frame.origin != origin {
            panel.setFrameOrigin(origin)
        }
        if hostState.diameter != state.diameter { hostState.diameter = state.diameter }
        host.setDisc(diameter: state.diameter, hoverScale: state.hoverScale)
        if drag == nil { refreshPointer() }
    }

    private func centerSettled() {
        OrbWindowLog.debug("settled frame \(OrbWindowLog.describe(panel.frame)) disc \(OrbWindowLog.describe(host.discScreenFrame))")
    }

    // MARK: Visibility

    private func setShown(_ shown: Bool) {
        guard shown != fader.isShown else { return }
        if shown {
            fader.setShown(true)
        } else {
            animator.finish()
            fader.setShown(false)
        }
        updateTracking()
        updateVisibleOnScreen()
    }

    private func updateVisibleOnScreen() {
        let visible = fader.isShown && visibility.allowsVisibility
        if interaction.isVisibleOnScreen != visible { interaction.isVisibleOnScreen = visible }
    }

    // MARK: Pointer

    private func updateTracking() {
        if fader.isShown, !model.settings.orbClickThrough {
            pointer.start()
        } else {
            pointer.stop()
        }
        refreshPointer()
    }

    private func refreshPointer() {
        updatePointer(at: NSEvent.mouseLocation)
    }

    /// Lets the panel take the mouse only while the pointer is over the disc; everything else clicks through.
    private func updatePointer(at location: NSPoint) {
        guard drag == nil else { return }
        let interactive = fader.isShown && !model.settings.orbClickThrough && !pointer.isForeignDragActive
        let inside = interactive && host.discContains(screenPoint: location)
        if panel.ignoresMouseEvents == inside {
            panel.ignoresMouseEvents = !inside
            OrbWindowLog.debug("pointer \(OrbWindowLog.describe(location)) \(inside ? "on disc: accepts mouse" : "off disc: click-through")")
        }
        setHovered(inside)
    }

    private func setHovered(_ hovered: Bool) {
        guard interaction.isHovered != hovered else { return }
        interaction.isHovered = hovered
        host.cursor = hovered ? .openHand : nil
        if hovered {
            NSCursor.openHand.set()
        } else {
            NSCursor.arrow.set()
        }
        let scale = hovered && !reduceMotion ? Self.hoverScale : 1
        animator.animate(hoverScale: scale, motion: reduceMotion ? nil : .hover)
    }

    private func beginDragging() {
        interaction.isDragging = true
        host.cursor = .closedHand
        NSCursor.closedHand.push()
        cursorPushed = true
    }

    private func endDragging() {
        drag = nil
        dragInterruptedMotion = false
        if interaction.isDragging { interaction.isDragging = false }
        if cursorPushed {
            NSCursor.pop()
            cursorPushed = false
        }
        refreshPointer()
        host.cursor = interaction.isHovered ? .openHand : nil
        (interaction.isHovered ? NSCursor.openHand : NSCursor.arrow).set()
    }

    /// Ends a tracked press that will not see its mouse-up (a plain move arrived, or a menu took over).
    private func finishPendingDrag() {
        guard let session = drag else { return }
        if session.isMoving || dragInterruptedMotion { settle(velocity: .zero) }
        endDragging()
    }

    /// Snaps the disc to the edge it was released near and keeps it fully on screen.
    private func settle(velocity: CGVector) {
        let current = animator.state.center
        guard let screen = Self.screen(containing: current) else { return }
        let target = OrbSnap.snapped(current, radius: radius, in: screen.visibleFrame)
        commit(target, on: screen, byUser: true)
        animator.animate(center: target, motion: motion(.snap), velocity: reduceMotion ? .zero : velocity)
        OrbWindowLog.debug("release \(OrbWindowLog.describe(current)) → \(OrbWindowLog.describe(target)) v=\(OrbWindowLog.describe(CGPoint(x: velocity.dx, y: velocity.dy))) visible \(OrbWindowLog.describe(screen.visibleFrame))")
    }
}

// MARK: - Host input

extension OrbWindowController: OrbHostViewDelegate {
    func hostPointerMoved() {
        refreshPointer()
    }

    func hostMouseDown(_ event: NSEvent) {
        if event.modifierFlags.contains(.control) {
            hostShowMenu(event)
            return
        }
        if event.clickCount >= 2 {
            endDragging()
            model.commands?.showDashboard()
            return
        }
        dragInterruptedMotion = animator.isMovingCenter
        animator.freezeCenter()
        drag = OrbDragSession(pointer: NSEvent.mouseLocation, center: animator.state.center)
        dragRegion = OrbSnap.dragRegion(screens: NSScreen.screens.map(\.orbSnapScreen), radius: radius)
    }

    func hostMouseDragged(_ event: NSEvent) {
        guard var session = drag else { return }
        guard let raw = session.center(for: NSEvent.mouseLocation) else { return }
        if !interaction.isDragging { beginDragging() }
        let center = OrbSnap.rubberBanded(raw, region: dragRegion, radius: radius)
        animator.setCenter(center)
        session.record(center)
        drag = session
    }

    func hostMouseUp(_ event: NSEvent) {
        guard let session = drag else { return }
        let moved = session.isMoving || dragInterruptedMotion
        let velocity = session.releaseVelocity()
        drag = nil
        if moved { settle(velocity: velocity) }
        endDragging()
    }

    func hostShowMenu(_ event: NSEvent?) {
        finishPendingDrag()
        let menu = MenuFactory.orbContextMenu(model: model)
        if let event {
            NSMenu.popUpContextMenu(menu, with: event, for: host)
        } else {
            menu.popUp(positioning: nil, at: NSPoint(x: host.bounds.midX, y: host.bounds.midY), in: host)
        }
        refreshPointer()
    }

    func hostPress() {
        model.commands?.showDashboard()
    }
}
