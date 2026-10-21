import Metal
import os

/// Shared Metal device and pipelines, created once on first use.
final class OrbGPU: Sendable {
    static let shared: OrbGPU? = OrbGPU()

    let device: MTLDevice
    let queue: MTLCommandQueue
    let over: MTLRenderPipelineState
    let additive: MTLRenderPipelineState
    let text: MTLRenderPipelineState
    /// The dashboard hero's vehicle layer (DESIGN 4c); nil if its shaders failed, which only hides the vehicle.
    let hero: HeroLayerGPU?
    static let pixelFormat = MTLPixelFormat.bgra8Unorm

    private init?() {
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else { return nil }
        do {
            let library = try device.makeLibrary(source: OrbShaderSource.source, options: nil)
            guard let vertex = library.makeFunction(name: "orb_vertex"),
                  let fragment = library.makeFunction(name: "orb_fragment") else { return nil }
            over = try Self.pipeline(device: device, vertex: vertex, fragment: fragment, additive: false)
            additive = try Self.pipeline(device: device, vertex: vertex, fragment: fragment, additive: true)
            guard let textVertex = library.makeFunction(name: "orb_text_vertex"),
                  let textFragment = library.makeFunction(name: "orb_text_fragment") else { return nil }
            text = try Self.pipeline(device: device, vertex: textVertex, fragment: textFragment, additive: false)
        } catch {
            Logger(subsystem: "io.github.zoefix.agentspeed", category: "orb").error("Orb shaders failed to compile")
            return nil
        }
        self.device = device
        self.queue = queue
        hero = HeroLayerGPU(device: device)
    }

    func pipeline(for blend: OrbBatch.Blend) -> MTLRenderPipelineState {
        blend == .over ? over : additive
    }

    private static func pipeline(device: MTLDevice, vertex: MTLFunction, fragment: MTLFunction, additive: Bool) throws -> MTLRenderPipelineState {
        let descriptor = MTLRenderPipelineDescriptor()
        descriptor.vertexFunction = vertex
        descriptor.fragmentFunction = fragment
        let attachment = descriptor.colorAttachments[0]!
        attachment.pixelFormat = pixelFormat
        attachment.isBlendingEnabled = true
        attachment.rgbBlendOperation = .add
        attachment.alphaBlendOperation = .add
        attachment.sourceRGBBlendFactor = .one
        attachment.destinationRGBBlendFactor = additive ? .one : .oneMinusSourceAlpha
        attachment.sourceAlphaBlendFactor = .one
        attachment.destinationAlphaBlendFactor = .oneMinusSourceAlpha
        return try device.makeRenderPipelineState(descriptor: descriptor)
    }
}
