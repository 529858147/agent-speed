import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("GPT-6.1 Terra pricing")
struct GPT61TerraPricingTests {
    private let at: Int64 = 1_793_059_200_000 // 2026-10-27 UTC

    @Test func canonicalAndRoutedNamesAreReportable() {
        let table = PriceBook.bundled
        for model in ["gpt-6.1-terra", "openai/gpt-6.1-terra", "gpt-6.1-terra-fast"] {
            #expect(table.reportModelId(model: model, provider: "openai") == "gpt-6.1-terra")
        }
        #expect(table.reportModelId(model: "gpt-5.6-terra", provider: "openai") == "gpt-5.6-terra")
        #expect(table.reportModelId(model: "gpt-6.1-terra-unpublished", provider: "openai") == "unknown")
    }

    @Test func standardPricesSeparateCacheReadsAndWrites() {
        let table = PriceBook.bundled
        let cases: [(TokenUsage, Int64)] = [
            (TokenUsage(input: 1000, output: 100), 1500),
            (TokenUsage(input: 1000, cachedInput: 1000), 50),
            (TokenUsage(input: 1000, cacheWriteInput: 1000), 1250),
            (TokenUsage(input: 1000, cachedInput: 800, cacheWriteInput: 100, output: 200), 1265),
        ]
        for (usage, expected) in cases {
            #expect(table.cost(model: "gpt-6.1-terra", provider: "openai", usage: usage, endMs: at, fast: false) == expected)
        }
        #expect(table.cost(model: "gpt-5.6-terra", provider: "openai", usage: TokenUsage(input: 1000, output: 100), endMs: at, fast: false) == 3200)
    }

    @Test func longContextStartsAbove272KAndPricesTheWholeRequest() {
        let table = PriceBook.bundled
        let cases: [(TokenUsage, Int64)] = [
            (TokenUsage(input: 272000, output: 2), 272010),
            (TokenUsage(input: 272001, output: 2), 544017),
            (TokenUsage(input: 272001, cachedInput: 272000, output: 2), 27217),
            (TokenUsage(input: 400000, cachedInput: 200000, cacheWriteInput: 100000, output: 200), 471500),
        ]
        for (usage, expected) in cases {
            #expect(table.cost(model: "gpt-6.1-terra", provider: "openai", usage: usage, endMs: at, fast: false) == expected)
        }
    }

    @Test func fastFlagAndSuffixUseTheSameDoubleRate() {
        let table = PriceBook.bundled
        for (usage, expected) in [
            (TokenUsage(input: 1000, cachedInput: 800, cacheWriteInput: 100, output: 200), Int64(2530)),
            (TokenUsage(input: 400000, cachedInput: 200000, cacheWriteInput: 100000, output: 200), Int64(943000)),
        ] {
            #expect(table.cost(model: "gpt-6.1-terra", provider: "openai", usage: usage, endMs: at, fast: true) == expected)
            #expect(table.cost(model: "gpt-6.1-terra-fast", provider: "openai", usage: usage, endMs: at, fast: false) == expected)
        }
    }
}
