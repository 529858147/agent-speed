import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import Metal
import Testing
@testable import AgentSpeed

/// Issue #31 (an orb on a 1× secondary display drew only a few digits of its readout): on macOS every text of an orb —
/// value, unit, title, the network lines' arrows, digits and units — is drawn from a texture rasterized for the frame's
/// backing scale and holding ink, across scale switches mid-run (a window moving between a 2× and a 1× display) and
/// across the text cache emptying when full. Rendered with Metal; skipped where there is no GPU.
@Suite("Orb text across backing scales")
@MainActor
struct OrbTextScaleTests {
    private static func model() -> AppModel {
        _ = NSApplication.shared
        let suite = "AgentSpeedOrbTextScaleTests"
        UserDefaults.standard.removePersistentDomain(forName: suite)
        let defaults = UserDefaults(suiteName: suite) ?? .standard
        let model = AppModel(
            settings: AppSettings(defaults: defaults),
            updates: UpdateController(configuration: .current(environment: [:]), store: UpdateStore(defaults: defaults))
        )
        model.settings.language = .zhHans
        model.l10n.apply(.zhHans)
        return model
    }

    /// The `led` orb of the report: idle, `↓ 52 B/s` over `↑ 5.99 KB/s`.
    private static func stage(_ model: AppModel, diameter: CGFloat = 236) -> OrbPreviewStage {
        let stage = OrbPreviewStage(model: model, style: .led, diameter: diameter, dark: true)
        TrafficOrbPreviews.show(stage, rx: 52, tx: 5_990)
        stage.reset(value: 0)
        return stage
    }

    /// Problems with the text of the last frame: a texture rasterized for another scale, or one without ink.
    private static func problems(_ scene: OrbScene) -> [String] {
        let scale = scene.backingScale
        return scene.texts.enumerated().compactMap { index, draw in
            let texture = draw.texture.texture
            let density = CGFloat(texture.width) / CGFloat(draw.instance.rect.z)
            if abs(density - scale) > 0.01 { return "text \(index): \(density) px/pt in a \(scale)× frame" }
            var bytes = [UInt8](repeating: 0, count: texture.width * texture.height * 4)
            texture.getBytes(&bytes, bytesPerRow: texture.width * 4,
                             from: MTLRegionMake2D(0, 0, texture.width, texture.height), mipmapLevel: 0)
            let ink = stride(from: 3, to: bytes.count, by: 4).contains { bytes[$0] > 0 }
            return ink ? nil : "text \(index): no ink in a \(scale)× frame"
        }
    }

    @Test func everyTextIsDrawnAfterEachScaleSwitch() throws {
        guard OrbGPU.shared != nil else { return }
        let model = Self.model()
        let stage = Self.stage(model)
        defer { stage.stop() }
        var counts: [Int] = []
        for scale: CGFloat in [2, 1, 2, 1] {
            stage.backingScale = scale
            stage.run(for: 0.5, at: 0)
            let scene = stage.lastScene
            #expect(scene.backingScale == scale)
            #expect(Self.problems(scene).isEmpty, "\(Self.problems(scene))")
            counts.append(scene.texts.count)
        }
        // Value, unit, title and both network lines (arrow, digits, unit) at every scale.
        #expect(counts.allSatisfy { $0 == counts[0] && $0 >= 12 }, "text draws per frame: \(counts)")
    }

    @Test func twoOrbsAtDifferentScalesShareTheCacheWithoutHoles() throws {
        guard OrbGPU.shared != nil else { return }
        let model = Self.model()
        let main = Self.stage(model, diameter: 148)
        let secondary = Self.stage(model)
        defer {
            main.stop()
            secondary.stop()
        }
        secondary.backingScale = 1
        for _ in 0..<30 {
            main.step { _ in 0 }
            secondary.step { _ in 0 }
        }
        #expect(Self.problems(main.lastScene).isEmpty, "\(Self.problems(main.lastScene))")
        #expect(Self.problems(secondary.lastScene).isEmpty, "\(Self.problems(secondary.lastScene))")
    }

    @Test func aFullCacheEmptiesWithoutLeavingHoles() throws {
        guard OrbGPU.shared != nil else { return }
        let model = Self.model()
        let stage = Self.stage(model)
        defer { stage.stop() }
        stage.backingScale = 1
        stage.run(for: 0.2, at: 0)
        // More distinct strings than the cache keeps: it empties at least once while the orb keeps drawing.
        for index in 0..<1_100 {
            _ = OrbTextCache.shared.texture("\(index)", style: OrbTextStyle(size: 9, weight: .regular), language: .en, scale: 1)
            if index % 97 == 0 { stage.step { _ in 0 } }
        }
        stage.step { _ in 0 }
        #expect(Self.problems(stage.lastScene).isEmpty, "\(Self.problems(stage.lastScene))")
    }
}
