# Agent Speed — Design Specification

The product should feel like a precision racing instrument that comes alive as your agents speed up. Calm and
quiet when idle, electric when cruising, and on fire at the redline. Every effect must be driven by real data, stay
smooth (60 fps, no dropped frames), and never cost noticeable CPU when idle.

Both platforms share this language. Platform idioms win for chrome (menus, settings controls, window behaviour);
this document wins for the orb, the bar widget and all speed visuals.

## 0. Appearance and language

- **Appearance** setting: Auto (default, follows the system and switches live), Light, Dark. It applies to every
  surface: orb, dashboard, settings, and the bar widget text. On Windows, Auto follows the app theme
  (`AppsUseLightTheme`) for windows and the taskbar theme (`SystemUsesLightTheme`) for the taskbar widget.
- **Language** setting: System (default), 繁體中文, 简体中文, English, 日本語. Switching applies live, no restart.
  Fallback: zh-HK/zh-MO/zh-TW → zh-Hant, other zh → zh-Hans, ja → ja, everything else → en.

## 1. Speed → heat

`R` = redline speed of the selected metric. Output speed (default metric): setting default **2000 tok/s** (the
slider's maximum), range 100–2000, step 50. Total throughput (includes cached input, typically ~1000× larger): its own
setting, default **2M tok/s** (maximum), range 20k–2M. Settings migration: a stored redline equal to the old
default (400 tok/s, 200 000 tok/s) counts as never set and moves to the new default once; any other stored value is
kept. The redline slider edits the value of the current metric. `v` = live value of the
selected metric.

- Heat `h = min(1, (v / R) ^ 0.6)`.
- Overdrive `o = clamp((v - R) / R, 0, 1)` — extra intensity beyond the redline.
- Both are smoothed with a critically damped spring (settle ≈ 0.6 s) before driving visuals.

### Effect tiers (hysteresis: a tier is entered at its threshold and left at 90 % of it; a tier must hold 0.8 s to take effect)

Tiers drive the effects below (sparks, embers, shimmer, tick dots). They are not shown as text; the visible label is
the speed title.

| Tier | zh-Hant | zh-Hans | English | 日本語 | Enter at |
| --- | --- | --- | --- | --- | --- |
| idle | 待機 | 待机 | IDLE | 待機 | `v < 0.5` for 3 s |
| cruise | 巡航 | 巡航 | CRUISE | 巡航 | `v ≥ 0.5` |
| boost | 加速 | 加速 | BOOST | 加速 | `v ≥ 0.15 R` |
| overdrive | 狂飆 | 狂飙 | OVERDRIVE | 爆速 | `v ≥ 0.375 R` |
| redline | 極限 | 极限 | REDLINE | 限界 | `v ≥ 0.75 R` |

### Speed titles (the visible label)

The label under the value (orb, dashboard hero, accessibility) is a vehicle title from this ladder, colored with the
heat color. Thresholds scale with `R` (shown values are for R = 400 tok/s; the default is 2000, where each is five times
higher), so every metric and redline
setting reads the same. Same hysteresis and 0.8 s hold as the effect tiers. Idle shows `tier.idle` (待機).

| Key | zh-Hant | zh-Hans | English | 日本語 | Enter at | at R = 400 |
| --- | --- | --- | --- | --- | --- | --- |
| `title.walk` | 步行 | 步行 | WALKING | 徒歩 | `v ≥ 0.5` | 0.5 |
| `title.bicycle` | 自行車 | 自行车 | BICYCLE | 自転車 | `0.02 R` | 8 |
| `title.ebike` | 電動自行車 | 电动车 | E-BIKE | 電動自転車 | `0.03 R` | 12 |
| `title.motorcycle` | 摩托車 | 摩托车 | MOTORCYCLE | バイク | `0.04 R` | 16 |
| `title.car` | 汽車 | 汽车 | CAR | 自動車 | `0.06 R` | 24 |
| `title.sportsCar` | 跑車 | 跑车 | SPORTS CAR | スポーツカー | `0.08 R` | 32 |
| `title.bulletTrain` | 高鐵 | 高铁 | BULLET TRAIN | 新幹線 | `0.12 R` | 48 |
| `title.raceCar` | 賽車 | 赛车 | RACE CAR | レースカー | `0.17 R` | 68 |
| `title.maglev` | 磁浮列車 | 磁悬浮列车 | MAGLEV | リニア | `0.24 R` | 96 |
| `title.jetliner` | 噴射客機 | 喷气客机 | JETLINER | ジェット機 | `0.34 R` | 136 |
| `title.fighterJet` | 戰鬥機 | 战斗机 | FIGHTER JET | 戦闘機 | `0.5 R` | 200 |
| `title.hypersonic` | 極音速飛機 | 高超音速飞机 | HYPERSONIC | 極超音速機 | `0.7 R` | 280 |
| `title.rocket` | 火箭 | 火箭 | ROCKET | ロケット | `1.0 R` | 400 |

A title change slides the old label up/out and the new one up/in (0.35 s); the gauge flash is reserved for effect-tier
changes, so climbing the ladder feels lively but never noisy. A title that does not fit the label width shrinks (down
to 80 % of the label size); it is never truncated.

### Heat palette (interpolate in OKLab or linear RGB between stops)

| h | Color | Note |
| --- | --- | --- |
| 0.00 | `#7C8798` | slate, idle |
| 0.10 | `#2DD4BF` | teal |
| 0.30 | `#38BDF8` | sky |
| 0.48 | `#818CF8` | indigo |
| 0.64 | `#E879F9` | fuchsia |
| 0.78 | `#FB7185` | rose |
| 0.90 | `#FB923C` | orange |
| 1.00 | `#FACC15` | gold |

White-hot core for highlights: `#FFF7D6`. With overdrive `o > 0`, highlights blend toward the white-hot core by `o`.

### Source colors

| Source | Color |
| --- | --- |
| Codex | `#34D399` mint |
| Claude Code | `#FF8A65` coral |
| Claude Desktop | `#B39DFF` lilac |
| DeepSeek | `#4D6BFE` DeepSeek blue |
| OpenCode | `#F4B13E` amber |
| Gemini CLI | `#34B8F0` sky |
| omp | `#84CC16` lime |
| Antigravity | `#EC4899` pink |

Pink is `#EC4899`, not the lighter `#F472B6` (too close to coral and lilac); lime is `#84CC16`, not the lighter `#A3E635`, which nearly vanishes on light backgrounds.

## 2. The orb (floating window)

A circular smoked-glass instrument. This section defines the shared frame and the default style, `led`; section 2a adds
four more styles and section 2b lets the user float several orbs, each tracking its own sources. Diameter `S` (per orb,
96–240 pt/DIP, `minimal` 64–240, default 148). The window is `1.5 S` square so the aura and sparks can leave the disc; everything outside the disc is fully transparent and ignores clicks wherever
the platform allows it. The orb follows the Appearance setting. The dark variant is described inline below; the
light variant ("porcelain") changes only these tokens:

| Token | Dark | Light |
| --- | --- | --- |
| Body gradient | `#1B2030 → #090B10` at 88 % | `#FFFFFF → #E7EBF3` at 94 % |
| Blur material | macOS `.hudWindow` | macOS `.popover` (light) |
| Rim | white 14 % top → 0 % | white 95 % top highlight + 1 px outer stroke black 8 % |
| Drop shadow | black 45 %, y `0.03 S`, blur `0.10 S` | black 16 %, y `0.04 S`, blur `0.12 S` |
| Unlit segments | white 7 % | black 8 % |
| Value text | white 96 % | `#0B0D12` 92 % |
| Unit / secondary | white 55 % | black 50 % |
| Title label | heat color | heat color darkened 18 % (OKLab L) for ≥ 3:1 contrast |
| Aura opacity | `0.55 * h`, additive | `0.32 * h`, normal blending |
| Sparks / embers | additive, heat → white-hot | normal blending, heat colors, size +20 %, white-hot replaced by `#FFB020` |
| Leading segment | white-hot `#FFF7D6` + bloom | heat color + bloom |

Layers, back to front:

1. **Aura** — radial gradient in the heat color around the disc, radius `0.5 S → 0.75 S`, opacity `0.55 * h`
   (0 at idle). Breathes: period 4 s at cruise → 0.7 s at redline, amplitude ±15 %. Flares on every response pulse.
2. **Body** — circle with a background blur of the desktop (macOS `NSVisualEffectView` `.hudWindow`; Windows: acrylic
   is not available for layered windows, use a solid gradient) overlaid with a vertical gradient `#1B2030 → #090B10`
   at 88 % (multiplied by the opacity setting). A 1 px inner stroke, white 14 % at the top fading to 0 % at the bottom
   (specular rim), and a soft inner shadow. A faint radial "lens" highlight at the top-left (white 6 %).
   - Embedded style (dashboard hero) only: the hero vehicle layer (section 4c) is drawn here, above the body and below
     the gauge.
3. **Gauge** — 270° arc starting at 135° (bottom-left) going clockwise to 45° (bottom-right), gap at the bottom.
   Radius `0.41 S`, made of **36 LED segments**, each `0.045 S` long radially, with 2.2° gaps. Unlit segments:
   white 7 %. Lit count = `h * 36` (fractional: the last segment is partially lit by brightness). Each lit segment has
   the heat color *of its own position* (segment i uses `h_i = i / 35`), so the arc shows the spectrum as it fills.
   The leading segment is drawn white-hot with a bloom (blurred copy, radius `0.03 S`). Four tick dots just outside
   the arc mark the tier thresholds.
   Redline: lit segments shimmer (random brightness 75–100 %, 12 Hz) and the outer edge of the arc gets a pulsing
   glow.
4. **Comet** — a 40° gradient arc (transparent → heat color → white-hot head) travelling just outside the gauge,
   clockwise. Hidden at idle. Period: 6 s at cruise → 0.7 s at redline (interpolate by `h`). Opacity `0.25 + 0.6 h`.
5. **Sparks** (from boost upward, `h ≥ 0.45`) — emitted from the leading segment and randomly along the lit arc,
   flying outward with a tangential component in the comet direction. Lifetime 0.5–1.2 s, size 1.2–2.8 px, color from
   heat to white-hot, additive blending, slight drag. Emission rate `120/s * ((h - 0.45) / 0.55)`.
   **Embers** at redline: rising flame particles around the upper half of the ring with turbulence and gravity
   pointing up, rate `+90/s * max(o, 0.3)`.
6. **Readout** — centered (floating orb only; the embedded style shows the hero vehicle instead).
   - Value: system font (SF Pro / Segoe UI Variable Display), weight bold, tabular digits, size `0.24 S`, white 96 %.
     Rolling digit transition on change. From boost upward the value gets a heat-colored glow (radius `0.035 S`).
     From overdrive upward the value leans forward: skew `-6° * h` (motion feel). At redline add a 0.6 px jitter
     (≤ 30 Hz) — disabled in "soft" effects and with Reduce Motion.
   - Unit `tok/s`: `0.072 S`, medium, white 55 %, tracking +0.5.
   - Title label (speed title, section 1): `0.062 S`, semibold, uppercase for Latin, tracking +1.6, in the heat
     color, below the unit. A title change slides the old label up/out and the new one up/in (0.35 s); an effect-tier
     change also flashes the gauge (+25 % brightness, 0.4 s).
   - Agent dots: in the gauge gap at the bottom, one 4 px dot per generating agent colored by source (max 6, then
     `+n`), gently pulsing while generating.
7. **Pulse** — on each `responseCompleted`: a shockwave ring from `0.5 S` to `0.74 S`, stroke 2 px → 0, opacity
   0.6 → 0 over 0.7 s ease-out, color = heat of that response's speed. Strength (opacity/width) scales with
   `log10(output)`. Coalesce: at most one wave per 150 ms.
8. **New peak** — on `newPeak`: 24 gold rays burst from the disc edge, the gauge sweeps 0 → current in 0.5 s, and a
   pill label `新紀錄 NEW PEAK` (localized) springs in above the value for 1.8 s.

Idle state: value `0` in white 60 %, label `待機`, gauge dim, comet hidden, and every 6 s a slow light sweep runs
along the unlit segments as a sign of life. Optional "fade when idle" (setting): after 60 s idle, fade the window to
45 % opacity; restore instantly on activity or hover.

Hover: the orb scales to 1.03 (spring), the title label crossfades to a compact info line
(`今日 1.2M · 3 個 agent`, counted over the orb's sources), and the cursor shows it is draggable. An orb tracking a
subset of sources also shows its source chip (2b).

Interaction:
- Drag anywhere on the disc to move. On release within 24 pt of a screen edge, snap to a 12 pt margin (spring); other
  orbs attract it magnetically (2b). Position (per orb, per display arrangement, 2b) persists.
- Double-click: open the dashboard.
- Right-click: the orb's context menu (2b) — style, sources, size (S / M / L presets 112 / 148 / 196), effects (Off /
  Soft / Standard / Max), always on top, click-through, add / duplicate / split / merge, hide, remove, settings, quit.
- Click-through mode (per orb): the orb ignores all mouse events; it is controlled from the menu bar / taskbar.

Effects intensity (setting, default Standard):

| Level | Behaviour |
| --- | --- |
| Off | Static colors and segments only; no aura breathing, comet, particles, pulses, jitter or skew |
| Soft | Aura, comet and pulses; no particles, no jitter |
| Standard | Everything above, sparks, embers, skew; jitter only at redline |
| Max | Standard with 1.6× particle rates, stronger bloom, and a faint heat-distortion shimmer at redline |

System "Reduce Motion" caps the level at Soft and replaces movement with crossfades. The table describes `led`; every
other style has its own row set in 2a.

Performance: the frame policy and budget of 2a apply to every style, `led` included (idle is static apart from the 6 s
sweep, active is capped at 60 fps, never the display rate). Particles use a fixed-size pool (≤ 400). The renderer
pauses entirely while the orb is hidden or fully occluded.

## 2a. Orb styles

Every floating orb has a **style**: `led` (the LED gauge of section 2, the default), `needle`, `liquid`, `reactor` or
`minimal`. The styles are one family: they share the disc and window, the body, the aura, the readout, the agent dots,
the response wave, the new-peak pill, hover, the source bezel (2b), the effects levels, accessibility and the frame
policy below; each one adds its own instrument between the body and the readout. The dashboard hero (section 4) always
uses `led` with the hero vehicle.

| Key | Name (`style.*`) | Personality | Primary encoding of `h` |
| --- | --- | --- | --- |
| `led` | LED 儀表 | precise, electric | lit segments of a 270° arc |
| `needle` | 指針轉速錶 | mechanical, physical, racing | needle angle on a 270° dial with a red zone |
| `liquid` | 能量液球 | organic, luminous, alive | liquid level in a glass sphere |
| `reactor` | 反應爐 | sci-fi, rotational energy | arc length and spin of counter-rotating rings |
| `minimal` | 極簡圓環 | quiet, typographic | one thin progress ring |

Conventions for this section: `c` = disc center, `S` = diameter, radii and lengths are fractions of `S`, angles are
degrees clockwise from 3 o'clock in screen space (y down). A *dial position* `n` maps to the angle `135° + 270° n` (the
LED arc). `heat(x)` is the heat palette; the *label color* is the title token (dark: heat; light: heat darkened 18 %
OKLab L, stepped to ≥ 4.5:1 against the body); *hot* is `mix(heat(h), #FFF7D6, 0.55 + 0.45 o)` in dark and
`mix(label color, #FFB020, 0.3)` in light. Glows are Gaussian with standard deviation `σ` (a canvas `shadowBlur` of
`2σ`), in points, independent of the backing scale. "Additive" applies to dark only; light uses normal blending.
`h_raw = min(1, (v / R)^0.6)` and `o_raw` are the unsmoothed values of section 1 (they step with the 500 ms engine tick);
`h`, `o` are the smoothed ones. Every speed-dependent oscillation uses an accumulated phase (4c resolved detail 3).

