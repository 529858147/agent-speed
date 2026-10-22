<div align="center">

<img src="assets/icon/icon-1024.png" width="128" alt="Agent Speed icon">

# Agent Speed

**Feel your AI agents run.** A native racing instrument for the live token speed of Codex, Claude Code, Claude Desktop, DeepSeek Harness, OpenCode and Gemini CLI — calm when idle, on fire at the redline.

[English](README.md) · [繁體中文](README.zh-TW.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

[![Release](https://img.shields.io/github/v/release/zoefix/agent-speed?style=flat-square)](https://github.com/zoefix/agent-speed/releases/latest)
![macOS 14+](https://img.shields.io/badge/macOS-14%2B-black?style=flat-square&logo=apple)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?style=flat-square&logo=windows)
[![License: MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)

<img src="docs/images/hero.png" width="760" alt="Agent Speed floating orb and menu bar">

</div>

## Install

**macOS 14 or later** (Apple Silicon and Intel) — paste into Terminal:

```sh
curl -fsSL https://raw.githubusercontent.com/zoefix/agent-speed/main/install.sh | sh
```

**Windows 10 / 11** (x64 and ARM64) — paste into PowerShell:

```powershell
irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1 | iex
```

That's it. The installer downloads the latest release from GitHub, verifies its SHA-256 checksum, installs it and starts it. Run the same command again to update.

<details>
<summary>Uninstall</summary>

```sh
curl -fsSL https://raw.githubusercontent.com/zoefix/agent-speed/main/install.sh | sh -s -- --uninstall
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1))) -Uninstall
```

Add `--purge` / `-Purge` to remove settings as well. On Windows you can also uninstall from *Settings → Apps*.
</details>

## What you get

- **Floating orb** — a smoked-glass instrument with a 36-segment LED gauge. As speed climbs, so does its title — from *Walking* and *Bicycle* through *Bullet Train* and *Fighter Jet* to *Rocket* at the redline — while the gauge sweeps through the heat spectrum, a comet races around the ring, sparks fly, embers rise and every finished response sends out a shockwave. Beat your daily peak and it celebrates.
- **Menu bar / taskbar** — a compact gauge and number that never jitters, right where you look. On Windows it lives inside the taskbar, next to the clock.
- **Dashboard** — a glowing line drawing of your current speed title's vehicle moves inside its orb — a walker, a bicycle pushing off, all the way to a rocket at the redline — plus a live chart, per-source speed, every active agent (including sub-agents) with its phase, and today's totals. Click a source to open its history.
- **Usage history** — every day since your oldest log: output and input tokens, requests, average speed and peak, charted by source over any date range, with CSV export. Only daily totals are kept, so your history survives even after your tools delete old logs.
- **Native everywhere** — SwiftUI + AppKit on macOS, WPF with Fluent/Mica on Windows. Light, dark or automatic theme. English, 繁體中文, 简体中文, 日本語.
- **Humane** — effects intensity from *Off* to *Max*, respects Reduce Motion and high contrast, click-through mode, magnetic edges, remembers its place on every display, launches at login.
- **Always current** — checks GitHub Releases in the background and updates with one click, verified by SHA-256.

<p align="center"><img src="docs/images/hero-vehicle.gif" width="480" alt="The vehicle inside the dashboard orb, from bicycle to rocket"></p>

<p align="center"><img src="docs/images/dashboards.png" width="560" alt="Dashboard in dark and light"></p>

<p align="center"><img src="docs/images/history.png" width="760" alt="Usage history window"></p>

## How speed is measured

Agent Speed reads the transcripts your agents already write on your disk:

| Source | Folder |
| --- | --- |
| Codex (CLI, IDE, desktop) | `~/.codex/sessions` and `archived_sessions`, compressed logs included (or `$CODEX_HOME`) |
| Claude Code (CLI, IDE) | `~/.claude/projects` (or `$CLAUDE_CONFIG_DIR/projects`) |
| Claude Desktop (Code and agent sessions) | `~/.claude/projects`, told apart automatically |
| DeepSeek Harness (`dsh`, desktop and CLI) | `~/.dsh/sessions` (or `$DSH_HOME/sessions`) |
| OpenCode (CLI and desktop) | `~/.local/share/opencode/opencode.db` (or `$XDG_DATA_HOME/opencode`) |
| Gemini CLI | `~/.gemini/tmp` (or `$GEMINI_CLI_HOME/.gemini/tmp`) |

**Speed = output tokens ÷ the real time the model spent on them** — from the moment the request was sent until the response finished, including time to first token. Parallel agents add up. While a response is still streaming, it is estimated from that agent's recent measured speed and corrected with the measured value the instant it completes. Optionally you can switch to *total throughput* (input + output).

The full, testable specification lives in [`spec/METRICS.md`](spec/METRICS.md). Both apps are verified against the same fixtures and an independent [reference implementation](spec/tools/reference.py).

Ordinary Claude Desktop chat conversations are not stored on your computer, so they cannot be measured.

## Privacy

Read-only. Local-only. No telemetry, no accounts. The only network request is the update check against GitHub Releases (it can be turned off), which sends nothing about you. Agent Speed never stores or displays conversation content — only token counts, model names and project folder names. The usage history keeps only daily totals, in a small database on your computer (`~/Library/Application Support/AgentSpeed`, `%LOCALAPPDATA%\AgentSpeed`), and can be cleared in Settings. Updates are verified against the release's SHA-256 checksums before they are installed; the checksums are published with the release, so they catch corrupted or tampered downloads, not a compromised repository.

## Build from source

<details>
<summary>macOS (Xcode 26 or later)</summary>

```sh
cd macos
swift test
./scripts/build-app.sh          # → dist/AgentSpeed-macOS.zip
```
</details>

<details>
<summary>Windows (.NET 10 SDK; also cross-builds from macOS/Linux)</summary>

```sh
dotnet test windows/tests/AgentSpeed.Core.Tests
dotnet test windows/tests/AgentSpeed.Presentation.Tests
./windows/scripts/publish.sh    # → dist/AgentSpeed-Windows-{x64,arm64}.zip
```
</details>

## Project layout

```
spec/       measurement + design specification, shared fixtures, reference implementation
macos/      Swift package: AgentSpeedCore (engine) and AgentSpeed (SwiftUI app)
windows/    .NET solution: AgentSpeed.Core (engine), AgentSpeed.Presentation, AgentSpeed (WPF app)
assets/     icon artwork
install.sh  install.ps1
```

## Friends

- [LINUX DO community](https://linux.do/)

## License

[MIT](LICENSE) © 2026 zoefix
