import MetalKit
import SwiftUI

/// Hosts the GPU-drawn layers of the orb. Runs at 60 fps while active and draws only on demand while paused.
struct OrbMetalView: NSViewRepresentable {
    let mode: OrbAnimator.FrameMode
    /// Changes whenever a paused orb must redraw once (content or settled values changed).
    let redrawKey: AnyHashable
    let scene: @MainActor (CGSize, CGFloat, TimeInterval) -> OrbScene

    func makeNSView(context: Context) -> OrbMTKView {
        let view = OrbMTKView()
        view.sceneProvider = scene
        view.apply(mode)
        return view
    }

    func updateNSView(_ view: OrbMTKView, context: Context) {
        view.sceneProvider = scene
        view.apply(mode)
        if view.redrawKey != redrawKey {
            view.redrawKey = redrawKey
            if view.isPaused { view.needsDisplay = true }
        }
    }

    static func dismantleNSView(_ view: OrbMTKView, coordinator: ()) {
        view.isPaused = true
        view.sceneProvider = nil
    }
}

final class OrbMTKView: MTKView, MTKViewDelegate {
    var sceneProvider: (@MainActor (CGSize, CGFloat, TimeInterval) -> OrbScene)?
    var redrawKey: AnyHashable?
    private let encoder: OrbEncoder?
    private var mode: OrbAnimator.FrameMode?

    init() {
        let gpu = OrbGPU.shared
        encoder = gpu.map(OrbEncoder.init)
        super.init(frame: .zero, device: gpu?.device)
        colorPixelFormat = OrbGPU.pixelFormat
        framebufferOnly = true
        clearColor = MTLClearColor(red: 0, green: 0, blue: 0, alpha: 0)
        layer?.isOpaque = false
        (layer as? CAMetalLayer)?.colorspace = CGColorSpace(name: CGColorSpace.sRGB)
        delegate = self
    }

    @available(*, unavailable)
    required init(coder: NSCoder) {
        fatalError("init(coder:) is not supported")
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    override var isOpaque: Bool { false }

    func apply(_ newMode: OrbAnimator.FrameMode) {
        guard newMode != mode else { return }
        mode = newMode
        switch newMode {
        case .active:
            enableSetNeedsDisplay = false
            preferredFramesPerSecond = 60
            isPaused = false
        case .paused:
            isPaused = true
            enableSetNeedsDisplay = true
            needsDisplay = true
        }
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {}

    func draw(in view: MTKView) {
        guard let encoder, let sceneProvider, bounds.width > 0,
              let pass = currentRenderPassDescriptor, let drawable = currentDrawable else { return }
        let scale = window?.backingScaleFactor ?? 2
        let scene = sceneProvider(bounds.size, scale, Clock.now)
        encoder.render(scene, size: bounds.size, scale: scale, pass: pass, present: drawable)
    }
}

/// Renders a scene offscreen (previews).
enum OrbSnapshot {
    static func image(_ scene: OrbScene, side: CGFloat, scale: CGFloat) -> CGImage? {
        guard let gpu = OrbGPU.shared else { return nil }
        let pixels = Int((side * scale).rounded())
        let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: OrbGPU.pixelFormat, width: pixels, height: pixels, mipmapped: false)
        descriptor.usage = [.renderTarget]
        descriptor.storageMode = .shared
        guard let texture = gpu.device.makeTexture(descriptor: descriptor) else { return nil }
        let pass = MTLRenderPassDescriptor()
        pass.colorAttachments[0].texture = texture
        pass.colorAttachments[0].loadAction = .clear
        pass.colorAttachments[0].storeAction = .store
        pass.colorAttachments[0].clearColor = MTLClearColor(red: 0, green: 0, blue: 0, alpha: 0)
        OrbEncoder(gpu: gpu).render(scene, size: CGSize(width: side, height: side), scale: scale, pass: pass, present: nil, waitUntilCompleted: true)
        let bytesPerRow = pixels * 4
        var bytes = [UInt8](repeating: 0, count: bytesPerRow * pixels)
        texture.getBytes(&bytes, bytesPerRow: bytesPerRow, from: MTLRegionMake2D(0, 0, pixels, pixels), mipmapLevel: 0)
        guard let provider = CGDataProvider(data: Data(bytes) as CFData) else { return nil }
        return CGImage(
            width: pixels, height: pixels, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: bytesPerRow,
            space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue),
            provider: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent
        )
    }
}