### Shared foundation

- **Window, body, rim, shadow, Reduce Transparency**: section 2, layer 2, unless a style's table overrides a token.
- **Aura**: section 2, layer 1, for every style except `minimal` (no aura). Its breathing and the idle sweep run on one
  global clock, so several orbs breathe and sweep together.
- **Readout**: section 2, layer 6 — same fonts, weights, tokens, glow (σ 0.035 S), forward lean (`−6° h` from
  overdrive), redline jitter and title slide — placed by the style's frame:

  | Style | Value size · baseline · max width | Unit | Title baseline · size · max width | Agent dots | Pill center |
  | --- | --- | --- | --- | --- | --- |
  | `led` | 0.24 S · c + 0.012 S · 0.62 S | 0.072 S, 0.104 S below the value | 0.09 S below the unit · 0.062 S · 0.58 S | c + 0.3785 S | c − 0.232 S |
  | `needle` | 0.205 S · c + 0.035 S · 0.40 S (inside the hub) | 0.062 S, 0.08 S below | fixed at c + 0.338 S (dial gap under the hub) · 0.062 S · 0.46 S; hover line ≤ 0.42 S at 85 % size | c + 0.405 S | c − 0.175 S |
  | `liquid` | as `led` | as `led` | as `led` | c + 0.3785 S, with a 1.2 pt halo | as `led` |
  | `reactor` | 0.205 S · c + 0.03 S · 0.46 S (core window) | 0.062 S, 0.08 S below | 0.074 S below the unit · 0.058 S · 0.50 S | c + 0.252 S (inside the core) | c − 0.172 S |
  | `minimal` | 0.30 S · c + 0.07 S · 0.66 S | none | 0.098 S below the value · 0.068 S · 0.62 S | none | c − 0.25 S |

  - The value shrinks to its max width. Titles shrink to 80 % and are never truncated (section 1); the hover info line
    shrinks to 70 %. Unit and title never go below 7 pt; below S = 64 (thumbnails, 2b and section 5) only the value is
    drawn.
  - **Odometer**: on a value change only the characters that changed roll, each clipped to its own character cell,
    travel 0.92 × the value size, 0.24 s ease-out (up when rising). Different lengths crossfade in 0.12 s. No roll with
    Off, Reduce Motion, `minimal` and S < 80 (the text simply updates).
  - Readout text is always drawn above every effect. Particles that pass behind it fade to 35 % inside the *readout
    zone* (an ellipse 0.30 S × 0.19 S around `c + (0, 0.07 S)`, Gaussian falloff) so it never competes with them.
