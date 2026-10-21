import Foundation

/// Compact number formatting shared by every surface (METRICS section 7).
public enum SpeedFormat {
    public static func speed(_ value: Double) -> String {
        guard value.isFinite, value > 0 else { return "0" }
        if value < 10 {
            let rounded = (value * 10).rounded() / 10
            return rounded >= 10 ? "10" : fixed(rounded, digits: 1)
        }
        if value < 1000 {
            let rounded = value.rounded()
            return rounded >= 1000 ? "1.00k" : String(Int(rounded))
        }
        return compact(value)
    }

    public static func tokens(_ value: Int) -> String {
        guard value > 0 else { return "0" }
        if value < 1000 { return String(value) }
        return compact(Double(value))
    }

    private static func compact(_ value: Double) -> String {
        let thousands = value / 1000
        if round(thousands, digits: 2) < 10 { return fixed(thousands, digits: 2) + "k" }
        if round(thousands, digits: 1) < 100 { return fixed(thousands, digits: 1) + "k" }
        if thousands.rounded() < 1000 { return String(Int(thousands.rounded())) + "k" }
        let millions = value / 1_000_000
        if round(millions, digits: 2) < 10 { return fixed(millions, digits: 2) + "M" }
        if round(millions, digits: 1) < 100 { return fixed(millions, digits: 1) + "M" }
        return String(Int(millions.rounded())) + "M"
    }

    private static func round(_ value: Double, digits: Int) -> Double {
        let scale = pow(10, Double(digits))
        return (value * scale).rounded() / scale
    }

    private static func fixed(_ value: Double, digits: Int) -> String {
        String(format: "%.\(digits)f", locale: Locale(identifier: "en_US_POSIX"), value)
    }
}
