# Agent Speed — Measurement Specification

This document is the single source of truth for how Agent Speed reads agent transcripts and turns them into
tokens-per-second. The macOS (Swift) and Windows (C#) implementations MUST follow it exactly and MUST pass the
shared fixtures in `spec/fixtures/`.

## 1. Vocabulary

| Term | Meaning |
| --- | --- |
| Source | `codex`, `claudeCode`, `claudeDesktop`, `deepseek` (DeepSeek Harness, `dsh`) |
| Agent | One transcript file. Agent id = absolute file path. Sub-agents have their own files and are separate agents. |
| Response | One model API call that produced a usage record. |
| Phase | `generating` (a request is in flight), `tools` (model finished, waiting for tool results), `idle` |

## 2. Locations

| Source | Root | Override |
| --- | --- | --- |
| Codex | `$CODEX_HOME/sessions`, else `~/.codex/sessions` | user setting |
| Claude Code + Claude Desktop | `$CLAUDE_CONFIG_DIR/projects`, else `~/.claude/projects` | user setting |
| DeepSeek Harness (`dsh`, desktop app and CLI) | `$DSH_HOME/sessions`, else `~/.dsh/sessions` | user setting |

`~` is `HOME` on macOS and `%USERPROFILE%` on Windows. Claude Code and Claude Desktop share one root; each file is
assigned to a source by its `entrypoint` field (section 4.1). Ordinary Claude Desktop chat conversations are not stored
locally and are out of scope.

Discovery: recursive enumeration of `*.jsonl` (DeepSeek: `session.v<N>.jsonl.zstd` or `session.v<N>.jsonl`), maximum
depth 6, never follow symlinks, at most 20 000 files per root.
The app never writes inside these folders.

## 3. Common rules

### 3.1 Timestamps
RFC 3339 / ISO 8601 with optional fractional seconds and `Z` or offset. Convert to Unix milliseconds (Int64).
Lines without a parseable timestamp are ignored except where stated.

### 3.2 Normalized token usage
Every response carries:

| Field | Codex (`usage` / `last_token_usage`) | Claude (`message.usage`) |
| --- | --- | --- |
| `input` (total input) | `input_tokens` (already includes cached) | `input_tokens + cache_creation_input_tokens + cache_read_input_tokens` |
| `cachedInput` | `cached_input_tokens` | `cache_read_input_tokens` |
| `cacheWriteInput` | `cache_write_input_tokens` (absent → 0) | `cache_creation_input_tokens` |
| `output` (includes reasoning) | `output_tokens` | `output_tokens` |
| `reasoning` | `reasoning_output_tokens` | `output_tokens_details.thinking_tokens` (absent → 0) |

Missing numeric fields are 0. Negative values are clamped to 0. `total = input + output`.

### 3.3 Response timing
- `endMs` = timestamp of the usage record (Claude: the latest line of that message).
- `startMs` = when the request was sent, tracked per file as `pendingStartMs` (rules per source below). May be null.
- Valid interval: `startMs != null`, `endMs >= startMs`, `endMs - startMs <= 15 min`. Otherwise the response counts in
  totals but never in speed.
- `final`: whether the usage is the final usage. Codex responses are always final. A Claude response is final once any
  of its lines has a non-null `stop_reason`. Recent Claude Desktop versions often write only the streaming snapshot of
  sub-agent messages (output ≈ 8 tokens) and never the final usage; such responses are *unfinalized*.
- A response is *measured* when it is final and has a valid interval, and *unmeasured* when it is unfinalized and has a
  valid interval. Unmeasured responses never contribute measured speed; their time is estimated instead (6.3).
- Minimum duration: if `endMs - startMs < 500 ms`, use `startMs' = endMs - 500`.
- `speed = output / ((endMs - startMs') / 1000)` — this is end-to-end output speed and includes time to first token.

### 3.4 Deduplication
A global, bounded (65 536 entries, FIFO eviction) set of response keys shared by all files of all sources.
A key first seen in file A and later seen in file B is ignored in B (forks and resumes copy history). Claude: repeated
lines of the same key in the same file are merged (4.1, 4.4). Codex: every repeat of a key is skipped (4.2).

### 3.5 Copied history
For Codex, once `session_meta` has been read, any later line whose timestamp is earlier than `fileCreatedMs - 2000`
is copied history (forks and sub-agents copy their parent's records): ignore it completely (no totals, no speed, no
state change). For Claude, copied history is removed by deduplication (3.4).

## 4. Parsers

Each parser is a per-file state machine fed complete lines in order. It produces:
- response upserts (`key`, `source`, `agentId`, `model`, `startMs`, `endMs`, normalized tokens)
- phase updates (`phase`, `phaseSinceMs`, `inFlightStartMs`)
- metadata (`project` = last path component of `cwd`, `model`, `isSubagent`)
- `lastLineMs` = the latest timestamp seen in the file

Lines that are not valid JSON objects are skipped and counted as malformed. Lines longer than 32 MiB are skipped.

### 4.1 Claude (Claude Code and Claude Desktop)

Source: the first line that has `entrypoint` decides: a value starting with `claude-desktop` (`claude-desktop`,
`claude-desktop-3p` on Windows) → `claudeDesktop`, anything else →
`claudeCode`. Before that, the file is `claudeCode`.

`isSubagent` = path contains a `subagents` directory component, or any line has `"isSidechain": true`.
`project` = basename of the latest `cwd`.

State: `pendingStartMs` (nullable), `preciseStart` (bool), `phase`.

| Line | Action |
| --- | --- |
| `type == "api-request"` | `pendingStartMs = ts`, `preciseStart = true`, phase `generating` (`inFlightStartMs = ts`) |
| `type == "user"`, content contains a text block (or string content) starting with `[Request interrupted by user` | phase `idle`, `pendingStartMs = null`, `preciseStart = false` |
| other `type == "user"` | if not `preciseStart`: `pendingStartMs = ts`; phase `generating` (`inFlightStartMs = pendingStartMs`) |
| `type == "assistant"` | see below |
| anything else | only updates `lastLineMs` |

Assistant line:
1. `key = message.id`, else `requestId`, else `uuid`. Skip the line if `message.model == "<synthetic>"`.
2. If `key` was first seen in another file: ignore the line entirely.
3. If `key` is new: create the response with `startMs = pendingStartMs` (may be null), then
   `pendingStartMs = null`, `preciseStart = false`.
4. Merge: `endMs = max(endMs, ts)`, every token field = `max(previous, new)`, `model = message.model`.
5. `final = final || stop_reason != null`.
6. Phase from `message.stop_reason`: `"tool_use"` → `tools`; `null`/missing → `tools` when the line's
   `message.content` contains a block of type `tool_use`, otherwise `generating`; `"pause_turn"` → `generating`
   (`inFlightStartMs` = the response start, or `ts` if unknown); any other value → `idle`.

### 4.2 Codex

Every line is an envelope `{ "timestamp", "type", "payload" }`.

State: `fileCreatedMs`, `myAgentPath`, `pendingStartMs`, `streaming` (bool), `sawModernUsage` (bool),
`previousLegacyTotals` (nullable), `phase`.

| Line | Action |
| --- | --- |
| first `session_meta` of the file | `fileCreatedMs = ts`; `project` from `payload.cwd`; `myAgentPath = payload.agent_path ?? "/root"`; `isSubagent = payload.thread_source == "subagent"` or `payload.source.subagent` exists; `threadId = payload.id`. Later `session_meta` lines (sub-agents copy their parent's) are ignored entirely. |
| `turn_context` | `model = payload.model`; `project` from `payload.cwd` when present |
| input (see below) | if not `streaming`: `pendingStartMs = ts`; phase `generating` (`inFlightStartMs = pendingStartMs`) |
| mailbox input (see below) | if not `streaming`: `pendingStartMs = ts`; the phase does not change |
| output (see below) | `streaming = true`; phase `generating` (`inFlightStartMs = pendingStartMs ?? ts`) |
| `token_usage_record` | modern usage (below) |
| `event_msg` + `payload.type == "token_count"` | if `sawModernUsage` and `streaming`: the response ended without a usage record — `streaming = false`, `pendingStartMs = ts`, phase `tools`. Otherwise legacy usage (below). |
| `event_msg` + `payload.type` in `task_complete`, `turn_aborted` | phase `idle`, `pendingStartMs = null`, `streaming = false` |

Input lines:
- `event_msg` with `payload.type` in `task_started`, `user_message`
- `response_item` with `payload.type` ending in `_output`
- `response_item` with `payload.type == "message"` and `payload.role` in `user`, `developer`, `system`

Mailbox input (a message from another agent; an orchestrator may receive it while it is blocked, so it marks a
possible request start but is not proof that a request is in flight):
- `response_item` with `payload.type == "agent_message"` and `payload.recipient == myAgentPath`

Output lines:
- `response_item` with `payload.type == "reasoning"`, or ending in `_call`, or `message` with role `assistant`

Modern usage (`token_usage_record`):
1. Skip if copied history (3.5).
2. `sawModernUsage = true`. `key = payload.response_id`, else `"codex:" + threadId + ":" + ts`.
   (`threadId` = `payload.thread_id` ?? `session_meta.payload.id` ?? file path.) Skip if already seen anywhere.
3. Tokens from `payload.usage`. Ignore `turn_token_usage` and `thread_token_usage`.
4. Emit the response with `startMs = pendingStartMs`, `endMs = ts`.
5. `pendingStartMs = ts`, `streaming = false`, phase `tools`.

Legacy usage (`event_msg` / `token_count`):
1. Ignore when `sawModernUsage` is true, when `payload.info` is null, or when copied history (3.5).
2. `total = info.total_token_usage`, `last = info.last_token_usage`.
3. If `previousLegacyTotals` is null → tokens = `last` (fallback `total`).
   Else `delta = total - previous` per field. If `delta.total_tokens == 0` → duplicate event, ignore (do not change
   any state). If any delta field is negative → tokens = `last`. Otherwise tokens = `delta`.
4. `previousLegacyTotals = total`.
5. `key = "codex-legacy:" + threadId + ":" + total.total_tokens`. Skip if already seen.
6. Emit with `startMs = pendingStartMs`, `endMs = ts`. Then `pendingStartMs = ts`, `streaming = false`, phase `tools`.

### 4.3 DeepSeek Harness (dsh)

Files are a sequence of independent zstd frames (section 5.1); the decoded stream is JSONL. Every event line has
`type`, `seq` (number) and `time` (Unix milliseconds, a number). Wrappers are unwrapped first: a line
`{"type":"event","event":{...}}` is processed as its inner event; a line `{"type":"snapshot","header":{...},
"records":[...]}` is processed as its header followed by each record (itself possibly an `event` wrapper) in order.

State: `sessionId`, `createdMs`, `parentSession`, `model`, `pendingStartMs`, `phase`.

| Event | Action |
| --- | --- |
| first header (`type == "session"`, or a snapshot `header`) | `sessionId = id`; `createdMs = createdAt`; `project` from `cwd`; `isSubagent = origin == "subagent"` or `delegationDepth > 0` or `parentSession` is a non-empty string; `parentSession = parentSession`. Later headers are ignored. |
| any event whose `time` is earlier than `createdMs - 2000` | fork-inherited history: ignore completely |
| `request/header` | `model = data.header.config.model` when present |
| `step/start` | `pendingStartMs = time`; phase `generating` (`inFlightStartMs = time`) |
| `assistant/message` | response (below) |
| `turn/end` | phase `idle`, `pendingStartMs = null` |
| anything else | only updates `lastLineMs` |

`assistant/message`:
1. `key = data.message.id`, else `"dsh:" + sessionId + ":" + seq`. Skip the event entirely if the key was seen anywhere.
2. When `data.usage` is an object, emit a response with `startMs = pendingStartMs`, `endMs = time`, `final = true`,
   `model = data.message.source.model ?? model`, and tokens `input = inputTokens + cacheReadTokens + cacheWriteTokens`,
   `cachedInput = cacheReadTokens`, `cacheWriteInput = cacheWriteTokens`, `output = outputTokens`, `reasoning = 0`
   (dsh does not report reasoning separately; it is included in `outputTokens`).
3. `pendingStartMs = null`; phase `tools` when `data.message.content` has a block of type `tool-call`, else `idle`.

`lastLineMs` is the latest `time` seen (excluding ignored inherited events). Verified against dsh's own session
statistics: the sum of `step/start → assistant/message` intervals equals dsh's `llmMs`, and the token sums equal its
`tokenUsage` totals.

### 4.4 Response upserts
A response is identified by its key. The first emission is a *completion* (used for live events, 6.4). Later
emissions with the same key in the same file are *updates*: the engine replaces the stored response.

## 5. Reading files

### 5.1 zstd frame streams (DeepSeek Harness)
`.zstd` files are read frame by frame: from the current offset, determine the compressed size of the next frame
(zstd `findFrameCompressedSize`); if the frame is not complete yet, stop and wait for more bytes. Decode each complete
frame (at most 64 MiB of output per frame, else the frame counts as malformed and is skipped), feed the decoded bytes
to the line reader, and advance the offset past the frame. A frame that fails to decode is skipped and counted as one
malformed line. Skippable frames are skipped. When the bytes at the offset are not a frame at all (unknown magic or
an invalid block header), count one malformed line and resynchronize at the next zstd frame or skippable-frame magic
after the offset; if none is present yet, keep the offset and retry when the file grows. Files that start at their end (older files) start at the current size;
files over 256 MiB are not seeded at all (they start at their end with the `partialHistory` warning), because a frame
stream cannot be entered in the middle.

- Startup reading order (it decides which file owns a duplicated key): files sorted by modification time ascending,
  ties broken by ordinal path comparison. Each file is read to its end before the next one starts.
- Startup: every file whose modification time is within `todayStartMs - 15 min` or later is read from offset 0.
  Older files start at their current size (nothing is read). Files larger than 256 MiB are read from
  `size - 64 MiB`, aligned to the next newline, and the snapshot carries a `partialHistory` warning.
- While running: read appended bytes. Changes are detected with the OS file watcher (FSEvents on macOS,
  `FileSystemWatcher` on Windows) plus a 1-second fallback that stats files modified in the last 2 minutes, plus a full
  rediscovery every 10 seconds. Newly discovered files are read from offset 0.
- Incomplete trailing lines are buffered until their newline arrives. A file that starts being read at its end
  skips the bytes before the first newline it reads only when the byte just before its start offset is not a newline
  (so a complete line appended later is never dropped). The 256 MiB tail rule always skips to the next newline.
- If a file shrinks or its identity changes, reset its offset and its parser state (keep the global dedupe set).
- Reading never blocks the UI thread. Steady-state CPU target: < 1% on an idle machine.

Responses produced while reading the startup backlog are *seeded*: they count toward totals, peaks and agent speed
history but never produce live events.

## 6. Engine

All engine functions take an explicit `nowMs` so they are deterministic and testable. Production calls them every
500 ms and after each batch of file changes.

### 6.1 Retention
Keep responses with `endMs >= nowMs - 30 min` for speed computation (the fallbacks in 6.2 look back 30 minutes).
Today totals are aggregated incrementally.

### 6.2 Agent recent speed
For agent `a`: the up to 3 most recent measured responses ending within the last 10 minutes.
`recentSpeed(a) = Σ output / Σ duration_seconds` (and `recentTotalSpeed(a)` with `input + output`).
Fallbacks for estimation when the agent has none: the same formula over the up to 10 most recent measured
responses with the same model within 30 minutes (any agent), else with the same source within 30 minutes, else none.
Only measured responses are ever used as a speed reference.

### 6.3 Live rate
Window `W = 3000 ms`, interval `[nowMs - W, nowMs]`.

- Measured: for every measured response, `output * overlap(interval, [start', end]) / (end - start')`.
- Unmeasured: for every unmeasured response, `estimatedSpeed * overlap(interval, [start', end]) / 1000`, counted as
  estimated (`estimatedSpeed` as below, for that response's agent).
- Estimated (in-flight): for every agent in phase `generating` with `inFlightStartMs != null` and
  `nowMs - lastLineMs <= 300 000`, add `estimatedSpeed * overlap(interval, [estimateStart, nowMs]) / 1000`,
  where `estimateStart = max(inFlightStartMs, latest endMs of any response of that agent)` (so a response that is
  still being written, such as a streaming Claude message, is never counted twice) and `estimatedSpeed` is the
  agent's recent speed or its fallback (6.2). No estimate if none exists.
- `live.output = (measured + estimated) / (W / 1000)`. `live.total` uses `input + output` and total speeds.
- `liveEstimatedShare = estimated / (measured + estimated)` (0 when both are 0).

A completed response replaces its own in-flight estimate automatically, because a response ending moves the agent
out of `generating` or starts a new in-flight interval at the response end.

Per-source and per-agent live rates use the same formula restricted to that source or agent.

### 6.4 Events (live only, never during seeding)
- `responseCompleted` — the first emission of a key (4.4), carrying source, project, model, output, total, duration
  and speed.
- `newPeak` — when the measured+estimated live output exceeds the previous peak of today by at least 1 tok/s, is at
  least 20 tok/s, and no `newPeak` was emitted in the last 60 seconds.

### 6.5 History
One-second buckets for the last 300 seconds, oldest first. Bucket `k` covers `[k*1000, (k+1)*1000)`.
`measured` = tokens of measured responses distributed uniformly over their intervals (the bucket value is tok/s for
that second). `estimated` = unmeasured-response and in-flight estimates for the same buckets (up to `nowMs`). Both for
output and total.

### 6.6 Today
`todayStartMs` = local midnight, supplied by the caller. A response belongs to today when `endMs >= todayStartMs`.
- `today`: sums of `requests`, `input`, `cachedInput`, `cacheWriteInput`, `output`, `reasoning` of all responses as
  recorded (unfinalized ones included, so output is a lower bound when any exist); per source as well.
- `unfinalizedToday`: number of today's unfinalized responses; when > 0 the snapshot carries the warning
  `unfinalizedUsage(count)`.
- `averageToday = Σ output / Σ duration` over today's measured responses (null when none).
- `peakToday`: maximum over today of the 3-bucket trailing average of *measured* one-second buckets, combined with the
  maximum live value observed while running. Keep a per-second measured array for the current day.
- On day change everything for "today" restarts at zero.

### 6.7 Agents list

Parent links (`parentId`, an agent id or null):
- Claude: a file at `<dir>/<session>/subagents/<name>.jsonl` has the parent `<dir>/<session>.jsonl` when that file is
  a tracked agent.
- DeepSeek: the tracked dsh file whose first header `id` equals this file's `parentSession` (the earliest created
  one if several).
- Codex: from the first `session_meta`, `parentThreadId = payload.parent_thread_id ??
  payload.source.subagent.thread_spawn.parent_thread_id`. The parent is the tracked file whose first `session_meta`
  has `payload.id == parentThreadId` (the earliest created one if several). `forked_from_id` alone is not a parent.
- Self references and cycles resolve to null.

List:
1. Staleness first: a phase older than 300 seconds without a new line is reported as `idle`.
2. Base set: agents whose `lastLineMs` is within the last 10 minutes, or whose phase is `generating`.
3. Closure: add every ancestor (following `parentId`) of an agent in the set, even if it is inactive.
4. Tree: an agent's tree parent is its `parentId` when that agent is in the list; otherwise it is a root, and the
   reported `parentId` is null.
5. Order groups (roots with all their descendants) by: any member `generating` first; then the sum of the members'
   live output, descending; then the latest member activity, descending; then root id (ordinal). Inside a group, list
   depth-first with every agent immediately followed by its children; siblings ordered by `generating` first, live
   output descending, latest activity descending, id (ordinal).

UIs indent an agent by its depth in this tree and draw connectors from parent to children. To avoid jitter a UI may
keep its previous order between groups or between siblings whose live values are nearly equal, but it never splits a
group or shows a child without its parent.

## 7. Number formatting (both platforms)

| Value | Display |
| --- | --- |
| `< 10` | one decimal (`4.2`); exactly 0 → `0` |
| `< 1000` | integer (`128`) |
| `< 10 000` | `1.28k` (two decimals) |
| `< 1 000 000` | `12.8k` / `128k` |
| else | `1.28M` |

Token counts in summaries use the same compact form (`1.2M`, `384k`).