- **Agent dots**: section 2, layer 6, at the style's position; only the orb's own agents (2b). None in `minimal`.
- **Response pulse** (`responseCompleted` of the orb's sources, 2b): the shockwave of section 2, layer 7, for every
  style except `minimal`, plus the style's accent below. Pulses toggle: Settings → Effects → Response pulses.
- **New peak** (per orb, METRICS 6.8): the pill of section 2, layer 8, for every style; the 24 gold rays for every style
  except `minimal`; plus the style's accent below. Toggle: Celebrate new peaks.
- **Tier flash**: an effect-tier change brightens the style's primary lit element by 25 % for 0.4 s.
- **Idle** (tier idle): value `0` in the idle value color, `tier.idle` in the secondary color, the instrument at rest.
  **Sign of life**: every 6 s on the global clock (skipping one that would start less than 2 s after entering idle) a
  0.9 s event as listed per style; `minimal` has none.
- **Hover**: scale 1.03 (spring), the title crossfades (0.2 s) to the info line (`today.summary`, counted over the orb's
  sources), plus the source chip of 2b. Unchanged otherwise.
- **Style change**: the outgoing and incoming renderers crossfade for 0.25 s (Reduce Motion: cut); springs, trackers
  and particles live in the orb, not in the style, so the value never jumps.

### 2a.1 `led` — LED gauge

Section 2, layers 1–8, unchanged; it is the reference for the family. Additions: the shared rules above (odometer,
readout zone, text floors), the frame policy and budget below, and LOD: S < 120 halves the spark and ember rates;
thumbnails draw the 36 segments without bloom.

### 2a.2 `needle` — Analog needle tachometer

A classic racing tachometer: graduated dial, red zone, a heavy needle that sweeps from the zero stop to the redline,
trembles at speed and pins against the peg beyond the redline, and shift lights that flash at the redline. The needle
shows `n = h` on the LED's 270° scale; the red zone starts at the redline-tier threshold `n_r = 0.75^0.6 = 0.8415`.

Layers, back to front:

1. **Aura**, **body**: shared.
2. **Dial band**: a ring stroke centered at r 0.41 S, width 0.075 S, over the scale (n −0.004 → 1.004); white 2.8 % /
   black 3.5 %.
3. **Red zone**: arc centered at r 0.4655 S, width 0.017 S, butt caps, n_r → 1; `#F43F5E` at 62 % (dark) / `#E11D48` at
   80 % (light). In the redline tier its opacity pulses 80 → 100 % at `2 + o` Hz; Standard and Max add a glow σ `0.025 S (0.6 + 0.4 o)`
   (× 1.3 at Max) in the same color.
4. **Graduations**: 41 ticks at n = k/40, outer radius 0.445 S, round caps; every 5th is major (9): length 0.062 S,
   width 0.014 S; minor: 0.03 S, 0.0075 S. Unlit: white 50 % / 24 % (major / minor; Increase Contrast × 1.6), light
   black 42 % / 20 %; unlit ticks inside the red zone use the red-zone color at 85 % / 60 %. **Lit** (`n_k ≤` needle,
   not idle): `heat(n_k)` (light: its label color) at 95 %; the last lit tick blends 75 % toward `#FFF7D6` (light: 25 %
   toward `#FFB020`); lit majors glow σ 0.015 S (dark, Soft and up).
5. **Trace**: an arc at r 0.366 S, width 0.011 S, from n = 0 to the needle; each point in `heat(its n)`, opacity rising
   from 30 % at 0 to 100 % at the needle, × 0.70 additive (dark) / × 0.85 (light). Hidden at idle.
6. **Stop pins**: dots of radius 0.011 S at r 0.405 S, n = −0.034 and n = 1.034; white 32 % / black 28 %.
7. **Shift lights** (S ≥ 120): five lamps of radius 0.019 S on r 0.33 S at −110°, −100°, −90°, −80°, −70°, housings
   white 8 % / black 9 %. Lamp i lights when the needle passes n = 0.70, 0.735, 0.77, 0.805, 0.8415, in `heat(that n)`
   (light: darkened 15 %), glow σ 0.02 S (dark). In the redline tier all five flash together at **3 Hz** (opacity
   28 → 100 % through `smoothstep(−0.35, 0.35, sin φ)`, ≈ 50 % duty) in `mix(heat(0.97), #FFF7D6, 0.55)` (light
   `#F59E0B`). This is the product's only blinking element and never exceeds 3 flashes per second.
8. **Wake** (Soft and up, while the needle turns faster than 12°/s): an annular wedge r 0.235 → 0.43 S trailing the
   needle by `4° + 0.085 s × |ω|` (≤ 55°), `heat(h)` from 32 % at the needle to 0, additive.
9. **Needle**: a tapered blade from the counterweight (r −0.09 S, hidden under the hub) through the pivot (width
   0.062 S) to a round tip at r 0.44 S (width 0.012 S). Fill `heat(h)` blended `0.05 + 0.45 o` toward `#FFF7D6`
   (dark) / the label color (light); idle `#9EA8BA` at 75 %. Dark: glow σ 0.0275 S at `0.45 + 0.35 h` (× 1.25 at
   Max). Light: drop shadow black 22 %, offset (0.006, 0.01) S, σ 0.007 S. Spine: a 0.0045 S line from r 0.21 S to the
   tip, `#FFF7D6` 75 % (dark) / white 55 % (light).
10. **Peg sparks** (Standard and up), below.
11. **Hub**: a disc of radius 0.235 S, vertical gradient `#262C3C → #0C0E14` at 95 % (light `#FFFFFF → #ECEFF5` at
    98 %), drop shadow black 55 % / 16 %, σ 0.025 S, y 0.012 S; a chrome bezel stroke 0.013 S with a 135° gradient
    white 42 % → 10 % → 22 % (light: white → `#A0A8B6` 55 % → `#78808E` 60 %).
12. **Readout, agent dots, wave, rays, pill**: shared (frame table).

Motion:

- **Needle dynamics**: target `n* = h_raw` (0 at idle), plus `0.02 + 0.05 o_raw` while `o_raw > 0` so it presses into
  the peg. An underdamped spring, frequency 1.5 Hz, damping ratio 0.55 (≈ 12 % overshoot on a step), integrated in
  sub-steps ≤ 1/480 s. Stops at n = −0.012 and 1.012; hitting one reverses the velocity × −0.32 (a small bounce).
  Off and Reduce Motion: the needle follows the smoothed `h` directly.
- **Tremble** (Standard and Max): amplitude `A = smoothstep(0.55, 1, h) × (0.3° + 1.0° o)` (× 1.4 at Max), offset
  `A (0.5 sin φ1 + 0.3 sin(φ2 + 1.3) + 0.2 sin(φ3 + 2.1))` at 11.3 / 17.9 / 26.1 Hz, added after the spring and
  clamped by the stops: pinned at the peg it reads as the needle chattering against the pin.
- **Peg sparks** (while pinned: needle ≥ 1.0105 and target > 1.012): `25 + 70 o` per s (× 1.6 Max) from the contact
  point at r 0.405 S, speed 0.35–0.9 S/s along the clockwise tangent ±30°, plus 0.2 S/s outward, gravity 1.1 S/s²,
  drag 2/s, life 0.22–0.55 s; streaks (velocity × 0.025 s), width 0.8–1.6 pt, `#FFF7D6` → `heat(0.95)` (light
  `#FFB020` → label color). Pools: 48 (Standard), 80 (Max).
- **Pulse accent**: the tip flares for 0.35 s — glow + 0.0175 S σ and a radial `#FFF7D6` (light `#FFB020`) spot of
  radius 0.06 S at 70 % × strength.
- **New-peak accent — the gauge sweep**: displayed `n + (1.012 − n)(smoothstep(0, 0.38, τ) − smoothstep(0.5, 1.0, τ))`
  for τ ∈ [0, 1] s: the needle swings to the peg and back, like an instrument cluster's start-up sweep; the shift lights
  fire once with it.
- **Title change**: shared slide. **Tier flash**: lit ticks and trace.
- **Idle**: needle on the zero stop, slate; trace and lamps dark. Sign of life: the 6 s sweep brightens the graduations
  as a band `exp(−90 d²)` (+35 % dark / +25 % light) travelling n −0.15 → 1.15 in 0.9 s, in step with `led`.

| Effects | `needle` |
| --- | --- |
| Off | needle on the smoothed `h` (no overshoot or tremble), red zone and lamps steady, no wake, aura breathing or pulses |
| Soft | spring overshoot and stop bounce, wake, shift-light flash, aura breathing, pulses with the tip flare; no tremble or sparks |
| Standard | Soft + tremble, peg sparks, red-zone glow |
| Max | Standard with sparks × 1.6, glows × 1.25, tremble × 1.4 |

Reduce Motion: as Off for motion (lamps steady-lit at the redline instead of flashing), crossfades for titles and the
new peak (pill only). Increase Contrast: ticks × 1.6 opacity, a 1 pt outline around the needle (black 60 % dark /
white light), glows × 0.5. Windows high contrast: ticks, needle and trace in the theme's text color, red zone in its
highlight color, no glow, wake or sparks. LOD: S < 120 drops the minor ticks and the shift lights; thumbnails show dial
band, red zone, majors, needle and value.

### 2a.3 `liquid` — Liquid energy orb

A glass sphere of glowing liquid. The level rises with speed, the surface waves grow, luminous motes stream up, and at
the redline it boils, splashes and steams; beyond it the core turns white-hot.

Layers, back to front (everything from 3 to 9 is clipped to the inner glass, r 0.455 S):

1. **Aura**: shared.
2. **Body**: section 2 without the lens highlight; dark gradient `#181D2B → #07090D`, light `#FFFFFF → #E4E8F0`.
   **Volume shading**: radial gradient centered at `c − (0.12, 0.16) S`, clear to 75 % of 0.5 S, then to black 42 %
   (light: `#1E283C` 12 %) at the edge.
3. **Light spill**: a 0.22 S band above the surface in the liquid color `L` from 0 to 16 % (additive; light 10 %),
   × `0.4 + 0.6 h`.
4. **Back wave** (S ≥ 120): the surface below with phase + 1.7 rad, 75 % amplitude, raised 0.008 S, filled with `L`
   darkened 25 % (light 8 %) at 55 % of the front opacity.
5. **Liquid body**: vertical gradient from `min(y_L, c + 0.2 S) − 0.04 S` to the bottom, `L` at `α` → `L` darkened 62 %
   (light 14 %) at `α + 0.2`. `L = heat(h)` (dark) / `mix(heat(h), white, 0.12)` (light); `α = 0.46 + 0.10 h` (dark) /
   0.80 (light); idle: slate at 0.40 / 0.55. Then a **spherical shade** over the liquid: radial from 35 % of the radius
   (center `c − (0.06, 0.08) S`) to black 50 % (light `#283042` 14 %) at the glass, so the liquid reads as a glowing
   volume, not a flat fill. Then the **inner glow** (additive), kept below the readout: radial at
   `(c_x, max(y_L + 0.08 S, c_y + 0.38 S))`, radius 0.32 S, `mix(L, #FFF7D6, 0.25 + 0.45 o)` at
   `(0.26 + 0.2 o)(0.3 + 0.7 h)` (× 1.25 Max; light `L` at 10 %). Then the **readout scrim** (dark only): an elliptical
   radial gradient centered at `c + (0, 0.1 S)`, radii 0.36 × 0.26 S, black 35 % → 30 % at 55 % → 0. Together they keep
   the white readout ≥ 4.5:1 on every heat color up to full overdrive (computed worst case: value 5.0:1, title
   4.55:1).
6. **Energy currents** (Soft and up, S ≥ 120, not Reduce Motion): three filaments at 25 / 50 / 75 % of the depth,
   `y = depth + 0.03 S sin(7u + φ) + 0.015 S sin(13u − 1.7 φ)` across the width, widths 0.018 / 0.014 / 0.010 S,
   blurred σ 0.006 S, `mix(L, #FFF7D6, 0.35 + 0.4 o)` at 10 % (light: white 22 %) × `(0.4 + 0.6 h)`, drifting with the
   wave phase.
7. **Motes and bubbles**, **pops**, **splashes**, **steam**, **falling droplets** (below).
8. **Meniscus**: the surface stroked at `(0.008 + 0.004 h) S` in `mix(L, #FFF7D6, 0.45 + 0.5 o)` with glow
   σ `(0.015 + 0.015 h) S` (× 1.3 Max); light: `heat(h)` darkened 25 %; idle white 28 % / slate 50 %.
9. **Glass**: wall stroke 0.006 S at r 0.455 S (white 9 % / black 6 %); bottom caustic: arc r 0.475 S, 55° → 125°,
   width 0.02 S, σ 0.008 S, `L` at `0.35 (0.3 + 0.7 h)` (light 0.3); specular ellipse 0.2 × 0.12 S rotated −34° at
   `c − (0.17, 0.22) S`, white 13 % / 75 % → 0; rim glint arc r 0.42 S, 200° → 236°, width 0.012 S, white 22 % / 95 %.
10. **Readout split by the surface**: above the surface in the normal tokens; below it in white 97 % / unit white 80 % /
    title white 94 %, each with a black 55 % shadow σ 0.015 S (dark), or ink `#0B0D12` 94 % / unit 72 % / title =
    `heat(h)` darkened 55 % without glow (light). The title therefore never sits in its own color on its own color.
11. **Agent dots** with a 1.2 pt halo (black 55 % / white 75 %), wave, rays, pill: shared.

Surface and level:

- Level (fraction of the inner diameter): `f* = 0.08 + 0.74 h + 0.06 o` (idle 0.08) through an underdamped spring,
  0.75 Hz, damping 0.62 (a gentle slosh after a change); Off and Reduce Motion: critically damped, settle 0.9 s. Mean
  surface `y_L = c_y + 0.455 S − 0.91 S f`.
- `y(x) = y_L + A [0.55 sin(2π x / 0.9 S + φ1) + 0.30 sin(2π x / 0.47 S − φ2) + 0.15 sin(2π x / 0.23 S + φ3)] + chop +
  ripples` with `A = (0.004 + 0.02 h + 0.018 o) S` (Soft × 0.6, idle 0.002 S, Off/Reduce Motion 0) through a spring
  (settle 0.8 s); phase rates φ1 `0.25 + 0.5 h + 0.3 o`, φ2 `0.4 + 0.8 h + 0.4 o`, φ3 `0.9 + 1.6 h` Hz. The two
  counter-moving components make a sloshing standing wave rather than a conveyor belt.
- **Boil chop** (redline tier, Standard and up): `0.006 S (0.5 + 0.5 o) sin(2π x / 0.11 S + φ4)(0.7 + 0.3 sin(x /
  0.05 S + 1.7 φ4))`, φ4 3.1 Hz; halved below S 120.
- **Ripples** (≤ 4): `−A_r e^(−τ/0.75) exp(−((d − 0.42 S τ) / 0.075 S)²) cos(2π (d − 0.42 S τ) / 0.1 S)`, `d` = distance
  from the impact, `A_r = 0.022 S (0.6 + 0.6 strength)`.

Particles:

- **Motes / bubbles** rise from near the bottom (x uniform in ±60 % of the radius), speed `(0.08 + 0.35 h + 0.25 o)
  S/s × U(0.7, 1.3)`, accelerating `(0.12 + 0.2 h) S/s²`, wobble 0.006 S at 2.3 Hz, radius `(0.006 + 0.01 h) S ×
  U(0.6, 1.4)` (≥ 0.9 pt). Their look follows `hot = smoothstep(0.35, 0.9, h)`: cool ones are glass bubbles (a ring
  of 28 % of the radius, white 50 % / 85 %, and a highlight dot); hot ones are luminous motes (radial gradient to
  2 × the radius: `mix(L, #FFF7D6, 0.9)` 95 % → 40 % at 35 % → 0, additive). They pop at the surface (a ring growing
  to 2.5 × in 0.25 s); while boiling, 55 % also throw 1–2 splash droplets.
- Rates: Soft `0.6 + 3 h` per s (≤ 6 alive); Standard `1.5 + 12 h` (+ `6 + 10 o` in the redline tier) per s, pool 32
  (16 below S 120); Max × 1.6, pool 48.
- **Splash droplets** (pool 40): ±0.25 S/s sideways, 0.35–0.7 S/s up, gravity 2.2 S/s², radius 0.006–0.011 S, gone
  when they fall back in.
- **Steam** (redline tier, Standard and up): `2 + 4 o` wisps per s (× 1.6 Max, pool 12), soft discs of radius
  0.03–0.06 S doubling as they rise 0.06–0.12 S/s for 0.8–1.4 s, `#FFF7D6` 12 % (light white 18 %) × `sin(π u)`.

Events:

- **Pulse accent**: a droplet (teardrop of radius `(0.012 + 0.006 strength) S` in the response's heat color) appears
  0.16 S above the surface (never above c − 0.414 S, never below c − 0.3 S) and falls at 2.6 S/s²; at impact a ripple,
  3–6 splash droplets (Standard and up) and 2 / 6 bubbles (Soft / Standard). At most 3 droplets in flight.
- **New-peak accent**: the level surges (velocity + 0.9/s on a 0.9 Hz, damping 0.45 spring: ≈ +0.1 of the diameter),
  a gold band (`#FACC15` 45 %, ±0.06 S) rises from the bottom to the surface over 0.9 s, and 22 gold motes stream up.
- **Tier flash**: the meniscus.
- **Idle**: a slate pool at 8 %, flat (static), no motes. Sign of life: one cool bubble rises from the bottom-left and
  pops (≤ 1.2 s, 30 fps).

| Effects | `liquid` |
| --- | --- |
| Off | static level by `h`, frozen surface shape, no particles, currents or droplets, fixed glow |
| Soft | waves × 0.6, ≤ 6 bubbles, currents, droplets with ripples (no splash), aura breathing; no boil, steam or splash |
| Standard | full waves and motes, boil chop, splashes, steam, surge |
| Max | Standard with particles × 1.6 and glows × 1.25 |

Reduce Motion: flat static surface (A = 0), the level eases critically damped, no particles, currents or droplets;
boiling reads as a brighter meniscus and inner glow; the new peak is the pill plus the gold band as a 0.3 s crossfade.
Increase Contrast: meniscus × 1.5 width, readout shadow 75 %, glows × 0.5. Windows high contrast: the liquid is a solid
fill in the theme's highlight color at 35 %, meniscus in the highlight color, no particles or glows. Reduce
Transparency: solid body and α + 0.15. LOD: S < 120 drops the back wave, currents and steam, halves the chop and caps
the motes at 16; thumbnails show the still liquid and the value.

### 2a.4 `reactor` — Reactor energy ring

A sci-fi core: a dark core window carries the readout; around it three counter-rotating energy rings spin up with
speed, lengthen until they close into a solid ring of fire beyond the redline, and throw lightning arcs.

Layers, back to front:

1. **Aura**: shared. **Body**: dark `#141824 → #06070B`; light as section 2 with the bottom `#E5E9F1`.
2. **Stator**: a full ring r 0.468 S, width 0.006 S, unlit token × 1.4; 24 notches r 0.456 → 0.474 S, width 0.005 S
   (every 6th × 2.6 opacity; 4 notches below S 120).
3. **Tracks**: unlit rings under rotor A (r 0.425 S, width 0.024 S) and rotor C (r 0.338 S, width 0.009 S).
4. **Corona** (not idle): radial gradient r 0.30 → 0.40 S, `heat(h)` (dark: blended `0.2 o` toward `#FFF7D6`) at
   `(0.10 + 0.42 h)` × breathing (±15 %, `0.3 + 1.7 h` Hz) + pulse flare (+0.25 fading over 0.35 s), additive (light
   `0.06 + 0.2 h`), × 1.2 at Max.
5. **Rotor A** (outer, clockwise): three arcs 120° apart, each `20° + 90° h + 10° o` long (capped at 120°: at full
   overdrive they join into a closed ring), width 0.024 S (idle 0.6 ×). Each arc is a gradient from the tail
   (`heat(h)` at 25 % dark / 50 % light) to the head (*hot*), glow σ 0.015 S (dark, × 1.3 Max).
6. **Rotor B** (middle, counter-clockwise): 24 coils (12 of 13° below S 120), 7.5° long, width 0.018 S at r 0.383 S.
   Energy `e_i = 0.18 + 0.82 (½ + ½ cos(3 θ_i − ψ))^2.2 (0.35 + 0.65 h)`: three bright bands run clockwise — against
   the coils — at `ψ̇ = 0.15 + 1.4 h + 0.6 o` rev/s; color `heat(h)`, blending toward *hot* above e 0.75; opacity `e`.
   Idle: slate at 25 %.
7. **Rotor C** (inner, clockwise, fastest): two arcs 180° apart, `14° + 130° h + 20° o` long (≤ 175°), width 0.009 S,
   gradient as A.
8. **Lightning** (redline tier, Standard and up): `1.5 + 6 o` bolts per s (× 1.6 Max), at most 3 alive (Max 4). A bolt
   lives 0.09–0.18 s and is re-randomized every 50 ms: from the core rim (r 0.306 S) to rotor A (r 0.425 S) within
   ±29°, or — when `o > 0.3`, 40 % of bolts — leaping outward from r 0.437 S to r 0.56–0.66 S, into the aura (still
   inside the 1.5 S window). An 8-segment polyline with perpendicular jitter `±0.048 S sin(π u)`, three strokes: glow
   0.03 S `heat(0.92)` 28 %, body 0.011 S 75 %, core 0.0045 S `#FFF7D6` (light: label color of 0.9 and `#FFB020`),
   full for 20 % of its life, then fading. 6 sparks at the far end (0.2–0.6 S/s, drag 3/s, 0.15–0.4 s; pool 40, Max 64).
   Flashes that brighten the corona are limited to 3 per second.
9. **Pulse flare**: a ring from r 0.30 to 0.50 S in 0.32 s (ease-out), width 0.012 S → 0, in the response's heat color
   at 55 %; it hands over to the shared wave at the disc edge.
10. **Core window**: a disc r 0.30 S, radial gradient `#181C28 → #080A0F` at 97 % (light white → `#EEF1F6`); rim 0.009 S
    in `mix(heat(h), hot, 0.4 o)` at `60 + 40 h` %, glow σ `(0.015 + 0.015 h) S` (dark); idle: unlit token × 2.2.
11. **Readout, agent dots (inside the window), wave, rays, pill**: shared.

Motion: rotor speeds in rev/s, integrated as angles: A `0.03 + 0.45 h^1.3 + 0.35 o + k`; B `−(0.02 + 0.30 h^1.3 +
0.25 o + 0.6 k)`; C `0.05 + 1.1 h^1.3 + 0.7 o + 1.2 k`. The pulse kick `k += 0.25 × strength` decays with τ 0.5 s, so
every completed response visibly spins the reactor up. At the redline rotor C turns ≈ 1.2 rev/s (≈ 7° per frame at
60 fps, smooth); at full overdrive ≈ 1.9 rev/s.

- **New-peak accent — lock**: each rotor swings forward (in its direction of travel) to the nearest pose with an arc
  head at 12 o'clock (symmetry 120° / 15° / 180°) over 0.42 s (`easeOutBack`, overshoot 1.2), holds to 0.57 s while all
  three flash gold `#FACC15` (intensity `1 − |τ/0.42 − 1| / 0.35`), then spins on from the locked pose.
- **Tier flash**: corona and arc heads.
- **Idle**: rotors stopped where they were, arcs at minimum length in slate at 38 % (light 45 %), corona off, core rim
  unlit. Sign of life: a light band (+60 %, Gaussian, ≈ 1/7 of the circle wide) runs once around rotor B in 0.9 s.

| Effects | `reactor` |
| --- | --- |
| Off | rotors still (arc lengths and color still encode `h`), steady corona, no bands, flares, kicks or lightning |
| Soft | rotation, energy bands, corona breathing, pulse flares and kicks, lock; no lightning or sparks |
| Standard | Soft + lightning and sparks |
| Max | Standard with bolts and sparks × 1.6 (4 alive), glows × 1.3, corona × 1.2 |

Reduce Motion: rotors frozen, bands static, corona steady, no lightning; the lock becomes a 0.3 s gold crossfade.
Increase Contrast: unlit tracks × 2, rotor tails at 60 %, glows × 0.5. Windows high contrast: rings in the theme's
text color, heads in its highlight color, no corona, glow or lightning. LOD: S < 120 has 12 coils and 4 notches;
thumbnails draw rotors A and C and the core only.

### 2a.5 `minimal` — Minimal ring

Only the number, one thin progress ring and the title; almost nothing moves. The calmest style, and the one that stays
clear at small sizes (it alone may go down to 64 pt).

Layers, back to front:

1. **Body**: section 2 without the lens highlight. No aura, agent dots, unit, wave or rays.
2. **Track**: a full circle r 0.425 S, width `w = max(2.5 pt, 0.034 S)`, unlit token × 1.15.
3. **Progress**: from 12 o'clock clockwise over `360° × p` with `p = h + o` (0 → 2): the first lap fills to the
   redline, a **second lap** runs over the first beyond it (complete at `v = 2R`), the way an activity ring passes 100 %.
   Round caps; a conic gradient from the label color darkened 28 % (light 10 %) at the start to the label color at the
   head; the second lap is the head color throughout and its head casts a shadow onto the lap below (black 55 % / 22 %,
   σ `0.35 w`, from 0.02 rad behind to 0.07 rad ahead of the head). Dark, from boost up: glow σ `0.0125 S` at 35 %.
4. **Head dot** (redline and pulses): radius `0.32 w` at the head, `mix(head, #FFF7D6, 0.6)` (light: head).
5. **Readout**: frame table; the value never rolls, titles crossfade in 0.25 s instead of sliding.

Motion: the ring follows the smoothed `h` and `o`. The only continuous animation: in the redline tier the glow
breathes at 1 Hz (σ `0.0125 → 0.025 S`, opacity 35 → 70 %) and the head dot shows at `0.6 ×` the breath. Pulse accent:
the head dot and glow blink (+30 %, 0.4 s); no wave. New peak: the ring tints toward gold (`#FACC15` / light `#C99700`)
by `sin(π τ / 0.7)` over 0.7 s, plus the pill. Idle: the track only; no sign of life, so an idle `minimal` orb draws
no frames at all.

| Effects | `minimal` |
| --- | --- |
| Off | no glow, breathing or blink |
| Soft, Standard | as described |
| Max | Standard with the glow × 1.3 |

Reduce Motion: steady glow (no breathing), the gold tint and blink become 0.2 s crossfades. Increase Contrast: track
× 2, ring width × 1.25, no glow. Windows high contrast: track in the theme's gray text color, ring in its highlight
color. Sizes 64–240 pt; at S < 80 the title uses 7 pt and the ring keeps `w ≥ 2.5 pt`.

### Frame policy and performance budget (normative for every style, `led` included)

The orb runs all day and there may be eight of them; a single Windows orb at the redline costs ≈ 38 % of one core today
on the 144 Hz 4K reference machine. Every style must stay far below that.

**Frame classes.** All orbs on a display share **one** frame clock (macOS: one `CADisplayLink` per screen; Windows:
one `CompositionTarget.Rendering` subscription or one DXGI/DComp frame callback), and each orb renders only on its own
k-th tick. "60 fps" means the integer divisor of the refresh rate closest to 60 (ties to the higher: 144 Hz → 72,
120 → 60, 60 → 60); "30 fps" likewise (144 → 28.8, 120 → 30); "15 fps" likewise. The display refresh rate itself is
never used above 72.

| State | `led` | `needle` | `liquid` | `reactor` | `minimal` |
| --- | --- | --- | --- | --- | --- |
| Idle, not hovered | static; 30 fps during the 0.9 s sweep every 6 s | static; 30 fps sweep | static; 30 fps for the 1.2 s bubble | static; 30 fps sweep | static, never animates |
| Cruise, boost (`h < 0.45`) | 30 fps | 30 fps; 60 while the needle turns > 90°/s | 30 fps | 30 fps | 30 fps while a spring settles, else static |
| Overdrive, redline | 60 fps | 60 fps | 60 fps (Soft: 30) | 60 fps | as above; 15 fps for the redline breathing |
| Transitions (title, peak, pulse, hover, style change) | 60 fps for their duration | 60 | 60 | 60 | 30 |
| Off or Reduce Motion | on change only: a value tick (≤ 2/s) renders the spring at 30 fps until it settles (change < 0.001), then stops | same | same | same | same |

- "Static" means no frames: the last frame stays on screen; the next one is drawn on data change, hover or the next
  scheduled sign of life (a single timer, not polling).
- When **four or more orbs** are in the 60 fps class at once, all of them drop to 30 fps except the hovered or most
  recently interacted one.
- Rendering pauses entirely while an orb is hidden, faded below 5 % opacity mid-fade, fully occluded (macOS
  `occlusionState`; Windows `DwmGetWindowAttribute(DWMWA_CLOAKED)` and monitor-off / session-lock notifications), or on
  a Space that is not visible. Data keeps flowing; the first frame after resuming shows the current state, never a
  catch-up animation.

**Budgets** (release build, Standard effects, S = 148 and S = 240, one orb unless stated; CPU = main + render threads,
% of one core averaged over 10 s; reference machines: an M1/M2 MacBook Air at 2× and the Windows 144 Hz 4K machine at
its native scale):

| Scenario | macOS | Windows |
| --- | --- | --- |
| Idle, any style | ≤ 0.2 % | ≤ 0.3 % |
| Cruise (30 fps) | ≤ 1.5 % | ≤ 3 % |
| Redline / overdrive, Standard | ≤ 3 % | ≤ 6 % |
| Redline / overdrive, Max | ≤ 4 % | ≤ 8 % |
| Eight orbs at redline, Standard | ≤ 12 % | ≤ 25 % |
| GPU time per orb frame (S = 240, 2× / 200 %) | ≤ 1.0 ms | ≤ 1.0 ms |
| Memory per orb (surfaces, buffers, atlases) | ≤ 12 MB | ≤ 12 MB |

Targets are part of the definition of done: each platform ships a benchmark harness that drives every style through
the scenario of the prototype (idle → climb → redline → overdrive → back) and reports these numbers.

**Techniques (required).**

- GPU only: every layer is a primitive of one instanced signed-distance-field pipeline (the macOS `OrbShaderSource`
  shapes — rounded rect, circle, ring, capsule, radial, arc — plus `polyline` for bolts and the liquid surface strip and
  `wedge` for the needle wake). Glows are the analytic Gaussian of the distance, never a blur pass; the liquid's
  surface, body, currents and spherical shade are one fragment shader evaluating `y(x)` per pixel; text comes from a
  glyph atlas (digits, `.kMB–`, the unit and the current titles per language, built once per size).
- No per-frame allocation: instance buffers and particle pools are sized once from the style's maximum budget (ring
  buffers), paths and geometry are rebuilt only on a size or appearance change; WPF: frozen resources only.
- CPU per frame ≤ 0.25 ms for state updates (springs, particles) at the maximum budget.
- **Windows**: the current path — a WPF layered window (`AllowsTransparency`) whose `DrawingVisual`s are rebuilt every
  frame and read back by `UpdateLayeredWindow` at the display rate — is the likely source of the 38 % (to be
  confirmed with a profile on the reference machine). The animated orb draws with
  Direct3D 11 into a composition swap chain (`CreateSwapChainForComposition`, premultiplied alpha) hosted by
  DirectComposition in a `WS_EX_NOREDIRECTIONBITMAP` window, running an HLSL port of the same instanced SDF shader as
  macOS (one shared instance layout). Click-through outside the disc and the click-through mode keep today's behaviour
  (WS_EX_TRANSPARENT toggled by cursor position); verify that it works with the composition window on Windows 10 and 11.
  WPF effects (`BlurEffect`, `DropShadowEffect`) are not allowed on animated content. If the WPF path is kept, it must
  meet the same budget, measured on the reference machine.

## 2b. Multiple orbs

The user can float up to **eight** orbs, each tracking any combination of sources — for example one orb for Claude
Code + Claude Desktop and one for Codex — with its own position, size, style and window options. An orb's value,
heat, title, tiers, agent dots, pulses, new peaks and hover line count only its own sources. The dashboard, the menu bar
/ taskbar widget and their accessibility always show all sources.

### Data model

Persisted as one JSON value in the settings store (key `orbs`), written atomically on every change:

```json
{
  "version": 1,
  "orbs": [
    {
      "id": "6F1C2A9E-…",
      "name": null,
      "sources": ["claudeCode", "claudeDesktop"],
      "style": "needle",
      "size": 148,
      "opacity": 1.0,
      "alwaysOnTop": true,
      "clickThrough": false,
      "fadeWhenIdle": false,
      "allSpaces": true,
      "visible": true,
      "redlineOutput": null,
      "redlineTotal": null
    }
  ],
  "layouts": {
    "<arrangement key>": { "<orb id>": { "display": "<display key>", "xAnchor": "high", "x": 12, "yAnchor": "fraction", "y": 0.31 } }
  }
}
```

| Field | Rule |
| --- | --- |
| `id` | UUID, never reused. |
| `name` | Custom name, 1–32 characters, trimmed; `null` = automatic: `orbs.sources.all` for all sources, the source name for one, `orbs.name.pair` for two, `orbs.name.more` (first source + count) for three or more, in source order (METRICS 1). |
| `sources` | `"all"` (every enabled source, including sources detected or added later) or a non-empty array of METRICS 1 keys, stored in source order without duplicates. Unknown keys from a newer version are kept and ignored. |
| `style` | `led` · `needle` · `liquid` · `reactor` · `minimal`; unknown → `led`. |
| `size` | 96–240 pt/DIP (`minimal` 64–240), clamped when the style changes (a 72 pt `minimal` becomes 96 pt `needle`). |
| `opacity` | 0.4–1.0. |
| `alwaysOnTop`, `clickThrough`, `fadeWhenIdle`, `allSpaces` | As the former global settings, now per orb (`allSpaces` macOS only). |
| `visible` | Hides one orb without removing it. |
| `redlineOutput`, `redlineTotal` | `null` = the General redline of that metric; otherwise this orb's redline (same ranges). Heat, tiers and titles of the orb use it. |
| `layouts` | Positions per **display arrangement**: the key is the sorted list of connected display keys (macOS display UUID; Windows monitor device ID) joined with `+`. Each entry uses today's position record (edge-docked distance or fraction of the usable area, per axis) on the named display. At most 16 arrangements are kept (least recently used dropped). |

Global (unchanged, shared by all orbs): Show floating orbs (master switch), Appearance, Language, metric, General
redline, Effects intensity, Celebrate new peaks, Response pulses. Effects stay global because the style already sets an
orb's character.

**Arrangement changes.** When displays are connected, removed or rearranged, each orb takes its record from the layout
of the new arrangement; if the arrangement is new, an orb keeps its record from the last arrangement when its display is
still connected, else moves to the main display keeping its anchors. Overlaps are then resolved in list order: a later
orb slides to the nearest free touching position (below). Nothing is ever placed off-screen.

**Migration** (first launch of the multi-orb version, when `orbs` is absent): create one orb — new id, `name null`,
`sources "all"`, `style "led"`, `size` = old `orbSize`, `opacity` = `orbOpacity`, the four toggles from the old keys,
`visible true` — and put the old per-display positions into the current arrangement's layout (every old display key is
kept as that orb's fallback record). The master switch takes the old `orbVisible`. The old keys are left untouched and
never written again, so a downgrade restores the single orb. After migration the orb looks and sits exactly as before
(test: same window frame and style).

**Defaults**: a fresh install has one orb tracking all sources, `led`, 148 pt, at today's default position.

**Limits**: 1–8 orbs. Add, Duplicate and Split are disabled at 8 (tooltip `orbs.limit`); a split that would exceed 8
creates the first sources in order until 8 and says so in its undo banner (`orbs.didSplitLimited`). The last orb cannot
be removed (Remove is disabled; the master switch hides it).

### Creating, placing and snapping

- **Add**: copies the style, size, opacity and toggles of the selected orb (Settings) or the clicked orb (menu). Its
  sources: the first detected, enabled source (source order) that no orb tracks *explicitly*; if every detected source
  is tracked explicitly, `"all"`. From the menu the new orb shows its chip for 2 s; from Settings it is selected and its
  editor opens.
- **Duplicate**: an exact copy with a new id; a custom name gets ` 2` appended.
- **Placement** of a new orb: on the display of the orb it came from (else the main display), try the four touching
  slots of that orb — left, below, right, above (12 pt gap, centers aligned) — and take the first one fully inside the
  usable area that overlaps no orb (disc + 12 pt). Otherwise search a 24 pt grid outward from that orb for the nearest
  free spot; if the display is full, cascade +24 / +24 pt from it, clamped inside the usable area. It springs in from
  0.6 × scale and 0 opacity (response 0.45 s, damping 0.78; Reduce Motion: 0.2 s fade).
- **Screen edges**: today's rule — released within 24 pt of a usable edge, the disc snaps to a 12 pt margin.
- **Orb to orb**: while dragging, each other orb on the display offers a touching position at a 12 pt gap along the
  line between the centers; within 16 pt of it the dragged disc is pulled in (the disc follows the pointer through a
  critically damped spring, settle 0.12 s, so the pull feels magnetic, not sticky). Release springs into place
  (response 0.35 s, damping 0.8). Released overlapping another orb, the disc slides to the nearest free touching
  position.
- **Alignment**: when the dragged center is within 8 pt of another orb's center x or y and the two discs are within
  64 pt of each other (edge to edge), it aligns; a 1 pt dashed guide in the accent color at 60 % shows while dragging.
  Priority: screen edge, then orb-to-orb touching, then alignment (alignment alone never pulls a disc away from an edge
  it is docked to).
- **Z-order**: all orbs use the window level of their `alwaysOnTop`; among orbs the last dragged or clicked is in front.
  Because snapping prevents overlap, the order rarely shows. Orbs never take keyboard focus (non-activating, as today).
- **Click-through** and **fade when idle** are per orb. Fade uses the orb's own idle state (its sources idle for 60 s)
  and multiplies its opacity.
- **Master switch**: hides or shows all orbs. Turning it on while every orb is individually hidden makes them all
  visible. The quick menu (menu bar / tray) keeps one item, `menu.showOrb` / `menu.hideOrb` with one orb and
  `menu.showOrbs` / `menu.hideOrbs` with several; with several orbs it adds a `settings.orb` submenu listing each orb
  (colour dots + name) with Show, Click-through and `menu.orb.settings` — the way back to a click-through orb.

### Identity at a glance

- **Source bezel**: an orb tracking a subset replaces the disc's white rim highlight with a colour rim of width
  `max(1 pt, 0.0105 S)` (1 px in thumbnails) on the disc edge, split into equal arcs clockwise from 12 o'clock in source
  order with 4° gaps, in the source colors at 85 % (light: darkened 10 %, 90 %). An unavailable source's arc is dashed
  (1.2 w on, 1.4 w off) in the unlit token × 2.5. Orbs tracking all sources keep the plain rim. Together with the
  agent dots, which are already in source colors, this identifies an orb without any label.
- **Source chip**: a capsule shown after 0.35 s of hover, during a drag, and for 2 s after the orb is created, split,
  merged or its sources change. Height 18 pt, 11 pt semibold, radius 9, padding 8, 6 pt colour dots; dark
  `rgba(20, 22, 30, 0.82)`, light `rgba(255, 255, 255, 0.9)`, hairline border and a soft shadow; drawn in a separate
  non-activating overlay so it may extend past the orb window. Placed centered 6 pt below the disc; when another orb or
  the screen edge is in the way, on the side facing the screen center, vertically centered; always clamped inside the
  usable area. Content: dot + name for up to two sources, else the first and `+n`; unavailable sources in secondary
  color with `source.notFound` / `source.disabled`. An all-sources orb shows `orbs.sources.all`, and only when more than
  one orb exists (a single orb behaves exactly as today).
- **Hover info line** inside the orb: `today.summary` over the orb's sources (today's output of those sources, the
  number of its agents).
