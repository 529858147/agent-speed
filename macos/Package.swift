// swift-tools-version: 6.0
import Foundation
import PackageDescription

/// The macOS SDK this build compiles against. SwiftPM sets `SDKROOT` while it evaluates the manifest; `xcrun` is the
/// fallback.
let sdkPath: String? = {
    let fileManager = FileManager.default
    if let root = ProcessInfo.processInfo.environment["SDKROOT"], root.hasPrefix("/"),
       fileManager.fileExists(atPath: root + "/SDKSettings.json") {
        return root
    }
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/xcrun")
    process.arguments = ["--sdk", "macosx", "--show-sdk-path"]
    let output = Pipe()
    process.standardOutput = output
    process.standardError = FileHandle.nullDevice
    guard (try? process.run()) != nil else { return nil }
    process.waitUntilExit()
    let path = String(decoding: output.fileHandleForReading.readDataToEndOfFile(), as: UTF8.self)
        .trimmingCharacters(in: .whitespacesAndNewlines)
    guard process.terminationStatus == 0, fileManager.fileExists(atPath: path + "/SDKSettings.json") else { return nil }
    return path
}()

/// The linker driver gets the SDK only as `--sysroot`, from which it cannot read the SDK version, so executables were
/// stamped with the deployment target as their SDK (LC_BUILD_VERSION `sdk 14.0`) and macOS 26+ ran them in the
/// compatibility appearance. `-isysroot` lets it read SDKSettings.json and stamp the real SDK; `minos` stays 14.0.
let sdkStamp: [LinkerSetting] = sdkPath.map { [.unsafeFlags(["-Xclang-linker", "-isysroot", "-Xclang-linker", $0])] } ?? []

let package = Package(
    name: "AgentSpeed",
    defaultLocalization: "en",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "AgentSpeed", targets: ["AgentSpeed"]),
    ],
    targets: [
        .target(
            name: "CZstd",
            path: "Sources/CZstd",
            exclude: ["LICENSE", "VENDOR.md"],
            publicHeadersPath: ".",
            cSettings: [
                .define("ZSTD_DISABLE_ASM", to: "1"),
                .define("ZSTD_LEGACY_SUPPORT", to: "0"),
                .define("ZSTD_TRACE", to: "0"),
            ]
        ),
        .target(
            name: "AgentSpeedCore",
            dependencies: ["CZstd"],
            path: "Sources/AgentSpeedCore"
        ),
        .executableTarget(
            name: "AgentSpeed",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeed",
            linkerSettings: sdkStamp
        ),
        .executableTarget(
            name: "AgentSpeedProbe",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeedProbe",
            linkerSettings: sdkStamp
        ),
        .testTarget(
            name: "AgentSpeedCoreTests",
            dependencies: ["AgentSpeedCore"],
            path: "Tests/AgentSpeedCoreTests"
        ),
    ]
)
