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
        .target(
            name: "AgentSpeedHero",
            path: "Sources/AgentSpeedHero"
        ),
        .executableTarget(
            name: "AgentSpeed",
            dependencies: ["AgentSpeedCore", "AgentSpeedHero"],
            path: "Sources/AgentSpeed"
        ),
        .executableTarget(
            name: "AgentSpeedProbe",
            dependencies: ["AgentSpeedCore"],
            path: "Sources/AgentSpeedProbe"
        ),
        .testTarget(
            name: "AgentSpeedCoreTests",
            dependencies: ["AgentSpeedCore"],
            path: "Tests/AgentSpeedCoreTests"
        ),
        .testTarget(
            name: "AgentSpeedHeroTests",
            dependencies: ["AgentSpeedHero"],
            path: "Tests/AgentSpeedHeroTests"
        ),
    ]
)
