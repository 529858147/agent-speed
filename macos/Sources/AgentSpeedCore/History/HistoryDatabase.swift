import Foundation

/// The persistent daily-history store (METRICS 8): file identities, the 64-bit hashes of the response keys each file
/// owns, per-file per-day contributions and day peaks. Only aggregates are stored — never keys in plain text or content.
final class HistoryDatabase {
    static let schemaVersion: Int64 = 1

    enum Failure: Error, Equatable {
        /// Written by a newer version of the app; left untouched.
        case newerSchema
    }

    /// A file (or OpenCode session) with its identity and contributions.
    struct Entry: Equatable, Sendable {
        var id: Int64?
        /// `TranscriptFamily` raw value, or `opencode`.
        var family: String
        var size: Int64
        var modified: Int64
        var version: Int64
        var days: [UsageKey: DayAggregate]
    }

    let path: String
    private let connection: SQLiteConnection

    /// Opens (creating when needed) the store at `path`; `:memory:` keeps it in memory.
    init(path: String) throws {
        self.path = path
        if path != ":memory:" {
            try FileManager.default.createDirectory(
                atPath: (path as NSString).deletingLastPathComponent, withIntermediateDirectories: true)
        }
        connection = try SQLiteConnection(path: path, mode: .readWriteCreate)
        if path != ":memory:" { try connection.execute("PRAGMA journal_mode = WAL") }
        try connection.execute("PRAGMA synchronous = NORMAL")
        try migrate()
    }

    static func inMemory() throws -> HistoryDatabase {
        try HistoryDatabase(path: ":memory:")
    }

    /// Removes a store and its WAL files.
    static func delete(at path: String) {
        for suffix in ["", "-wal", "-shm"] {
            try? FileManager.default.removeItem(atPath: path + suffix)
        }
    }

    private func migrate() throws {
        let version = try scalar("PRAGMA user_version") ?? 0
        guard version <= Self.schemaVersion else { throw Failure.newerSchema }
        guard version < Self.schemaVersion else { return }
        try connection.transaction {
            try connection.execute("""
                CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY,
                    path TEXT NOT NULL UNIQUE,
                    family TEXT NOT NULL,
                    size INTEGER NOT NULL,
                    modified INTEGER NOT NULL,
                    version INTEGER NOT NULL,
                    keys BLOB NOT NULL
                );
                CREATE TABLE IF NOT EXISTS contributions (
                    file_id INTEGER NOT NULL,
                    day TEXT NOT NULL,
                    source TEXT NOT NULL,
                    requests INTEGER NOT NULL,
                    input INTEGER NOT NULL,
                    cached_input INTEGER NOT NULL,
                    cache_write_input INTEGER NOT NULL,
                    output INTEGER NOT NULL,
                    reasoning INTEGER NOT NULL,
                    measured_output INTEGER NOT NULL,
                    measured_ms INTEGER NOT NULL,
                    unfinalized INTEGER NOT NULL,
                    agents INTEGER NOT NULL,
                    PRIMARY KEY (file_id, day, source)
                ) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS contribution_models (
                    file_id INTEGER NOT NULL,
                    day TEXT NOT NULL,
                    source TEXT NOT NULL,
                    model TEXT NOT NULL,
                    requests INTEGER NOT NULL,
                    output INTEGER NOT NULL,
                    PRIMARY KEY (file_id, day, source, model)
                ) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS day_peaks (day TEXT PRIMARY KEY, peak REAL NOT NULL) WITHOUT ROWID;
                PRAGMA user_version = \(Self.schemaVersion);
                """)
        }
    }

    private func scalar(_ sql: String) throws -> Int64? {
        let statement = try connection.prepare(sql)
        var value: Int64?
        try statement.forEachRow { value = $0.optionalInt64(0) }
        return value
    }

    // MARK: - Meta

    func meta(_ key: String) throws -> String? {
        let statement = try connection.prepare("SELECT value FROM meta WHERE key = ?1")
        statement.bind(1, key)
        var value: String?
        try statement.forEachRow { value = $0.string(0) }
        return value
    }

