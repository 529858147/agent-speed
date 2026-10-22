import Metal
import simd

/// Encodes an `OrbScene` into a render pass. Keeps a small ring of grow-only instance buffers so the CPU never waits on
/// the GPU and a warm encoder never allocates buffers.
final class OrbEncoder {
    private static let ringSize = 3
    private let gpu: OrbGPU
    private var buffers: [MTLBuffer?]
    private var index = 0
    private let semaphore = DispatchSemaphore(value: ringSize)
    private lazy var heroEncoder: HeroLayerEncoder? = gpu.hero.map(HeroLayerEncoder.init)

    private struct Uniforms {
        var viewport: SIMD4<Float>
        var transform: SIMD4<Float>
    }

    init(gpu: OrbGPU) {
        self.gpu = gpu
        buffers = Array(repeating: nil, count: Self.ringSize)
    }

    /// GPU time of completed frames, for benchmarks.
    let gpuTime = OrbGPUTimer()

    /// Draws the scene into `pass` and commits. `scale` is pixels per point; `size` is in points.
    func render(_ scene: OrbScene, size: CGSize, scale: CGFloat, pass: MTLRenderPassDescriptor,
                present drawable: MTLDrawable?, waitUntilCompleted: Bool = false) {
        render(count: 1, size: size, scale: scale, pass: pass, present: drawable, waitUntilCompleted: waitUntilCompleted) { _ in
            (scene, CGRect(origin: .zero, size: size), nil)
        }
    }

    /// Draws `count` scenes into one pass and commits once (one drawable for several small orbs, e.g. the Settings
    /// style cards). Scene `i` is a canvas of `frame.size` points drawn at `frame` (points from the top-left of the
    /// `size` drawable) and clipped to `clip`; a nil clip draws the scene over the whole drawable as `render` does.
    func render(count: Int, size: CGSize, scale: CGFloat, pass: MTLRenderPassDescriptor, present drawable: MTLDrawable?,
                waitUntilCompleted: Bool = false, scene: (Int) -> (scene: OrbScene, frame: CGRect, clip: CGRect?)) {
        semaphore.wait()
        guard let commandBuffer = gpu.queue.makeCommandBuffer() else {
            semaphore.signal()
            return
        }
        let semaphore = semaphore
        let timer = gpuTime
        commandBuffer.addCompletedHandler { buffer in
            semaphore.signal()
            timer.record(buffer.gpuEndTime - buffer.gpuStartTime)
        }

        let stride = MemoryLayout<OrbInstance>.stride
        var total = 0
        var hasVehicle = false
        for index in 0..<count {
            let item = scene(index).scene
            total += item.instanceCount
            hasVehicle = hasVehicle || item.vehicle != nil
        }
        var buffer: MTLBuffer?
        if total > 0, let instances = instanceBuffer(minimumLength: total * stride) {
            buffer = instances
            var offset = 0
            for index in 0..<count {
                let item = scene(index).scene
                let itemCount = item.instanceCount
                guard itemCount > 0 else { continue }
                item.instances.withUnsafeBufferPointer { source in
                    guard let base = source.baseAddress else { return }
                    (instances.contents() + offset * stride).copyMemory(from: base, byteCount: itemCount * stride)
                }
                offset += itemCount
            }
        }

        if hasVehicle { gpu.hero?.configure(pass) }
        if let encoder = commandBuffer.makeRenderCommandEncoder(descriptor: pass) {
            var first = 0
            let pixels = CGSize(width: (size.width * scale).rounded(), height: (size.height * scale).rounded())
            for index in 0..<count {
                let item = scene(index)
                if let clip = item.clip {
                    encoder.setViewport(MTLViewport(originX: Double(item.frame.minX * scale), originY: Double(item.frame.minY * scale),
                                                    width: Double(item.frame.width * scale), height: Double(item.frame.height * scale),
                                                    znear: 0, zfar: 1))
                    let rect = CGRect(x: clip.minX * scale, y: clip.minY * scale, width: clip.width * scale, height: clip.height * scale)
                        .integral.intersection(CGRect(origin: .zero, size: pixels))
                    guard !rect.isNull, rect.width >= 1, rect.height >= 1 else {
                        first += item.scene.instanceCount
                        continue
                    }
                    encoder.setScissorRect(MTLScissorRect(x: Int(rect.minX), y: Int(rect.minY), width: Int(rect.width), height: Int(rect.height)))
                }
                encode(item.scene, size: item.frame.size, scale: scale, buffer: buffer, first: first, into: encoder)
                first += item.scene.instanceCount
            }
            encoder.endEncoding()
        }
        if let drawable { commandBuffer.present(drawable) }
        commandBuffer.commit()
        if waitUntilCompleted { commandBuffer.waitUntilCompleted() }
    }

