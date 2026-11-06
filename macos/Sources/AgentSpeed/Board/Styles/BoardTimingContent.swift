import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// 計時塔 (DESIGN 2c Styles; `board-timing.html`): a racing broadcast timing tower on a hatched slab: the heat rail
/// on the top edge, a `boards.live` marker, monospaced numbers, 30 one-second bars, position blocks and intervals. With
/// the live metrics (Live metrics, *Hero*) the hero's right side is the data box and the bars become a full-width strip.
@MainActor
final class BoardTimingContent: BoardContentBuilding {
    let size: BoardSize
    let palette: BoardPalette
    let l10n: Localizer
    let capsules: BoardCapsuleDrawing
    private(set) var rows: BoardTimingRows
    private var memory = BoardTickMemory()
    private lazy var box = BoardTimingBox(size: size, palette: palette, l10n: l10n)

    init(size: BoardSize, dark: Bool, highContrast: Bool, l10n: Localizer, capsules: BoardCapsuleDrawing) {
        self.size = size
        palette = BoardPalette(style: .timing, dark: dark, highContrast: highContrast)
        self.l10n = l10n
        self.capsules = capsules
        rows = BoardTimingRows(size: size, palette: palette, capsules: capsules)
    }

    private var side: CGFloat { CGFloat(size.side) }
    private var pad: CGFloat { size == .small ? 12 : size == .medium ? 14 : 16 }
    private var heroHeight: CGFloat { size == .small ? 70 : size == .medium ? 78 : 86 }
    private var big: CGFloat { size == .small ? 30 : size == .medium ? 36 : 42 }

    var wakeAt: TimeInterval? { box.narrowsAt }
    var reduceMotion = false

    func climbsUntil(after now: TimeInterval) -> TimeInterval? { box.climbsUntil(after: now) }

