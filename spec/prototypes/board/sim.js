// Shared simulation for the leaderboard panel prototypes (spec/prototypes/board/*.html).
// Every direction renders the same data, so the comparison is about design only.
// Numbers, titles, heat and colors follow spec/METRICS.md 6-7 and spec/DESIGN.md 1; modes follow EXECUTION-MODES.
(function (global) {
  "use strict";

  const SOURCES = {
    codex: { name: "Codex", color: "#34D399" },
    claudeCode: { name: "Claude Code", color: "#FF8A65" },
    claudeDesktop: { name: "Claude Desktop", color: "#B39DFF" },
    deepseek: { name: "DSH", color: "#4D6BFE" },
    opencode: { name: "OpenCode", color: "#F4B13E" },
    gemini: { name: "Gemini CLI", color: "#34B8F0" },
  };

  const HEAT = [
    [0.0, "#7C8798"], [0.1, "#2DD4BF"], [0.3, "#38BDF8"], [0.48, "#818CF8"],
    [0.64, "#E879F9"], [0.78, "#FB7185"], [0.9, "#FB923C"], [1.0, "#FACC15"],
  ];

  const EFFORT_LEVELS = { off: 0, minimal: 1, low: 1, medium: 2, high: 3, xhigh: 4, max: 5, ultra: 5, ultracode: 5 };
  const BOLTS = { fast: 1, highspeed: 1, ultrafast: 2 };

  // Redline 2000 tok/s (DESIGN 1 default); the 13 title thresholds as fractions of R.
  const REDLINE = 2000;
  const TITLES = [
    ["walk", "步行", "WALKING", 0.5 / REDLINE], ["bicycle", "自行車", "BICYCLE", 0.02], ["ebike", "電動自行車", "E-BIKE", 0.03],
    ["motorcycle", "摩托車", "MOTORCYCLE", 0.04], ["car", "汽車", "CAR", 0.06], ["sportsCar", "跑車", "SPORTS CAR", 0.08],
    ["bulletTrain", "高鐵", "BULLET TRAIN", 0.12], ["raceCar", "賽車", "RACE CAR", 0.17], ["maglev", "磁浮列車", "MAGLEV", 0.24],
    ["jetliner", "噴射客機", "JETLINER", 0.34], ["fighterJet", "戰鬥機", "FIGHTER JET", 0.5],
    ["hypersonic", "極音速飛機", "HYPERSONIC", 0.7], ["rocket", "火箭", "ROCKET", 1.0],
  ];

  function hex(c) { return [1, 3, 5].map(i => parseInt(c.slice(i, i + 2), 16) / 255); }
  function toLinear(c) { return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }
  function toSrgb(c) { return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.pow(c, 1 / 2.4) - 0.055; }

  /** Heat color for h in 0..1, interpolated in linear RGB; returns [r, g, b] in 0..255. */
  function heatRgb(h) {
    h = Math.max(0, Math.min(1, h));
    let i = 0;
    while (i < HEAT.length - 2 && h > HEAT[i + 1][0]) i++;
    const [h0, c0] = HEAT[i], [h1, c1] = HEAT[i + 1];
    const t = (h - h0) / (h1 - h0);
    const a = hex(c0).map(toLinear), b = hex(c1).map(toLinear);
    return a.map((v, k) => Math.round(255 * toSrgb(v + (b[k] - v) * t)));
  }
  function heatColor(h, alpha) {
    const [r, g, b] = heatRgb(h);
    return alpha === undefined ? `rgb(${r},${g},${b})` : `rgba(${r},${g},${b},${alpha})`;
  }
  function heatOf(v) { return Math.min(1, Math.pow(Math.max(0, v) / REDLINE, 0.6)); }

  /** METRICS 7. */
  function formatSpeed(v) {
    if (!(v > 0)) return "0";
    if (v < 10) return v.toFixed(1);
    if (v < 999.5) return String(Math.round(v));
    const units = [[1e3, "k"], [1e6, "M"], [1e9, "B"]];
    for (let u = 0; u < units.length; u++) {
      const [scale, suffix] = units[u];
      const x = v / scale;
      const digits = x < 9.995 ? 2 : x < 99.95 ? 1 : 0;
      const text = x.toFixed(digits);
      if (parseFloat(text) < 1000 || u === units.length - 1) return text + suffix;
    }
    return String(v);
  }

  function titleFor(v) {
    let title = null;
    for (const [key, zh, en, at] of TITLES) if (v >= at * REDLINE) title = { key, zh, en };
    return title || { key: "idle", zh: "待機", en: "IDLE" };
  }

  // Small seeded PRNG so every reload tells the same story.
  function rng(seed) {
    let s = seed >>> 0 || 1;
    return () => { s ^= s << 13; s ^= s >>> 17; s ^= s << 5; return (s >>> 0) / 4294967296; };
  }

  const MODELS = {
    "claude-opus-5-5": [55, 115], "claude-sonnet-5-5": [80, 160], "claude-haiku-5-5": [150, 260],
    "gpt-6.1-sol": [90, 210], "deepseek-flash": [110, 180], "gemini-3.5-pro": [100, 190],
  };

  // Live intelligence, thinking and cost rate (METRICS 6.9, 9.5) for board-metrics.html. They are derived from the
  // state above and never draw from the shared random sequence, so every other page renders exactly as before.
  // Output list prices in USD per million tokens (spec/assets/prices.json; gpt-6.1-sol and gemini-3.5-pro assumed).
  const PRICES = { "claude-opus-5-5": 20, "claude-sonnet-5-5": 10, "claude-haiku-5-5": 0.5, "gpt-6.1-sol": 20,
                   "deepseek-flash": 1.2, "gemini-3.5-pro": 12 };
  function classOf(model) { const p = PRICES[model]; return p === undefined ? 80 : p >= 15 ? 100 : p >= 3 ? 80 : 60; }
  function hashUnit(text) {
    let h = 2166136261;
    for (let i = 0; i < text.length; i++) { h ^= text.charCodeAt(i); h = Math.imul(h, 16777619); }
    h ^= h >>> 16; h = Math.imul(h, 0x85ebca6b); h ^= h >>> 13; h = Math.imul(h, 0xc2b2ae35); h ^= h >>> 16;
    return (h >>> 0) / 4294967296;
  }
  /** METRICS 6.9 window sums over a set of agents; additive, so groups and the board use the same rule. */
  function aggregate(list) {
    let w = 0, wr = 0, r = 0, g = 0, cost = 0, known = 0;
    for (const a of list) {
      w += a.live; g += a.live * a.grade; cost += a.costRate;
      if (a.recordsReasoning) { wr += a.live; r += a.thinking; }
      if (a.priced) known += a.live;
    }
    const share = wr > 0 ? r / wr : 0;
    return { intelligence: w > 0.05 ? g / w + 20 * share : null, thinking: r, thinkingShare: share,
             costRate: cost, costKnownShare: w > 0.05 ? known / w : 1 };
  }

  function scenario(name) {
    const roots = [];
    const add = (o) => { roots.push(o); return o; };
    if (name === "duel") {
      add({ project: "opencdn", source: "claudeCode", model: "claude-opus-5-5", effort: "max", speed: null, subs: 2 });
      add({ project: "claude-manager", source: "codex", model: "gpt-6.1-sol", effort: "ultra", speed: "ultrafast", subs: 2 });
    } else if (name === "team") {
      add({ project: "opencdn", source: "claudeCode", model: "claude-opus-5-5", effort: "max", speed: null, subs: 3 });
      add({ project: "claude-manager", source: "codex", model: "gpt-6.1-sol", effort: "xhigh", speed: "fast", subs: 2 });
      add({ project: "agent-speed", source: "claudeCode", model: "claude-opus-5-5", effort: "ultracode", speed: null, subs: 4 });
      add({ project: "nebula", source: "opencode", model: "claude-sonnet-5-5", effort: "high", speed: null, subs: 0 });
      add({ project: "rain", source: "deepseek", model: "deepseek-flash", effort: null, speed: null, subs: 0 });
    } else if (name === "calm") {
      add({ project: "agent-speed", source: "claudeCode", model: "claude-opus-5-5", effort: "xhigh", speed: null, subs: 0, lazy: true });
      add({ project: "notes", source: "codex", model: "gpt-6.1-sol", effort: "medium", speed: null, subs: 0, lazy: true });
    } else {
      add({ project: "tihu", source: "claudeDesktop", model: "deepseek-flash", effort: null, speed: null, subs: 100, workflow: true });
      add({ project: "opencdn", source: "claudeCode", model: "claude-opus-5-5", effort: "max", speed: null, subs: 3 });
      add({ project: "claude-manager", source: "codex", model: "gpt-6.1-sol", effort: "ultra", speed: "ultrafast", subs: 2 });
      add({ project: "agent-speed", source: "claudeCode", model: "claude-opus-5-5", effort: "ultracode", speed: null, subs: 1 });
    }
    return roots;
  }

  function effortOf(value) {
    return value ? { value, level: EFFORT_LEVELS[value] ?? null, top: value === "ultra" || value === "ultracode" } : null;
  }

  function create(options) {
    const opts = Object.assign({ scenario: "swarm", seed: 7, tickMs: 500 }, options || {});
    const listeners = new Set();
    let random, agents, history, t, today, cost, peak, prevRanks, timer = null, scenarioName, costToday;
    let order = [], scores = new Map(), pressure = new Map();

    function makeAgent(id, parentId, root, label, lazy) {
      const [lo, hi] = MODELS[root.model];
      return {
        id, parentId, project: root.project, label, source: root.source, sourceName: SOURCES[root.source].name,
        sourceColor: SOURCES[root.source].color, model: root.model,
        effort: effortOf(parentId && root.workflow ? null : root.effort),
        speedMode: root.speed, bolts: BOLTS[root.speed] || 0,
        base: lo + (hi - lo) * random(), phase: "idle", phaseLeft: 1 + random() * 6, live: 0, inst: 0, window: [], target: 0,
        todayOutput: Math.round(2e5 + random() * 4e6), lazy: !!lazy, lastActive: 0,
        seed: hashUnit(id), grade: 0, thinking: 0, recordsReasoning: true, priced: true, costRate: 0, intelligence: null,
      };
    }

    function reset(name) {
      scenarioName = name;
      random = rng(opts.seed + name.length * 101);
      agents = []; history = []; t = 0; today = 128.4e6; cost = 42.1; peak = 9120; prevRanks = new Map(); costToday = 42.1;
      order = []; scores = new Map(); pressure = new Map();
      scenario(name).forEach((root, r) => {
        const rootId = "a" + r;
        agents.push(makeAgent(rootId, null, root, root.project, root.lazy));
        for (let i = 0; i < root.subs; i++) {
          const label = root.workflow ? "count-" + (i + 1) : ["explore", "review", "tests", "docs"][i % 4] + (i >= 4 ? "-" + i : "");
          const sub = makeAgent(rootId + "s" + i, rootId, root, label, root.lazy);
          if (root.workflow) { sub.phaseLeft = random() * 3; sub.base *= 0.75 + random() * 0.4; }
          agents.push(sub);
        }
      });
      for (let i = 0; i < 6; i++) advance(true);
    }

    function measure(a) {
      const e = a.effort, level = e && e.level !== null ? e.level + (e.top ? 1 : 0) : 2;
      a.grade = classOf(a.model) + 6 * level;
      // DeepSeek Harness does not record reasoning apart from output (METRICS 6.9).
      a.recordsReasoning = a.source !== "deepseek";
      // Reasoning share by effort, breathing ±20 % over ~7 s so the index moves with the work.
      const breath = 0.8 + 0.4 * (0.5 + 0.5 * Math.sin(t * 0.9 + a.seed * 6.283));
      a.thinking = a.recordsReasoning ? a.live * (0.07 + 0.05 * level) * (0.7 + 0.6 * a.seed) * breath : 0;
      // Ultrafast has no list price (cost.reason.noFastPrice); Fast is billed at 2x. Input and cache add 1.3-1.9x.
      a.priced = a.speedMode !== "ultrafast";
      a.costRate = a.priced ? a.live * (PRICES[a.model] || 0) * (1.3 + 0.6 * a.seed) * (a.bolts ? 2 : 1) : 0;
      // The agent's own live index (none while idle: rows then show its grade, quieter).
      a.intelligence = aggregate([a]).intelligence;
    }

    function nextPhase(a) {
      const busy = a.lazy ? 0.25 : a.parentId ? 0.72 : 0.6;
      if (a.phase === "generating") {
        a.phase = random() < 0.8 ? "tools" : "idle";
        a.phaseLeft = a.phase === "tools" ? 0.6 + random() * 3.5 : 2 + random() * 8;
      } else {
        a.phase = random() < busy ? "generating" : a.phase === "tools" ? "idle" : "tools";
        a.phaseLeft = a.phase === "generating" ? 2.5 + random() * 12 : 0.8 + random() * 5;
        if (a.phase === "generating") a.burst = 0.75 + random() * 0.55;
      }
    }

    function advance(silent) {
      const dt = opts.tickMs / 1000;
      t += dt;
      // Duel: two leaders trade places every few seconds.
      const duel = scenarioName === "duel";
      for (const a of agents) {
        a.phaseLeft -= dt;
        if (a.phaseLeft <= 0) nextPhase(a);
        const wobble = 0.82 + 0.36 * random();
        a.target = a.phase === "generating" ? a.base * (a.burst || 1) * wobble : 0;
        if (duel && !a.parentId) a.target *= 1 + 0.55 * Math.sin(t / 3.1 + (a.id === "a0" ? 0 : Math.PI));
        a.inst += (a.target - a.inst) * (a.phase === "generating" ? 0.6 : 0.85);
        if (a.inst < 0.05) a.inst = 0;
        a.window.push(a.inst);
        if (a.window.length > 6) a.window.shift();
        a.live = a.window.reduce((x, y) => x + y, 0) / 6;
        if (a.live < 0.05) a.live = 0;
        if (a.live > 0) a.lastActive = t;
        a.todayOutput += a.live * dt;
        measure(a);
      }
      // Occasional mode changes so capsules animate.
      if (!silent && random() < 0.04) {
        const roots = agents.filter(a => !a.parentId);
        const a = roots[Math.floor(random() * roots.length)];
        const ladder = ["medium", "high", "xhigh", "max"];
        if (a.effort && !a.effort.top) a.effort = effortOf(ladder[Math.floor(random() * ladder.length)]);
        if (a.source === "codex") { a.speedMode = ["fast", "ultrafast", null][Math.floor(random() * 3)]; a.bolts = BOLTS[a.speedMode] || 0; }
      }
      const total = agents.reduce((s, a) => s + a.live, 0);
      today += total * dt;
      cost += total * dt * 15e-6;
      costToday += agents.reduce((s, a) => s + a.costRate, 0) * dt / 1e6;
      peak = Math.max(peak, total);
      history.push(total);
      if (history.length > 120) history.shift();
      if (!silent) emit(total);
    }

    function snapshot(total) {
      const byRoot = new Map();
      for (const a of agents) {
        const key = a.parentId || a.id;
        if (!byRoot.has(key)) byRoot.set(key, { root: null, subs: [] });
        if (a.parentId) byRoot.get(key).subs.push(a); else byRoot.get(key).root = a;
      }
      const groups = [...byRoot.values()].map(g => {
        const subs = g.subs.slice().sort((x, y) => y.live - x.live || y.lastActive - x.lastActive || (x.id < y.id ? -1 : 1));
        const subLive = subs.reduce((s, a) => s + a.live, 0);
        const generating = [g.root, ...subs].filter(a => a.phase === "generating").length;
        return Object.assign({ id: g.root.id, root: g.root, subs, subLive, live: g.root.live + subLive, generating,
                 lastActive: Math.max(g.root.lastActive, ...subs.map(s => s.lastActive), 0) }, aggregate([g.root, ...subs]));
      });
      // Stable ranking (DESIGN 2c): rank by the value the row shows (the engine's 3 s live rate); a group passes the
      // one above only when it has led by more than 10 % (+1 tok/s) for 1 s (two ticks), so small wobbles and
      // single-tick spikes never reshuffle the board.
      for (const g of groups) g.score = g.live;
      const byId = new Map(groups.map(g => [g.id, g]));
      order = order.filter(id => byId.has(id));
      // Groups new to the board enter at the place of their value, without an event.
      for (const g of groups.slice().sort((x, y) => y.score - x.score || y.lastActive - x.lastActive)) {
        if (order.includes(g.id)) continue;
        const at = order.findIndex(id => byId.get(id).score < g.score);
        order.splice(at < 0 ? order.length : at, 0, g.id);
      }
      const baseline = order.slice();
      const leads = (a, b) => a.score > b.score * 1.1 + 1 || (b.score < 0.05 && a.score < 0.05 && a.lastActive > b.lastActive + 1);
      const held = new Map();
      for (const a of groups) for (const b of groups) {
        if (a === b || !leads(a, b)) continue;
        const key = a.id + ">" + b.id;
        held.set(key, (pressure.get(key) || 0) + 1);
      }
      pressure = held;
      const beats = (a, b) => (pressure.get(a.id + ">" + b.id) || 0) >= 2 || a.score > b.score * 2 + 20;
      for (let i = 1; i < order.length; i++) {
        let j = i;
        while (j > 0 && beats(byId.get(order[j]), byId.get(order[j - 1]))) {
          [order[j - 1], order[j]] = [order[j], order[j - 1]];
          j--;
        }
      }
      groups.sort((x, y) => order.indexOf(x.id) - order.indexOf(y.id));
      // Only passing someone is an event (DESIGN 2c); places gained because others left are not.
      const events = [];
      groups.forEach((g, i) => {
        g.rank = i + 1;
        g.prevRank = baseline.indexOf(g.id) + 1;
        if (g.rank < g.prevRank) events.push({ type: g.rank === 1 ? "newLeader" : "overtake", id: g.id, from: g.prevRank, to: g.rank });
      });
      prevRanks = new Map(groups.map(g => [g.id, g.rank]));
      const generating = agents.filter(a => a.phase === "generating").length;
      const tools = agents.filter(a => a.phase === "tools").length;
      return {
        t, scenario: scenarioName, groups, events,
        total: { live: total, heat: heatOf(total), overdrive: Math.max(0, Math.min(1, (total - REDLINE) / REDLINE)),
                 title: titleFor(total), generating, tools, agents: agents.length, today, cost, peak, costToday,
                 ...aggregate(agents) },
        history: history.slice(),
      };
    }

    function emit(total) {
      const snap = snapshot(total);
      listeners.forEach(fn => fn(snap));
    }

    reset(opts.scenario);
    return {
      subscribe(fn) { listeners.add(fn); emit(agents.reduce((s, a) => s + a.live, 0)); return () => listeners.delete(fn); },
      start() { if (!timer) timer = setInterval(() => advance(false), opts.tickMs); },
      stop() { clearInterval(timer); timer = null; },
      step() { advance(false); },
      setScenario(name) { reset(name); emit(agents.reduce((s, a) => s + a.live, 0)); },
      get running() { return timer !== null; },
    };
  }

  global.AgentSim = { create, heatColor, heatRgb, heatOf, formatSpeed, titleFor, SOURCES, EFFORT_LEVELS, BOLTS, REDLINE,
                      PRICES, classOf };
})(typeof window !== "undefined" ? window : globalThis);
