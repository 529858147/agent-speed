import Darwin
import Foundation

/// Reads complete lines appended to one file (METRICS 5). Read-only, never follows symlinks.
struct FileTailer: Sendable {
    static let defaultMaxLineBytes = 32 << 20
    static let chunkBytes = 4 << 20
    /// Files larger than this are seeded from their last `tailWindowBytes` only.
    static let largeFileBytes: Int64 = 256 << 20
    static let tailWindowBytes: Int64 = 64 << 20

    enum Start: Equatable, Sendable {
        case beginning
        /// Start at the given size; an unterminated last line there is skipped.
        case end(Int64)
        /// Start `bytes` before the end, aligned to the next newline.
        case tail(Int64)
    }

    struct ReadResult: Equatable, Sendable {
        var bytesRead = 0
        /// The file shrank or was replaced; offset and parser state must restart.
        var didReset = false
        var failed = false
        var stat: FileStat?
    }

    let path: String
    let maxLineBytes: Int
    private(set) var offset: Int64 = 0
    private(set) var identity: FileIdentity?
    private(set) var oversizedLines = 0
    private var start: Start?
    private var partial: [UInt8] = []
    /// Skipping bytes until the next newline (oversized or unaligned line).
    private var discarding = false

    init(path: String, start: Start = .beginning, maxLineBytes: Int = FileTailer.defaultMaxLineBytes) {
        self.path = path
        self.start = start
        self.maxLineBytes = max(1, maxLineBytes)
    }

    /// Reads every complete line appended since the last call.
    mutating func read(onReset: () -> Void = {}, onLine: (UnsafeRawBufferPointer) -> Void) -> ReadResult {
        var result = ReadResult()
        let descriptor = Darwin.open(path, O_RDONLY | O_NOFOLLOW | O_CLOEXEC | O_NONBLOCK)
        guard descriptor >= 0 else {
            result.failed = true
            return result
        }
        defer { Darwin.close(descriptor) }
        guard let stat = FileStat.fstat(descriptor), stat.isRegularFile else {
            result.failed = true
            return result
        }
        result.stat = stat

        if let identity, identity != stat.identity || stat.size < offset {
            restart()
            result.didReset = true
            onReset()
        }
        identity = stat.identity
        if let pending = start {
            position(at: pending, size: stat.size, descriptor: descriptor)
            start = nil
        }
        guard offset < stat.size else { return result }

        let capacity = Int(min(Int64(Self.chunkBytes), stat.size - offset))
        let buffer = UnsafeMutableRawBufferPointer.allocate(byteCount: capacity, alignment: 16)
        defer { buffer.deallocate() }
        while offset < stat.size {
            let wanted = Int(min(Int64(capacity), stat.size - offset))
            let count = Darwin.pread(descriptor, buffer.baseAddress, wanted, off_t(offset))
            if count < 0 {
                if errno == EINTR { continue }
                result.failed = true
                break
            }
            if count == 0 { break }
            consume(UnsafeRawBufferPointer(rebasing: buffer[0..<count]), onLine: onLine)
            offset += Int64(count)
            result.bytesRead += count
        }
        return result
    }

    /// Forgets everything; the next read starts at offset 0.
    mutating func restart() {
        offset = 0
        partial = []
        discarding = false
        start = nil
    }

    private mutating func position(at start: Start, size: Int64, descriptor: Int32) {
        switch start {
        case .beginning:
            offset = 0
        case .end(let recordedSize):
            offset = min(recordedSize, size)
            if offset > 0 {
                var last: UInt8 = 0
                if Darwin.pread(descriptor, &last, 1, off_t(offset - 1)) == 1, last != 0x0A {
                    discarding = true
                }
            }
        case .tail(let window):
            if size > window {
                offset = size - window
                discarding = true
            } else {
                offset = 0
            }
        }
    }

    private mutating func consume(_ chunk: UnsafeRawBufferPointer, onLine: (UnsafeRawBufferPointer) -> Void) {
        guard let base = chunk.baseAddress else { return }
        var lineStart = 0
        while lineStart < chunk.count {
            let remaining = chunk.count - lineStart
            if let newline = memchr(base + lineStart, 0x0A, remaining) {
                let lineEnd = base.distance(to: UnsafeRawPointer(newline))
                let fragment = UnsafeRawBufferPointer(rebasing: chunk[lineStart..<lineEnd])
                if discarding {
                    discarding = false
                } else if partial.isEmpty {
                    if fragment.count > maxLineBytes {
                        oversizedLines += 1
                    } else {
                        onLine(fragment)
                    }
                } else if partial.count + fragment.count > maxLineBytes {
                    oversizedLines += 1
                    releasePartial()
                } else {
                    partial.append(contentsOf: fragment)
                    partial.withUnsafeBytes { onLine($0) }
                    releasePartial()
                }
                lineStart = lineEnd + 1
            } else {
                let fragment = UnsafeRawBufferPointer(rebasing: chunk[lineStart..<chunk.count])
                if !discarding {
                    if partial.count + fragment.count > maxLineBytes {
                        oversizedLines += 1
                        releasePartial()
                        discarding = true
                    } else {
                        partial.append(contentsOf: fragment)
                    }
                }
                lineStart = chunk.count
            }
        }
    }

    private mutating func releasePartial() {
        if partial.capacity > 1 << 20 {
            partial = []
        } else {
            partial.removeAll(keepingCapacity: true)
        }
    }
}
