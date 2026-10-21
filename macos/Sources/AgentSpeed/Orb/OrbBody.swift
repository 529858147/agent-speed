import SwiftUI

/// The static smoked-glass (or porcelain) disc: drop shadow, body gradient, inner shadow, lens and rim
/// (DESIGN section 2, layer 2). Only redraws when its inputs change.
struct OrbBody: View, Equatable {
    let diameter: CGFloat
    let canvasSide: CGFloat
    let tokens: OrbTokens
    /// The user's opacity setting (0.4...1), applied to the body tint.
    let opacity: Double
    /// No desktop blur behind (Reduce Transparency, embedded, previews): draw an opaque body.
    let solid: Bool
    let increaseContrast: Bool

    nonisolated static func == (lhs: OrbBody, rhs: OrbBody) -> Bool {
        lhs.diameter == rhs.diameter && lhs.canvasSide == rhs.canvasSide && lhs.tokens.isDark == rhs.tokens.isDark
            && lhs.opacity == rhs.opacity && lhs.solid == rhs.solid && lhs.increaseContrast == rhs.increaseContrast
    }

    var body: some View {
        let s = diameter
        ZStack {
            shadow
            Circle()
                .fill(LinearGradient(colors: [tokens.bodyTop.color, tokens.bodyBottom.color], startPoint: .top, endPoint: .bottom))
                .opacity(solid ? 1 : tokens.bodyOpacity * opacity)
                .frame(width: s, height: s)
            Circle()
                .strokeBorder(tokens.innerShadow.color, lineWidth: s * 0.05)
                .blur(radius: s * 0.03)
                .frame(width: s, height: s)
                .clipShape(Circle())
            Circle()
                .fill(RadialGradient(
                    colors: [tokens.lens.color, tokens.lens.color.opacity(0)],
                    center: UnitPoint(x: 0.3, y: 0.2),
                    startRadius: 0,
                    endRadius: s * 0.55
                ))
                .frame(width: s, height: s)
            Circle()
                .strokeBorder(
                    LinearGradient(
                        colors: [rimTop.color, rimTop.color.opacity(0)],
                        startPoint: .top,
                        endPoint: UnitPoint(x: 0.5, y: 0.75)
                    ),
                    lineWidth: 1
                )
                .frame(width: s, height: s)
            if let stroke = tokens.outerStroke {
                Circle()
                    .stroke(stroke.opacity(increaseContrast ? 2.2 : 1).color, lineWidth: increaseContrast ? 1 : 0.5)
                    .frame(width: s, height: s)
            }
        }
        .frame(width: canvasSide, height: canvasSide)
        .allowsHitTesting(false)
    }

    private var rimTop: RGBColor {
        increaseContrast ? tokens.rimTop.opacity(tokens.isDark ? 2.2 : 1) : tokens.rimTop
    }

    /// The drop shadow only outside the disc, so it never darkens the glass.
    private var shadow: some View {
        Circle()
            .fill(tokens.shadow.color)
            .frame(width: diameter, height: diameter)
            .blur(radius: diameter * tokens.shadowBlur / 2)
            .offset(y: diameter * tokens.shadowOffset)
            .frame(width: canvasSide, height: canvasSide)
            .mask {
                ZStack {
                    Rectangle()
                    Circle()
                        .frame(width: diameter - 1, height: diameter - 1)
                        .blendMode(.destinationOut)
                }
                .compositingGroup()
            }
    }
}
