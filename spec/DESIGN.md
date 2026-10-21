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

`R` = redline speed of the selected metric. Output speed (default metric): setting default **400 tok/s**, range
100–2000, step 50. Total throughput (includes cached input, typically ~1000× larger): its own setting, default
**200k tok/s**, range 20k–2M. The redline slider edits the value of the current metric. `v` = live value of the
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
heat color. Thresholds scale with `R` (shown values are for the default 400 tok/s), so every metric and redline
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

## 2. The orb (floating window)

A circular smoked-glass instrument. Diameter `S` (setting 96–240 pt/DIP, default 148). The window is `1.5 S` square so
the aura and sparks can leave the disc; everything outside the disc is fully transparent and ignores clicks wherever
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
(`今日 1.2M · 3 個 agent`), and the cursor shows it is draggable.

Interaction:
- Drag anywhere on the disc to move. On release within 24 pt of a screen edge, snap to a 12 pt margin (spring).
  Position (center point, per display) persists.
- Double-click: open the dashboard.
- Right-click: context menu — Size (S / M / L presets 112 / 148 / 196), Effects (Off / Soft / Standard / Max),
  Always on top, Click-through, Hide floating orb, Settings…, Quit.
- Click-through mode: the orb ignores all mouse events; it is controlled from the menu bar / taskbar.

Effects intensity (setting, default Standard):

| Level | Behaviour |
| --- | --- |
| Off | Static colors and segments only; no aura breathing, comet, particles, pulses, jitter or skew |
| Soft | Aura, comet and pulses; no particles, no jitter |
| Standard | Everything above, sparks, embers, skew; jitter only at redline |
| Max | Standard with 1.6× particle rates, stronger bloom, and a faint heat-distortion shimmer at redline |

System "Reduce Motion" caps the level at Soft and replaces movement with crossfades.

Performance: when idle and not hovered, render at ≤ 10 fps (or only on change). Particles use a fixed-size pool
(≤ 400). The renderer pauses entirely while the orb is hidden or fully occluded.

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
   any history are hidden). Center: range segmented control `7 天 · 30 天 · 90 天 · 今年 · 全部 · 自訂…`; 自訂 opens a
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
- **懸浮窗 Floating orb** — Show, Size (slider + presets), Opacity (40–100 %), Always on top, Click-through,
  Fade when idle, Show on all Spaces (macOS).
- **特效 Effects** — Intensity (Off / Soft / Standard / Max), Celebrate new peaks, Response pulses,
  **Preview effects** button: runs the demo driver for 20 s sweeping through every tier.
- **資料來源 Data sources** — per source: toggle, detected path, status (`找到 128 個工作階段` / `未找到資料夾`),
  Change…, Reset. Below the list: `history.open` and `history.clear` (with a confirmation dialog `history.clear.confirm`).
- **關於 About** — version, GitHub link, license, privacy statement: read-only, local-only, no network, no telemetry,
  conversation content is never stored or displayed.

All settings apply live without restart and persist immediately.

## 6. Typography & spacing

- macOS: system font (SF Pro; PingFang TC/SC and Hiragino Sans chosen automatically). Windows: `Segoe UI Variable`
  (fallback `Segoe UI`) with `Microsoft JhengHei UI` (zh-Hant) / `Microsoft YaHei UI` (zh-Hans) / `Yu Gothic UI` (ja).
- Numbers: always tabular digits.
- Spacing scale: 4, 8, 12, 16, 20, 24, 32.
- Animations: springs (response 0.45 s, damping 0.82) for layout; 0.2 s ease-out for color and opacity.

## 7. Accessibility

- Every interactive element is reachable by keyboard and has an accessible label.
- The orb exposes its value as an accessibility element (`每秒 128 個 token，加速`), updated at most once per second.
- Respect Reduce Motion, Reduce Transparency (solid body instead of blur), Increase Contrast (stronger strokes),
  and high-contrast themes on Windows.
- Text contrast ≥ 4.5:1 for all secondary text.

## 8. Localization

Languages: zh-Hant (primary), zh-Hans, en, ja. Follow the system language unless overridden in settings (section 0).
All shared strings live in `spec/strings.json`; each platform embeds that table (copied at build time or as a
resource) and keeps platform-only strings in its own table with all four languages. Never hard-code user-visible text.