- **Settings highlight**: hovering, selecting or focusing an orb row in Settings rings that orb on the desktop (2 pt
  accent ring 4 pt outside the disc, 60 %, fading in over 0.15 s), so the user always knows which orb is which.

### Unavailable sources

A source is unavailable when it is disabled (Settings → Data Sources) or not detected (no folder and no data today).
An orb never deletes or rewrites its sources because of that.

- Some unavailable: the orb works with the rest; their bezel arcs are dashed and the chip greys them.
- All unavailable: the orb dims to 55 % of its opacity, shows the value `–` (en dash) in the idle value color and the
  title `source.disabled` if every source is disabled, else `source.notFound`; the instrument rests, there is no sign of
  life and no hover info line (the chip explains: `Gemini CLI · 未偵測到`). Accessibility: `a11y.orb.unavailable`.
  When a source comes back, the orb wakes through the normal start-up.
- An all-sources orb is never unavailable.

### Context menu (right-click on an orb)

Native menu (macOS `NSMenu` with leading check marks; Windows Fluent context menu), submenus open away from the screen
edge. Windows (this menu and the tray quick menu): every label starts in one text column whether or not the item has an
icon (icons sit in a fixed leading column), and toggle items show their check mark trailing, right-aligned in the same
column as submenu chevrons, so checked and unchecked items never shift the text:

