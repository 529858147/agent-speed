import Foundation

/// Read-only access to OpenCode's `opencode.db` (METRICS 4.5, 5.2). Every query is its own short read transaction,
/// so OpenCode's checkpoints are never held back.
final class OpencodeDatabase {
    static let fileName = "opencode.db"
    static let requiredTables: Set<String> = ["session_v2", "session_message"]

    enum OpenError: Error, Equatable {
        case missing
        case unreadable
        /// The tables or columns of METRICS 4.5 are absent.
        case unsupported
    }

    struct SessionActivity: Equatable, Sendable {
        var rows: Int64
        var lastUpdatedMs: Int64
    }

    let path: String
    private let connection: SQLiteConnection
    private let sessionsStatement: SQLiteStatement
    private let sessionsSinceStatement: SQLiteStatement
    private let sessionRowsStatement: SQLiteStatement
    private let sessionRowsSinceStatement: SQLiteStatement
    private let rowsCreatedSinceStatement: SQLiteStatement
    private let maxCreatedStatement: SQLiteStatement

    private static let sessionColumns = "id, parent_id, directory, time_created, time_updated"
    private static let rowColumns = "id, session_id, type, seq, time_created, time_updated, data"

    init(path: String) throws(OpenError) {
        guard let stat = FileStat.stat(path), stat.isRegularFile else { throw .missing }
        self.path = path
        let connection: SQLiteConnection
        do {
            connection = try SQLiteConnection(path: path, mode: .readOnly)
        } catch {
            throw .unreadable
        }
        self.connection = connection
        do {
            let tables = try Self.tableNames(connection)
            guard Self.requiredTables.isSubset(of: tables) else { throw OpenError.unsupported }
            sessionsStatement = try connection.prepare("SELECT \(Self.sessionColumns) FROM session_v2")
            sessionsSinceStatement = try connection.prepare(
                "SELECT \(Self.sessionColumns) FROM session_v2 WHERE time_updated >= ?1")
            sessionRowsStatement = try connection.prepare(
                "SELECT \(Self.rowColumns) FROM session_message WHERE session_id = ?1 ORDER BY seq")
            sessionRowsSinceStatement = try connection.prepare(
                "SELECT \(Self.rowColumns) FROM session_message WHERE session_id = ?1 AND time_updated >= ?2 ORDER BY seq")
            rowsCreatedSinceStatement = try connection.prepare(
                "SELECT \(Self.rowColumns) FROM session_message WHERE time_created >= ?1 ORDER BY session_id, seq")
            maxCreatedStatement = try connection.prepare("SELECT max(time_created) FROM session_message")
        } catch let error as OpenError {
            throw error
        } catch let error as SQLiteError {
            throw Self.classify(error)
        } catch {
            throw .unreadable
        }
    }

    /// `opencode.db` inside a configured folder; a configured path that names the database file itself also works.
    static func databasePath(forRoot root: String) -> String {
        if let stat = FileStat.stat(root), stat.isRegularFile { return root }
        return (root as NSString).appendingPathComponent(fileName)
    }

    private static func tableNames(_ connection: SQLiteConnection) throws -> Set<String> {
        let statement = try connection.prepare("SELECT name FROM sqlite_master WHERE type = 'table'")
        var names: Set<String> = []
        try statement.forEachRow { row in
            if let name = row.string(0) { names.insert(name) }
        }
        return names
    }

    /// A prepare failure on an opened database means the schema differs.
    private static func classify(_ error: SQLiteError) -> OpenError {
        let primary = error.code & 0xFF
        return primary == 1 ? .unsupported : .unreadable
    }

    // MARK: - Sessions

    func sessions() throws -> [OpencodeSessionInfo] {
        try readSessions(sessionsStatement)
    }

    func sessions(updatedSince ms: Int64) throws -> [OpencodeSessionInfo] {
        sessionsSinceStatement.bind(1, ms)
        return try readSessions(sessionsSinceStatement)
    }

