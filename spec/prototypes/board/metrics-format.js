// Formatting for the board's new live metrics (board-metrics.html): money per DESIGN 9 (rate and amount forms),
// the intelligence index per METRICS 6.9 and the metric labels of spec/strings.json. Plain numbers stand in for the
// apps' decimal arithmetic; spec/fixtures/money-format.json is the shared check.
(function (global) {
  "use strict";

  // spec/assets/currencies.json (symbols and digits) with its fallback rates per US dollar.
  const CURRENCIES = {
    USD: { digits: 2, symbol: "US$", symbols: { en: "$" }, rate: 1 },
    TWD: { digits: 0, symbol: "NT$", rate: 32.552 },
    CNY: { digits: 2, symbol: "CN¥", symbols: { "zh-Hans": "¥" }, rate: 6.67476 },
    HKD: { digits: 2, symbol: "HK$", rate: 7.84589 },
    JPY: { digits: 0, symbol: "JP¥", symbols: { ja: "¥" }, rate: 157.728 },
    EUR: { digits: 2, symbol: "€", rate: 0.889437 },
    KRW: { digits: 0, symbol: "₩", rate: 1323.05 },
  };

  // intel.glyph, intel.thinking.glyph, cost.glyph, cost.subscription.tag (spec/strings.json).
  const LABELS = {
    "zh-Hant": { intel: "智力", thinking: "思考", cost: "費用", tokens: "tok/s", subscription: "訂閱" },
    en: { intel: "IQ", thinking: "TH", cost: "Cost", tokens: "tok/s", subscription: "Subscription" },
  };
  // currency.<code> in zh-Hant, for the accessibility labels.
  const NAMES = { USD: "美元", TWD: "新臺幣", CNY: "人民幣", HKD: "港幣", JPY: "日圓", EUR: "歐元", KRW: "韓元" };
  function currencyName(code) { return NAMES[code] || code; }

  function symbolOf(code, lang) {
    const c = CURRENCIES[code];
    return (c.symbols && c.symbols[lang]) || c.symbol;
  }

  // Significant-digit rounding that moves to the next magnitude when it rounds up (0.0996 → 0.10).
  function significant(v, n, maxDecimals) {
    let mag = Math.floor(Math.log10(v));
    let dec = Math.min(maxDecimals, Math.max(0, n - 1 - mag));
    let text = (Math.round(v * Math.pow(10, dec) + 1e-9) / Math.pow(10, dec)).toFixed(dec);
    if (parseFloat(text) >= Math.pow(10, mag + 1)) {
      mag += 1; dec = Math.min(maxDecimals, Math.max(0, n - 1 - mag));
      text = parseFloat(text).toFixed(dec);
    }
    return text;
  }

  // Drop trailing fractional zeros beyond `d` decimals, then pad to `d`.
  function trim(text, d) {
    let [int, frac = ""] = text.split(".");
    while (frac.length > d && frac.endsWith("0")) frac = frac.slice(0, -1);
    while (frac.length < d) frac += "0";
    return frac ? int + "." + frac : int;
  }

  function group(intText) { return intText.replace(/\B(?=(\d{3})+(?!\d))/g, ","); }

  function compact(v) { return global.AgentSim ? global.AgentSim.formatSpeed(v) : String(Math.round(v)); }

  /** DESIGN 9 rate form: the number only. */
  function rateNumber(value, d) {
    if (value === 0) return "0";
    if (value < 5e-7) return "<0.000001";
    if (value < 1) {
      const t = significant(value, 2, 6);
      if (parseFloat(t) < 1) return trim(t, d);
      value = parseFloat(t);
    }
    if (value < 100) {
      const t = significant(value, 3, 6);
      if (parseFloat(t) < 100) return trim(t, d);
      value = parseFloat(t);
    }
    if (value < 999999.5) return group(String(Math.round(value)));
    return compact(value);
  }

  /** DESIGN 9 amount form: the number only. */
  function amountNumber(value, d) {
    if (value === 0) return (0).toFixed(d);
    if (value < 5e-7) return "<0.000001";
    if (value < 1) {
      const t = significant(value, 2, 6);
      if (parseFloat(t) < 1) return trim(t, d);
      value = parseFloat(t);
    }
    const fixed = (Math.round(value * Math.pow(10, d) + 1e-9) / Math.pow(10, d)).toFixed(d);
    if (parseFloat(fixed) < 1e6) { const [i, f] = fixed.split("."); return group(i) + (f ? "." + f : ""); }
    return compact(value);
  }

  function prefix(symbol) { return /[A-Za-z]$/.test(symbol) ? symbol + " " : symbol; }

  /**
   * A live cost rate. Returns { number, unit, symbol, partial, unknown }: the unit is `cost.unitPerSecond`
   * (`US$/s`); partial asks for the small `≥`; unknown means nothing generating is priced (`–`).
   */
  function rate(microUsdPerSecond, knownShare, code, lang) {
    const c = CURRENCIES[code], symbol = symbolOf(code, lang);
    const value = microUsdPerSecond * c.rate / 1e6;
    const unknown = knownShare === 0 && microUsdPerSecond === 0;
    return { number: unknown ? "–" : rateNumber(value, c.digits), unit: symbol + "/s", symbol,
             partial: !unknown && knownShare < 0.995 && value > 0, unknown };
  }

  /** An amount with its symbol in front (`US$42.35`, `NT$1,235`). */
  function amount(usd, code, lang) {
    const c = CURRENCIES[code];
    return prefix(symbolOf(code, lang)) + amountNumber(usd * c.rate, c.digits);
  }

  /** METRICS 6.9: rounded half away from zero; none → `–`. */
  function intelligence(value) { return value === null || value === undefined ? "–" : String(Math.round(value)); }

  global.MetricFormat = { CURRENCIES, LABELS, rate, amount, intelligence, rateNumber, amountNumber, symbolOf, currencyName };
})(typeof window !== "undefined" ? window : globalThis);