1. Header (disabled): the orb's colour dots and name.
2. `orbs.style` ▸ the five style names, check on the current one.
3. `orbs.sources` ▸ `orbs.sources.all`, separator, every source in source order with its colour dot and a check when
   tracked; unavailable ones show their status in secondary text and stay selectable. Unchecking the last tracked source
   is disabled (`orbs.sources.lastOne`). Checking `orbs.sources.all` replaces the set; checking a source on an
   all-sources orb makes the set that single source.
4. `menu.size` ▸ `menu.size.small` / `.medium` / `.large` (112 / 148 / 196; `minimal` sizes below 96 are set in
   Settings).
5. `menu.effects` ▸ Off / Soft / Standard / Max (global).
6. Separator; `menu.alwaysOnTop`, `menu.clickThrough` (this orb).
7. Separator; `orbs.add`; `menu.orb.duplicate`; `menu.orb.split` (enabled when the orb tracks two or more available
   sources, or all sources with two or more detected); `menu.orb.mergeAll` (enabled with two or more orbs).
8. Separator; `menu.orb.hide` (with one visible orb this item is `menu.hideOrb`, the master switch);
   `menu.orb.remove` (two or more orbs; asks `orbs.remove.confirm` / `orbs.remove.detail` with a destructive
   `orbs.remove.button` and Cancel).
9. Separator; `menu.orb.settings` (opens Settings → 懸浮窗 with this orb selected; macOS shows ⌘,); `menu.dashboard`;
   `menu.quit` (⌘Q).

### Split and merge

- **Split this orb** (menu): one orb per available source of its set (all detected, enabled sources for an
  all-sources orb), in source order. The first keeps the orb's id and position; the others are new orbs with the same
  style, size, opacity and toggles and automatic names. They form a tidy row extending toward the screen interior from
  the original position — a column when the original is docked to a left or right edge — with 12 pt gaps and aligned
  centers, wrapping into a second row or column when the display runs out; orbs in the way are pushed along the same
  axis.
- **Split by source** (Settings): replaces all orbs by one orb per detected, enabled source, styled like the selected
  orb, laid out from the first orb's position by the same rule. Disabled with `orbs.split.unavailable` when fewer than
  two sources are detected.
- **Merge** (Settings `orbs.merge`, menu `menu.orb.mergeAll`): replaces all orbs by one all-sources orb that keeps the
  selected (Settings) or clicked (menu) orb's id, style, size, toggles and position.
- **Motion**: split — the original scales to 0.85 and fades (0.2 s) while the children spring out of its center to
  their slots (stagger 60 ms, response 0.45 s, damping 0.78), their bezels draw on over 0.3 s and their chips appear
  0.28 s after landing for 2 s. Merge is the reverse: the orbs fly to the target (0.3 s) and fade, the merged orb
  springs from 0.85 to 1. Reduce Motion: 0.2 s crossfades in place.
- **Undo**: split, merge and remove can be undone for 8 s and restore the previous list and layout exactly. In Settings
  an inline banner above the orb list (`orbs.didSplit` / `orbs.didMerge` / `orbs.didRemove` · `orbs.undo`); on the
  desktop an undo chip under the first resulting orb, stacked under its source chip. Removing with the Settings − button
  needs no confirmation because of the undo; the menu asks (it has no banner).

### Accessibility and keyboard

- Each orb window is one accessibility element. Label: `a11y.orb.named` (`Claude Code + Claude Desktop：每秒 128 個
  token，高鐵`); with a single all-sources orb, today's `a11y.orb`. Unavailable: `a11y.orb.unavailable`. Hint /
  help: `a11y.orb.sources` (`追蹤 Claude Code、Claude Desktop`; names joined with `list.separator`). Updated at most
  once per second.
- Actions (VoiceOver custom actions; UI Automation Invoke and the context menu key): `menu.dashboard`,
  `a11y.orb.showMenu`, `a11y.orb.moveCorner` (cycles top-right → bottom-right → bottom-left → top-left of its display,
  placed as a drag release, skipping occupied corners to the next free touching slot), `menu.orb.settings`.
- Keyboard: orbs never take focus; everything is reachable from Settings (below) and the quick menu's orb submenu.

### Engine

Per orb, from the per-source data the engine already keeps (METRICS 6.8): live value = the sum of the per-source live
rates of its sources; its own tier and title trackers (section 1 hysteresis and hold) with its redline; its agents and
`responseCompleted` events filtered by source; its own `peakToday` and `newPeak`. Editing an orb's sources recomputes
its peak and suppresses its celebration for 60 s, so changing settings never fires confetti.

## 3. Menu bar (macOS) and taskbar (Windows) widget

Glanceable and calm; it must never jitter in width.

- Layout: `[gauge glyph] [value] [unit]`.
- Gauge glyph: 270° ring, 1.75 pt stroke (scale with DPI), diameter = text cap height × 1.2 (≈ 13 pt in the menu
  bar, ≈ 14 px at 100 % on the taskbar), optically centered on the digits,
  track at 25 % of the label color, fill fraction `h` in the heat color with a 6 px soft glow from overdrive upward.
  At redline the fill flickers gently (opacity 85–100 %, 6 Hz). Monochrome option uses the label color only.
- Value: system font, 13 pt (menu bar) / 15 px semibold (taskbar), tabular digits, placed tight against the glyph
  (4–5 pt gap, never more). The item hugs its content: its width grows immediately when the formatted value gets
  longer and shrinks only after the shorter width has held for 6 s, so with tabular digits it changes only when the
  digit count changes and never flickers. Idle value uses the secondary label color.
- Unit `tok/s`: 10 pt / 11 px, secondary color, optional (setting).
- Display modes (setting): glyph + value (default), value only, glyph only.
- Click: open/close the dashboard. Right-click (Windows) / ⌥-click (macOS): quick menu.
- The widget follows the system/taskbar light or dark theme for text; the glyph keeps heat colors.

## 4. Dashboard

macOS: popover-style panel under the menu bar item. Windows: flyout above the taskbar with Mica. Width 368 pt/DIP.
Follows system light/dark. Corner radius: platform default (macOS 12–16 pt, Windows 8 px). 20 pt padding.

Sections (top to bottom, 16 pt spacing, hairline separators):

1. **Hero** — a 112 pt orb (same component, ring effects capped at Soft) on the left, its center carrying the hero
   vehicle of the current speed title (section 4c); on the right the value at 44 pt bold tabular with `tok/s`, the
   speed title in heat color, and one line of context: `生成中 3 · 工具 1`.
2. **Live chart** — last 2 minutes (segmented control 1m / 2m / 5m), height 96. Smooth area chart (Catmull-Rom),
   fill = vertical gradient mapped through the heat palette by value, line 1.5 px. The in-flight estimated part is
   drawn with 45 % opacity and a dashed top line at the right edge. A dashed horizontal line marks today's peak with a
   small label. Hover/drag shows a crosshair with time and value.
3. **Sources** — one row per detected source: colored dot, name, live tok/s (right), today's output tokens
   (secondary); a live value below 0.05 shows the tertiary `–` like the agents list. Each row is a button that
   opens the Usage History window (section 4b) pre-filtered to that source:
   on hover the row gets a subtle highlight and a trailing chevron fades in; keyboard focusable; a11y hint "顯示用量歷史". Sources whose folder does not exist and that have no data today are not listed here (Settings → Data
   Sources still lists every source with its status); a source that is detected but disabled is dimmed.