    private func readSessions(_ statement: SQLiteStatement) throws -> [OpencodeSessionInfo] {
        var sessions: [OpencodeSessionInfo] = []
        try statement.forEachRow { row in
            guard let id = row.string(0) else { return }
            sessions.append(OpencodeSessionInfo(
                id: id,
                parentId: row.string(1),
                directory: row.string(2),
                createdMs: row.int64(3),
                updatedMs: row.int64(4)
            ))
        }
        return sessions
    }

    /// Rows and the latest `time_updated` of every session (one scan of the table).
    func sessionActivity() throws -> [String: SessionActivity] {
        let statement = try connection.prepare(
            "SELECT session_id, count(*), max(time_updated) FROM session_message GROUP BY session_id")
        var activity: [String: SessionActivity] = [:]
        try statement.forEachRow { row in
            guard let id = row.string(0) else { return }
            activity[id] = SessionActivity(rows: row.int64(1), lastUpdatedMs: row.int64(2))
        }
        return activity
    }

    // MARK: - Rows

    /// Every row of a session in `seq` order. `skip` sees `(id, time_updated)` first and can avoid decoding `data`.
    func rows(session id: String, updatedSince ms: Int64? = nil, skip: (String, Int64) -> Bool = { _, _ in false }) throws -> [OpencodeRow] {
        let statement: SQLiteStatement
        if let ms {
            statement = sessionRowsSinceStatement
            statement.bind(2, ms)
        } else {
            statement = sessionRowsStatement
        }
        statement.bind(1, id)
        var rows: [OpencodeRow] = []
        try statement.forEachRow { row in
            if let decoded = Self.decodeRow(row, skip: skip) { rows.append(decoded.row) }
        }
        return rows
    }

    /// Rows created at or after `ms`, using the `time_created` index, with their session ids.
    func rows(createdSince ms: Int64, skip: (String, Int64) -> Bool = { _, _ in false }) throws -> [(session: String, row: OpencodeRow)] {
        rowsCreatedSinceStatement.bind(1, ms)
        var rows: [(session: String, row: OpencodeRow)] = []
        try rowsCreatedSinceStatement.forEachRow { row in
            if let decoded = Self.decodeRow(row, skip: skip) { rows.append(decoded) }
        }
        return rows
    }

    /// The current versions of the given rows.
    func rows(ids: [String], skip: (String, Int64) -> Bool = { _, _ in false }) throws -> [(session: String, row: OpencodeRow)] {
        guard !ids.isEmpty else { return [] }
        var rows: [(session: String, row: OpencodeRow)] = []
        let batch = 200
        var start = 0
        while start < ids.count {
            let slice = ids[start..<min(ids.count, start + batch)]
            let placeholders = Array(repeating: "?", count: slice.count).joined(separator: ",")
            let statement = try connection.prepare(
                "SELECT \(Self.rowColumns) FROM session_message WHERE id IN (\(placeholders)) ORDER BY session_id, seq")
            for (offset, id) in slice.enumerated() { statement.bind(Int32(offset + 1), id) }
            try statement.forEachRow { row in
                if let decoded = Self.decodeRow(row, skip: skip) { rows.append(decoded) }
            }
            start += batch
        }
        return rows
    }

    func maxCreatedMs() throws -> Int64? {
        var value: Int64?
        try maxCreatedStatement.forEachRow { row in value = row.optionalInt64(0) }
        return value
    }

    private static func decodeRow(_ row: SQLiteStatement, skip: (String, Int64) -> Bool) -> (session: String, row: OpencodeRow)? {
        guard let id = row.string(0), let session = row.string(1) else { return nil }
        let updated = row.int64(5)
        if skip(id, updated) { return nil }
        let data = row.withText(6) { OpencodeDataExtractor.extract($0) }
        return (session, OpencodeRow(
            id: id,
            seq: row.int64(3),
            kind: OpencodeRow.kind(of: row.string(2)),
            timeCreated: row.int64(4),
            timeUpdated: updated,
            data: data
        ))
    }
}
