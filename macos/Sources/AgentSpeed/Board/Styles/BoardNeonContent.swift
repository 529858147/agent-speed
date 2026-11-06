import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// 霓虹座艙 (DESIGN 2c Styles; `board-neon.html`): a night cockpit in the LED orb's language: a grid deck lit by the
/// heat, glowing LED digits (chromatic fringes from h = 0.85), the 24-segment LED bar and a breathing neon tube. With
/// the live metrics (Live metrics, *Hero*) the hero grows and its right side is the LED sub-gauge over the LED bar.
@MainActor
final class BoardNeonContent: BoardContentBuilding {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let capsules: BoardCapsuleDrawing
    private(set) var rows: BoardListRows
    private var memory = BoardTickMemory()
    private lazy var gauge = BoardNeonGauge(size: size, palette: palette, l10n: l10n)

    init(size: BoardSize, dark: Bool, highContrast: Bool, l10n: Localizer, capsules: BoardCapsuleDrawing) {
        self.size = size
        palette = BoardPalette(style: .neon, dark: dark, highContrast: highContrast)
        self.l10n = l10n
        self.capsules = capsules
        rows = BoardListRows(style: .neon, size: size, palette: palette, capsules: capsules)
    }

    private var side: CGFloat { CGFloat(size.side) }
    private var pad: CGFloat { size == .small ? 14 : size == .medium ? 16 : 18 }
    private var big: CGFloat { size == .small ? 34 : size == .medium ? 40 : 46 }

    var wakeAt: TimeInterval? { gauge.narrowsAt }
    var reduceMotion = false

    func climbsUntil(after now: TimeInterval) -> TimeInterval? { gauge.climbsUntil(after: now) }

    func layout(_ tick: BoardTick) -> BoardContentLayout {
        memory.begin(tick)
        let geometry = BoardContentBuilders.geometry(.neon, size, tick)
        if rows.geometry != geometry {
            rows = BoardListRows(style: .neon, size: size, palette: palette, capsules: capsules, geometry: geometry,
                                 caption: l10n("intel.glyph"))
        }
        let rowsTop = CGFloat(geometry.rankingTop)
        let models = BoardRowModels.make(tick, l10n: l10n, twoLine: geometry.twoLine)
        let lives = BoardRowModels.lives(tick)
        let column = BoardRowModels.capsuleColumn(models, compact: rows.compact, capsules: rows.capsules)
        var items = deck(tick)
        let (hero, number) = self.hero(tick, geometry: geometry)
        items += hero
        if geometry.hasIntelligence {
            items.append(rows.captions(frame: CGRect(x: pad - 6, y: rowsTop, width: side - 2 * pad + 12, height: CGFloat(geometry.captionsHeight)),
                                       intel: l10n("intel.glyph"), unit: l10n("unit.tokS")))
        }
        var anchors: [String: (row: CGRect, rank: CGPoint)] = [:]
        var subRows: [String: CGRect] = [:]
        for (index, row) in tick.rows.enumerated() {
            let frame = rows.frame(row, rowsTop: rowsTop, pad: pad)
            switch row.kind {
            case .group(let group): anchors[tick.groups[group].id] = (frame, rows.rankCenter(frame))
            case .sub: subRows[row.key] = frame
            case .moreSubs, .moreGroups: break
            }
            items += rows.items(models[index], key: row.key, frame: frame, column: column,
                                accents: BoardRowAccents(row, live: lives[index], tick: tick, memory: memory, model: models[index],
                                                         reduceMotion: reduceMotion))
        }
        items.append(footer(tick))
        items += tube(tick)
        memory.end(tick, rows: zip(tick.rows, lives).map { ($0.key, $1) },
                   scores: Dictionary(zip(tick.rows, models).compactMap { row, model in model.intel.map { (row.key, $0) } },
                                      uniquingKeysWith: { a, _ in a }))
        let scoreArea = geometry.hasGauge && tick.metrics.readouts.intelligence ? gauge.scoreArea : nil
        return BoardContentLayout(items: items, groupRows: anchors, number: number, subRows: subRows, scoreArea: scoreArea)
    }