4. **Agents** — up to 6 rows sorted by section 6.7 of METRICS: project (semibold), source + model (secondary,
   truncated), phase badge (generating: animated three-dot shimmer in the source color; tools: wrench glyph;
   idle: grey dot), live tok/s right-aligned. Sub-agents are indented 12 pt with a thin connector line.
   A live value below 0.05 (it would display as `0` or `0.0`) is never shown as a number: `tools` shows a small hourglass glyph (SF
   Symbol `hourglass` / Segoe Fluent `HourGlass`, 11 pt, secondary color, slow 2 s opacity breathing 55–100 %, static
   with Reduce Motion); `idle` and `generating` show an en dash `–` in tertiary color. Glyph and dash sit in the same
   right-aligned trailing slot as the digits (same baseline, no width change), crossfade 0.2 s to and from numbers,
   and read as the phase name for accessibility (`phase.tools`, `phase.idle`).
   Empty state: `目前沒有 agent 在工作` with a muted gauge glyph.
   Collapsed (default) shows the first 6 rows of the depth-first order (so a child never appears without its parent); when more agents exist, a disclosure row `agents.expand` ("+4 個 agent",
   with a small chevron, accessibility label `agents.expand.a11y`) expands the list in place with a spring height
   animation; expanded shows every agent and ends with `agents.collapse`. Beyond 12 rows the expanded list scrolls
   inside a fixed-height region (12 rows tall) so the panel never outgrows the screen. The hidden count counts agents,
   not groups. The expanded/collapsed
   choice is remembered.
5. **Today** — 2×2 metric grid: 輸出, 輸入 (含快取), 請求, 平均速度; plus 今日峰值 with time. The section header
   carries a small "歷史" text button (clock glyph) that opens the Usage History window for all sources.
6. **Footer** — floating orb toggle, metric info button (ⓘ explaining the measurement in one paragraph), settings,
   quit.

Metric explanation text (localized):
> 速度＝輸出 token ÷ 實際生成時間（從送出請求到完成，含等待第一個字）。多個 agent 會加總。進行中的回應先以該 agent
> 近期的實測速度估算，回應完成後立即以實測值校正。資料只來自本機紀錄。

## 4b. Usage History window

A native, resizable window (default 920 × 680, minimum 720 × 540, frame remembered; macOS: SwiftUI in an `NSWindow`
with a unified toolbar and the system window material; Windows: Fluent window with Mica). Title `history.title`.
Opened from a source row (pre-selected source), the Today header's 歷史 button (all sources), the quick menu
(`history.open`), and Settings → Data Sources. Only one instance; reopening focuses it and applies the new filter.

Layout, top to bottom, 24 pt outer padding, 20 pt section spacing:

1. **Toolbar** — leading: source picker (`history.allSources` plus every source with its color dot; sources without
   any history are hidden). Center: range segmented control `今天 · 昨天 · 7 天 · 30 天 · 90 天 · 今年 · 全部 · 自訂…`
   (`history.range.today`, `history.range.yesterday`). A single-day range (今天, 昨天, or a custom range of one day)
   compares with the day before (`history.summary.vsPreviousDay`), shows card sparklines over the 7 days ending on that
   day with the selected day's point highlighted, replaces the daily chart with a breakdown of that day
   (`history.chart.bySource` with 全部來源: one horizontal bar per source in its colour, sorted by output, with output,
   share and average speed; with one source, `history.chart.byModel`: the same per model), and today's figures update
   live (marked 進行中). The toolbar narrows the labels before it ever wraps. 自訂 opens a
   compact popover with two date pickers (`從` / `到`, clamped to the available data, the end never before the start)
   and `套用`. The resolved range is always spelled out under the title in the locale's date format (e.g.
   "2026/9/22 – 10/21 · 30 天"). Trailing: `匯出 CSV…` (the rows currently shown, locale-independent columns).
2. **Summary** — four cards in a row (two rows when narrow): 輸出 token, 輸入 token（含快取）, 請求, 平均速度 (Σ measured
   output ÷ Σ measured time over the range, never an average of daily averages). Each card: large tabular value,
   a secondary line (活躍 N 天 / 單日最高 date / 比前一段期間 +12%, colored green/red only for the delta), and a tiny
   sparkline of the range. Values use the compact formatter (METRICS 7) with full numbers in the tooltip.
3. **Chart** (height ≈ 260) — one bar per day of output tokens; with 全部來源 the bars stack by source in source
   colors (largest at the bottom), otherwise a single bar in the source color with a soft vertical gradient. An
   average-speed line on a secondary axis (right) in the label color with small dots on days with measurements,
   breaks (no line) on days without measurements. Today's bar is drawn hatched/lighter and labelled 進行中. Weekend
   bars are not special-cased; month boundaries get a faint vertical rule and a month label. Hover (or keyboard
   arrows) shows a crosshair and a tooltip card: date with weekday, output, input (incl. cache), requests, average
   speed, peak (if recorded; all-sources view only, METRICS 8) and the top 3 models with share bars. Clicking a bar scrolls the table to that day and
   highlights it for 1.2 s. Transitions between ranges/sources animate bar heights with a spring (≤ 0.45 s);
   Reduce Motion → crossfade.
4. **Daily table** — newest first; columns: 日期 (date + weekday; 今天 / 昨天 for the latest two), 請求, 輸出, 輸入（含快取）,
   快取命中 (cachedInput ÷ input, percentage), 平均速度 (tok/s, `history.noSpeed` when none), 峰值 (if recorded;
   all-sources view only),
   Agent, 主要模型 (up to 2 model names, rest as "+n"). Sortable headers, tabular digits, right-aligned numbers,
   alternating subtle row tint, hover highlight linked with the chart, keyboard navigation, copy row as text.
   Days without data inside the range are not listed (the chart shows the gap).
5. **Footer** (secondary text) — left: analysis state: `history.analyzing` with a thin progress bar and
   `history.analyzingFiles` while the background analysis runs (the window is usable and updates live as days come
   in), otherwise `history.updated`; right: `history.privacy`.

Empty state: centered muted gauge glyph, `history.empty` and `history.empty.hint`. First launch after upgrading:
the window opens instantly with whatever is cached and fills in as the analysis progresses — never a blocking
spinner. All numbers and dates follow the selected language; weeks start per locale.

## 4c. Hero vehicle

The empty center of the dashboard hero orb (section 4.1) carries a glowing line drawing of the current speed title's
vehicle, moving at a pace set by the live value. It belongs to the instrument rather than sitting on it: one stroke
weight, the heat colors and the same glow as the LED gauge and the comet. The floating orb keeps its readout.

Source of truth: `spec/assets/vehicles.json` (art, rigs, emitters and the per-vehicle motion constants).
`python3 spec/tools/vehicles.py` validates it; `--export` regenerates `spec/assets/vehicles/*.svg` (generated, never
edited) and `--preview DIR` renders PNG previews in both appearances. Both apps embed the JSON (copied at build time,
like `strings.json`) and draw only from it.

### Art format

- Canvas 100 × 100 units, y down, every vehicle faces right (+x). The canvas maps to a square of side `0.72 S`
  centered on the orb (`S` = 112 pt, so 1 unit = 0.806 pt).
- `safeCircle` (center 50, 50, r 42): every part, every rig pose and the full sweep of every spinning part stay inside
  it, stroke included. Parts flagged `bleed` (train bodies, flames, the shock cone) may run to the canvas edge and
  dissolve in the mask; the leading half of a bleeding body (x > 45) still stays inside.
- Every vehicle carries a similar visual weight and is centered for its own shape: wide, low vehicles (cars, aircraft)
  span almost the full width of the safe circle, tall ones (walker, riders, rocket) more of its height.
- `stroke.width` 2.0 units (1.6 pt), round caps and joins. A part's `role` (table `roles`) sets its weight multiplier,
  opacity, glow share, whether it is `sprung` (moves with the body) and its paint: `color: "hot"` uses the accent;
  only `flame` (heat fill at 30 %) and `flameCore` (white-hot fill) fill.
- Geometry: `d` holds absolute `M L C Q Z` commands, tokens separated by single spaces, numbers with at most two
  decimals (WPF `Geometry.Parse` reads it directly; macOS builds a `CGPath` with a small tokenizer), or `circle` =
  `[cx, cy, r]`. No arcs, no transforms.
- Optional part fields: `spin {pivot, radius, symmetry}` (rotates with the ground, below), `nozzle {at, dir}` (flames
  scale about it), `sprung` (overrides the role), `bleed`.
- `rig`: `walk` (the walker) or `pedal` (bicycle and e-bike riders); limbs are generated, see Continuous motion.
- `emitters`: `{id, type, at, dir}` plus type fields — `trail` (`lanes`, `length`), `dust`, `exhaust`, `arc`
  (`count` = puffs or arcs per start-up burst), `sparks`, `contrail` (`weak`), `afterburner`, `plume`. `dir` is a unit
  vector (0, 0 for radial bursts).
- `idle`: `{vehicle: "last", fallback: "walk"}` (see Idle).
- `groundY` (road, rail and guideway vehicles): the height of the vehicle's road line, rail or guideway top; tires
  touch it and the scenery is drawn from it. Each vehicle has its own (60.1–79), because each one is centered for its
  own shape.
- `motion`: `speed [lo, hi]` world speed range in units/s, `rest` (ground distance of the static pose), and
  `bob` / `float` / `hover` amplitudes in units. `heading` (rocket only): travel direction in degrees, screen space,
  0 = right, −48 = up-right.
- `scenery`: shared backdrops `road`, `rail`, `guideway`, `sky`, `stratosphere`, `space`; each vehicle names one.
- Draw order: far rig limbs, then `parts` in array order, then near limbs and the head.

### Placement, mask and layer order

- Drawn after the body (layer 2) and before the gauge (layer 3), in place of the readout. The mask ends at 0.353 S
  (49 units; the validator keeps it ≤ 0.3585 S), inside the gauge's inner edge (0.3875 S) and above the agent dots
  (0.3785 S), so drawing the layer as an overlay above the whole orb looks identical and is allowed.
- A soft circular mask covers the whole layer (scenery, speed lines, particles and glow included): opacity 1 up to
  r = 42 units (`mask.solid`, equal to the safe circle), falling linearly to 0 at r = 49 (`mask.clear`). Speed lines
  and train bodies dissolve at the rim; nothing ever reaches the gauge arc or the agent dots.
- Per frame: (1) draw scenery, speed lines, vehicles and particles into a layer of `ceil(0.72 S × scale)` px;
  (2) draw the vehicle strokes again into a glow source, each part at its role's `glow` share; (3) blur the glow source
  and composite it under the crisp layer; (4) apply the mask; (5) composite onto the orb.

### Color and glow

| | Dark | Light |
| --- | --- | --- |
| Stroke `C` | heat color of the smoothed heat `h` | title label color: heat darkened 18 % (OKLab L), then stepped to ≥ 4.5:1 on the popover (`heatText`) |
| Accent `H` (`light` and `glow` roles, flame outline) | `mix(C, #FFF7D6, 0.65 + 0.35 o)` | `mix(C, #FFB020, 0.45)` |
| Flame core | `#FFF7D6`, toward white by `o` | `#FFB020` |
| Glow blur σ | `1.0 + 1.4 h + 0.6 o` units (× 1.2 at Max) | same |
| Glow opacity | `0.30 + 0.45 h + 0.15 o`, additive | `0.14 + 0.18 h`, normal blending |

Mixes are in OKLab. Increase Contrast: stroke weights × 1.3, `detail` / `window` / `rim` / `spoke` opacity at least
0.9, glow × 0.5, `C` at 7:1 (light) / 4.5:1 (dark). Where a GPU blur is unavailable, approximate the glow with two
extra strokes under each part: width `w + 1.2 σ` at 0.5 × the glow opacity and `w + 2.6 σ` at 0.2 ×.

### Speed model

Everything that moves derives from one world distance `D`, so wheels, cranks, feet, road dashes and sleepers stay
locked together: no sliding feet, no slipping tires.

- Smoothed value: `v_s = R · h_s^(1/0.6)` while `h_s < 1`, else `R (1 + o_s)` (`h_s`, `o_s`: the section 1 springs).
- Band progress inside the displayed title `k` (thresholds `T_k` from section 1; walk = 0.5 tok/s):
  `b = clamp(ln(v_s / T_k) / ln(T_(k+1) / T_k), 0, 1)`; rocket: `b = o_s`.
- Coasting: `c = clamp(v_s / T_k, 0, 1)`. The title holds through hysteresis, but the vehicle slows when the agents
  stop.
- Ground speed target `G* = 0` when parked, else `lerp(lo, hi, b) · √c` with `speed [lo, hi]` from the JSON. Each
  range starts at or above the previous title's top (walk 24–41 … rocket 838–1008 units/s), so an upgrade never slows
  the world down.
- `G` follows `G*` with a critically damped spring, settle 0.9 s, evaluated exactly per frame: `ω = 6.2 / settle`,
  `c1 = G − G*`, `c2 = a + ω c1`, then `G ← G* + (c1 + c2 Δt) e^(−ω Δt)` and `a ← (c2 − ω (c1 + c2 Δt)) e^(−ω Δt)`
  (`a = dG/dt`, used below). The same form, settle 0.5 s, drives `wake`. `D += G Δt`; keep `D` in double precision
  and reduce angles modulo 2π before handing them to the GPU. Off and Reduce Motion freeze `D` at `motion.rest`.

### Continuous motion

