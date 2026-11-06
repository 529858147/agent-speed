# Pi (Pi Coding Agent) — research notes

Read from the source of `earendil-works/pi` at `d5b3de22d0e24a3e9c38a9410d3329b099717283` (2026-10-25; npm
`@earendil-works/pi-coding-agent` 1.4.0, formerly `@mariozechner/pi-coding-agent` and the repository
`badlogic/pi-mono`) and from the published npm type declarations. Requested in GitHub issue #12. No real Pi session
file was read; the parser rules are derived from the writer's code and checked on synthetic fixtures
(`spec/tools/pi_fixtures.py`).

## Summary

- Pi is the upstream of omp (METRICS 4.7). The session format is the same JSONL family (session version 3), so the
  rules of 4.7 apply with the differences of METRICS 4.13: the root, `durationMs` instead of `duration`, `usage.reasoning`
  instead of `usage.reasoningTokens`, and four kinds of side usage that Pi's own totals count.
- Roots: `<agent dir>/sessions` (`$PI_CODING_AGENT_DIR`, else `~/.pi/agent`) with one folder per working directory
  (`--<cwd with / \ : replaced by ->--`) and one file per session (`<ISO timestamp with : and . replaced by
  ->_<uuid>.jsonl`); a custom session folder (`--session-dir`, `$PI_CODING_AGENT_SESSION_DIR`, `sessionDir` in
  `<agent dir>/settings.json`) holds the session files directly.
- Writes: nothing until the session has a user or assistant message, then the whole history at once (`wx`), then one
  `appendFileSync` per entry. A message is appended at its `message_end`: a user prompt before its request, an
  assistant message once, complete, after its stream, a tool result when the tool finished. No record exists while a
  request is in flight; partial output is never written.
- Timing: `message.timestamp` is the request start (Unix ms); `message.durationMs` (pi-ai 0.99+, monotonic) runs from
  it to the end of the response. Older messages have no `durationMs`; their end is the entry `timestamp`, a few
  milliseconds after the stream ended.
- Totals (`footer.ts`, `getSessionStats`): assistant `usage`, `usage` entries (side calls such as `cache_warm`),
  `toolResult.usage` (model usage of a tool's own execution) and the `usage` of `compaction` and `branch_summary`
  entries. Agent Speed counts the same four.
- Effort: assistant messages carry `thinkingLevel` (Pi's level: `off`, `minimal`, `low`, `medium`, `high`, `xhigh`,
  `max`) and `providerThinkingLevel` (pi-ai 0.99+); `thinking_level_change` entries record selections.
- No sub-agents in Pi itself (`README.md` 19); packages may add them. Forks (`/fork`, `/tree` branches,
  `forkFrom`) write a new file with a fresh header and copy the source entries with their `id` and `timestamp`.
- Process: Node.js 22.19+. The installers put a launcher at `<agent dir>/bin/pi` that `exec`s
  `<agent dir>/install/releases/<version>/node_modules/.bin/pi`; npm installs `@earendil-works/pi-coding-agent`
  globally. Either way the process is `node …/@earendil-works/pi-coding-agent/dist/bundle/cli.js`, which sets
  `process.title = "pi"`; release binaries built by `scripts/build-binaries.sh` are named `pi`.

## Evidence

| Fact | Where (repository paths at the commit above) |
| --- | --- |
| Agent dir `$PI_CODING_AGENT_DIR`, else `~/.pi/agent`; sessions `<agent dir>/sessions`; `PI_CODING_AGENT_SESSION_DIR` | `packages/coding-agent/src/config.ts` 581–650 (`ENV_AGENT_DIR`, `ENV_SESSION_DIR`, `getAgentDir`, `getSessionsDir`) |
| Bucket folder `--<cwd>--` | `packages/coding-agent/src/core/session-manager.ts` 589–593 |
| A custom session folder (`--session-dir`, `$PI_CODING_AGENT_SESSION_DIR`, `sessionDir` in settings.json) holds the files directly, no bucket folders | `packages/coding-agent/src/main.ts` 691–695; `core/settings-manager.ts` 690–693; `session-manager.ts` 1755–1756, 1932–1951 |
| File name `<timestamp>_<sessionId>.jsonl` | `session-manager.ts` 1079–1080, 1672–1673, 1844–1845 |
| Header `{type:"session", version, id, timestamp (ISO), cwd, parentSession?}`, version 3 | `session-manager.ts` 41–50 |
| Entries `{type, id (8 hex chars of a UUID), parentId, timestamp (ISO)}` | `session-manager.ts` 57–62, 277–284 |
| Entry types `message`, `thinking_level_change`, `model_change`, `usage`, `compaction`, `branch_summary`, `custom`, `custom_message`, `context_edit`, `label`, `session_info` | `session-manager.ts` 64–200 |
| `usage` entry `{kind, provider, model, usage, note?}`; kind `cache_warm` | `session-manager.ts` 79–89, 1244–1250; `core/cache-warmer.ts` 343 |
| `compaction.usage`, `branch_summary.usage` | `session-manager.ts` 91–122 |
| First write when a user or assistant message exists, then appends | `session-manager.ts` 1166–1188 |
| Messages appended at `message_end` | `core/agent-session.ts` 1147–1165 |
| Forks copy entries under a fresh header | `session-manager.ts` 1640–1700, 1836–1866 |
| `AssistantMessage`: `api`, `provider`, `model`, `responseModel?`, `responseId?`, `providerThinkingLevel?`, `thinkingLevel?`, `usage`, `stopReason`, `timestamp` (request start), `durationMs?` | `packages/ai/src/types.ts` 585–616 |
| `Usage`: `input` (non-cached), `output` (includes reasoning), `cacheRead`, `cacheWrite`, `cacheWrite1h?` (subset of `cacheWrite`), `reasoning?` (subset of `output`), `totalTokens`, `cost` | `types.ts` 459–487 |
| `StopReason`: `pending`, `stop`, `length`, `toolUse`, `error`, `aborted`, `deferred` | `types.ts` 489 |
| Tool calls are content blocks `{type:"toolCall", id, name, arguments}` | `types.ts` 444–448 |
| `toolResult` `{toolCallId, toolName, isError, timestamp, usage?, durationMs?}` | `types.ts` 640–656 |
| Pi's totals add assistant, `usage`, `toolResult.usage`, `compaction`/`branch_summary` usage | `modes/interactive/components/footer.ts` 124–138; `core/agent-session.ts` 4243–4275 |
| `durationMs` and `thinkingLevel` from pi-ai 0.99.0 (absent in 0.87.1); `usage` entries in coding-agent 1.0.0 | npm type declarations of `@earendil-works/pi-ai` and `@earendil-works/pi-coding-agent` |
| Thinking levels `off` … `max` | `packages/ai/src/models.ts` 1259 |
| No built-in sub-agents | `README.md` 19 |
| Installers, launcher, Node.js requirement | `README.md` 25–43; `https://pi.dev/install.sh` (launcher written by `write_managed_install_launcher`) |
| `process.title = "pi"` | `packages/coding-agent/src/cli/setup.ts` 5 |
