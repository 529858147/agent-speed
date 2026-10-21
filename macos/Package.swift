// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AgentSpeed",
    defaultLocalization: "en",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "AgentSpeed", targets: ["AgentSpeed"]),
    ],
    targets: [
        .target(
            name: "AgentSpeedCore",
            path: "Sources/AgentSpeedCore"
        ),
        .executableTarget(
            name: "AgentSpeed",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeed"
        ),
        .testTarget(
            name: "AgentSpeedCoreTests",
            dependencies: ["AgentSpeedCore"],
            path: "Tests/AgentSpeedCoreTests"
        ),
    ]
)