    /// The deck: a 22 pt grid, and the heat's light from the top right as a soft item of its own.
    private func deck(_ tick: BoardTick) -> [BoardContentItem] {
        let heat = (tick.heat * 50).rounded() / 50
        let palette = self.palette, side = self.side
        let board = CGRect(x: 0, y: 0, width: side, height: side)
        let grid = BoardContentItem(key: "ambience", frame: board, motion: .none,
                                    revision: AnyHashable([AnyHashable("deck"), AnyHashable(palette.dark), AnyHashable(palette.highContrast)])) { context, _ in
            let grid = palette.dark ? RGBColor(red: 120 / 255, green: 150 / 255, blue: 1, alpha: 0.05)
                : RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: 0.04)
            context.setFillColor(grid.cgColor)
            var offset: CGFloat = 0
            while offset < side {
                context.fill(CGRect(x: offset, y: 0, width: 1, height: side))
                context.fill(CGRect(x: 0, y: side - 1 - offset, width: side, height: 1))
                offset += 22
            }
        }
        guard !palette.highContrast, heat > 0 else { return [grid] }
        let center = CGPoint(x: side * 0.85, y: 0)
        let radius = CGSize(width: side * 1.2 * 0.6, height: side * 0.8 * 0.6)
        let color = palette.heat(heat).opacity(heat * 0.22 * 1.4)
        var light = BoardContentItem.part("deck.light", of: board,
                                          rect: CGRect(x: center.x - radius.width, y: 0, width: 2 * radius.width, height: radius.height),
                                          revision: AnyHashable("light")) { context in
            BoardDrawing.radial(center: center, radius: radius, color: .white, in: context)
        }
        light.tint = color.cgColor
        light.rasterScale = 0.5
        return [light, grid]
    }

    private func hero(_ tick: BoardTick, geometry: BoardGeometry) -> ([BoardContentItem], CGRect) {
        let palette = self.palette, big = self.big, heroHeight = CGFloat(geometry.bandHeight)
        let heat = (tick.heat * 50).rounded() / 50
        let heatColor = palette.heatText(max(0.06, heat))
        let glow = palette.heat(max(0.06, heat))
        let look = BoardHeroNumber.Look(font: BoardFont(family: .mono, size: big, weight: .bold, tracking: -0.03),
                                        color: palette.dark ? RGBColor(hex: 0xFFFDF5) : palette.ink,
                                        hot: palette.dark ? .white : palette.ink, hotGlow: palette.glows ? glow : nil, hotBlur: 12,
                                        fringes: palette.glows && palette.dark && heat > 0.85)
        let frame = CGRect(x: pad, y: pad - 2, width: side - 2 * pad, height: heroHeight)
        let now = Clock.now
        let gaugeWidth = geometry.hasGauge ? gauge.width(tick.metrics, now: now) : 0
        // With the sub-gauge the value and its meta line centre in the band above the LED bar, beside the sub-gauge.
        let top = geometry.hasGauge ? ((heroHeight - 17 - (big * 1.08 + 8 + 12)) / 2).rounded() : 0
        let room = geometry.hasGauge ? frame.width - gaugeWidth - CGFloat(BoardGeometry.heroGap) : frame.width
        let number = BoardHeroNumber(tick.valueText, value: tick.value, look: look, origin: CGPoint(x: frame.minX, y: frame.minY + top))
        let unit = BoardLabel(l10n("unit.tokS"), BoardFont(family: .mono, size: 10.5, weight: .bold, tracking: 0.14), color: heatColor)
        // Beside the sub-gauge at S the meta line tightens: no tracking, 4 pt between the title and the context.
        let tight = geometry.hasGauge && size == .small
        let metaGap: CGFloat = tight ? 4 : 8
        let title = BoardLabel(tick.title, BoardFont(size: 12, weight: .bold, tracking: tight ? 0 : 0.06), color: heatColor)
        let counts = BoardLabel("· " + l10n("agents.context", tick.generating, tick.tools),
                                BoardFont(family: .mono, size: 10.5, weight: .semibold, tracking: tight ? 0 : 0.06), color: palette.ink3)
        let peakHeat = HeatPalette.heat(value: tick.peak, redline: tick.redline)
        let (lit, peak) = BoardHistoryShape.led(heat: heat, peakHeat: peakHeat)
        let theme = AnyHashable([AnyHashable(palette.dark), AnyHashable(palette.highContrast), AnyHashable(l10n.language.rawValue)])
        let baseline = top + number.baseline
        let unitX = number.label.width + 7
        let textGlow = palette.glows ? glow.opacity(0.55) : nil
        // Cached apart: the glow follows the digits, the unit their width and the heat, the meta line the title and
        // counts, the LED bar its lit segments.
        var items: [BoardContentItem] = []
        if palette.glows {
            let blurs: [CGFloat] = palette.dark ? [4 + heat * 16, 10, 3] : [8]
            let reach = (blurs.max() ?? 0) * 1.5
            var item = BoardContentItem.part("hero.glow", of: frame, rect: number.label.box(x: 0, baseline: baseline, margin: reach),
                                             revision: AnyHashable([AnyHashable(tick.valueText), AnyHashable(heat), theme])) { context in
                number.drawGlow(in: context, x: 0, top: top, color: palette.dark ? glow : glow.opacity(0.55), blurs: blurs)
            }
            item.rasterScale = BoardContentItem.softScale
            items.append(item)
        }
        let heatKey = AnyHashable([heatColor.red, heatColor.green, heatColor.blue])
        items.append(.part("hero.unit", of: frame, rect: unit.box(x: unitX, baseline: baseline, margin: 12),
                           revision: AnyHashable([AnyHashable(unitX), heatKey, theme])) { context in
            unit.draw(in: context, x: unitX, baseline: baseline, glow: textGlow, blur: 8)
        })
        let metaMid = top + big * 1.08 + 8 + 6
        items.append(.part("hero.meta", of: frame, rect: CGRect(x: 0, y: metaMid - 22, width: room + 12, height: 44),
                           revision: AnyHashable([AnyHashable(tick.title), AnyHashable(tick.generating), AnyHashable(tick.tools),
                                                  heatKey, AnyHashable(room), AnyHashable(top), theme])) { context in
            let metaBaseline = title.baseline(centeredOn: metaMid)
            let shownTitle = title.truncated(to: room)
            shownTitle.draw(in: context, x: 0, baseline: metaBaseline, glow: textGlow, blur: 10)
            counts.truncated(to: max(0, room - shownTitle.width - metaGap))
                .draw(in: context, x: shownTitle.width + metaGap, baseline: counts.baseline(centeredOn: metaMid))
        })
        let led = CGRect(x: 0, y: frame.height - 6 - 7, width: frame.width, height: 7)
        let ledRevision = AnyHashable([AnyHashable(lit), AnyHashable(peak ?? -1), theme])
        if palette.glows {
            var item = BoardContentItem.part("hero.led.glow", of: frame, rect: led.insetBy(dx: -12, dy: -12), revision: ledRevision) { context in
                BoardHeroHistory.led(lit: lit, peak: peak, glows: true, in: led, palette: palette, context: context)
            }
            item.rasterScale = BoardContentItem.softScale
            items.append(item)
        }
        items.append(.part("hero.led", of: frame, rect: led.insetBy(dx: -1, dy: -1), revision: ledRevision) { context in
            BoardHeroHistory.led(lit: lit, peak: peak, glows: false, in: led, palette: palette, context: context)
        })
        let decals = tick.newPeak ? [number.sheen(), BoardHeroNumber.peakChip(style: .neon, l10n: l10n, glows: palette.glows)] : []
        items += number.items(memory: memory, decals: decals)
        if geometry.hasGauge {
            items += gauge.items(tick.metrics, in: BoardGaugeFrame(top: frame.minY, height: CGFloat(geometry.gaugeArea),
                                                                    right: side - pad, memory: memory, now: now,
                                                                    reduceMotion: reduceMotion))
        }
        return (items.inHero(), number.rect)
    }

    private func footer(_ tick: BoardTick) -> BoardContentItem {
        var parts = [BoardFooterPart("boards.today", value: tick.todayText, l10n: l10n)]
        if let cost = tick.costText { parts.append(BoardFooterPart(value: cost, quiet: tick.costIsTag)) }
        if size != .small { parts.append(BoardFooterPart("boards.peak", value: tick.peakText, l10n: l10n)) }
        let palette = self.palette
        let frame = CGRect(x: pad, y: side - 11 - 18, width: side - 2 * pad, height: 18)
        let revision = AnyHashable([tick.todayText, tick.costText ?? "", tick.costIsTag ? "t" : "", tick.peakText, palette.dark ? "d" : "l",
                                    palette.highContrast ? "c" : "", l10n.language.rawValue])
        return BoardContentItem(key: "footer", frame: frame, motion: .none, revision: revision) { context, box in
            BoardFooterPart.draw(parts, label: BoardFont(family: .mono, size: 10, weight: .semibold, tracking: 0.06),
                                 strong: BoardFont(family: .mono, size: 10, weight: .bold, tracking: 0.06),
                                 labelColor: palette.ink3, strongColor: palette.ink2, width: box.width, midY: box.height / 2, in: context)
        }
    }

    /// The neon tube on the edge (`.panel::after`): always lit, brighter with heat, breathing faster as it rises
    /// (3.2 s → 1 s; quantized so a new heat rarely restarts the breath) while agents work; an idle board is still. Its
    /// inner glow is a soft item under the crisp tube, both breathing together; the heat comes in 1/20 steps.
    private func tube(_ tick: BoardTick) -> [BoardContentItem] {
        let heat = (tick.heat * 20).rounded() / 20
        let palette = self.palette, side = self.side
        let corner = CGFloat(BoardStyle.neon.cornerRadius)
        let rect = CGRect(x: 0, y: 0, width: side, height: side)
        let base = palette.heat(max(0.06, heat))
        let opacity = palette.highContrast ? 1 : 0.45 + heat * 0.55
        let revision = AnyHashable([AnyHashable(heat), AnyHashable(palette.dark), AnyHashable(palette.highContrast)])
        var ambient: BoardContentAmbient?
        if !palette.highContrast, tick.generating > 0 {
            let breath = (tick.heat * 10).rounded() / 10
            ambient = .breathe(period: 3.2 - 2.2 * breath, low: Float((0.3 + 0.5 * breath) / (0.45 + 0.55 * breath)))
        }
        var items: [BoardContentItem] = []
        if palette.glows {
            var glow = BoardContentItem(key: "tube.glow", frame: rect, motion: .none, revision: revision) { context, _ in
                // The light the edge casts inward (`box-shadow: inset`): the glow of everything outside the board.
                let shape = CGPath(roundedRect: rect, cornerWidth: corner, cornerHeight: corner, transform: nil)
                context.addPath(shape)
                context.clip()
                BoardGlow.fill(base.opacity(0.55 * opacity), blur: 6 + heat * 18, in: context) { outside in
                    outside.addRect(rect.insetBy(dx: -40, dy: -40))
                    outside.addPath(shape)
                    outside.fillPath(using: .evenOdd)
                }
            }
            glow.rasterScale = BoardContentItem.softScale
            glow.ambient = ambient
            items.append(glow)
        }
        var tube = BoardContentItem(key: "tube", frame: rect, motion: .none, revision: revision) { context, _ in
            let edge = base.mixOKLab(.white, 0.12).opacity(0.85 * opacity)
            BoardDrawing.stroke(rect, radius: corner, width: palette.highContrast ? 1 : 1.2, color: edge, in: context)
        }
        tube.ambient = ambient
        items.append(tube)
        return items
    }
}
