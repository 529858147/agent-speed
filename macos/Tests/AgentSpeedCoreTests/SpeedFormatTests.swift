import Testing
@testable import AgentSpeedCore

@Test(arguments: [
    (0.0, "0"), (4.24, "4.2"), (9.96, "10"), (128.4, "128"), (999.6, "1.00k"),
    (1284, "1.28k"), (9999, "10.0k"), (12_840, "12.8k"), (128_400, "128k"), (999_700, "1.00M"), (1_284_000, "1.28M"),
])
func speedFormatting(value: Double, expected: String) {
    #expect(SpeedFormat.speed(value) == expected)
}
