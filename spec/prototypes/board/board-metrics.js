// A leaderboard board that can render any style (glass / timing / neon) with any metrics layout option (A / B / C),
// several on one page. Board rules follow board-glass/-timing/-neon.html (DESIGN 2c); the options add the live
// intelligence, thinking and cost readouts (METRICS 6.9, 9.5; DESIGN 9) and a per-row intelligence column.
(function (global) {
  "use strict";
  const S = global.AgentSim, F = global.MetricFormat;
  const NS = "http://www.w3.org/2000/svg";
  const BOLT = '<svg viewBox="0 0 8 10"><path d="M4.8 0 0.6 5.6h2.7L2.6 10 7.4 4.1H4.6z"/></svg>';
  const LIMITS = { S: { groups: 4, subs: [0, 0, 0, 0] }, M: { groups: 6, subs: [2, 1, 1] }, L: { groups: 8, subs: [3, 3, 3, 2, 2] } };
  const STYLES = {
    glass: { radius: 26, fx: { radius: 26 }, spring: [170, 0.9], rowExtra: 2, moreH: 20, firstY: 6, colsH: 12 },
    timing: { radius: 20, fx: { radius: 20, comets: [1, 1, 2, 2, 3], ringWidth: 1.2 }, spring: [260, 0.72], rowExtra: 0, moreH: 19, firstY: 0, colsH: 14 },
    neon: { radius: 22, fx: { radius: 22, comets: [1, 2, 3, 4, 4], ringWidth: 1.4, warp: true }, spring: [170, 0.9], rowExtra: 2, moreH: 20, firstY: 6, colsH: 12 },
  };
  const KEYS = ["intel", "think", "cost"];
  // DESIGN 2c Live metrics: the arc and the 12-segment meter span the index's whole range, 60 (light, off, no
  // reasoning) to 156 (flagship top rung, all reasoning); one segment is 8 points.
  const IQ_MIN = 60, IQ_SPAN = 96, SEGMENTS = 12;
  // The sub table's number cell is as wide as this sample in its font, so the table never moves with its values.
  const NUMBER_SAMPLE = "0.00088", WIDE_HOLD_MS = 6000;
  // Thinking hidden at zero: a live 思考 that reads `0` for 3 s fades out (0.25 s) while the rest glides (smoothstep
  // 0.3 s) to the re-centred stack; a value above 0 brings it back at once.
  const THINK_HOLD_S = 3, FADE_MS = 250, GLIDE_MS = 300, SMOOTHSTEP = "cubic-bezier(.33,0,.67,1)";
  let uid = 0;

  const el = (tag, cls, html) => { const e = document.createElement(tag); if (cls) e.className = cls; if (html !== undefined) e.innerHTML = html; return e; };

  /** Per-character rolling digits; a change of length crossfades instead (DESIGN 2c: digits roll, nothing jumps). */
  function roll(node, text, up, reduce) {
    const old = node.dataset.t;
    if (old === text) return;
    node.dataset.t = text;
    if (node.hasAttribute("data-ghost")) node.setAttribute("data-ghost", text.replace(/[0-9]/g, "8"));
    if (reduce || old === undefined || old.length !== text.length || /[^0-9.,k]/.test(old + text)) {
      node.replaceChildren(...[...text].map(c => { const s = el("span", "slot"), ch = el("span", old === undefined ? "ch" : "ch fade"); ch.textContent = c; s.appendChild(ch); return s; }));
      return;
    }
    [...text].forEach((c, i) => {
      if (old[i] === c) return;
      const slot = node.children[i], prev = slot.querySelector(".ch:not(.out)"), next = el("span", "ch in");
      next.textContent = c; next.style.transform = `translateY(${up ? 100 : -100}%)`; next.style.opacity = "0";
      if (prev) prev.classList.add("out");
      slot.appendChild(next);
      requestAnimationFrame(() => {
        next.style.transform = ""; next.style.opacity = "";
        if (prev) { prev.style.transform = `translateY(${up ? -100 : 100}%)`; prev.style.opacity = "0"; }
      });
      setTimeout(() => prev && prev.remove(), 300);
    });
  }

  function create(host, options) {
    const o = Object.assign({ style: "glass", option: "B", size: "M", metrics: { intel: true, think: true, cost: true },
                              subscriptionOnly: false, noReasoning: false, currency: "USD", lang: "zh-Hant",
                              reduceMotion: false, effects: true, tip: null }, options);
    const st = STYLES[o.style], id = ++uid;
    const L = F.LABELS[o.lang];
    const on = (k) => !!o.metrics[k];
    const anyMetric = () => KEYS.some(on);
    const optB = () => o.option === "B" && anyMetric();
    const unitTok = o.style === "timing" ? "TOK/S" : "tok/s";
    // Two-line rows (the model under the project) at M and L in every option; S keeps single-line rows. Only C, an
    // alternative that was not chosen, puts the group's cost per second on that line (DESIGN 2c: rows carry no cost).
    const rows2 = true;
    const rowCost = o.option === "C" && on("cost");

    // ── Build ─────────────────────────────────────────────────────────────────────────
    const panel = el("section", `mb ${o.style} opt${o.option}`);
    panel.dataset.size = o.size;
    panel.setAttribute("role", "list");
    panel.setAttribute("aria-label", "排行看板");
    if (on("intel")) panel.classList.add("cap-on");
    if (rows2) panel.classList.add("rows2");
    if (rowCost) panel.classList.add("rowcost");
    if (o.style === "glass") panel.appendChild(el("div", "aurora", "<i></i><i></i>"));
    if (o.style === "timing") panel.appendChild(el("div", "rail", "<i></i>"));

    const hero = el("div", "hero"), hl = el("div", "hl"), hr = el("div", "hr");
    if (o.style === "timing") hl.appendChild(el("span", "live", "<i></i>LIVE"));
    const value = el("div", "value"), num = el("span", "num");
    if (o.style === "neon") { const glow = el("span", "glow"); glow.appendChild(num); value.appendChild(glow); } else value.appendChild(num);
    value.appendChild(el("span", "unit", unitTok));
    const meta = el("div", "meta"), title = el("span", "title"), counts = el("span", "counts");
    meta.append(title, counts);
    hl.append(value, meta);
    hero.append(hl, hr);
    panel.appendChild(hero);

    // History: glass sparkline, timing 30 bars, neon 24-segment LED (always full width in neon).
    const history = makeHistory(optB() && o.style !== "neon");
    if (o.style === "neon") { const led = el("div", "led"); for (let i = 0; i < 24; i++) led.appendChild(el("i")); hero.appendChild(led); history.led = led; }
    else if (!optB()) hr.appendChild(history.node);

    const cells = {};
    function cell(k, extra) {
      const c = el("div", "m" + (extra ? " " + extra : ""));
      c.dataset.k = k;
      const label = el("span", "ml"); label.textContent = L[k === "think" ? "thinking" : k];
      const mv = el("span", "mv"), ge = el("span", "ge", "≥"), mn = el("span", "mn"), mu = el("span", "mu");
      if (o.style === "neon") mn.setAttribute("data-ghost", "");
      mv.append(ge, mn, mu);
      c.append(label, mv);
      cells[k] = c;
      return c;
    }

    let arc = null, segs = null, subsEl = null, dboxEl = null;
    if (o.option === "A" && anyMetric()) {
      const band = el("div", "band");
      KEYS.filter(on).forEach(k => band.appendChild(cell(k)));
      panel.appendChild(band);
    } else if (optB()) {
      if (o.style === "timing") {
        const box = el("div", "dbox");
        KEYS.filter(on).forEach((k, i) => box.appendChild(cell(k, i === 0 && k === "intel" ? "main" : "")));
        hr.appendChild(box);
        dboxEl = box;
      } else {
        const g = el("div", "gauge"), ks = KEYS.filter(on);
        const main = ks[0] === "intel" ? ks.shift() : null;
        if (main && o.style === "glass") {
          const dial = el("div", "dial");
          dial.innerHTML = `<svg viewBox="0 0 66 66" aria-hidden="true"><defs><linearGradient id="pearl-${id}" x1="0" y1="1" x2="1" y2="0">` +
            `<stop offset="0" style="stop-color:var(--pearl-c)"/><stop offset=".5" style="stop-color:var(--pearl-a)"/><stop offset="1" style="stop-color:var(--pearl-b)"/></linearGradient></defs>` +
            `<path class="trk" d="${arcPath()}" pathLength="100"/><path class="arc" d="${arcPath()}" pathLength="100" stroke-dasharray="100 100" stroke-dashoffset="100" style="stroke:url(#pearl-${id})"/>` +
            `<circle class="tip" r="2.3" cx="${33 + 29 * Math.cos(150 * Math.PI / 180)}" cy="${33 + 29 * Math.sin(150 * Math.PI / 180)}" style="transform-origin:33px 33px;transform-box:view-box"/></svg>`;
          dial.appendChild(cell("intel", "main"));
          g.appendChild(dial);
          arc = { path: dial.querySelector(".arc"), tip: dial.querySelector(".tip") };
        } else if (main) {
          g.appendChild(cell("intel", "main"));
          if (o.style === "neon") { segs = el("div", "segs"); for (let i = 0; i < 12; i++) segs.appendChild(el("i")); g.appendChild(segs); }
        }
        if (ks.length) { subsEl = el("div", "subs"); ks.forEach(k => subsEl.appendChild(cell(k))); g.appendChild(subsEl); }
        hr.appendChild(g);
      }
      if (o.style !== "neon") { const strip = el("div", "strip"); strip.appendChild(history.node); panel.appendChild(strip); }
    }

    const board = el("div", "board");
    panel.appendChild(board);
    let cols = null;
    if (on("intel")) {
      cols = el("div", "cols");
      cols.innerHTML = o.style === "timing" ? `<i class="ln"></i><span class="g">間隔</span><span>${L.intel}</span><span>${unitTok}</span>`
                                            : `<i class="ln"></i><span>${L.intel}</span><span>${unitTok}</span>`;
      board.appendChild(cols);
    }

    const foot = el("div", "foot");
    const today = el("b"), costToday = el("b"), peak = el("b");
    if (o.option === "C" && anyMetric()) {
      foot.classList.add("two");
      // Live over today, column by column: 智力 over today's tokens, the cost rate over today's cost, 思考 over the peak.
      ["intel", "cost", "think"].forEach(k => foot.appendChild(on(k) ? cell(k) : el("span")));
    }
    const s1 = el("span"), s2 = el("span"), s3 = el("span", "peak");
    if (o.style === "timing") { s1.append("TODAY ", today); s3.append("PEAK ", peak); } else { s1.append("今日 ", today, " tok"); s3.append("峰值 ", peak); }
    s2.appendChild(costToday);
    foot.append(s1, s2, s3);
    panel.appendChild(foot);
    host.appendChild(panel);

    // Fixed number cell of the sub table (DESIGN 2c): the sample's width; a wider value widens it at once and it
    // narrows 6 s after the last value that needed it.
    const subCell = { base: 0, cur: 0, wideAt: 0 };
    const unitCost = () => { const u = F.rate(0, 1, o.currency, o.lang).unit; return o.style === "timing" ? u.toUpperCase() : u; };
    const probeWidth = (host, cls, texts) => Math.max(0, ...texts.map(t => {
      const probe = el("span", cls + " probe"); probe.textContent = t; host.appendChild(probe);
      const cs = getComputedStyle(probe), w = probe.getBoundingClientRect().width + parseFloat(cs.marginLeft) + parseFloat(cs.marginRight);
      probe.remove(); return w;
    }));
    const tableKeys = KEYS.filter(k => on(k) && !(k === "intel" && o.style !== "timing"));
    // Measured once the board is in the document (a detached element has no computed style).
    let cellsMeasured = false;
    function measureCells() {
      if (cellsMeasured || !panel.isConnected) return;
      cellsMeasured = true;
      if (subsEl) {
        subCell.base = subCell.cur = Math.ceil(probeWidth(subsEl, "mn", [NUMBER_SAMPLE]));
        subsEl.style.setProperty("--subnum", subCell.cur + "px");
        // Label and unit cells are sized for every switched-on row, so hiding 思考 never moves the table sideways.
        subsEl.style.setProperty("--sublabel", Math.ceil(probeWidth(subsEl, "ml", tableKeys.map(k => L[k === "think" ? "thinking" : k]))) + "px");
        subsEl.style.setProperty("--subunit", Math.ceil(probeWidth(subsEl, "mu", tableKeys.map(k => k === "think" ? unitTok : unitCost()))) + "px");
      }
      if (dboxEl) dboxEl.style.setProperty("--tabw", Math.ceil(probeWidth(dboxEl, "ml", KEYS.filter(on).map(k => L[k === "think" ? "thinking" : k]))) + "px");
    }
    function fitSubCell() {
      if (!subsEl || !cellsMeasured) return;
      let need = subCell.base;
      subsEl.querySelectorAll(".mn").forEach(n => { need = Math.max(need, Math.ceil(n.scrollWidth)); });
      const now = performance.now();
      if (need > subCell.cur) { subCell.cur = need; subCell.wideAt = now; }
      else if (need > subCell.base) subCell.wideAt = now;
      else if (subCell.cur > subCell.base && now - subCell.wideAt >= WIDE_HOLD_MS) subCell.cur = subCell.base;
      subsEl.style.setProperty("--subnum", subCell.cur + "px");
    }

    const fx = global.BoardFX.create(panel, st.fx);
    fx.setReduceMotion(o.reduceMotion || !o.effects);

    function arcPath() {
      const r = 29, a0 = 150 * Math.PI / 180, a1 = 30 * Math.PI / 180;
      return `M${(33 + r * Math.cos(a0)).toFixed(2)} ${(33 + r * Math.sin(a0)).toFixed(2)} A${r} ${r} 0 1 1 ${(33 + r * Math.cos(a1)).toFixed(2)} ${(33 + r * Math.sin(a1)).toFixed(2)}`;
    }

    function makeHistory(wide) {
      if (o.style === "glass") {
        const svg = document.createElementNS(NS, "svg");
        svg.setAttribute("class", "spark"); svg.setAttribute("aria-hidden", "true"); svg.setAttribute("preserveAspectRatio", "none");
        const W = wide ? 300 : 104, H = wide ? 16 : 40;
        svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
        svg.innerHTML = `<defs><linearGradient id="sg-${id}" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="currentColor" stop-opacity="${wide ? .28 : .35}"/>` +
          `<stop offset="1" stop-color="currentColor" stop-opacity="0"/></linearGradient></defs><path class="a" fill="url(#sg-${id})"/>` +
          `<path class="l" fill="none" stroke="currentColor" stroke-width="${wide ? 1.25 : 1.5}" stroke-linejoin="round" vector-effect="non-scaling-stroke"/>` +
          (wide ? "" : `<circle class="d" r="2.4" fill="currentColor"/>`);
        return { node: svg, W, H, wide };
      }
      const bars = el("div", "bars");
      for (let i = 0; i < 30; i++) bars.appendChild(el("i"));
      return { node: bars, wide };
    }

    // ── Rows ──────────────────────────────────────────────────────────────────────────
    const items = new Map();
    function capsule() {
      return el("span", "cap none", '<span class="meter"><i></i><i></i><i></i><i></i><i></i></span><span class="word"></span><span class="bolts"></span>');
    }
    function paintCapsule(c, agent) {
      const e = agent.effort, bolts = agent.bolts, key = (e ? e.value : "-") + "/" + bolts;
      if (c.dataset.key === key) return;
      c.dataset.key = key;
      const meter = c.querySelector(".meter");
      c.classList.toggle("none", !e && !bolts);
      c.classList.toggle("max", !!e && e.value === "max");
      c.classList.toggle("top", !!e && e.top);
      meter.style.display = e && e.level !== null ? "" : "none";
      [...meter.children].forEach((bar, i) => bar.classList.toggle("on", !!e && e.level > i));
      const w = c.querySelector(".word"); w.textContent = e ? e.value : ""; w.style.display = e ? "" : "none";
      c.querySelector(".bolts").innerHTML = BOLT.repeat(bolts);
    }

    function makeItem(kind) {
      const it = el("div", "item " + kind);
      it.setAttribute("role", "listitem");
      const timing = o.style === "timing";
      if (kind === "more") {
        it.innerHTML = timing ? '<div class="row"><span></span><span></span><span class="name"></span><span></span><span class="gap"></span><span class="iq"></span><span class="speed"></span></div>'
          : '<div class="row"><span></span><span></span><span class="name"><span class="stack"><i></i><i></i><i></i></span><span class="label"></span></span><span></span><span class="iq"></span><span class="speed"></span></div>';
      } else {
        it.innerHTML = timing
          ? '<div class="row"><span class="wipe"></span><span class="pos"><span></span></span><span class="stripe"></span><span class="name"><b></b><small></small></span>' +
            '<span class="capslot"></span><span class="gap"></span><span class="iq"></span><span class="speed"></span><span class="l2"></span><span class="chip"></span></div>'
          : '<div class="row"><span class="bar"></span><span class="sweep"></span><span class="rank"><span></span></span><span class="badge"></span><span class="name"><b></b><small></small></span>' +
            '<span class="capslot"></span><span class="iq"></span><span class="speed"></span><span class="l2"></span><span class="chip"></span></div>';
        it.querySelector(".capslot").replaceWith(capsule());
        if (kind === "group") {
          it.addEventListener("mouseenter", (ev) => o.tip && o.tip.show(tipHtml(it.dataset.id), ev.currentTarget));
          it.addEventListener("mouseleave", () => o.tip && o.tip.hide());
        }
      }
      it.style.opacity = "0";
      board.appendChild(it);
      return { el: it, kind, y: null, target: 0, v: 0, alive: true };
    }

    function badge(b, phase) {
      const cls = phase === "generating" ? "gen" : phase === "tools" ? "tools" : "idle";
      if (b.dataset.phase === cls) return;
      b.dataset.phase = cls; b.className = "badge " + cls; b.innerHTML = cls === "gen" ? "<i></i><i></i><i></i>" : "";
    }

    function iqCell(node, live, grade) {
      const shown = live !== null && live !== undefined;
      const text = !on("intel") ? "" : shown ? String(Math.round(live)) : grade !== null && grade !== undefined ? String(grade) : "–";
      if (node.dataset.t !== text) { node.dataset.t = text; node.innerHTML = text ? `<span>${text}</span>` : ""; }
      node.classList.toggle("grade", !shown);
    }

    function costLine(node, live, rate, share) {
      if (!rowCost || !(live > 0.05)) { node.textContent = ""; return; }
      if (o.subscriptionOnly) { node.innerHTML = `<span class="tagword">${L.subscription}</span>`; node.classList.remove("partial"); node.classList.add("dim"); return; }
      const r = F.rate(rate, share, o.currency, o.lang);
      const unit = r.unknown ? "" : `<span class="u">${o.style === "timing" ? r.unit.toUpperCase() : r.unit}</span>`;
      node.innerHTML = `<span class="ge">≥</span>${r.number}${unit}`;
      node.classList.toggle("partial", r.partial);
      node.classList.toggle("dim", r.unknown);
    }

    function paintRow(it, agent, p) {
      const row = it.el.firstElementChild;
      it.el.dataset.id = p.id;
      row.style.setProperty("--src", agent.sourceColor);
      const h = S.heatOf(p.live), live = p.live > 0.05;
      if (o.style !== "timing") {
        row.style.setProperty("--share", (p.total > 0 ? Math.min(1, p.live / p.total) : 0).toFixed(4));
        row.style.setProperty("--rowheat", S.heatColor(Math.max(0.12, h)));
        badge(row.querySelector(".badge"), p.generating ? "generating" : agent.phase);
      } else {
        row.querySelector(".gap").textContent = p.gap || "";
      }
      row.classList.toggle("gen", p.generating || agent.phase === "generating");
      row.querySelector(".name b").textContent = p.name;
      row.querySelector(".name small").textContent = p.detail;
      const sp = row.querySelector(".speed");
      sp.textContent = live ? S.formatSpeed(p.live) : "–";
      sp.classList.toggle("dim", !live);
      if (o.style !== "timing") sp.style.color = live && h > 0.45 ? S.heatColor(h) : "";
      iqCell(row.querySelector(".iq"), p.intelligence, p.grade);
      costLine(row.querySelector(".l2"), p.live, p.costRate, p.costKnownShare);
      paintCapsule(row.querySelector(".cap"), agent);
      if (p.rank !== undefined) {
        const r = row.querySelector(o.style === "timing" ? ".pos span" : ".rank span");
        if (r.textContent !== String(p.rank)) {
          const prev = Number(r.textContent) || p.rank;
          if (!o.reduceMotion && r.textContent) {
            r.style.transition = "none"; r.style.transform = `translateY(${p.rank < prev ? 100 : -100}%)`;
            requestAnimationFrame(() => { r.style.transition = ""; r.style.transform = ""; });
          }
          r.textContent = p.rank;
        }
        it.el.classList.toggle("lead", p.rank === 1);
      }
      const iqWords = !on("intel") ? "" : p.intelligence !== null && p.intelligence !== undefined ? `，智力 ${Math.round(p.intelligence)}`
        : p.grade !== null && p.grade !== undefined ? `，等級 ${p.grade}` : "";
      let costWords = "";
      if (p.rank && rowCost && o.size !== "S" && live) {
        const r = F.rate(p.costRate, p.costKnownShare, o.currency, o.lang);
        costWords = o.subscriptionOnly ? "，訂閱" : r.unknown ? "，價格未知" : `，費用每秒 ${r.partial ? "至少 " : ""}${r.number} ${F.currencyName(o.currency)}`;
      }
      row.setAttribute("aria-label", p.rank ? `第 ${p.rank} 名，${p.name}，每秒 ${live ? S.formatSpeed(p.live) : 0} 個 token${iqWords}${costWords}` : p.name);
    }

    function accent(it, ev) {
      if (o.reduceMotion || !o.effects) return;
      const row = it.el.firstElementChild;
      const sw = row.querySelector(o.style === "timing" ? ".wipe" : ".sweep"), chip = row.querySelector(".chip");
      sw.classList.remove("go"); chip.classList.remove("go"); void row.offsetWidth;
      sw.classList.add("go"); chip.textContent = "▲" + (ev.from - ev.to); chip.classList.add("go");
      const p = panel.getBoundingClientRect(), r = row.getBoundingClientRect(), y = r.top - p.top + r.height / 2;
      const src = getComputedStyle(row).getPropertyValue("--src").trim() || "#ffffff";
      fx.streak(r.left - p.left, y, r.right - p.left, src);
      const rk = row.querySelector(o.style === "timing" ? ".pos" : ".rank").getBoundingClientRect();
      const rx = rk.left - p.left + rk.width / 2, ry = rk.top - p.top + rk.height / 2;
      if (ev.type === "newLeader") {
        if (o.style === "neon") fx.lightning(rx + 30 + Math.random() * 120, -6, rx, ry, "#FACC15", { branches: 3, life: 0.45 });
        fx.celebrate(rx, ry);
      } else {
        fx.burst(rx, ry, { count: 14, speed: 130, life: .7, colors: [src, "#FFFFFF"], size: 1.5, gravity: 60 });
      }
    }

    // Groups first, then sub rows in rank order while room remains (DESIGN 2c Layout per size).
    function layout(snap) {
      const lim = LIMITS[o.size], cs = getComputedStyle(panel);
      const rowH = parseFloat(cs.getPropertyValue("--row")) + (rows2 && o.size !== "S" ? 8 : st.rowExtra);
      const subH = parseFloat(cs.getPropertyValue("--sub")), moreH = st.moreH;
      const y0 = cols ? st.colsH + 2 : st.firstY;
      const room = board.clientHeight - y0;
      const all = snap.groups;
      let groups = all.slice(0, lim.groups);
      const restRow = () => all.length > groups.length ? moreH : 0;
      while (groups.length > 1 && groups.length * rowH + restRow() > room) groups = groups.slice(0, -1);
      let free = room - groups.length * rowH - restRow();
      const plan = groups.map((g, i) => ({ g, subs: g.subs.filter(s => s.live > 0.05).slice(0, lim.subs[i] || 0), more: false }));
      for (const p of plan) {
        const keep = [];
        for (const s of p.subs) {
          const needMore = p.g.subs.length - keep.length - 1 > 0 && !p.more ? moreH : 0;
          if (free >= subH + needMore) { keep.push(s); free -= subH; if (needMore) { p.more = true; free -= moreH; } }
        }
        if (p.more && keep.length === p.g.subs.length) { p.more = false; free += moreH; }
        p.subs = keep;
      }
      const list = [];
      let y = y0;
      for (const p of plan) {
        list.push({ key: p.g.id, kind: "group", g: p.g, y }); y += rowH;
        for (const s of p.subs) { list.push({ key: s.id, kind: "sub", g: p.g, s, y }); y += subH; }
        if (p.more) { list.push({ key: p.g.id + "#more", kind: "more", g: p.g, rest: p.g.subs.length - p.subs.length, y }); y += moreH; }
      }
      const hidden = all.slice(groups.length);
      if (hidden.length) list.push({ key: "#rest", kind: "more", groups: true, g: hidden[0], rest: hidden.length, restLive: hidden.reduce((s, g) => s + g.live, 0), y });
      return list;
    }

    // ── Metrics ───────────────────────────────────────────────────────────────────────
    const prevNum = {};
    // 思考 row visibility (DESIGN 2c, Thinking hidden at zero).
    let thinkShown = true, zeroSince = null;
    function rowItems(c) {
      return o.style === "timing" ? [c.querySelector(".ml"), c.querySelector(".mv")]
                                  : [c.querySelector(".ml"), c.querySelector(".ge"), c.querySelector(".mn"), c.querySelector(".mu")];
    }
    function setThink(show, instant) {
      const c = cells.think; if (!c || show === thinkShown) return;
      thinkShown = show;
      const container = c.parentElement, stack = o.style === "timing" ? container : container.closest(".gauge");
      const items = rowItems(c);
      const below = KEYS.slice(KEYS.indexOf("think") + 1).filter(k => cells[k]).flatMap(k => rowItems(cells[k]));
      const offsets = () => { const t = stack.getBoundingClientRect().top; return below.map(e => e.getBoundingClientRect().top - t); };
      const h0 = stack.getBoundingClientRect().height, o0 = offsets();
      if (!show) {
        const cr = container.getBoundingClientRect();
        const pos = items.map(e => { const r = e.getBoundingClientRect(); return [r.left - cr.left, r.top - cr.top, r.width]; });
        items.forEach((e, i) => Object.assign(e.style, { position: "absolute", left: pos[i][0] + "px", top: pos[i][1] + "px", width: pos[i][2] + "px", margin: "0" }));
        c.classList.add("leaving");
      } else {
        c.classList.remove("off", "leaving");
        items.forEach(e => { e.getAnimations().forEach(a => a.cancel()); Object.assign(e.style, { position: "", left: "", top: "", width: "", margin: "" }); });
      }
      const h1 = stack.getBoundingClientRect().height, o1 = offsets();
      const animate = !instant && !o.reduceMotion;
      const finish = () => { if (!thinkShown) { c.classList.add("off"); c.classList.remove("leaving"); items.forEach(e => { e.getAnimations().forEach(a => a.cancel()); Object.assign(e.style, { position: "", left: "", top: "", width: "", margin: "" }); }); } };
      if (!animate) { finish(); return; }
      stack.animate([{ height: h0 + "px" }, { height: h1 + "px" }], { duration: GLIDE_MS, easing: SMOOTHSTEP });
      below.forEach((e, i) => { const d = o0[i] - o1[i]; if (Math.abs(d) > 0.01) e.animate([{ transform: `translateY(${d}px)` }, { transform: "none" }], { duration: GLIDE_MS, easing: SMOOTHSTEP }); });
      items.forEach(e => e.animate([{ opacity: show ? 0 : 1 }, { opacity: show ? 1 : 0 }], { duration: FADE_MS, easing: "ease-out", fill: show ? "none" : "forwards" }));
      if (!show) setTimeout(finish, FADE_MS);
    }
    function thinkVisibility(total, t, instant) {
      if (!cells.think || !optB()) return;                                  // the chosen layout B only
      const others = KEYS.some(k => k !== "think" && on(k));
      if (total.intelligence === null) { zeroSince = null; return; }       // quiet: keep the current visibility
      if (parseFloat(S.formatSpeed(total.thinking)) === 0) {             // reads zero: `0`, or `0.0` below 0.05
        if (zeroSince === null) zeroSince = t;
        if (thinkShown && others && t - zeroSince >= THINK_HOLD_S - 1e-9) setThink(false, instant);
      } else {
        zeroSince = null;
        if (!thinkShown) setThink(true, instant);
      }
    }
    function paintMetrics(total) {
      const reduce = o.reduceMotion;
      const put = (k, text, unit, state) => {
        const c = cells[k]; if (!c) return;
        const mn = c.querySelector(".mn"), before = prevNum[k];
        roll(mn, text, before === undefined || parseFloat(text) >= before, reduce);
        prevNum[k] = parseFloat(text);
        c.querySelector(".mu").textContent = unit;
        for (const name of ["partial", "unknown", "none", "zero", "tag"]) c.classList.toggle(name, !!(state && state[name]));
      };
      const iq = total.intelligence, shown = iq === null ? null : Math.round(iq);
      // Quiet state (nothing generated in the window): 智力 and 思考 show `–`, the cost its real `0`, all in the quiet ink.
      put("intel", F.intelligence(iq), "", { none: iq === null });
      put("think", iq === null ? "–" : S.formatSpeed(total.thinking), unitTok, { none: iq === null, zero: iq !== null && !(total.thinking > 0) });
      const r = F.rate(total.costRate, total.costKnownShare, o.currency, o.lang);
      const costUnit = o.style === "timing" ? r.unit.toUpperCase() : r.unit;
      if (o.subscriptionOnly) put("cost", L.subscription, "", { tag: true });
      else put("cost", r.number, costUnit, { partial: r.partial, unknown: r.unknown, zero: r.number === "0" });
      fitSubCell();
      const frac = shown === null ? 0 : Math.max(0, Math.min(1, (shown - IQ_MIN) / IQ_SPAN));
      if (arc) {
        arc.path.setAttribute("stroke-dashoffset", (100 - frac * 100).toFixed(2));
        arc.path.style.opacity = frac > 0 ? "1" : "0";
        arc.tip.style.transform = `rotate(${(frac * 240).toFixed(2)}deg)`;
        arc.tip.style.opacity = shown === null ? "0" : "1";
      }
      if (segs) {
        const lit = shown === null ? 0 : Math.max(0, Math.min(SEGMENTS, Math.floor((shown - IQ_MIN) / 8 + 0.5)));
        [...segs.children].forEach((seg, i) => seg.classList.toggle("on", i < lit));
      }
      const thinkOn = on("think") && thinkShown;
      panel.setAttribute("aria-label", `排行看板，全部來源：每秒 ${S.formatSpeed(total.live)} 個 token，${total.title.zh}` +
        (on("intel") && thinkOn ? (iq === null ? "，智力：目前沒有輸出" : `，智力 ${shown}，思考每秒 ${S.formatSpeed(total.thinking)} 個 token`)
          : on("intel") ? (iq === null ? "，智力：目前沒有輸出" : `，智力 ${shown}`)
          : thinkOn ? `，思考每秒 ${iq === null ? 0 : S.formatSpeed(total.thinking)} 個 token` : "") +
        (on("cost") ? (o.subscriptionOnly ? "，這些來源標記為訂閱，不計入費用"
          : r.unknown ? "，費用：價格未知" : `，費用每秒 ${r.partial ? "至少 " : ""}${r.number} ${F.currencyName(o.currency)}（API 等值）`) : ""));
    }

    function drawHistory(hist, color, heat) {
      if (history.led) {
        const lit = Math.round(heat * 24), pk = Math.min(23, Math.round(S.heatOf(last.total.peak) * 24) - 1);
        [...history.led.children].forEach((seg, i) => {
          seg.classList.toggle("on", i < lit); seg.classList.toggle("pk", i === pk && i >= lit);
          seg.style.setProperty("--c", S.heatColor((i + 1) / 24));
        });
        return;
      }
      if (o.style === "glass") {
        const svg = history.node; svg.style.color = color;
        const n = hist.length; if (n < 2) return;
        const { W, H } = history, max = Math.max(10, ...hist) * 1.12;
        const pts = hist.map((v, i) => [i / (n - 1) * W, H - (history.wide ? 1.5 : 2) - v / max * (H - (history.wide ? 3 : 6))]);
        let d = `M${pts[0][0].toFixed(1)},${pts[0][1].toFixed(1)}`;
        for (let i = 1; i < pts.length; i++) {
          const [x0, y0] = pts[i - 1], [x1, y1] = pts[i];
          d += ` Q${x0.toFixed(1)},${y0.toFixed(1)} ${((x0 + x1) / 2).toFixed(1)},${((y0 + y1) / 2).toFixed(1)}`;
        }
        const lp = pts[pts.length - 1]; d += ` T${lp[0].toFixed(1)},${lp[1].toFixed(1)}`;
        svg.querySelector(".l").setAttribute("d", d);
        svg.querySelector(".a").setAttribute("d", d + ` L${W},${H} L0,${H} Z`);
        const dot = svg.querySelector(".d"); if (dot) { dot.setAttribute("cx", lp[0]); dot.setAttribute("cy", lp[1]); }
        return;
      }
      const h30 = hist.slice(-30), max = Math.max(10, ...h30), H = history.wide ? (o.size === "S" ? 10 : 12) : 38;
      [...history.node.children].forEach((b, i) => { const v = h30[i - (30 - h30.length)] || 0; b.style.height = Math.max(history.wide ? 1.5 : 2, v / max * H) + "px"; });
    }

    // ── Render ────────────────────────────────────────────────────────────────────────
    let last = null, lastTotal = 0;
    const plain = (iq, share) => iq === null || iq === undefined || !o.noReasoning ? iq : iq - 20 * (share || 0);
    const agentShare = (a) => a.recordsReasoning && a.live > 0 ? a.thinking / a.live : 0;
    function render(snap, instant) {
      last = snap;
      // `noreasoning=1`: sources that never report reasoning apart from output (METRICS 6.9: their weight leaves w', so
      // the index loses its thinking term and 思考 reads 0, which hides after 3 s).
      const total = o.noReasoning ? Object.assign({}, snap.total, { thinking: 0, thinkingShare: 0,
                                                                    intelligence: plain(snap.total.intelligence, snap.total.thinkingShare) }) : snap.total;
      roll(num, S.formatSpeed(total.live), total.live >= lastTotal, o.reduceMotion || instant);
      lastTotal = total.live;
      fx.setHeat(total.heat, total.overdrive);
      const heat = S.heatColor(Math.max(o.style === "timing" ? 0.1 : 0.06, total.heat));
      const ps = panel.style;
      ps.setProperty("--heat", heat);
      ps.setProperty("--heat-2", S.heatColor(Math.min(1, total.heat + 0.18)));
      ps.setProperty("--heat-soft", S.heatColor(total.heat, .55));
      ps.setProperty("--heat-amount", total.heat.toFixed(3));
      ps.setProperty("--maxc", S.heatColor(0.82)); ps.setProperty("--topa", S.heatColor(0.72)); ps.setProperty("--topb", S.heatColor(1));
      if (o.style === "neon") ps.setProperty("--breath", (3.2 - 2.2 * total.heat).toFixed(2) + "s");
      if (o.style === "timing") panel.querySelector(".live").classList.toggle("idle", total.generating === 0);
      title.textContent = total.title.zh;
      counts.textContent = o.style === "timing" ? `GEN ${total.generating} · TOOL ${total.tools}` : `· 生成中 ${total.generating} · 工具 ${total.tools}`;
      today.textContent = S.formatSpeed(total.today) + (o.style === "timing" ? " TOK" : "");
      costToday.textContent = o.subscriptionOnly ? L.subscription : F.amount(total.costToday, o.currency, o.lang);
      peak.textContent = S.formatSpeed(total.peak);
      drawHistory(snap.history, heat, total.heat);
      measureCells();
      paintMetrics(total);
      thinkVisibility(total, snap.t, instant);

      const list = layout(snap), seen = new Set();
      for (const entry of list) {
        seen.add(entry.key);
        let it = items.get(entry.key);
        const fresh = !it;
        if (fresh) { it = makeItem(entry.kind); items.set(entry.key, it); }
        it.alive = true; it.el.classList.remove("gone");
        if (entry.kind === "group") {
          const g = entry.g, r = g.root, i = snap.groups.indexOf(g), ahead = i > 0 ? snap.groups[i - 1] : null;
          const subs = g.subs.length ? (o.style === "timing" ? `${g.subs.length} 子` : `${g.subs.length} 個子 agent`) : "";
          paintRow(it, r, { id: g.id, live: g.live, total: total.live, rank: g.rank, generating: g.generating > 0, name: r.project,
                            detail: [r.model, subs].filter(Boolean).join(" · "), intelligence: plain(g.intelligence, g.thinkingShare), grade: r.grade,
                            costRate: g.costRate, costKnownShare: g.costKnownShare,
                            gap: !ahead ? "領先" : (ahead.live - g.live >= 0 ? "−" : "+") + S.formatSpeed(Math.abs(ahead.live - g.live)) });
        } else if (entry.kind === "sub") {
          const s = entry.s;
          paintRow(it, s, { id: s.id, live: s.live, total: total.live, generating: false, name: s.label, detail: "",
                            intelligence: plain(s.intelligence, agentShare(s)), grade: s.grade, costRate: s.costRate, costKnownShare: s.priced ? 1 : 0 });
        } else {
          const row = it.el.firstElementChild;
          row.style.setProperty("--src", entry.g.root.sourceColor);
          const restLive = entry.groups ? entry.restLive : entry.g.subs.slice(entry.g.subs.length - entry.rest).reduce((s, a) => s + a.live, 0);
          const label = entry.groups ? `還有 ${entry.rest} 個 agent` : `+${entry.rest} 個子 agent`;
          (row.querySelector(".label") || row.querySelector(".name")).textContent = label;
          row.querySelector(".speed").textContent = restLive > 0.05 ? S.formatSpeed(restLive) : "";
        }
        it.target = entry.y;
        if (fresh || instant || o.reduceMotion) { it.y = instant || o.reduceMotion ? entry.y : entry.y + 10; it.v = 0; }
        if (instant) it.el.style.opacity = ""; else requestAnimationFrame(() => { it.el.style.opacity = ""; });
      }
      for (const [key, it] of items) {
        if (seen.has(key) || !it.alive) continue;
        it.alive = false; it.el.classList.add("gone");
        setTimeout(() => { if (!it.alive) { it.el.remove(); items.delete(key); } }, 320);
      }
      if (!instant) for (const ev of snap.events) { const it = items.get(ev.id); if (it) accent(it, ev); }
      let w = 0;
      board.querySelectorAll(".item:not(.gone) .cap:not(.none)").forEach(c => { c.style.width = "auto"; w = Math.max(w, c.scrollWidth); });
      board.querySelectorAll(".cap").forEach(c => { c.style.width = ""; });
      panel.style.setProperty("--capw", w ? Math.ceil(w) + "px" : "0px");
      if (instant) { for (const it of items.values()) { it.y = it.target; it.v = 0; it.el.style.transform = `translate3d(0, ${it.y}px, 0)`; } }
      else kick();
    }

    // Springs per style; the loop stops when everything has settled (no idle frames).
    let raf = 0, lastT = 0;
    function kick() { if (!raf) { lastT = performance.now(); raf = requestAnimationFrame(frame); } }
    function frame(now) {
      const dt = Math.max(0, Math.min(0.05, (now - lastT) / 1000)); lastT = now;
      const [k, z] = st.spring, c = 2 * Math.sqrt(k) * z;
      let moving = false;
      for (const it of items.values()) {
        if (it.y === null) it.y = it.target;
        it.v += (-k * (it.y - it.target) - c * it.v) * dt; it.y += it.v * dt;
        if (Math.abs(it.y - it.target) < 0.05 && Math.abs(it.v) < 0.05) { it.y = it.target; it.v = 0; } else moving = true;
        it.el.style.transform = `translate3d(0, ${it.y.toFixed(2)}px, 0)`;
      }
      raf = moving ? requestAnimationFrame(frame) : 0;
    }

    function tipHtml(gid) {
      if (!last) return "";
      const g = last.groups.find(x => x.id === gid); if (!g) return "";
      const r = g.root, e = r.effort, cls = S.classOf(r.model);
      const klass = cls === 100 ? "旗艦" : cls === 80 ? "標準" : "輕量";
      const rate = F.rate(g.costRate, g.costKnownShare, o.currency, o.lang);
      // DESIGN 2c Live metrics, Tooltip: the intelligence lines follow 智力; B names no cost (only C's cost rows do).
      const iq = !on("intel") ? "" : (g.intelligence === null ? ""
        : `<br>智力 ${Math.round(plain(g.intelligence, g.thinkingShare))} · 思考 ${o.noReasoning ? "–" : Math.round(g.thinkingShare * 100) + "%"}`) +
        (r.grade !== null && r.grade !== undefined ? `<br>等級 ${r.grade} · ${klass}` : "");
      const cost = !rowCost || !(g.live > 0.05) ? "" : o.subscriptionOnly ? `<br>${r.sourceName} 標記為訂閱，不計入費用`
        : `<br>費用 ${rate.unknown ? "價格未知" : (rate.partial ? "≥" : "") + rate.number + " " + rate.unit}（API 等值）` + (rate.partial ? "<br>部分模型價格未知，未計入" : "");
      return `<b>${r.project}</b> · ${r.sourceName}<br>${r.model}<br>推理強度：${e ? e.value : "未記錄"}` +
        `<br>速度 <b>${S.formatSpeed(g.live)}</b> tok/s${iq}${cost}` + (on("intel") ? `<span class="cap-note">估算指數，不是測驗分數</span>` : "");
    }

    return {
      el: panel, render,
      setEffects(v) { o.effects = v; fx.setReduceMotion(o.reduceMotion || !v); },
      setReduceMotion(v) { o.reduceMotion = v; fx.setReduceMotion(v || !o.effects); },
      setNoReasoning(v) { o.noReasoning = v; if (last) render(last, false); },
      destroy() { cancelAnimationFrame(raf); fx.setReduceMotion(true); panel.remove(); },
    };
  }

  global.MetricBoards = { create, STYLES };
})(typeof window !== "undefined" ? window : globalThis);
