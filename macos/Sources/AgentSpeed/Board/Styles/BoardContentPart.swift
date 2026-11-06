import CoreGraphics

extension BoardContentItem {
    /// Pixels per point of soft lights (glows, blooms): their blur hides the lower resolution and costs a quarter.
    static let softScale: CGFloat = 0.5

    /// A piece of a larger drawing cached on its own, so a change of one piece redraws only that piece: `rect` (whole
    /// points, in `parent`'s coordinates, kept inside it) of what `draw` paints in `parent`'s coordinates.
    static func part(_ key: String, of parent: CGRect, rect: CGRect, motion: Motion = .none, revision: AnyHashable,
                     draw: @escaping (CGContext) -> Void) -> BoardContentItem {
        let bounds = CGRect(origin: .zero, size: parent.size)
        var local = rect.integral.intersection(bounds)
        if local.isNull { local = CGRect(x: 0, y: 0, width: 1, height: 1) }
        let origin = local.origin
        return BoardContentItem(key: key, frame: local.offsetBy(dx: parent.minX, dy: parent.minY), motion: motion,
                                revision: revision) { context, _ in
            context.translateBy(x: -origin.x, y: -origin.y)
            draw(context)
        }
    }
}

extension BoardLabel {
    /// The box the glyphs of this line can paint at `x` and `baseline`, grown by `margin` (glows) on every side.
    func box(x: CGFloat, baseline: CGFloat, margin: CGFloat = 1) -> CGRect {
        CGRect(x: x, y: baseline - font.ascender, width: width, height: font.ascender - font.descender)
            .insetBy(dx: -margin - 1, dy: -margin - 1)
    }
}
