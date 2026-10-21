import Darwin
import Foundation

struct DiscoveredFile: Equatable, Sendable {
    var path: String
    var stat: FileStat
}

/// Which file names under a root are transcripts (METRICS 2).
enum TranscriptNames: Sendable {
    /// `*.jsonl`
    case jsonl
    /// `session.v<N>.jsonl` or `session.v<N>.jsonl.zstd` (DeepSeek Harness).
    case dshSession

    func matches(_ name: String) -> Bool {
        switch self {
        case .jsonl:
            return name.hasSuffix(".jsonl")
        case .dshSession:
            let prefix = "session.v"
            guard name.hasPrefix(prefix) else { return false }
            let rest = name.utf8.dropFirst(prefix.utf8.count)
            let digits = rest.prefix { $0 >= UInt8(ascii: "0") && $0 <= UInt8(ascii: "9") }
            guard !digits.isEmpty else { return false }
            let suffix = rest.dropFirst(digits.count)
            return suffix.elementsEqual(".jsonl".utf8) || suffix.elementsEqual(".jsonl.zstd".utf8)
        }
    }
}

/// Finds transcript files under a root (METRICS 2): `*.jsonl` (DeepSeek: `session.v<N>.jsonl(.zstd)`), depth
/// limit 6, no symlinks, at most 20 000 files per root (the most recently modified ones).
enum TranscriptDiscovery {
    static let maxDepth = 6
    static let maxFiles = 20_000

    /// Removes trailing slashes so agent ids are stable.
    static func normalizedRoot(_ path: String) -> String {
        var root = path
        while root.count > 1, root.hasSuffix("/") { root.removeLast() }
        return root
    }

    /// True when the root exists, is a directory and can be listed.
    static func isAvailable(root: String) -> Bool {
        guard let info = FileStat.stat(root), info.isDirectory else { return false }
        return access(root, R_OK | X_OK) == 0
    }

    static func enumerate(
        root: String,
        names: TranscriptNames = .jsonl,
        maxDepth: Int = maxDepth,
        maxFiles: Int = maxFiles
    ) -> [DiscoveredFile] {
        var index = DirectoryIndex(root: root, names: names, maxDepth: maxDepth)
        return mostRecent(files(at: index.scan()), limit: maxFiles)
    }

    /// Keeps the `limit` most recently modified files.
    static func mostRecent(_ files: [DiscoveredFile], limit: Int = maxFiles) -> [DiscoveredFile] {
        guard files.count > limit else { return files }
        return Array(files.sorted { lhs, rhs in
            lhs.stat.modifiedNs != rhs.stat.modifiedNs ? lhs.stat.modifiedNs > rhs.stat.modifiedNs : lhs.path < rhs.path
        }.prefix(limit))
    }

    /// Stats candidate paths, keeping regular files only.
    static func files(at paths: [String]) -> [DiscoveredFile] {
        paths.compactMap { path in
            guard let stat = FileStat.lstat(path), stat.isRegularFile else { return nil }
            return DiscoveredFile(path: path, stat: stat)
        }
    }

    /// Startup reading order (METRICS 5): modification time ascending, then ordinal path comparison.
    /// Times compare at 100 ns resolution and paths by UTF-16 code units, matching .NET ordering.
    static func startupOrder(_ lhs: DiscoveredFile, _ rhs: DiscoveredFile) -> Bool {
        let lhsTicks = lhs.stat.modifiedNs / 100
        let rhsTicks = rhs.stat.modifiedNs / 100
        if lhsTicks != rhsTicks { return lhsTicks < rhsTicks }
        return lhs.path.utf16.lexicographicallyPrecedes(rhs.path.utf16)
    }
}
