import Foundation
import SystemConfiguration

/// The computer's name from System Settings → General → Sharing (DESIGN 11, Default username).
enum ReportComputerName {
    static var current: String? {
        #if DEBUG
        if let name = ProcessInfo.processInfo.environment["AGENT_SPEED_COMPUTER_NAME"] { return name }
        #endif
        guard let name = SCDynamicStoreCopyComputerName(nil, nil) as String?, !name.isEmpty else { return nil }
        return name
    }
}
