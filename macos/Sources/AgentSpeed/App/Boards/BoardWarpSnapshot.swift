import AgentSpeedOrbModel
import Metal
import QuartzCore

/// The neon warp as the render server draws it, for the offscreen frames: a `BoardWarpLayer` showing the look the
/// effects' heat asks for, rendered by `CARenderer` one second after it starts (every streak in flight).
@MainActor
enum BoardWarpSnapshot {
    static func image(_ fx: BoardEffects, side: Double, corner: CGFloat, dark: Bool, scale: CGFloat = 2) -> CGImage? {
        guard fx.warpRequested, dark, let device = OrbGPU.shared?.device, let queue = device.makeCommandQueue() else { return nil }
        let pixels = Int((side * scale).rounded())
        let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .bgra8Unorm, width: pixels, height: pixels,
                                                                  mipmapped: false)
        descriptor.usage = [.renderTarget, .shaderRead]
        descriptor.storageMode = .managed
        guard let texture = device.makeTexture(descriptor: descriptor) else { return nil }
        let root = CALayer()
        root.anchorPoint = .zero
        root.frame = CGRect(x: 0, y: 0, width: pixels, height: pixels)
        root.sublayerTransform = CATransform3DMakeScale(scale, scale, 1)
        // The texture's first row is the layer tree's bottom: flipped, the tree reads top to bottom as the frames do.
        root.isGeometryFlipped = true
        let warp = BoardWarpLayer()
        root.addSublayer(warp.layer)
        warp.setBoard(side: CGFloat(side), corner: corner)
        let renderer = CARenderer(mtlTexture: texture, options: [kCARendererMetalCommandQueue: queue])
        renderer.layer = root
        renderer.bounds = root.bounds
        CATransaction.flush()
        warp.update(BoardWarpLayer.Look(fx), running: true)
        CATransaction.flush()
        renderer.beginFrame(atTime: CACurrentMediaTime() + 1, timeStamp: nil)
        renderer.addUpdate(renderer.bounds)
        renderer.render()
        renderer.endFrame()
        guard let commandBuffer = queue.makeCommandBuffer(), let blit = commandBuffer.makeBlitCommandEncoder() else { return nil }
        blit.synchronize(resource: texture)
        blit.endEncoding()
        commandBuffer.commit()
        commandBuffer.waitUntilCompleted()
        var bytes = [UInt8](repeating: 0, count: pixels * pixels * 4)
        texture.getBytes(&bytes, bytesPerRow: pixels * 4, from: MTLRegionMake2D(0, 0, pixels, pixels), mipmapLevel: 0)
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: &bytes, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: pixels * 4,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue
                                          | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return nil }
        return context.makeImage()
    }
}
