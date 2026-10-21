import AppKit
import SwiftUI

/// Receives pointer and accessibility input from the disc.
@MainActor
protocol OrbHostViewDelegate: AnyObject {
    func hostPointerMoved()
    func hostMouseDown(_ event: NSEvent)
    func hostMouseDragged(_ event: NSEvent)
    func hostMouseUp(_ event: NSEvent)
    func hostShowMenu(_ event: NSEvent?)
    func hostPress()
}

/// Content view of the orb panel: the clipped desktop blur behind the SwiftUI renderer. Only the disc takes part in
/// hit testing; the rest of the canvas lets clicks through. The disc is also the single accessibility element.
final class OrbHostView: NSView {
    /// Extra reach around the disc edge for pointer hits.
    static let hitTolerance: CGFloat = 2

    weak var delegate: OrbHostViewDelegate?
    /// Shown while the pointer is over the disc; nil leaves the cursor alone.
    var cursor: NSCursor?

    var accessibilityLabelText = ""
    var accessibilityValueText = ""
    var accessibilityHelpText = ""

    let blurView = OrbBlurView(frame: .zero)
    private let hostingView: NSHostingView<OrbHostContent>
    private(set) var discDiameter: CGFloat
    private(set) var hoverScale: CGFloat = 1
    private var trackingArea: NSTrackingArea?

    init(content: OrbHostContent, diameter: CGFloat) {
        discDiameter = diameter
        hostingView = NSHostingView(rootView: content)
        super.init(frame: NSRect(x: 0, y: 0, width: OrbPanel.side(for: diameter), height: OrbPanel.side(for: diameter)))
        wantsLayer = true

        hostingView.sizingOptions = []
        hostingView.safeAreaRegions = []
        hostingView.frame = bounds
        hostingView.autoresizingMask = [.width, .height]
        hostingView.setAccessibilityElement(false)

        addSubview(blurView)
        addSubview(hostingView)
        updateBlurFrame()
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    func setDisc(diameter: CGFloat, hoverScale: CGFloat) {
        guard diameter != discDiameter || hoverScale != self.hoverScale else { return }
        discDiameter = diameter
        self.hoverScale = hoverScale
        updateBlurFrame()
    }

    /// Whether a point in screen coordinates is on the disc (including the hover scale and tolerance).
    func discContains(screenPoint point: NSPoint) -> Bool {
        guard let window else { return false }
        let local = convert(window.convertPoint(fromScreen: point), from: nil)
        return discContains(local)
    }

    /// The disc in screen coordinates.
    var discScreenFrame: NSRect {
        guard let window else { return .zero }
        return window.convertToScreen(convert(discRect(scale: hoverScale), to: nil))
    }

    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize)
        updateBlurFrame()
    }

    override var isOpaque: Bool { false }
    override var mouseDownCanMoveWindow: Bool { false }

    // MARK: Hit testing and pointer

    override func hitTest(_ point: NSPoint) -> NSView? {
        let local = superview.map { convert(point, from: $0) } ?? point
        return discContains(local) ? self : nil
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let trackingArea { removeTrackingArea(trackingArea) }
        let area = NSTrackingArea(
            rect: .zero,
            options: [.mouseEnteredAndExited, .mouseMoved, .cursorUpdate, .activeAlways, .inVisibleRect],
            owner: self,
            userInfo: nil
        )
        addTrackingArea(area)
        trackingArea = area
    }

    override func mouseEntered(with event: NSEvent) { delegate?.hostPointerMoved() }
    override func mouseExited(with event: NSEvent) { delegate?.hostPointerMoved() }
    override func mouseMoved(with event: NSEvent) { delegate?.hostPointerMoved() }

    override func cursorUpdate(with event: NSEvent) {
        if let cursor, discContains(convert(event.locationInWindow, from: nil)) {
            cursor.set()
        } else {
            super.cursorUpdate(with: event)
        }
    }

    override func mouseDown(with event: NSEvent) { delegate?.hostMouseDown(event) }
    override func mouseDragged(with event: NSEvent) { delegate?.hostMouseDragged(event) }
    override func mouseUp(with event: NSEvent) { delegate?.hostMouseUp(event) }
    override func rightMouseDown(with event: NSEvent) { delegate?.hostShowMenu(event) }

    // MARK: Accessibility

    override func isAccessibilityElement() -> Bool { true }
    override func accessibilityRole() -> NSAccessibility.Role? { .image }
    override func accessibilityLabel() -> String? { accessibilityLabelText }
    override func accessibilityValue() -> Any? { accessibilityValueText }
    override func accessibilityHelp() -> String? { accessibilityHelpText }
    override func accessibilityChildren() -> [Any]? { [] }
    override func accessibilityFrame() -> NSRect { discScreenFrame }
    override func accessibilityHitTest(_ point: NSPoint) -> Any? { self }

    override func accessibilityPerformPress() -> Bool {
        delegate?.hostPress()
        return true
    }

    override func accessibilityPerformShowMenu() -> Bool {
        delegate?.hostShowMenu(nil)
        return true
    }

    // MARK: Geometry

    private func discRect(scale: CGFloat) -> NSRect {
        let side = discDiameter * scale
        return NSRect(x: bounds.midX - side / 2, y: bounds.midY - side / 2, width: side, height: side)
    }

    private func discContains(_ point: NSPoint) -> Bool {
        let radius = discDiameter * hoverScale / 2 + Self.hitTolerance
        return hypot(point.x - bounds.midX, point.y - bounds.midY) <= radius
    }

    private func updateBlurFrame() {
        let frame = discRect(scale: hoverScale)
        if blurView.frame != frame { blurView.frame = frame }
    }
}
