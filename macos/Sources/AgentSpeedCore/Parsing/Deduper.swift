import Foundation

/// Identifies one transcript file for deduplication ownership.
typealias FileID = Int32

/// Global, bounded set of response keys shared by every file (METRICS 3.4). FIFO eviction.
struct Deduper: Sendable {
    enum Claim: Equatable {
        /// The key was never seen; the caller now owns it.
        case new
        /// The key was first seen in the same file.
        case sameFile
        /// The key was first seen in another file.
        case otherFile
    }

    static let defaultCapacity = 65_536

    let capacity: Int
    private var owners: [String: FileID] = [:]
    private var ring: [String] = []
    private var head = 0

    init(capacity: Int = Deduper.defaultCapacity) {
        self.capacity = max(1, capacity)
        owners.reserveCapacity(min(self.capacity, 4096))
    }

    var count: Int { owners.count }

    func contains(_ key: String) -> Bool { owners[key] != nil }

    mutating func claim(_ key: String, for file: FileID) -> Claim {
        if let owner = owners[key] {
            return owner == file ? .sameFile : .otherFile
        }
        if ring.count < capacity {
            ring.append(key)
        } else {
            owners[ring[head]] = nil
            ring[head] = key
            head = (head + 1) % capacity
        }
        owners[key] = file
        return .new
    }
}