    func setMeta(_ key: String, _ value: String) throws {
        let statement = try connection.prepare(
            "INSERT INTO meta (key, value) VALUES (?1, ?2) ON CONFLICT(key) DO UPDATE SET value = excluded.value")
        statement.bind(1, key)
        statement.bind(2, value)
        try statement.run()
    }

    // MARK: - Files

    /// Every stored file with its contributions, by path.
    func loadEntries() throws -> [String: Entry] {
        var paths: [Int64: String] = [:]
        var entries: [String: Entry] = [:]
        let files = try connection.prepare("SELECT id, path, family, size, modified, version FROM files")
        try files.forEachRow { row in
            guard let path = row.string(1) else { return }
            let id = row.int64(0)
            paths[id] = path
            entries[path] = Entry(id: id, family: row.string(2) ?? "", size: row.int64(3), modified: row.int64(4),
                                  version: row.int64(5), days: [:])
        }
        let contributions = try connection.prepare("""
            SELECT file_id, day, source, requests, input, cached_input, cache_write_input, output, reasoning,
                   measured_output, measured_ms, unfinalized, agents FROM contributions
            """)
        try contributions.forEachRow { row in
            guard let path = paths[row.int64(0)], let day = row.string(1),
                  let source = row.string(2).flatMap(SourceKind.init(rawValue:)) else { return }
            var aggregate = DayAggregate()
            aggregate.totals = TokenTotals(
                requests: Int(row.int64(3)), input: Int(row.int64(4)), cachedInput: Int(row.int64(5)),
                cacheWriteInput: Int(row.int64(6)), output: Int(row.int64(7)), reasoning: Int(row.int64(8)))
            aggregate.measuredOutput = Int(row.int64(9))
            aggregate.measuredMs = row.int64(10)
            aggregate.unfinalized = Int(row.int64(11))
            aggregate.agents = Int(row.int64(12))
            entries[path]?.days[UsageKey(day: day, source: source)] = aggregate
        }
        let models = try connection.prepare("SELECT file_id, day, source, model, requests, output FROM contribution_models")
        try models.forEachRow { row in
            guard let path = paths[row.int64(0)], let day = row.string(1),
                  let source = row.string(2).flatMap(SourceKind.init(rawValue:)), let model = row.string(3) else { return }
            let key = UsageKey(day: day, source: source)
            guard entries[path]?.days[key] != nil else { return }
            entries[path]?.days[key]?.models[model] = ModelTally(requests: Int(row.int64(4)), output: Int(row.int64(5)))
        }
        return entries
    }

    /// Adds the key hashes owned by every stored file except `excluded` to a hashed deduper.
    func loadKeys(excluding excluded: Set<Int64>, into deduper: inout Deduper) throws {
        let statement = try connection.prepare("SELECT id, keys FROM files")
        var buffer: [UInt64] = []
        try statement.forEachRow { row in
            guard !excluded.contains(row.int64(0)) else { return }
            buffer.removeAll(keepingCapacity: true)
            row.withBlob(1) { Self.unpack($0, into: &buffer) }
            deduper.insertSettled(buffer)
        }
    }

    /// The key hashes one stored file owns.
    func keys(ofFile id: Int64) throws -> [UInt64] {
        let statement = try connection.prepare("SELECT keys FROM files WHERE id = ?1")
        statement.bind(1, id)
        var keys: [UInt64] = []
        try statement.forEachRow { row in row.withBlob(0) { Self.unpack($0, into: &keys) } }
        return keys
    }

    func storedKeyCount() throws -> Int64 {
        try scalar("SELECT coalesce(sum(length(keys)), 0) / 8 FROM files") ?? 0
    }