- **Spin** — angle `D / spin.radius` rad, clockwise. Wheels use their tire radius (rolling without slip); cranks use
  tire radius × gear (bicycle 1.15; e-bike 1.9, so its rider pedals lazily while the motor works).
- **Spoke blur** — spoke frequency `f = G / (2π radius) × symmetry`, `blur = smoothstep((f − 14) / 12)`. Spokes fade by
  `1 − blur` while two motion arcs appear (radii `0.72 (radius − 2)` and `0.45 (radius − 2)`, 120° long, on opposite
  sides, turning clockwise at 0.6 rev/s, opacity `0.5 blur`, spoke weight). Bicycle spokes stay crisp, car spokes
  start to blur, sports and race car wheels read as spinning discs.
- **Walker rig** (all lengths and amplitudes from `rig`) — gait phase `p = (D / cycle) mod 1`; near leg `p`, far leg
  `p + 0.5`. Stance (`p < stance`): the foot rests at `groundY − 0.6` and slides from `+a` to `−a` (`a = cycle ·
  stance / 2`) at exactly ground speed. Swing (`s = (p − stance) / (1 − stance)`): `x = −a + 2a smoothstep(s)`, lift
  `lift · sin(π s)`. Hip bob `−bob · cos(4π (p − stance / 2))`. Knees by two-bone IK (`thigh`, `shin`), the knee on
  the +x side of the hip–foot line (`cross(foot − hip, knee − hip) < 0`, y down); a `foot`-long toe stroke points
  forward from the ankle. Shoulder = hip + `torso`, head circle = shoulder + `head` (dx, dy, r). Arms (`upperArm`,
  `forearm`) swing `−armSwing° · cos(2π p)` from vertical, forearms bent a further `elbow`°. Far limbs at 42 %
  opacity. Parked: legs and arms blend to a standing pose (feet ±1.6 under the hip, arms ±4°, elbows halved, hip at
  `groundY − 0.6 − 0.97 (thigh + shin)`), time constant 0.18 s once `G < 6`.
- **Pedal rig** — crank angle `φ = D / crank.radius`; near pedal `crank.pivot + crank.length (−sin φ, cos φ)`, far
  pedal opposite. Hip bob `bob · cos 2φ`, applied to the torso, arm and head (`rider` parts) too. Legs (`thigh`,
  `shin`) reach the pedals by the same IK; pedals are `pedal`-long horizontal strokes that stay level.
- **Suspension** (road vehicles, sprung parts only):
  `y = bob (0.6 sin(2π 1.7 (1 + 0.5 b) t) + 0.4 sin(2π 3.3 (1 + 0.5 b) t + 1.3)) (0.4 + 0.6 b)`. Acceleration pitch
  `clamp(−0.0035 a, −1°, 1°)` about the rear tire's ground contact, and a forward lurch of the whole vehicle
  `clamp(0.008 a, −1.2, 1.2)` units.
- **Aircraft** — float `y = float · sin(2π 0.32 t)`, pitch `0.8° (float / 0.7) cos(2π 0.32 t)` about the vehicle center.
  Rocket: `float · sin(2π 0.55 t)` across its heading, no pitch.
- **Maglev** — hover lift `hover · (0.75 + 0.25 sin(2π 0.7 t))` units. The hover-gap glow line stays centered in the
  gap, opacity × `(0.5 + 0.5 b)`, and a 14-unit bright pulse travels along it from nose to tail at `0.5 G`.
- **Flames** (afterburner, scramjet, rocket plume) — scaled about `nozzle.at`: along `nozzle.dir` by
  `(0.55 + 0.45 b + 0.35 o) · flicker`, `flicker = 1 + 0.1 · mean(sin 2π17t, sin(2π23t + 1.1), sin(2π31t + 2.3))`;
  across by `(1 + 0.06 sin 2π13t) · min(1, 0.6 + 0.4 length)`. Standard and Max add three shock diamonds when
  `b > 0.35`: at 3.0 / 5.8 / 8.6 units × the flame length behind the nozzle, half-height 1.0 / 0.82 / 0.64
  (width 0.9 ×), weight 0.35, opacity `0.75 (b − 0.35) / 0.65`, shimmering ±25 % at 19 Hz.
- **Shock cone** (hypersonic) — scaled vertically about the nose tip by `1.15 − 0.35 b` (the Mach cone narrows with
  speed), opacity × `(0.4 + 0.6 b)(0.7 + 0.3 sin 2π11t)`.
- **Contrails** — start 2 units behind each `contrail` emitter, 46 units long (`weak` wing-tip vapor: 22), sampled
  every 2 units at `y(d) = at.y + float(t − d / 90) + 0.35 sin((at.x − d + 0.05 D) / 7) · d / 40`, so the trail ripples
  behind the floating aircraft. Weight 0.8 (weak 0.45); opacity fades linearly from `0.55 (0.5 + 0.5 b)` (weak 0.26)
  to 0.
- **Trail marks** — the short speed marks behind each vehicle (`trail` emitters): lane `i` starts at
  `at.x − 1.5 − 2.5 (i mod 2)`, length `length (0.45 + 0.55 b + 0.25 h)(1 + 0.18 sin(2π (5.3 + 1.7 i) t + i))` ×
  `min(1, G / 20)`, opacity 0.55 fading to 0 along the mark, weight 0.5.
