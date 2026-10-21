import Foundation

/// Parent links between agents (METRICS 6.7).
enum AgentParents {
    /// What parent resolution needs to know about one tracked agent.
    struct Member: Equatable, Sendable {
        var id: String
        var family: TranscriptFamily
        var thread: SessionThread?

        init(id: String, family: TranscriptFamily, thread: SessionThread? = nil) {
            self.id = id
            self.family = family
            self.thread = thread
        }
    }

    /// Parent id of every member that has one. Self references and cycles resolve to no parent.
    static func resolve(_ members: [Member]) -> [String: String] {
        var ids = Set<String>()
        // Codex threads and DeepSeek sessions, each by family: the earliest created file names a thread.
        var threads: [TranscriptFamily: [String: (createdMs: Int64, id: String)]] = [:]
        for member in members {
            ids.insert(member.id)
            guard member.family != .claude, let thread = member.thread, let threadId = thread.id else { continue }
            if let earliest = threads[member.family]?[threadId],
               earliest.createdMs < thread.createdMs
                || (earliest.createdMs == thread.createdMs && ordinal(earliest.id, precedes: member.id)) {
                continue
            }
            threads[member.family, default: [:]][threadId] = (thread.createdMs, member.id)
        }
        var parents: [String: String] = [:]
        for member in members {
            let parent: String?
            switch member.family {
            case .codex, .dsh:
                parent = member.thread?.parentId.flatMap { threads[member.family]?[$0]?.id }
            case .claude:
                parent = claudeParentPath(of: member.id).flatMap { ids.contains($0) ? $0 : nil }
            }
            if let parent, parent != member.id { parents[member.id] = parent }
        }
        breakCycles(&parents)
        return parents
    }

    /// `<dir>/<session>/subagents/<name>.jsonl` → `<dir>/<session>.jsonl`.
    static func claudeParentPath(of path: String) -> String? {
        guard let slash = path.lastIndex(of: "/") else { return nil }
        let folder = path[..<slash]
        let marker = "/subagents"
        guard folder.hasSuffix(marker) else { return nil }
        let session = folder.dropLast(marker.count)
        guard let last = session.last, last != "/" else { return nil }
        return session + ".jsonl"
    }

    /// Drops the link of every agent whose chain of parents loops, visiting agents in ordinal id order.
    static func breakCycles(_ parents: inout [String: String]) {
        for id in parents.keys.sorted(by: ordinal) {
            var seen: Set<String> = []
            var current: String? = id
            while let node = current, seen.insert(node).inserted {
                current = parents[node]
            }
            if current != nil { parents[id] = nil }
        }
    }

    /// Ordinal (UTF-16 code unit) order, as on Windows.
    static func ordinal(_ lhs: String, precedes rhs: String) -> Bool {
        lhs.utf16.lexicographicallyPrecedes(rhs.utf16)
    }
}
