import Foundation
import Testing
@testable import AgentSpeedCore

enum Fixtures {
    static let root: URL = URL(fileURLWithPath: #filePath)
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .appendingPathComponent("spec/fixtures", isDirectory: true)

    static func json(_ name: String) throws -> [String: Any] {
        let data = try Data(contentsOf: root.appendingPathComponent(name))
        return try #require(JSONSerialization.jsonObject(with: data) as? [String: Any])
    }
}

/// Splits a buffer into lines the way the tailer does (newline-terminated, last fragment included).
func forEachLine(in data: Data, _ body: (UnsafeRawBufferPointer) -> Void) {
    data.withUnsafeBytes { buffer in
        var start = 0
        for index in 0..<buffer.count where buffer[index] == 0x0A {
            body(UnsafeRawBufferPointer(rebasing: buffer[start..<index]))
            start = index + 1
        }
        if start < buffer.count {
            body(UnsafeRawBufferPointer(rebasing: buffer[start..<buffer.count]))
        }
    }
}

extension Dictionary where Key == String, Value == Any {
    func int64(_ key: String) -> Int64? { (self[key] as? NSNumber)?.int64Value }
    func int(_ key: String) -> Int { (self[key] as? NSNumber)?.intValue ?? 0 }
    func double(_ key: String) -> Double? { (self[key] as? NSNumber)?.doubleValue }
    func string(_ key: String) -> String? { self[key] as? String }
}