- **Scenery** (opacity 0.30 dark / 0.34 light, weight 0.55, scrolling with `D` × the scenery's parallax):
  - road: dashes 7 / gaps 5 on `groundY`. Anti-strobe smear `s = clamp(G / 60 / 12 / 0.45, 0, 1)` lengthens each dash by
    `0.85 s × gap` and multiplies the opacity by `1 − 0.35 s`: at race-car speed the markings blur into a line instead
    of appearing to run backwards.
  - rail: rail on `groundY` (opacity × 1.3); sleepers every 9 units (2.6 deep, leaning back 1.2), smeared with
    `s = clamp(G / 60 / 9 / 0.4, 0, 1)`: the lean grows by `4 s`, opacity × `1 − 0.55 s`.
  - guideway: beams on `groundY` and `groundY + 3.4`, pylons every 38 units hanging 9 units below.
  - sky: the two cloud outlines of the JSON on two layers, parallax 0.12 (near, below) and 0.05 (far, above),
    spacing 72.
  - stratosphere: the Earth's limb (circle center y 178, r 108, 207°–333°) with a soft blurred under-glow; fixed.
  - space: 14 stars streaking against the rocket's heading at `0.5 G` × depth (0.5–1.2), length
    `max(0.3, 0.5 G / 60 × 1.3)` × depth.
- **Speed lines** — pool of 16. Live count `N = round((2 + 8 h) · clamp(G / lo, 0, 1))`, capped at 5 (Soft, 0.6 N),
  9 (Standard) and 14 (Max, 1.4 N); a new line every `0.9 / N × (0.5…1.5)` s. Each line: length `(6 + 20 h) × (0.7…1.3)`
  plus `1.5 G / 60` (never shorter than one frame of travel), speed `1.25 max(G, 40) × (0.85…1.15)`, opacity
  `(0.16 + 0.32 h) × (0.7…1.3)` fading from head to tail, weight 0.5, 0.12 s fade-in, accent color at redline (dark).
  `y` is uniform in 16…84; a line whose `y` crosses the vehicle's bounds is born at the vehicle's tail (it peels off the
  body) instead of crossing the drawing. Rocket lines run against the heading across the whole disc.
- **Particle streams** (Standard and Max; pool of 64): rocket sparks `26 + 60 o` per s from the nozzle; race-car floor
  sparks `30 (b − 0.55) / 0.45` per s from the floor with gravity 260 units/s²; afterburner embers `6 + 14 b` per s
  from the overdrive tier up. Sparks are short streaks (velocity × 0.022 s) shading from the flame core to `C`, life
  0.25–0.45 s. Max: × 1.6.
- **Camera shake** (Standard and Max, redline tier, not parked): amplitude `A = 0.2 + 0.5 o` units (× 1.4 at Max);
  `x = A (0.5 sin 2π23t + 0.3 sin(2π31t + 1) + 0.2 sin(2π41t + 2))`,
  `y = A (0.5 sin(2π19t + 0.5) + 0.3 sin(2π29t + 2) + 0.2 sin(2π37t + 1))`. It moves the layer's content, never the orb
  or the mask.

### Title transitions

A transition starts on the same title-change event as the label slide (after the 0.8 s hold and the 90 % exit
hysteresis), so label and vehicle change together. Transitions last at most 0.75 s, less than the hold, so they never
queue; should one still be running, the incoming vehicle becomes the outgoing one from its current position. A jump
across several titles goes straight to the final vehicle. `τ` = time since the change; offsets run along the heading
(horizontal, up-right for the rocket).

| | Outgoing | Incoming | Extras |
| --- | --- | --- | --- |
| Upgrade | accelerates away: `+90 easeInCubic(τ / 0.34)`, opacity `1 − smoothstep((τ − 0.12) / 0.18)` | swoops in from `τ = 0.12`: spring −62 → 0, initial velocity +140 units/s, response 0.5 s, damping 0.8 (≈ 1 unit overshoot), opacity `smoothstep((τ − 0.12) / 0.14)` | while faster than 40 units/s, 3 motion ghosts trailing by speed × 0.012 / 0.026 / 0.042 s at 26 / 13 / 6 %; at `τ = 0.32` a flash on the incoming vehicle's center (radial gradient, radius 8 → 36 units easeOutCubic over 0.35 s, opacity 0.42 dark / 0.26 light × `(1 − u)²` × `min(1.6, 1 + 0.2 (titles climbed − 1))`, × 0.6 at Soft) and +60 % glow fading over 0.4 s; 4 burst speed lines |
| Downgrade | falls back: `−46 easeInOutSine(τ / 0.55)`, opacity `1 − smoothstep(τ / 0.45)` | glides in from +8: `8 (1 − easeOutCubic((τ − 0.1) / 0.55))`, opacity `smoothstep((τ − 0.1) / 0.35)` | none; `G` eases down through its spring |
| Reduce Motion | 0.3 s crossfade | 0.3 s crossfade | none |
| Off | cut | cut | none |

Scenery belongs to its vehicle and crossfades with it when it changes (road → rail → road → guideway → sky →
stratosphere → space). When both vehicles use the same scenery, it is drawn once and its ground line glides from the
outgoing vehicle's `groundY` to the incoming one's over 0.45 s (`0.5 − 0.5 cos(π u)`), so the road never jumps.

Motion keeps every drawing inside the soft fade: lurch, pitch, float and camera shake carry a tip at most ≈ 1.7 units
past the safe circle (a hard downgrade; mask still ≥ 75 % there), the motorcycle's start-up wheelie ≈ 2.2 units, and
the rocket's lift-off deliberately starts 6 units back inside the fade. Nothing comes near the clear radius (49) and
nothing is ever cut.

### Idle, start-up and parking

- **Idle** — the last vehicle stays parked, also across relaunches: the apps persist the last non-idle title key in
  their settings (`heroLastTitle`, a vehicle key such as `sportsCar`, written on every title change). First launch
  ever, or an unknown value: the walker, standing. Color `C(0)` (slate) at 55 % (dark) / 50 % (light) × breathing
  `0.86 + 0.14 sin(2π t / 4.8)`; no glow, speed lines, flames, contrails or hover; scenery still. Every 6 s, in step
  with the gauge's idle sweep, a light band crosses the vehicle's strokes: an 18-unit horizontal gradient (white-hot
  at 75 % dark / slate at 60 % light) travelling x −10 → 110 in 0.9 s, ease-in-out.
- **Parking** (title → idle) — `G* = 0` (the vehicle has already coasted down with `c`); `wake` → 0 (critically
  damped, settle 0.5 s) fades color, glow, flames, contrails and hover; the walker settles into the standing pose;
  riders keep their last crank position.
- **Start-up** (idle → title) — `wake` → 1 recolors slate → heat. Same vehicle: it starts where it stands. Another
  vehicle: the parked one fades out (0–0.2 s) and the new one fades in on the spot (0.12–0.40 s). The choreography
  starts 0.15 s (same) / 0.30 s (other) after the change; `G` rises from 0 through its spring, which gives every launch
  its natural acceleration. `bump(a, p, e)`: smoothstep from 0 at `a` to 1 at `p`, back to 0 at `e` (seconds).

| Vehicle | Start-up |
| --- | --- |
| walk | first step from standing; 3 dust puffs at the feet |
| bicycle | the rider stands on the pedals (hip 1.3 up and 0.8 forward, `bump(0, 0.18, 0.6)`), push-off lurch `1.6 bump(0, 0.22, 0.9)`, 6 dust puffs at the rear tire |
| e-bike | as the bicycle, plus power-on: battery LEDs and headlight flare `bump(0, 0.08, 0.5)`, 7 electric arcs around the hub motor (30 ms apart, 0.16–0.30 s each) |
| motorcycle | two exhaust pops (0 s, 0.18 s), 0.3 s engine shake (0.35 units), wheelie −3° `bump(0.12, 0.34, 0.8)` about the rear contact, lurch `1.4 bump(0.1, 0.35, 0.9)`, dust |
| car, sports car | double headlight blink (`bump(0, 0.04, 0.12)`, `bump(0.16, 0.2, 0.3)`), squat −1.3° `bump(0.05, 0.25, 0.7)`, lurch `1.2 bump(0.08, 0.3, 0.9)`; car: exhaust puff; sports car: an exhaust flame pop (0.22 s) |
| race car | launch: 5 tire-smoke puffs, squat −1.1° `bump(0, 0.2, 0.6)`, lurch `2.0 bump(0.05, 0.28, 0.9)` |
| bullet train | headlight on `bump(0, 0.15, 0.7)`, gentle pull-away lurch `1.0 bump(0.1, 0.45, 1.1)` |
| maglev | levitation: the hover gap ignites (glow +`bump(0, 0.3, 0.8)`), the body lifts by `smoothstep((τ − 0.2) / 0.5)` |
| jetliner | engines spool up: intake light `bump(0, 0.2, 0.8)`, contrails grow over 1.2 s, climb 2 units `bump(0.1, 0.5, 1.2)` |
| fighter jet, hypersonic | afterburner light-off: the flame springs 0 → 1 (initial velocity 9/s, response 0.32 s, damping 0.55, ≈ 25 % overshoot), nozzle flash (glow +`bump(0, 0.06, 0.35)`), shock cone draws on over 0.4 s, lurch `1.4 bump(0, 0.2, 0.8)` |
| rocket | ignition: white-hot flash, the plume springs to full (initial velocity 8/s, response 0.36 s, damping 0.5), 6 exhaust rings, 0.6 s shake (0.8 units), lift-off from 6 units back along the heading (easeInCubic, 0.9 s) |

Puffs and rings are outline circles (weight 0.45 → 0.22) growing by 2.6–5 units while fading over 0.45–0.85 s; dust
drifts back with the road. Every start-up adds 3 burst speed lines. Soft keeps the start-ups but caps each burst at 6
particles.

### Effects intensity

| Level | Vehicle layer |
| --- | --- |
| Off | Static glyph in its rest pose, static scenery, 1–3 static speed marks by `b` (`< 0.34`, `< 0.67`, else), fixed glow (0.30 dark / 0.12 light), cuts between vehicles |
| Soft | Every continuous motion (wheels, cranks, gait, suspension, scenery, trail marks, flames, contrails, hover, shock cone), ≤ 5 speed lines, transitions with ghosts and a 60 % flash, start-up bursts ≤ 6 particles; no particle streams, no shake |
| Standard | Soft plus particle streams, shock diamonds, ≤ 9 speed lines, camera shake at redline |
| Max | Standard with 1.6× particles, ≤ 14 speed lines, glow radius × 1.2, shake × 1.4 |

The vehicle layer follows the user's Effects setting (default Standard); the hero's ring layers (aura, comet, pulses)
stay capped at Soft as in section 4. Reduce Motion still caps the vehicle layer at Soft (below).

### Accessibility and performance

- Reduce Motion caps the layer at Soft and removes translation, rotation, scrolling, shake and particles: the vehicle
  sits in its rest pose, and speed reads from 1–3 static speed marks (by `b`), flame length (by `b`, no flicker) and
  the glow. Titles crossfade (0.3 s); a start-up is a crossfade plus the recolor; idle is static (no breathing, no
  sweep).
- Increase Contrast: see Color and glow. Windows high-contrast themes: no glow, scenery or speed lines; strokes in the
  theme's highlight color, flames as outlines.
- Decorative: hidden from VoiceOver and UI Automation (the hero already announces value and title), never
  hit-testable, no announcements of its own.
- Runs only while the dashboard is visible (popover or flyout open and not occluded). While moving it renders at the
  display rate; parked, at 10 fps for the breathing and 30 fps during the 0.9 s sweep; Off and Reduce Motion redraw
  only on change.
- Budget: ≤ 2 ms CPU per frame at 60 Hz on Apple Silicon (and on the Windows reference machine), blur on the GPU. No
  allocations per frame: parse the JSON once, build every part path once (`CGPath` / frozen `StreamGeometry`), keep
  fixed pools (64 particles, 16 speed lines) and reuse mutable paths for rig limbs and contrails (WPF: update
  `PathFigure` points in place).


### Resolved details (normative; found while implementing)

1. Glow is composited **over** the strokes, not under: additive in dark mode, normal blending in light mode. Glow share
   values above 1 (2, 1.6) clamp to 1.
2. Afterburner and rocket ignition overshoot come from the spring parameters given above (≈ 14.5 % fighter jet,
   ≈ 18.6 % rocket); the "about 25 %" wording is descriptive only.
3. Suspension and every other speed-dependent oscillation use an **accumulated phase**: `φ += 2π · f(b) · dt`, then
   `sin(φ)`; never `sin(2π · f(b) · t)`, which jumps when `b` changes.
4. The spoke motion arc rotates at its own 0.6 rev/s only; it does not add the wheel's rotation.
5. Leaving idle into the walker, the standing pose fades into the gait with a 0.18 s time constant (no pose jump).
6. During a transition, scene parts shared by both vehicles (road, track, sky) are drawn once at full strength, not at
   the max of the two vehicle opacities.
7. With Reduce Motion, contrails are frozen as well.
8. The lurch (forward surge) applies to every vehicle along its direction of travel (the rocket: up-right).
9. A title change during a running transition continues from the current positions, never restarting at 0.
10. The idle light band starts 0.63 s into the gauge's 6 s sweep cycle, so it crosses the vehicle's center while the
    gauge sweep passes the top.
11. Frame rate: the largest integer divisor of the display refresh rate that is ≥ 60 (144 Hz → 72, 120 Hz → 60,
    60 Hz → 60); parked/idle: ≈ 15 fps; static: no frames.
12. At Soft, start-up engine shake (motorcycle, rocket) stays; redline camera shake needs Standard or higher.
13. The spring `settle` time leaves ≈ 1.5 % of the step at `settle` (ω = 6.2 / settle); the behaviour is defined by ω.

## 5. Settings

Native settings window (macOS: SwiftUI `Settings`/grouped `Form`; Windows: Fluent WPF window with Mica). Sections:

- **一般 General** — Appearance (Auto / Light / Dark), Language (System / 繁體中文 / 简体中文 / English / 日本語),
  Launch at login, Speed metric
  (Output speed / Total throughput incl. input), Redline speed (slider 100–2000, step 50). Under the slider the heat
  strip carries one thin tick per speed title (positions `(t / R)^0.6`, so they stay put while the values move); below
  it `settings.redline.hint` and the `title.ladder` list: every title followed by its threshold at the current
  redline (`自行車 8 · 電動自行車 12 · … · 火箭 400`), title in secondary color, value semibold tabular in the heat color
  of that threshold, wrapping across lines. The values update live while the slider is dragged; hovering or focusing a
  title highlights its tick. With Total throughput the same list shows that metric's values (compact `k`/`M`).
- **選單列 / 工作列 Menu bar / Taskbar** — Display mode, Colored glyph, Show unit.
- **懸浮窗 Floating orbs** — the orb list and the per-orb editor (Floating orb pane, below).
- **特效 Effects** — Intensity (Off / Soft / Standard / Max), Celebrate new peaks, Response pulses,
  **Preview effects** button: runs the demo driver for 20 s sweeping through every tier.
- **資料來源 Data sources** — per source: toggle, detected path, status (`找到 128 個工作階段` / `未找到資料夾`),
  Change…, Reset. Below the list: `history.open` and `history.clear` (with a confirmation dialog `history.clear.confirm`).
  - omp: one row. The path is the default sessions root (METRICS 2); the status counts the sessions of the default
    root and every named-profile root together. Change… picks one `sessions` folder and replaces all automatic roots;
    Reset returns to them. Caption `settings.sources.ompHint`.
  - Antigravity: one row. The path lists the stores found (`~/.gemini/antigravity-cli`, `antigravity`,
    `antigravity-ide`); the status counts conversation databases (`source.sessions`). Change… picks one store folder
    (or its `conversations` folder) and replaces all three; Reset returns to them. A chosen `*-backup` store is never
    read (METRICS 4.8) and shows `source.folderMissing`. Caption `settings.sources.antigravityHint`.
- **關於 About** — version, GitHub link, license, privacy statement: read-only, local-only, no network, no telemetry,
  conversation content is never stored or displayed.

All settings apply live without restart and persist immediately.

### Floating orb pane (懸浮窗)

Calm and native: the platform's grouped form (macOS inset groups, Windows Fluent cards), content up to 680 pt wide,
13 pt text, 11 pt secondary captions, groups 10 pt radius, spacing from section 6, hairline separators, the system
accent color for selection, no extra borders. The pane scrolls when taller than the window. Top to bottom:

1. **Master group** — `settings.orb.show` switch; caption `orbs.allNote` (with `settings.bar` on macOS,
   `settings.taskbar` on Windows).
2. **Orb list** — header `settings.orb` with `orbs.count` (`3 / 8`) trailing. One row per orb, 52 pt tall:
   - a **live thumbnail**: the real renderer at 38 pt (value only, section 2a LOD), effects capped at Soft, ≤ 15 fps,
     only while the pane is visible;
   - the name (13 pt; custom or automatic) over `orbs.row.detail` (style name · size) in secondary;
   - trailing: the orb's live value (`128 tok/s`, tertiary, tabular; `–` when unavailable) and an eye toggle
     (`orbs.visible`); a hidden orb's row dims to 50 % and its value reads `orbs.hidden`.
   - Selection tints the row with the accent; hovering, selecting or focusing a row rings that orb on the desktop (2b).
     Rows reorder by drag (list order only). Keyboard: ↑ / ↓ select, Space toggles visibility, ⌘D / Ctrl+D duplicates,
     ⌫ / Delete removes, Tab moves into the editor.
   - List footer: borderless `+` (`orbs.add`), `−` (`orbs.remove`) and `orbs.duplicate` on the left; text buttons
     `orbs.split` and `orbs.merge` on the right, with `orbs.split.hint` / `orbs.merge.hint` as help tags; merge is
     disabled with one orb, split with fewer than two detected sources (`orbs.split.unavailable`). After split, merge or
     remove an inline banner above the list offers `orbs.undo` for 8 s.
3. **Editor of the selected orb**, headed by its colour dots and name (17 pt semibold):
   - **`orbs.sources`** — first row `orbs.sources.all` (checkbox, caption `orbs.sources.allHint`), then every source in
     source order: checkbox, colour dot, name, trailing status (`source.notFound` / `source.disabled`) in secondary.
     With all sources on, the rows show checked and disabled. The last checked source cannot be unchecked (help tag
     `orbs.sources.lastOne`).
   - **`orbs.style`** — five cards in one row (≈ 120 × 112 pt; 3 + 2 rows below 560 pt of width): an animated preview
     (the real renderer at 64 pt, effects Soft, looping cruise → redline → cruise every 8 s, each card 1.6 s later than
     the previous, ≤ 24 fps, only while visible; with Reduce Motion a still frame at the redline) above the style name
     (`style.*`); the selected card has a 2 pt accent ring and a faint accent fill. Arrow keys move between cards; each
     card's accessibility label is `orbs.preview.a11y`. Below the cards the selected style's name (semibold) and its
     description (`style.*.desc`).
   - **Size** — `menu.size.small` / `.medium` / `.large` presets (112 / 148 / 196) as a compact segmented control left of
     a slider (96–240, `minimal` 64–240, step 4) with the value in pt. **Opacity** — slider 40–100 % with the value.
   - Switches: `menu.alwaysOnTop`; `menu.clickThrough` with the platform hint (the orb is then controlled from the menu
     bar / taskbar orb submenu); `settings.orb.fadeIdle`; `settings.orb.allSpaces` (macOS). Footer: the platform's
     Reset Position for this orb (it returns to the default corner, or to the creation placement rule when another orb
     is there).
   - **`orbs.advanced`** disclosure (collapsed; its summary lists `orbs.name`, `settings.redline`): `orbs.name` text
     field (placeholder `orbs.name.auto` with the automatic name; empty = automatic); `settings.redline` as a
     segmented `orbs.redline.global` / `orbs.redline.custom`, with custom showing the General slider (same range, step
     and title ladder, for the current metric) and the caption `orbs.redline.hint`.
4. Every change applies live and persists immediately: the desktop orb crossfades to a new style (0.25 s), springs to
   a new size around its docked edges, and shows its source chip for 2 s after a sources change.

The thumbnails and previews together stay under 3 % of one core while the pane is visible and draw nothing when it is
not.

## 6. Typography & spacing

- macOS: system font (SF Pro; PingFang TC/SC and Hiragino Sans chosen automatically). Windows: `Segoe UI Variable`
  (fallback `Segoe UI`) with `Microsoft JhengHei UI` (zh-Hant) / `Microsoft YaHei UI` (zh-Hans) / `Yu Gothic UI` (ja).
- Numbers: always tabular digits.
- Spacing scale: 4, 8, 12, 16, 20, 24, 32.
- Animations: springs (response 0.45 s, damping 0.82) for layout; 0.2 s ease-out for color and opacity.

## 7. Accessibility

- Every interactive element is reachable by keyboard and has an accessible label.
- The orb exposes its value as an accessibility element (`每秒 128 個 token，加速`), updated at most once per second.
  With several orbs each one is named after its sources and offers custom actions (2b, Accessibility and keyboard).
- Blinking is limited to the needle's shift lights and the reactor's bolt flashes, both ≤ 3 flashes per second.
- Respect Reduce Motion, Reduce Transparency (solid body instead of blur), Increase Contrast (stronger strokes),
  and high-contrast themes on Windows.
- Text contrast ≥ 4.5:1 for all secondary text.

## 8. Localization

Languages: zh-Hant (primary), zh-Hans, en, ja. Follow the system language unless overridden in settings (section 0).
All shared strings live in `spec/strings.json`; each platform embeds that table (copied at build time or as a
resource) and keeps platform-only strings in its own table with all four languages. Never hard-code user-visible text.
