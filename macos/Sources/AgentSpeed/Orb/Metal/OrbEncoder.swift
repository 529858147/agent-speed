import Metal
import simd

/// Encodes an `OrbScene` into a render pass. Keeps a small ring of instance buffers so the CPU never waits on
/// the GPU.
final class OrbEncoder {
    private static let ringSize = 3
    private let gpu: OrbGPU
    private var buffers: [MTLBuffer?]
    private var index = 0
    private let semaphore = DispatchSemaphore(value: ringSize)

    init(gpu: OrbGPU) {
        self.gpu = gpu
        buffers = Array(repeating: nil, count: Self.ringSize)
    }

    /// Draws the scene into `pass` and commits. `scale` is pixels per point; `size` is in points.
    func render(_ scene: OrbScene, size: CGSize, scale: CGFloat, pass: MTLRenderPassDescriptor,
                present drawable: MTLDrawable?, waitUntilCompleted: Bool = false) {
        semaphore.wait()
        guard let commandBuffer = gpu.queue.makeCommandBuffer() else {
            semaphore.signal()
            return
        }
        let semaphore = semaphore
        commandBuffer.addCompletedHandler { _ in semaphore.signal() }

        let count = scene.instanceCount
        let stride = MemoryLayout<OrbInstance>.stride
        var buffer: MTLBuffer?
        if count > 0 {
            buffer = instanceBuffer(minimumLength: count * stride)
            if let pointer = buffer?.contents().bindMemory(to: OrbInstance.self, capacity: count) {
                var offset = 0
                for batch in scene.batches {
                    batch.instances.withUnsafeBufferPointer { source in
                        (pointer + offset).update(from: source.baseAddress!, count: source.count)
                    }
                    offset += batch.instances.count
                }
            }
        }

        if let encoder = commandBuffer.makeRenderCommandEncoder(descriptor: pass) {
            var uniforms = SIMD4<Float>(Float(size.width), Float(size.height), Float(scale), 0)
            encoder.setVertexBytes(&uniforms, length: MemoryLayout<SIMD4<Float>>.stride, index: 1)
            encoder.setFragmentBytes(&uniforms, length: MemoryLayout<SIMD4<Float>>.stride, index: 1)
            if let buffer {
                var offset = 0
                for batch in scene.batches where !batch.instances.isEmpty {
                    encoder.setRenderPipelineState(gpu.pipeline(for: batch.blend))
                    encoder.setVertexBuffer(buffer, offset: offset * stride, index: 0)
                    encoder.setFragmentBuffer(buffer, offset: offset * stride, index: 0)
                    encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 6, instanceCount: batch.instances.count)
                    offset += batch.instances.count
                }
            }
            if !scene.texts.isEmpty {
                encoder.setRenderPipelineState(gpu.text)
                for draw in scene.texts {
                    var instance = draw.instance
                    encoder.setVertexBytes(&instance, length: MemoryLayout<OrbTextInstance>.stride, index: 0)
                    encoder.setFragmentBytes(&instance, length: MemoryLayout<OrbTextInstance>.stride, index: 0)
                    encoder.setFragmentTexture(draw.texture.texture, index: 0)
                    encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 6)
                }
            }
            encoder.endEncoding()
        }
        if let drawable { commandBuffer.present(drawable) }
        commandBuffer.commit()
        if waitUntilCompleted { commandBuffer.waitUntilCompleted() }
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