    /// Saves files with their owned keys and contributions in one transaction; returns their row ids by path.
    @discardableResult
    func save(_ files: [(path: String, entry: Entry, keys: [UInt64])]) throws -> [String: Int64] {
        guard !files.isEmpty else { return [:] }
        return try connection.transaction {
            let upsert = try connection.prepare("""
                INSERT INTO files (path, family, size, modified, version, keys) VALUES (?1, ?2, ?3, ?4, ?5, ?6)
                ON CONFLICT(path) DO UPDATE SET family = excluded.family, size = excluded.size,
                    modified = excluded.modified, version = excluded.version, keys = excluded.keys
                RETURNING id
                """)
            let clearDays = try connection.prepare("DELETE FROM contributions WHERE file_id = ?1")
            let clearModels = try connection.prepare("DELETE FROM contribution_models WHERE file_id = ?1")
            let insertDay = try connection.prepare("""
                INSERT INTO contributions (file_id, day, source, requests, input, cached_input, cache_write_input,
                    output, reasoning, measured_output, measured_ms, unfinalized, agents)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13)
                """)
            let insertModel = try connection.prepare("""
                INSERT INTO contribution_models (file_id, day, source, model, requests, output)
                VALUES (?1, ?2, ?3, ?4, ?5, ?6)
                """)
            var ids: [String: Int64] = [:]
            for (path, entry, keys) in files {
                upsert.bind(1, path)
                upsert.bind(2, entry.family)
                upsert.bind(3, entry.size)
                upsert.bind(4, entry.modified)
                upsert.bind(5, entry.version)
                Self.pack(keys) { upsert.bind(6, blob: $0) }
                var id: Int64?
                try upsert.forEachRow { id = $0.int64(0) }
                guard let id else { continue }
                ids[path] = id
                clearDays.bind(1, id)
                try clearDays.run()
                clearModels.bind(1, id)
                try clearModels.run()
                for (key, day) in entry.days {
                    insertDay.bind(1, id)
                    insertDay.bind(2, key.day)
                    insertDay.bind(3, key.source.rawValue)
                    insertDay.bind(4, day.totals.requests)
                    insertDay.bind(5, day.totals.input)
                    insertDay.bind(6, day.totals.cachedInput)
                    insertDay.bind(7, day.totals.cacheWriteInput)
                    insertDay.bind(8, day.totals.output)
                    insertDay.bind(9, day.totals.reasoning)
                    insertDay.bind(10, day.measuredOutput)
                    insertDay.bind(11, day.measuredMs)
                    insertDay.bind(12, day.unfinalized)
                    insertDay.bind(13, day.agents)
                    try insertDay.run()
                    for (model, tally) in day.models {
                        insertModel.bind(1, id)
                        insertModel.bind(2, key.day)
                        insertModel.bind(3, key.source.rawValue)
                        insertModel.bind(4, model)
                        insertModel.bind(5, tally.requests)
                        insertModel.bind(6, tally.output)
                        try insertModel.run()
                    }
                }
            }
            return ids
        }
    }

    // MARK: - Peaks

    func peaks() throws -> [String: Double] {
        let statement = try connection.prepare("SELECT day, peak FROM day_peaks")
        var peaks: [String: Double] = [:]
        try statement.forEachRow { row in
            if let day = row.string(0) { peaks[day] = row.double(1) }
        }
        return peaks
    }

    /// Keeps the larger of the stored and the given peak.
    func recordPeak(_ peak: Double, day: String) throws {
        let statement = try connection.prepare("""
            INSERT INTO day_peaks (day, peak) VALUES (?1, ?2)
            ON CONFLICT(day) DO UPDATE SET peak = max(peak, excluded.peak)
            """)
        statement.bind(1, day)
        statement.bind(2, peak)
        try statement.run()
    }

    // MARK: - Key packing

    /// Little-endian 8-byte hashes.
    private static func pack<T>(_ keys: [UInt64], _ body: (UnsafeRawBufferPointer) -> T) -> T {
        let little = keys.map(\.littleEndian)
        return little.withUnsafeBytes(body)
    }

    private static func unpack(_ bytes: UnsafeRawBufferPointer, into keys: inout [UInt64]) {
        let count = bytes.count / 8
        keys.reserveCapacity(keys.count + count)
        for index in 0..<count {
            keys.append(UInt64(littleEndian: bytes.loadUnaligned(fromByteOffset: index * 8, as: UInt64.self)))
        }
    }
}