    /// Encodes one scene whose instances start at `first` in `buffer`.
    private func encode(_ scene: OrbScene, size: CGSize, scale: CGFloat, buffer: MTLBuffer?, first: Int,
                        into encoder: MTLRenderCommandEncoder) {
        let stride = MemoryLayout<OrbInstance>.stride
        if let vehicle = scene.vehicle {
            heroEncoder?.encode(vehicle.list, origin: vehicle.origin, unit: vehicle.unit, viewport: size, scale: scale, into: encoder)
        }
        var uniforms = Uniforms(
            viewport: SIMD4(Float(size.width), Float(size.height), Float(scale), 0),
            transform: SIMD4(Float(scene.contentScale), Float(scene.center.x), Float(scene.center.y), 0)
        )
        var liquid = scene.liquid
        encoder.setFragmentTexture(gpu.palette, index: 0)
        for batch in scene.batches {
            uniforms.viewport.w = batch.clip
            switch batch.pass {
            case .shapes(let blend):
                guard let buffer, batch.count > 0 else { continue }
                encoder.setRenderPipelineState(gpu.pipeline(for: blend))
                encoder.setVertexBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 1)
                encoder.setFragmentBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 1)
                encoder.setVertexBuffer(buffer, offset: (first + batch.start) * stride, index: 0)
                encoder.setFragmentBuffer(buffer, offset: (first + batch.start) * stride, index: 0)
                encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 6, instanceCount: batch.count)
            case .liquid(let mode):
                var mode = mode
                encoder.setRenderPipelineState(gpu.liquid)
                encoder.setVertexBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 1)
                encoder.setFragmentBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 1)
                encoder.setVertexBytes(&liquid, length: MemoryLayout<OrbLiquidUniforms>.stride, index: 2)
                encoder.setFragmentBytes(&liquid, length: MemoryLayout<OrbLiquidUniforms>.stride, index: 2)
                encoder.setFragmentBytes(&mode, length: MemoryLayout<Int32>.stride, index: 3)
                encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 6)
            }
        }
        if !scene.texts.isEmpty {
            uniforms.viewport.w = 0
            encoder.setRenderPipelineState(gpu.text)
            encoder.setVertexBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 1)
            encoder.setFragmentBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 1)
            encoder.setFragmentBytes(&liquid, length: MemoryLayout<OrbLiquidUniforms>.stride, index: 2)
            for draw in scene.texts {
                var instance = draw.instance
                encoder.setVertexBytes(&instance, length: MemoryLayout<OrbTextInstance>.stride, index: 0)
                encoder.setFragmentBytes(&instance, length: MemoryLayout<OrbTextInstance>.stride, index: 0)
                encoder.setFragmentTexture(draw.texture.texture, index: 0)
                encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 6)
            }
        }
    }

    private func instanceBuffer(minimumLength: Int) -> MTLBuffer? {
        index = (index + 1) % Self.ringSize
        if let buffer = buffers[index], buffer.length >= minimumLength { return buffer }
        let length = max(minimumLength, 64 * 1024)
        let buffer = gpu.device.makeBuffer(length: length, options: .storageModeShared)
        buffers[index] = buffer
        return buffer
    }
}

/// Sums the GPU time of completed command buffers (written from Metal's completion queue).
final class OrbGPUTimer: @unchecked Sendable {
    private let lock = NSLock()
    private var total = 0.0
    private var frames = 0

    func record(_ seconds: Double) {
        guard seconds > 0, seconds < 1 else { return }
        lock.lock()
        total += seconds
        frames += 1
        lock.unlock()
    }

    /// Total GPU seconds and frames since the last call.
    func drain() -> (seconds: Double, frames: Int) {
        lock.lock()
        defer {
            total = 0
            frames = 0
            lock.unlock()
        }
        return (total, frames)
    }
}
