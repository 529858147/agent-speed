import Foundation

/// Follows one transcript file: plain JSONL (METRICS 5), or a zstd frame stream for `.zstd` files (METRICS 5.1).
struct TranscriptReader: Sendable {
    private var plain: FileTailer?
    private var frames: ZstdFrameTailer?

    init(path: String, start: FileTailer.Start = .beginning, maxLineBytes: Int = FileTailer.defaultMaxLineBytes) {
        if Self.isFrameStream(path) {
            frames = ZstdFrameTailer(path: path, start: start, maxLineBytes: maxLineBytes)
        } else {
            plain = FileTailer(path: path, start: start, maxLineBytes: maxLineBytes)
        }
    }

    static func isFrameStream(_ path: String) -> Bool {
        path.hasSuffix(".zstd")
    }

    /// Reads every complete line appended since the last call. `onMalformed` reports frames that failed to decode.
    mutating func read(
        onReset: () -> Void = {},
        onMalformed: () -> Void = {},
        onLine: (UnsafeRawBufferPointer) -> Void
    ) -> FileTailer.ReadResult {
        if frames != nil {
            return frames!.read(onReset: onReset, onMalformed: onMalformed, onLine: onLine)
        }
        return plain!.read(onReset: onReset, onLine: onLine)
    }
}
