// Shared effects layer for the leaderboard panel prototypes: perimeter comets that race around the rounded square,
// a heat bloom, and a particle canvas for sparks, streaks, shockwaves and the new-leader celebration.
// Only transform / opacity / stroke-dashoffset animations plus one canvas that runs only while something is alive.
(function (global) {
  "use strict";
  const NS = "http://www.w3.org/2000/svg";
  const TAU = Math.PI * 2;

  function create(panel, options) {
    const opts = Object.assign({ radius: 26, palette: null, comets: [1, 1, 2, 3, 4], ringWidth: 1.6 }, options || {});
    let reduce = false, heat = 0, overdrive = 0, gold = 0, width = 0, height = 0, dpr = 1;

    // ── Perimeter (SVG) ───────────────────────────────────────────────────────────────
    const svg = document.createElementNS(NS, "svg");
    svg.setAttribute("aria-hidden", "true");
    svg.style.cssText = "position:absolute;inset:0;width:100%;height:100%;pointer-events:none;z-index:8;overflow:visible";
    const defs = document.createElementNS(NS, "defs");
    defs.innerHTML = '<filter id="fxGlow" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="2.4"/></filter>' +
                     '<filter id="fxGlowWide" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur stdDeviation="6"/></filter>';
    svg.appendChild(defs);
    const ringGlow = rect({ "stroke-width": 6, filter: "url(#fxGlowWide)", opacity: 0 });
    const ring = rect({ "stroke-width": opts.ringWidth, opacity: 0 });
    svg.append(ringGlow, ring);
    const comets = [];
    for (let i = 0; i < 4; i++) {
      const g = document.createElementNS(NS, "g");
      // tail (long, faint), body (glow), core (short, bright)
      const tail = rect({ "stroke-width": 2, opacity: .35 });
      const glow = rect({ "stroke-width": 5, filter: "url(#fxGlow)", opacity: .9 });
      const core = rect({ "stroke-width": 1.8, opacity: 1 });
      g.append(tail, glow, core);
      g.style.opacity = "0";
      g.style.transition = "opacity .8s ease";
      svg.appendChild(g);
      comets.push({ g, tail, glow, core, anims: [] });
    }
    panel.appendChild(svg);

    function rect(attrs) {
      const r = document.createElementNS(NS, "rect");
      r.setAttribute("fill", "none");
      r.setAttribute("stroke-linecap", "round");
      r.setAttribute("pathLength", "1000");
      for (const k in attrs) r.setAttribute(k, attrs[k]);
      return r;
    }

    // ── Particles (canvas) ────────────────────────────────────────────────────────────
    const canvas = document.createElement("canvas");
    canvas.setAttribute("aria-hidden", "true");
    canvas.style.cssText = "position:absolute;inset:-40px;width:calc(100% + 80px);height:calc(100% + 80px);pointer-events:none;z-index:9";
    panel.appendChild(canvas);
    const ctx = canvas.getContext("2d");
    const parts = [], waves = [], bolts = [];
    // Optional hyperspace streaks behind the content (direction C): their own canvas, running only while hot.
    let warp = null;
    if (opts.warp) {
      const wc = document.createElement("canvas");
      wc.setAttribute("aria-hidden", "true");
      wc.style.cssText = "position:absolute;inset:0;width:100%;height:100%;pointer-events:none;z-index:0";
      panel.insertBefore(wc, panel.firstChild);
      warp = { canvas: wc, ctx: wc.getContext("2d"), stars: [] };
    }
    let raf = 0, lastT = 0;

    function resize() {
      const r = panel.getBoundingClientRect();
      width = r.width; height = r.height; dpr = Math.min(2, window.devicePixelRatio || 1);
      canvas.width = Math.round((width + 80) * dpr); canvas.height = Math.round((height + 80) * dpr);
      if (warp) { warp.canvas.width = Math.round(width * dpr); warp.canvas.height = Math.round(height * dpr); }
      for (const el of svg.querySelectorAll("rect")) {
        const inset = 0.8;
        el.setAttribute("x", inset); el.setAttribute("y", inset);
        el.setAttribute("width", Math.max(0, width - inset * 2)); el.setAttribute("height", Math.max(0, height - inset * 2));
        el.setAttribute("rx", opts.radius - inset); el.setAttribute("ry", opts.radius - inset);
      }
    }
    new ResizeObserver(resize).observe(panel);
    resize();

    // Comet dash patterns on a 1000-unit perimeter.
    function startComets() {
      comets.forEach((c, i) => {
        c.anims.forEach(a => a.cancel());
        c.tail.setAttribute("stroke-dasharray", "150 850");
        c.glow.setAttribute("stroke-dasharray", "46 954");
        c.core.setAttribute("stroke-dasharray", "16 984");
        // Heads line up: tail ends where the core ends.
        const base = -i * 250;
        const frames = (off) => [{ strokeDashoffset: base + off }, { strokeDashoffset: base + off - 1000 }];
        c.anims = [[c.tail, 0], [c.glow, -104], [c.core, -134]].map(([el, off]) =>
          el.animate(frames(off), { duration: 6000, iterations: Infinity, easing: "linear" }));
      });
    }
    startComets();

    function color(h) { return (opts.palette || global.AgentSim.heatColor)(h); }

    function setHeat(h, o) {
      heat = h; overdrive = o || 0;
      const tier = h < 0.05 ? 0 : h < 0.32 ? 1 : h < 0.56 ? 2 : h < 0.78 ? 3 : 4;
      const count = reduce ? 0 : opts.comets[tier];
      const cHead = gold > 0 ? "#FFE58A" : color(Math.min(1, h + 0.12));
      const cTail = gold > 0 ? "#FACC15" : color(Math.max(0.1, h - 0.15));
      comets.forEach((c, i) => {
        c.g.style.opacity = i < count ? String(tier === 0 ? 0.35 : 1) : "0";
        c.tail.setAttribute("stroke", cTail); c.glow.setAttribute("stroke", cHead);
        c.core.setAttribute("stroke", gold > 0 || h > 0.85 ? "#FFF7D6" : cHead);
        // Lap time: 7 s idle → 1.6 s at the redline (faster still while gold).
        const lap = (7 - 5.4 * h) / (gold > 0 ? 1.8 : 1);
        c.anims.forEach(a => { a.playbackRate = 6 / lap; });
      });
      const ringOn = reduce ? 0 : Math.max(0, (h - 0.55) / 0.45);
      ring.setAttribute("stroke", gold > 0 ? "#FACC15" : color(h));
      ringGlow.setAttribute("stroke", gold > 0 ? "#FACC15" : color(h));
      ring.setAttribute("opacity", (0.12 + ringOn * 0.55 + overdrive * 0.3).toFixed(3));
      ringGlow.setAttribute("opacity", (ringOn * 0.55 + overdrive * 0.4 + (gold > 0 ? .5 : 0)).toFixed(3));
      panel.style.setProperty("--bloom", (0.15 + h * 0.6 + overdrive * 0.4).toFixed(3));
      // Redline: sparks fly off the comet heads.
      if (!reduce && h > 0.78) emberTick();
      if (warp && !reduce && h > 0.2) kick();
    }

    // A point on the rounded-rect perimeter for t in 0..1 (clockwise from the top-left corner's end).
    function perimeterPoint(t) {
      const r = opts.radius, w = width, h = height;
      const straightW = w - 2 * r, straightH = h - 2 * r, arc = Math.PI * r / 2;
      const total = 2 * straightW + 2 * straightH + 4 * arc;
      let d = ((t % 1) + 1) % 1 * total;
      const seg = [[straightW, (s) => [r + s, 0]], [arc, (s) => corner(w - r, r, -Math.PI / 2 + s / r)],
                   [straightH, (s) => [w, r + s]], [arc, (s) => corner(w - r, h - r, s / r)],
                   [straightW, (s) => [w - r - s, h]], [arc, (s) => corner(r, h - r, Math.PI / 2 + s / r)],
                   [straightH, (s) => [0, h - r - s]], [arc, (s) => corner(r, r, Math.PI + s / r)]];
      for (const [len, fn] of seg) { if (d <= len) return fn(d); d -= len; }
      return [r, 0];
      function corner(cx, cy, a) { return [cx + Math.cos(a) * r, cy + Math.sin(a) * r]; }
    }
    let lastEmber = 0;
    function emberTick() {
      const now = performance.now();
      if (now - lastEmber < 120) return;
      lastEmber = now;
      comets.forEach((c, i) => {
        if (c.g.style.opacity === "0") return;
        const a = c.anims[2]; if (!a || a.currentTime == null) return;
        const t = ((a.currentTime * a.playbackRate) % 6000) / 6000;
        const [x, y] = perimeterPoint(t + i * 0.25 + 0.016);
        const outward = Math.atan2(y - height / 2, x - width / 2);
        burst(x, y, { count: 3, speed: 70, spread: 0.9, angle: outward, life: 0.6, colors: ["#FFF7D6", color(heat)], size: 1.4, gravity: 40 });
      });
    }

    // ── Particle API ──────────────────────────────────────────────────────────────────
    function burst(x, y, o) {
      if (reduce) return;
      const n = o.count || 20;
      for (let i = 0; i < n; i++) {
        const a = (o.angle ?? Math.random() * TAU) + (Math.random() - 0.5) * (o.spread ?? TAU);
        const v = (o.speed || 160) * (0.35 + Math.random() * 0.75);
        parts.push({ x: x + 40, y: y + 40, vx: Math.cos(a) * v, vy: Math.sin(a) * v, life: (o.life || 0.9) * (0.6 + Math.random() * 0.6),
                     age: 0, size: (o.size || 1.8) * (0.6 + Math.random() * 0.8), color: o.colors[i % o.colors.length],
                     gravity: o.gravity ?? 90, drag: o.drag ?? 1.6, streak: o.streak ?? true });
      }
      kick();
    }
    function streak(x0, y, x1, colorA) {
      if (reduce) return;
      for (let i = 0; i < 26; i++) {
        const x = x0 + Math.random() * (x1 - x0);
        parts.push({ x: x + 40, y: y + 40 + (Math.random() - 0.5) * 8, vx: 220 + Math.random() * 260, vy: (Math.random() - 0.5) * 30,
                     life: 0.35 + Math.random() * 0.35, age: 0, size: 1 + Math.random() * 1.2, color: i % 3 ? colorA : "#FFFFFF",
                     gravity: 0, drag: 2.5, streak: true });
      }
      kick();
    }
    function shockwave(x, y, colorA, maxR, life) {
      if (reduce) return;
      waves.push({ x: x + 40, y: y + 40, r: 4, maxR: maxR || Math.hypot(width, height), age: 0, life: life || 0.9, color: colorA });
      kick();
    }
    function celebrate(x, y) {
      shockwave(x, y, "#FACC15", Math.hypot(width, height) * 1.1, 1.1);
      shockwave(x, y, "#FFF7D6", Math.hypot(width, height) * 0.6, 0.7);
      burst(x, y, { count: 70, speed: 260, life: 1.3, colors: ["#FACC15", "#FFF1A8", "#FB923C", "#FFFFFF"], size: 2.2, gravity: 160 });
      gold = 2.2;
      setHeat(heat, overdrive);
    }

    // Lightning: a jagged, branching bolt between two points that flickers out in a quarter second.
    function lightning(x0, y0, x1, y1, colorA, o) {
      if (reduce) return;
      const make = (ax, ay, bx, by, depth) => {
        const pts = [[ax, ay]];
        const n = 7 + Math.floor(Math.random() * 4), len = Math.hypot(bx - ax, by - ay);
        const nx = -(by - ay) / (len || 1), ny = (bx - ax) / (len || 1);
        for (let i = 1; i < n; i++) {
          const t = i / n, j = (Math.random() - 0.5) * len * 0.18 * (1 - Math.abs(t - 0.5));
          pts.push([ax + (bx - ax) * t + nx * j, ay + (by - ay) * t + ny * j]);
        }
        pts.push([bx, by]);
        return pts;
      };
      const main = make(x0 + 40, y0 + 40, x1 + 40, y1 + 40);
      const branches = [];
      for (let i = 0; i < ((o && o.branches) ?? 2); i++) {
        const k = 2 + Math.floor(Math.random() * (main.length - 3)), [bx, by] = main[k];
        branches.push(make(bx, by, bx + (Math.random() - 0.3) * 60, by + (Math.random() - 0.2) * 50));
      }
      bolts.push({ paths: [main, ...branches], age: 0, life: (o && o.life) || 0.32, color: colorA });
      kick();
    }

    function kick() { if (!raf) { lastT = performance.now(); raf = requestAnimationFrame(frame); } }
    function frame(now) {
      const dt = Math.max(0, Math.min(0.05, (now - lastT) / 1000)); lastT = now;
      if (gold > 0) { gold = Math.max(0, gold - dt); if (gold === 0) setHeat(heat, overdrive); }
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, width + 80, height + 80);
      ctx.globalCompositeOperation = "lighter";
      for (let i = parts.length - 1; i >= 0; i--) {
        const p = parts[i];
        p.age += dt;
        if (p.age >= p.life) { parts.splice(i, 1); continue; }
        p.vx -= p.vx * p.drag * dt; p.vy -= p.vy * p.drag * dt; p.vy += p.gravity * dt;
        const px = p.x, py = p.y;
        p.x += p.vx * dt; p.y += p.vy * dt;
        const fade = 1 - p.age / p.life;
        ctx.globalAlpha = fade;
        ctx.strokeStyle = p.color; ctx.fillStyle = p.color;
        if (p.streak) {
          ctx.lineWidth = p.size; ctx.lineCap = "round";
          ctx.beginPath(); ctx.moveTo(px - p.vx * dt * 2.5, py - p.vy * dt * 2.5); ctx.lineTo(p.x, p.y); ctx.stroke();
        } else {
          ctx.beginPath(); ctx.arc(p.x, p.y, p.size, 0, TAU); ctx.fill();
        }
      }
      for (let i = waves.length - 1; i >= 0; i--) {
        const w = waves[i];
        w.age += dt;
        if (w.age >= w.life) { waves.splice(i, 1); continue; }
        const t = w.age / w.life, e = 1 - Math.pow(1 - t, 3);
        ctx.globalAlpha = (1 - t) * 0.85;
        ctx.strokeStyle = w.color; ctx.lineWidth = 2.5 * (1 - t) + 0.5;
        ctx.beginPath(); ctx.arc(w.x, w.y, Math.max(0, 4 + (w.maxR - 4) * e), 0, TAU); ctx.stroke();
      }
      for (let i = bolts.length - 1; i >= 0; i--) {
        const b = bolts[i];
        b.age += dt;
        if (b.age >= b.life) { bolts.splice(i, 1); continue; }
        const flicker = (1 - b.age / b.life) * (0.65 + 0.35 * Math.sin(b.age * 90));
        b.paths.forEach((path, k) => {
          for (const [w, a, c] of [[6, .25, b.color], [2.2, .8, b.color], [1, 1, "#FFFFFF"]]) {
            ctx.globalAlpha = flicker * a * (k ? 0.6 : 1); ctx.strokeStyle = c; ctx.lineWidth = w * (k ? 0.6 : 1); ctx.lineJoin = "round";
            ctx.beginPath(); path.forEach(([x, y], j) => j ? ctx.lineTo(x, y) : ctx.moveTo(x, y)); ctx.stroke();
          }
        });
      }
      ctx.globalAlpha = 1;
      let warpOn = false;
      if (warp) {
        const wctx = warp.ctx, cx = width * 0.72, cy = height * 0.28;
        wctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        wctx.clearRect(0, 0, width, height);
        warpOn = !reduce && heat > 0.2;
        if (warpOn) {
          const want = Math.round(20 + heat * 70);
          while (warp.stars.length < want) warp.stars.push({ a: Math.random() * TAU, d: Math.random() * 40, v: 0 });
          if (warp.stars.length > want) warp.stars.length = want;
          wctx.globalCompositeOperation = "lighter";
          const speed = 60 + heat * 520 + overdrive * 400;
          for (const st of warp.stars) {
            st.d += speed * dt * (0.4 + st.d / 140);
            if (st.d > Math.hypot(width, height)) { st.d = Math.random() * 20; st.a = Math.random() * TAU; }
            const x = cx + Math.cos(st.a) * st.d, y = cy + Math.sin(st.a) * st.d, tail = Math.min(60, 6 + st.d * 0.18 * (0.3 + heat));
            wctx.globalAlpha = Math.min(1, st.d / 120) * (0.25 + heat * 0.55);
            wctx.strokeStyle = color(Math.min(1, heat + (st.a % 0.3)));
            wctx.lineWidth = 0.8 + heat * 0.8;
            wctx.beginPath(); wctx.moveTo(x - Math.cos(st.a) * tail, y - Math.sin(st.a) * tail); wctx.lineTo(x, y); wctx.stroke();
          }
          wctx.globalAlpha = 1;
        }
      }
      const alive = parts.length || waves.length || bolts.length || gold > 0 || warpOn;
      if (!alive) ctx.clearRect(0, 0, width + 80, height + 80);
      raf = alive ? requestAnimationFrame(frame) : 0;
    }

    return {
      setHeat, burst, streak, shockwave, celebrate, lightning, perimeterPoint,
      setReduceMotion(v) { reduce = v; setHeat(heat, overdrive); if (v) { parts.length = 0; waves.length = 0; } },
      setPalette(fn) { opts.palette = fn; setHeat(heat, overdrive); },
    };
  }

  global.BoardFX = { create };
})(typeof window !== "undefined" ? window : globalThis);