    func layout(_ tick: BoardTick) -> BoardContentLayout {
        memory.begin(tick)
        let geometry = BoardContentBuilders.geometry(.timing, size, tick)
        if rows.geometry != geometry {
            rows = BoardTimingRows(size: size, palette: palette, capsules: capsules, geometry: geometry,
                                   caption: l10n("intel.glyph").uppercased(with: l10n.locale))
        }
        let rowsTop = CGFloat(geometry.rankingTop)
        let models = BoardRowModels.make(tick, l10n: l10n, twoLine: geometry.twoLine)
        let lives = BoardRowModels.lives(tick)
        let column = BoardRowModels.capsuleColumn(models, compact: rows.compact, capsules: rows.capsules)
        var items = slab(tick)
        let leader = models.first { $0.rank == 1 }?.name
        let (hero, number) = self.hero(tick, leader: leader, geometry: geometry)
        items += hero
        if geometry.hasIntelligence {
            let locale = l10n.locale
            items.append(rows.captions(frame: CGRect(x: pad - 4, y: rowsTop, width: side - 2 * pad + 8, height: CGFloat(geometry.captionsHeight)),
                                       gap: l10n("boards.gap").uppercased(with: locale),
                                       intel: l10n("intel.glyph").uppercased(with: locale),
                                       unit: l10n("unit.tokS").uppercased(with: locale)))
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
        memory.end(tick, rows: zip(tick.rows, lives).map { ($0.key, $1) },
                   scores: Dictionary(zip(tick.rows, models).compactMap { row, model in model.intel.map { (row.key, $0) } },
                                      uniquingKeysWith: { a, _ in a }))
        let scoreArea = geometry.hasGauge && tick.metrics.readouts.intelligence ? box.scoreArea : nil
        return BoardContentLayout(items: items, groupRows: anchors, number: number, subRows: subRows, scoreArea: scoreArea)
    }

    /// The slab's 135° hatch, and the heat rail along the top edge as an item of its own (a new heat redraws only the
    /// rail).
    private func slab(_ tick: BoardTick) -> [BoardContentItem] {
        let heat = (tick.heat * 100).rounded() / 100
        let palette = self.palette, side = self.side
        let hatch = BoardContentItem(key: "ambience", frame: CGRect(x: 0, y: 0, width: side, height: side), motion: .none,
                                     revision: AnyHashable([AnyHashable("slab"), AnyHashable(palette.dark),
                                                            AnyHashable(palette.highContrast)])) { context, _ in
            guard !palette.highContrast else { return }
            let hatch = (palette.dark ? RGBColor.white : RGBColor.black).opacity(0.025)
            context.setStrokeColor(hatch.cgColor)
            context.setLineWidth(2)
            let step = 7 * 2.squareRoot()
            var c = -step
            while c < 2 * side + step {
                context.move(to: CGPoint(x: c + 1, y: -1))
                context.addLine(to: CGPoint(x: -1, y: c + 1))
                c += step
            }
            context.strokePath()
        }
        let reach: CGFloat = palette.glows ? 16 : 0
        // Drawn at full heat and stretched to the heat by its layer, so a rising rail slides instead of redrawing.
        var rail = BoardContentItem(key: "rail", frame: CGRect(x: 0, y: 0, width: side, height: 3 + reach), motion: .none,
                                    revision: AnyHashable([AnyHashable(palette.dark), AnyHashable(palette.highContrast)])) { context, _ in
            BoardDrawing.fill(CGRect(x: 0, y: 0, width: side, height: 3), color: palette.line, in: context)
            let rail = CGRect(x: 0, y: 0, width: side, height: 3)
            let stops: [(RGBColor, CGFloat)] = [(RGBColor(hex: 0x2DD4BF), 0), (RGBColor(hex: 0x818CF8), 0.4),
                                                 (RGBColor(hex: 0xFB7185), 0.75), (RGBColor(hex: 0xFACC15), 1)]
            BoardDrawing.glowing(palette.glows ? palette.heat(max(0.1, heat)).opacity(0.7) : nil, blur: 10, in: context) {
                BoardDrawing.gradient(rail, stops: stops, from: CGPoint(x: 0, y: 0), to: CGPoint(x: side, y: 0), in: $0)
            }
        }
        rail.stretch = heat
        return [hatch, rail]
    }

    private func hero(_ tick: BoardTick, leader: String?, geometry: BoardGeometry) -> ([BoardContentItem], CGRect) {
        let palette = self.palette, big = self.big, heroHeight = self.heroHeight, locale = l10n.locale
        let heat = (tick.heat * 50).rounded() / 50
        let live = BoardLabel(l10n("boards.live").uppercased(with: locale),
                              BoardFont(size: 10, weight: .bold, tracking: 0.14), color: palette.ink2)
        let look = BoardHeroNumber.Look(font: BoardFont(family: .mono, size: big, weight: .heavy, tracking: -0.04), color: palette.ink,
                                        hot: BoardPalette.green, hotGlow: palette.glows ? BoardPalette.green.opacity(0.8) : nil, hotBlur: 12)
        let unit = BoardLabel(l10n("unit.tokS").uppercased(with: locale),
                              BoardFont(family: .mono, size: 11, weight: .semibold, tracking: 0.04), color: palette.ink3)
        let title = BoardLabel(tick.title, BoardFont(size: 11, weight: .bold, tracking: 0.04), color: RGBColor(hex: 0x0B0D12))
        let tight = geometry.hasGauge && size == .small
        let metaGap: CGFloat = tight ? 4 : 7
        let counts = BoardLabel(l10n("agents.context", tick.generating, tick.tools).uppercased(with: locale),
                                BoardFont(family: .mono, size: 10.5, weight: .semibold, tracking: tight ? 0 : 0.03), color: palette.ink3)
        // The live line is a 18 pt line box (the page's strut) with its 10 pt label 6.5 pt down; the column outgrows
        // the hero at every size, so it starts at the top and the bars center on it. Beside the data box the live line
        // is its label's own 10 pt and the column centres in the band.
        let liveLine: CGFloat = geometry.hasGauge ? 10 : 18
        let liveDrop: CGFloat = geometry.hasGauge ? 0 : 6.5
        let column = liveLine + 6 + big * 1.08 + 7 + 17
        let top = geometry.hasGauge ? ((CGFloat(geometry.bandHeight) - column) / 2).rounded() : max(0, ((heroHeight - column) / 2).rounded())
        let numTop = top + liveLine + 6
        let barCount = size == .small ? 20 : 30
        // The meta line runs past the hero's height into the rows' top, as on the page.
        let frame = CGRect(x: pad, y: pad + 2, width: side - 2 * pad, height: max(heroHeight, top + column).rounded(.up))
        let number = BoardHeroNumber(tick.valueText, value: tick.value, look: look, origin: CGPoint(x: frame.minX, y: frame.minY + numTop))
        let history = Array(tick.history.suffix(30).map { ($0 * 10).rounded() / 10 })
        let generating = tick.generating > 0
        let theme = AnyHashable([AnyHashable(palette.dark), AnyHashable(palette.highContrast), AnyHashable(l10n.language.rawValue)])
        let unitX = number.label.width + 6
        let now = Clock.now
        let boxWidth = geometry.hasGauge ? box.width(tick.metrics, now: now) : 0
        // Beside the data box the meta line keeps to the left column (the context truncates first, then the title).
        let room = geometry.hasGauge ? frame.width - boxWidth - CGFloat(BoardGeometry.heroGap) : frame.width
        // The live line and unit, the meta line and the bars are cached apart (a new history redraws only the bars).
        var hero = BoardContentItem(key: "hero", frame: frame, motion: .none, revision: AnyHashable([AnyHashable(unitX), theme])) { context, _ in
            live.draw(in: context, x: 13, baseline: live.baseline(lineBoxTop: top + liveDrop))
            unit.draw(in: context, x: unitX, baseline: numTop + number.baseline)
        }
        let pillTop = numTop + big * 1.08 + 7
        let shownTitle = title.truncated(to: max(0, room - 14))
        let pill = CGRect(x: 0, y: pillTop, width: shownTitle.width + 14, height: 17)
        let pillColor = HeatPalette.color(max(0.1, heat))
        let meta = BoardContentItem.part("hero.meta", of: frame, rect: CGRect(x: 0, y: pillTop - 2, width: frame.width, height: 21),
                                         revision: AnyHashable([AnyHashable(tick.title), AnyHashable(tick.generating),
                                                                AnyHashable(tick.tools), AnyHashable(heat), AnyHashable(room), theme])) { context in
            BoardDrawing.fill(pill, radius: 5, color: pillColor, in: context)
            shownTitle.draw(in: context, x: 7, baseline: title.baseline(centeredOn: pill.midY))
            // The meta line sits below the bars, so the counts may run under them (the page lets them overflow).
            counts.truncated(to: max(0, room - pill.width - metaGap))
                .draw(in: context, x: pill.maxX + metaGap, baseline: counts.baseline(centeredOn: pill.midY))
        }
        let barsWidth = CGFloat(barCount) * 5 - 2
        let barsRect = CGRect(x: frame.width - barsWidth, y: top + (max(heroHeight, column) - 38) / 2, width: barsWidth, height: 38)
        let barColor = palette.heat(max(0.1, heat))
        let bars: BoardContentItem
        if geometry.hasStrip {
            let strip = CGRect(x: pad, y: CGFloat(geometry.stripTop), width: side - 2 * pad, height: CGFloat(geometry.stripHeight))
            bars = .part("hero.strip", of: CGRect(x: 0, y: 0, width: side, height: side), rect: strip.insetBy(dx: -1, dy: -1),
                         revision: AnyHashable([AnyHashable(history), AnyHashable(heat), AnyHashable(strip.height), theme])) { context in
                BoardHeroHistory.stripBars(history, in: strip, color: barColor, context: context)
            }
        } else {
            bars = .part("hero.bars", of: frame, rect: barsRect.insetBy(dx: -1, dy: -1),
                         revision: AnyHashable([AnyHashable(history), AnyHashable(heat), theme])) { context in
                BoardHeroHistory.bars(history, count: barCount, in: barsRect, color: barColor, context: context)
            }
        }
        if memory.fresh, let leader, tick.events.contains(where: { $0.kind == .newLeader }) {
            hero.accents.append(.decal(banner(leader, width: side, top: heroHeight - 16)))
        }

        let dotFrame = CGRect(x: frame.minX, y: frame.minY + top + liveDrop + 1.5, width: 7, height: 7)
        var dot = BoardContentItem(key: "live", frame: dotFrame, motion: .none, revision: AnyHashable([generating, palette.dark])) { context, box in
            BoardDrawing.fillCircle(CGRect(origin: .zero, size: box), color: generating ? BoardPalette.red : palette.ink3, in: context)
        }
        dot.ambient = generating ? .pulse(period: 1.6) : nil

        // The tower has no room above its number: the chip pops on the live line, after `boards.live`.
        let chip = CGPoint(x: 13 + live.width + 8, y: top + liveDrop + 5 - 7.75 - numTop)
        let decals = tick.newPeak ? [number.sheen(), BoardHeroNumber.peakChip(style: .timing, l10n: l10n, glows: palette.glows, origin: chip)] : []
        var items = [hero, meta, bars, dot] + number.items(memory: memory, decals: decals)
        if geometry.hasGauge {
            items += box.items(tick.metrics, in: BoardGaugeFrame(top: CGFloat(geometry.bandTop), height: CGFloat(geometry.bandHeight),
                                                                  right: side - pad, memory: memory, now: now,
                                                                    reduceMotion: reduceMotion))
        }
        return (items.inHero(), number.rect)
    }

    /// `boards.newLeader` sliding across the bottom of the hero for 1.9 s (`.banner`); in the hero's points.
    private func banner(_ name: String, width: CGFloat, top: CGFloat) -> BoardDecal {
        let marker = "\u{E000}"
        let parts = l10n("boards.newLeader", marker).components(separatedBy: marker)
        let ink = RGBColor(hex: 0x120D00)
        let caption = BoardFont(size: 11, weight: .heavy, tracking: 0.16)
        let labels = parts.enumerated().flatMap { index, part -> [BoardLabel] in
            let text = BoardLabel(part.trimmingCharacters(in: .whitespaces), caption, color: ink)
            return index < parts.count - 1 ? [text, BoardLabel(name, BoardFont(size: 11, weight: .bold, tracking: 0.02), color: ink)] : [text]
        }.filter { $0.width > 0 }
        let pad = self.pad
        return BoardDecal(frame: CGRect(x: -pad, y: top, width: width, height: 22), motion: .banner(seconds: 1.9), celebration: true) { context, box in
            BoardDrawing.gradient(CGRect(origin: .zero, size: box),
                                  stops: [(BoardPalette.gold, 0), (RGBColor(hex: 0xFFF1A8), 0.4), (BoardPalette.gold, 0.7), (BoardPalette.gold.opacity(0), 1)],
                                  from: .zero, to: CGPoint(x: box.width, y: 0), in: context)
            var x = pad
            for label in labels {
                label.truncated(to: max(0, box.width - x - pad)).draw(in: context, x: x, baseline: label.baseline(centeredOn: box.height / 2))
                x += label.width + 8
            }
        }
    }

    private func footer(_ tick: BoardTick) -> BoardContentItem {
        var parts = [BoardFooterPart("boards.today", value: tick.todayText, l10n: l10n, uppercase: true)]
        if let cost = tick.costText { parts.append(BoardFooterPart(value: cost, quiet: tick.costIsTag)) }
        if size != .small { parts.append(BoardFooterPart("boards.peak", value: tick.peakText, l10n: l10n, uppercase: true)) }
        let palette = self.palette, pad = self.pad
        let frame = CGRect(x: 0, y: side - 28, width: side, height: 28)
        let revision = AnyHashable([tick.todayText, tick.costText ?? "", tick.costIsTag ? "t" : "", tick.peakText, palette.dark ? "d" : "l",
                                    palette.highContrast ? "c" : "", l10n.language.rawValue])
        return BoardContentItem(key: "footer", frame: frame, motion: .none, revision: revision) { context, box in
            BoardDrawing.fill(CGRect(origin: .zero, size: box), color: palette.slab2, in: context)
            BoardDrawing.fill(CGRect(x: 0, y: 0, width: box.width, height: 1), color: palette.line, in: context)
            context.saveGState()
            context.translateBy(x: pad, y: 0)
            BoardFooterPart.draw(parts, label: BoardFont(family: .mono, size: 10, weight: .bold, tracking: 0.06),
                                 strong: BoardFont(family: .mono, size: 10, weight: .bold, tracking: 0.06),
                                 labelColor: palette.ink3, strongColor: palette.ink2, width: box.width - 2 * pad,
                                 midY: box.height / 2, in: context)
            context.restoreGState()
        }
    }
}
