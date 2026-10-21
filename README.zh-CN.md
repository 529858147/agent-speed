<div align="center">

<img src="assets/icon/icon-1024.png" width="128" alt="Agent Speed 图标">

# Agent Speed

**看见你的 AI agent 全速奔跑。** 为 Codex、Claude Code、Claude Desktop 与 DeepSeek Harness 打造的原生实时 token 速度仪表——空闲时安静，冲到红线时全面燃烧。

[English](README.md) · [繁體中文](README.zh-TW.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

<img src="docs/images/hero.png" width="760" alt="Agent Speed 悬浮窗与菜单栏">

</div>

## 安装

**macOS 14 及以上**（Apple Silicon 与 Intel）——粘贴到「终端」：

```sh
curl -fsSL https://raw.githubusercontent.com/zoefix/agent-speed/main/install.sh | sh
```

**Windows 10 / 11**（x64 与 ARM64）——粘贴到 PowerShell：

```powershell
irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1 | iex
```

就这样。安装程序会从 GitHub 下载最新版本、验证 SHA-256 校验码、安装并启动。之后重新运行同一行命令即可更新。

<details>
<summary>卸载</summary>

```sh
curl -fsSL https://raw.githubusercontent.com/zoefix/agent-speed/main/install.sh | sh -s -- --uninstall
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1))) -Uninstall
```

加上 `--purge` / `-Purge` 会同时删除设置。Windows 也可以在「设置 → 应用」中卸载。
</details>

## 功能

- **悬浮窗**——烟熏玻璃质感的仪表，36 格 LED 表盘。速度从「巡航 → 加速 → 狂飙 → 极限」一路攀升时，表盘沿着热度光谱点亮、彗星绕圈疾驰、火花四溅、余烬升腾，每完成一次回应就释放一道冲击波；打破今日纪录还会庆祝。
- **菜单栏／任务栏**——精简的仪表与数字，宽度固定不跳动。Windows 版直接嵌在任务栏里、时钟旁边。
- **面板**——实时曲线、各来源速度、所有正在工作的 agent（含子 agent）及其状态、今日统计。
- **全原生**——macOS 使用 SwiftUI + AppKit，Windows 使用 WPF 与 Fluent／Mica。浅色、深色、自动主题；繁體中文、简体中文、English、日本語。
- **人性化**——特效强度从「关闭」到「热血全开」，遵循「减弱动态效果」与高对比度设置，支持鼠标穿透、边缘磁吸、记住每块屏幕上的位置、登录时自动启动。
- **永远最新**——后台检查 GitHub Releases，有新版一键更新，并以 SHA-256 验证。

<p align="center"><img src="docs/images/dashboards.png" width="560" alt="深色与浅色面板"></p>

## 速度怎么算

Agent Speed 只读取 agent 本来就写在你电脑上的记录：

| 来源 | 文件夹 |
| --- | --- |
| Codex（CLI、IDE、桌面版） | `~/.codex/sessions`（或 `$CODEX_HOME/sessions`） |
| Claude Code（CLI、IDE） | `~/.claude/projects`（或 `$CLAUDE_CONFIG_DIR/projects`） |
| Claude Desktop（Code 与 agent 会话） | `~/.claude/projects`，自动区分 |
| DeepSeek Harness（`dsh`，桌面版与命令行） | `~/.dsh/sessions`（或 `$DSH_HOME/sessions`） |

**速度＝输出 token ÷ 模型实际花在这些 token 上的时间**——从发出请求到回应完成，含等待第一个字。多个 agent 并行时会加总。回应仍在进行时，先以该 agent 近期的实测速度估算，完成的瞬间立即以实测值校正。也可以切换成「总吞吐量」（输入＋输出）。

完整且可测试的规格见 [`spec/METRICS.md`](spec/METRICS.md)。两个平台的程序都以同一份测试数据，以及一份独立的[参考实现](spec/tools/reference.py)交叉验证。

Claude Desktop 普通聊天对话不会保存在本机，因此无法统计。

## 隐私

只读、只在本机。不收集数据、不需要账号；唯一的网络连接是向 GitHub Releases 检查更新（可关闭），不发送任何个人数据，安装更新前会先验证 SHA-256 校验码（校验码与发布版本放在一起，能拦下损坏或被篡改的下载，但无法防范仓库本身被入侵）。Agent Speed 不会保存或显示对话内容——只用到 token 数量、模型名称与项目文件夹名称。

## 从源码构建

<details>
<summary>macOS（Xcode 26 及以上）</summary>

```sh
cd macos
swift test
./scripts/build-app.sh          # → dist/AgentSpeed-macOS.zip
```
</details>

<details>
<summary>Windows（.NET 10 SDK；也能在 macOS／Linux 交叉构建）</summary>

```sh
dotnet test windows/tests/AgentSpeed.Core.Tests
dotnet test windows/tests/AgentSpeed.Presentation.Tests
./windows/scripts/publish.sh    # → dist/AgentSpeed-Windows-{x64,arm64}.zip
```
</details>

## 许可证

[MIT](LICENSE) © 2026 zoefix
