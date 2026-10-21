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
6. **Readout** — centered.
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

1. **Hero** — a 112 pt orb (same component, effects capped at Soft) on the left; on the right the value at 44 pt bold
   tabular with `tok/s`, the speed title in heat color, and one line of context: `生成中 3 · 工具 1`.
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
   speed, peak (if recorded) and the top 3 models with share bars. Clicking a bar scrolls the table to that day and
   highlights it for 1.2 s. Transitions between ranges/sources animate bar heights with a spring (≤ 0.45 s);
   Reduce Motion → crossfade.
4. **Daily table** — newest first; columns: 日期 (date + weekday; 今天 / 昨天 for the latest two), 請求, 輸出, 輸入（含快取）,
   快取命中 (cachedInput ÷ input, percentage), 平均速度 (tok/s, `history.noSpeed` when none), 峰值 (if recorded),
   Agent, 主要模型 (up to 2 model names, rest as "+n"). Sortable headers, tabular digits, right-aligned numbers,
   alternating subtle row tint, hover highlight linked with the chart, keyboard navigation, copy row as text.
   Days without data inside the range are not listed (the chart shows the gap).
5. **Footer** (secondary text) — left: analysis state: `history.analyzing` with a thin progress bar and
   `history.analyzingFiles` while the background analysis runs (the window is usable and updates live as days come
   in), otherwise `history.updated`; right: `history.privacy`.

Empty state: centered muted gauge glyph, `history.empty` and `history.empty.hint`. First launch after upgrading:
the window opens instantly with whatever is cached and fills in as the analysis progresses — never a blocking
spinner. All numbers and dates follow the selected language; weeks start per locale.

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
