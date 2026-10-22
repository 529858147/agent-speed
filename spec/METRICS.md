# Agent Speed — Measurement Specification

This document is the single source of truth for how Agent Speed reads agent transcripts and turns them into
tokens-per-second. The macOS (Swift) and Windows (C#) implementations MUST follow it exactly and MUST pass the
shared fixtures in `spec/fixtures/`.

## 1. Vocabulary

| Term | Meaning |
| --- | --- |
| Source | `codex`, `claudeCode`, `claudeDesktop`, `deepseek` (DeepSeek Harness, `dsh`), `opencode`, `gemini` (Gemini CLI), `omp` (oh-my-pi), `antigravity` (Google Antigravity) |
| Agent | One transcript file. Agent id = absolute file path. Sub-agents have their own files and are separate agents. (OpenCode: one per session row, 4.5; Antigravity: one per conversation database, 4.8.) |
| Response | One model API call that produced a usage record. |
| Phase | `generating` (a request is in flight), `tools` (model finished, waiting for tool results), `idle` |

## 2. Locations

| Source | Root | Override |
| --- | --- | --- |
| Codex | `$CODEX_HOME/sessions`, else `~/.codex/sessions` | user setting |
| Claude Code + Claude Desktop | `$CLAUDE_CONFIG_DIR/projects`, else `~/.claude/projects` | user setting |
| DeepSeek Harness (`dsh`, desktop app and CLI) | `$DSH_HOME/sessions`, else `~/.dsh/sessions` | user setting |
| OpenCode (CLI and desktop app) | `$XDG_DATA_HOME/opencode/opencode.db`, else `~/.local/share/opencode/opencode.db` (same form on Windows under `%USERPROFILE%`) | user setting (folder that contains `opencode.db`) |
| Gemini CLI (interactive, `-p`, ACP/IDE sessions) | `<home>/.gemini/tmp`; on macOS also `<home>/.cache/.gemini/tmp` (seatbelt sandbox, v0.63+). `<home>` = `$GEMINI_CLI_HOME`, else `~` | user setting (one `tmp` folder; replaces both defaults) |
| omp (oh-my-pi: TUI, `-p`, RPC, ACP) | The default root plus one root per named profile (4.7). Default: `$PI_CODING_AGENT_DIR/sessions`, else on macOS `$XDG_DATA_HOME/omp/sessions` when `$XDG_DATA_HOME/omp` exists, else `~/<cfg>/agent/sessions`, where `<cfg>` = `$PI_CONFIG_DIR`, else `.omp`. Profiles: `~/<cfg>/profiles/<name>/agent/sessions` for every folder `<name>` | user setting (one `sessions` folder; replaces every automatic root) |
| Antigravity (CLI `agy`, desktop app, IDE, IDE extensions) | three store folders: `~/.gemini/antigravity-cli`, `~/.gemini/antigravity`, `~/.gemini/antigravity-ide` (each contains `conversations`) | user setting (one store folder, or its `conversations` folder; replaces all three) |

`~` is `HOME` on macOS and `%USERPROFILE%` on Windows. Claude Code and Claude Desktop share one root; each file is
assigned to a source by its `entrypoint` field (section 4.1). Ordinary Claude Desktop chat conversations are not stored
locally and are out of scope.

Discovery: recursive enumeration of `*.jsonl` (Codex also `*.jsonl.zst`, 5.3; DeepSeek: `session.v<N>.jsonl.zstd` or `session.v<N>.jsonl`; Gemini CLI:
fixed depths, see 4.6; omp: see 4.7; Antigravity: conversation databases, see 4.8), maximum depth 6, never follow
symlinks, at most 20 000 files per root.
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
- phase updates (`phase`, `phaseSinceMs`, `inFlightStartMs`; `inFlightStartMs` is null whenever the phase is not
  `generating`)
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

### 4.5 OpenCode

OpenCode (2.x) stores everything in one SQLite database in WAL mode. It is opened read-only (`mode=ro`, never
written), with a busy timeout of 2 s, and every read is a short transaction so OpenCode's checkpoints are never
blocked. When OpenCode is not running it closes the database cleanly and deletes `opencode.db-wal` and
`opencode.db-shm`; a `mode=ro` open then either fails (macOS) or creates those two files (Windows `winsqlite3`), which
the app must never do. So: when the database header says WAL (bytes 18 and 19 are both 2) and no `-wal` file exists,
open with `immutable=1` directly, which reads the database file alone and creates nothing; otherwise open `mode=ro`,
and if that fails while no `-wal` file exists, fall back to `immutable=1`. Drop an immutable connection on the next
change of `opencode.db` or as soon as a `-wal` file appears, and reopen with `mode=ro`. A database without the tables `session_v2` and `session_message` is reported as unsupported (warning
`unsupportedFormat`) and contributes nothing.

Agents: one per session row. Agent id `opencode:<session_v2.id>`; `project` = basename of `session_v2.directory`;
`isSubagent = session_v2.parent_id IS NOT NULL`; parent link `opencode:<parent_id>` when that session is tracked;
`model` = the latest assistant `data.model.id`; `lastLineMs` = the latest `session_message.time_updated` of the
session.

Rows of `session_message` are processed per session in `seq` order; `data` is JSON:

| `type` | Action |
| --- | --- |
| `assistant` | Fork copies are ignored: skip rows whose `data.time.created < session_v2.time_created - 2000`. If `data.time.streamed` is absent the request is in flight: phase `generating`, `inFlightStartMs = data.time.created`, no response yet. Otherwise emit a response (key = row `id`) when `data.tokens` is an object: `startMs = data.time.created`, `endMs = data.time.streamed` (the stream end; tool execution between `streamed` and `completed` is not generation), `final = true`, `model = data.model.id`, tokens `input = tokens.input + tokens.cache.read + tokens.cache.write`, `cachedInput = tokens.cache.read`, `cacheWriteInput = tokens.cache.write`, `output = tokens.output + tokens.reasoning` (OpenCode reports reasoning separately), `reasoning = tokens.reasoning`. Phase afterwards: `tools` when `data.finish == "tool-calls"`, else `idle`. A later update of the same row replaces the response (4.4). |
| `compaction` | When `data.tokens` is an object: a response with the same token mapping, `startMs = data.time.created ?? null`, `endMs = data.time.completed ?? time_updated`, `model = data.model.id`. No phase change. |
| `idle` | phase `idle` |
| anything else | no change |

Verified on real data: per-session sums of these responses equal OpenCode's own `session_v2.tokens_*` totals except
for small background calls (titles) that OpenCode never stores as messages.

### 4.6 Gemini CLI

#### Format eras

| Versions | Session file | What changes for us |
| --- | --- | --- |
| ≤ v0.38 | `session-*.json` (sub-agents `<id>.json`): one pretty-printed JSON object, rewritten in place | Legacy snapshot (below) |
| v0.39 – v0.63 | `*.jsonl`, append-only | Full-history checkpoints `{"$set":{"messages":[…]}}` (v0.44+); failed requests (v0.54+) and aborted requests (v0.57+) are rolled back with a checkpoint |
| ≥ v0.64 | `*.jsonl`, append-only | `$patch` deltas replace checkpoints; rollbacks write `$rewindTo` |

Other version boundaries used below: ≤ v0.31 a response without text is first written when its tool calls finish;
v0.44+ every tool-result submission writes a `user` record; v0.54+ usage and thoughts are written only for the
successful stream attempt; v0.37+ sub-agents live in a per-parent directory; v0.29+ project directories are slugs.

#### Layout and discovery

- `<root>/<projectDir>/chats/<file>` — main sessions. `<file>` = `session-<YYYY-MM-DDTHH-MM>-<sessionId[0:8]>.jsonl`
  (UTC minute; a same-minute collision inserts `-<n>` before the short id).
- `<root>/<projectDir>/chats/<parentSessionId>/<sessionId>.jsonl` — sub-agents (v0.37+). Both ids have every
  character outside `[A-Za-z0-9_-]` replaced by `_`. Nested sub-agents are stored flat under the *main* session id.
- v0.33 – v0.36 wrote sub-agents as `session-*.json` directly in `chats/` (identified only by `kind`).
- `<projectDir>` is a slug of the project folder name (`agent-speed`, `agent-speed-1`, …) containing a `.project_root`
  file with the absolute project root (v0.29+; lowercased on Windows), or, for older versions, the SHA-256 hex of
  the project root. `<root>/../projects.json` maps project roots to slugs.
- Discovery: regular files (never symlinks) ending in `.jsonl` or `.json` whose path relative to the root is exactly
  `<projectDir>/chats/<file>` or `<projectDir>/chats/<dir>/<file>`. Nothing else under `tmp` is a session
  (`checkpoint-*.json`, `logs.json`, tool outputs, plans). Temporary and backup files (`*.tmp-<pid>`,
  `*.unreadable-<ms>`) do not match.
- Docker/Podman sandbox sessions are written to an ephemeral host temp directory and are out of scope.

`.jsonl` files use the line reader of section 5. `.json` files are whole-document snapshots (Legacy, below).
The startup reading order (section 5) covers all Gemini roots together.

#### Record classification

Every line is one JSON object, classified in this order (Gemini's own loader uses the same order):

1. `$rewindTo` is a string → rewind
2. `$patch` is an object → patch
3. `id` is a string → message record (`type`: `user`, `gemini`, `info`, `warning`, `error`)
4. `$set` is an object → metadata update
5. `sessionId` and `projectHash` are strings → header (first line of every file)
6. anything else → ignored

A message record is the complete message. The same `id` is appended again whenever the message changes (tool calls
attached, rarely tokens attached late, tool results masked); a later record replaces the earlier one and keeps the
original `timestamp`. A message `timestamp` is its creation time, never its update time. A tool call `timestamp` is the
time its batch of tool calls finished (all calls of a batch share it).

#### Timing model

- A `user` record is written immediately before the API request is sent: after compression, tool-output masking, model
  routing and loop detection, before `BeforeModel` hooks and connection retries. From v0.44 tool results are submitted
  as a `user` record too; before v0.44 the next request starts right after the tool calls are recorded on the previous
  `gemini` message.
- A `gemini` record is created once, after the last stream chunk (and after `AfterModel` hooks), already carrying the
  final `tokens` (from v0.32; earlier only when the response had text). The stream start is never recorded.
- Therefore `startMs` = latest `user` record, or the tool-call completion time when no `user` record followed it;
  `endMs` = the `gemini` record `timestamp`. The duration includes time to first token, 429 back-off and mid-stream
  retries of the same request.

State: `sessionId`, `pendingStartMs`, `pendingMarker` (id of the record that set `pendingStartMs`),
`seenToolCallIds` (set), `phase`; all start empty / `idle`.

| Record | Action |
| --- | --- |
| header | `sessionId`; `isSubagent` when `kind == "subagent"`; `startTime`, `lastUpdated` count for `lastLineMs`. A header that carries a `messages` array (a legacy object written on one line) is replayed like a legacy file. |
| `$set` | `lastUpdated` counts for `lastLineMs`; `kind`, `sessionId` as in the header; a non-empty string `summary` → phase `idle`, `pendingStartMs = null` (a sub-agent writes it when it finished; a main session only receives it from a later process); a `messages` array → checkpoint (below). Other keys are ignored. |
| `$rewindTo` | phase `idle`, `pendingStartMs = null` (user `/rewind`, cancelled or failed request). Responses are never removed. |
| `$patch` | if phase is `generating` and `$patch.removeIds` contains `pendingMarker` → phase `idle`, `pendingStartMs = null`. Nothing else. |
| `user` | `pendingStartMs = ts`, `pendingMarker = id`, phase `generating` (`inFlightStartMs = ts`) |
| `error` | phase `idle`, `pendingStartMs = null` (the interactive CLI writes it when a request failed) |
| `info` whose `content` is exactly the string `Request cancelled.` | phase `idle`, `pendingStartMs = null` (the interactive CLI writes it when the user cancels, before any rollback record) |
| other `info`, `warning`, other types | only `lastLineMs` |
| `gemini` | see below |

Message records without a parseable `timestamp` are ignored.

`gemini` record:
1. `key = id`. If the key was first seen in another file, ignore the record entirely (including its tool calls).
2. If `tokens` is an object:
   - New key: emit a response with `startMs = pendingStartMs`, `endMs = ts`, `final = true`, `model = model`. Exception:
     when the record already carries tool calls with parseable timestamps and `ts >=` the earliest of them, the
     message was created when its tools finished (≤ v0.31), the stream end is unknown, and `startMs = null`. Then
     `pendingStartMs = null`, `pendingMarker = null`. Phase `tools` when `content` has no visible text (a string
     that is empty after trimming, or no non-thought `text` part) or contains a `functionCall` part; otherwise `idle`.
   - Known key (same file): replace the tokens, `model = model ?? previous`, `endMs = max(endMs, ts)`. Phase unchanged.
3. Tool calls: the entries of `toolCalls` that have a string `id` not yet in `seenToolCallIds` are new (add them).
   If there are new calls:
   - one of them is named `complete_task` with `status == "success"` → phase `idle`, `pendingStartMs = null`
     (a sub-agent finished);
   - else all new calls have `status == "cancelled"` → phase `idle`, `pendingStartMs = null` (nothing is submitted);
   - else phase `generating`, `pendingStartMs` = the latest `timestamp` of the new calls (else `ts`),
     `pendingMarker = id`, `inFlightStartMs = pendingStartMs`.
4. A `gemini` record without `tokens` and without new tool calls changes nothing (synthetic turns: compression
   acknowledgement, binary-content acknowledgement, interrupted-response placeholder).

Step 2 runs before step 3, so a record that both creates a response and reports finished tools ends in the phase of
step 3.

Checkpoint (`$set.messages`, v0.44 – v0.63): for every `gemini` message in it that has `tokens` and whose key is not
owned by another file: a known key is updated as in step 2; an unknown key (only possible when reading started
mid-file) is emitted with `startMs = null`. A checkpoint changes no other state, except: if phase is `generating` and
no message in it has `id == pendingMarker`, the in-flight request was rolled back → phase `idle`,
`pendingStartMs = null`.

Tokens (`tokens` is Gemini's `usageMetadata`; missing fields are 0, negatives clamp to 0):

| Field | Value |
| --- | --- |
| `input` | `tokens.input + tokens.tool` |
| `cachedInput` | `tokens.cached` |
| `cacheWriteInput` | 0 |
| `output` | `tokens.output + tokens.thoughts` |
| `reasoning` | `tokens.thoughts` |

`tokens.input` is `promptTokenCount` (already includes cached content); `tokens.output` is `candidatesTokenCount`
(excludes thoughts); `tokens.tool` is `toolUsePromptTokenCount`: results of server-side tools (search grounding, code
execution, URL context) fed back to the model as input and not part of `promptTokenCount`. `tokens.total` is the sum of
all four, so normalized `input + output == total`. `tokens.total` itself is not used.

Metadata:
- `lastLineMs` = the latest parseable timestamp among header/`$set` `startTime` and `lastUpdated`, message `timestamp`,
  `toolCalls[].timestamp` and `thoughts[].timestamp` of every record, including records ignored by deduplication.
  Messages inside checkpoints are not scanned.
- `model` = the model of the latest response created or updated in this file.
- `project` = basename of the trimmed contents of `<projectDir>/.project_root` (split on `/` and `\`); else the key of
  `<root>/../projects.json` `projects` whose value equals the `<projectDir>` name; else null. The session file itself
  never records the working directory. Read once per project directory and cache.
- `isSubagent` = `kind == "subagent"` (header or `$set`), or the file is inside `chats/<dir>/`.

#### Legacy `.json` (≤ v0.38)

The file is one JSON object `{sessionId, projectHash, startTime, lastUpdated, messages[], kind?, directories?,
summary?}`, rewritten in place (not atomically) on every change. Whenever its size or modification time changes, read
the whole file (files over 64 MiB are not read and carry the `partialHistory` warning) and parse it. On a parse failure
(caught mid-write) count one malformed line and keep the previous state. On success reset the file's parser state
(never the dedupe set) and replay: the object as a header, then each `messages` element in order as a message record
with the rules above. Responses of earlier snapshots missing from the new one (rewound) stay recorded; same-key
emissions are updates (4.4). The startup rules of section 5 apply (files modified before `todayStartMs - 15 min` are
not read).

#### Deduplication and copied history

- `key` = message `id` (random UUID), shared global dedupe set (3.4): the first file that emits a key owns it.
- Resuming a `.jsonl` session appends to the same file; nothing is copied.
- Resuming a legacy `.json` session (v0.39+) copies its header and every message, unchanged, into a new file with the
  same name plus `l` (`….json` → `….jsonl`); the `.json` stays. The copied responses are owned by the `.json` (older
  modification time, read first) and ignored. If the `.json` was not read, the copies count with their original
  timestamps, normally before today.
- When a `.jsonl` cannot be reloaded on resume, Gemini rewrites it atomically (rename): the identity change resets the
  file (section 5); the same keys merge as same-file updates.
- `$set.messages` checkpoints repeat messages already in the file; they merge by key.

#### Known approximations

- `tools` vs `idle` after a response: Gemini writes a response's tool calls only after they finish, so a response
  that has text *and* calls tools shows `idle` while its tools run (and while they await approval). A final answer
  with empty text after a tool result shows `tools` until the 300-second staleness rule.
- Before v0.44 the next request's start is the tool-completion time, which also includes compression, masking and
  routing that run before the request.
- Up to v0.53 Gemini writes a `gemini` record for an invalid stream attempt before retrying; the retry's response
  then has `startMs = null` (totals only). v0.54+ record only the successful attempt, so tokens of failed attempts are lost.
- LLM calls that Gemini does not record (chat compression, model-routing classifier, next-speaker check, loop
  detection, session summaries, helper tools) are not counted; totals are lower than Gemini's `/stats`.
- Before v0.54 failed requests are not rolled back and before v0.57 cancelled ones are not: the interactive CLI's
  `error` / `Request cancelled.` records end the phase; in `-p` and ACP sessions the phase stays `generating` until
  staleness. A quota fallback that adds tool results without sending them also leaves `generating` until staleness.
- If the previous `gemini` message has no tokens when a new response's usage arrives, Gemini attaches the new usage
  to that older message: the response is emitted under the older key with an invalid interval (totals only).
- Derived from the Gemini CLI source (v0.66, evidence in `spec/research/gemini-cli.md`); not yet verified against real
  session files.

### 4.7 omp (oh-my-pi)

omp (`can1357/oh-my-pi`, MIT) is a fork of pi (`badlogic/pi-mono`) with its own JSONL session files. Upstream pi
(`~/.pi/agent/sessions`, no request durations) is out of scope. Every model response is written once, complete, after
its stream ended, with omp's own request start (`message.timestamp`) and measured request duration
(`message.duration`); nothing is written while a request is in flight. Evidence: `spec/research/omp.md`.

#### Format eras

| Versions | Change | Effect on these rules |
| --- | --- | --- |
| ≤ 2.3 | binary `pi`, root `~/.pi/agent/sessions`, session version 2 | out of scope |
| 3.0 – 6.9.0 | root `~/.omp/agent/sessions`, session version 3; assistant messages have no `duration` | `endMs` = entry timestamp |
| 6.9.69+ | `duration` and `ttft` on assistant messages | `endMs = timestamp + duration` |
| 16.3+ | the first physical line is a fixed 256-byte `title` slot, rewritten in place | ignored |
| 17.2.5 – 17.2.8 | bucket folders named `<scope>-<basename>-<sha256>` (17.2.9 migrates them back) | none |
| 18.1+ | `model_usage` entries (side calls), `completedAt` on assistant messages, `runtime` on entries | side calls count in totals; second `endMs` fallback |

omp migrates older session files on load and rewrites them on its next write; files written today come almost
entirely from 18.x.

#### Roots and discovery

- Automatic roots (section 2): the default root, then one root per named profile. Profile names are the folders in
  `~/<cfg>/profiles` and, on macOS when `$XDG_DATA_HOME` is set, in `$XDG_DATA_HOME/omp/profiles`. A profile's root is
  `$XDG_DATA_HOME/omp/profiles/<name>/sessions` on macOS when `$XDG_DATA_HOME/omp/profiles/<name>` exists, else
  `~/<cfg>/profiles/<name>/agent/sessions`. Profiles ignore `$PI_CODING_AGENT_DIR`. `XDG_*` variables are never used on
  Windows. Roots that do not exist are skipped; a path listed twice is scanned once.
- Sessions written to custom folders (`--session-dir`, `--session`, `PI_CODING_AGENT_SESSION_DIR`; omp lists them in
  `<agent dir>/custom-session-files/`) are read only when the user setting points at their folder.
- Layout: `<root>/<bucket>/<YYYY-MM-DDTHH-MM-SS-mmmZ>_<sessionId>.jsonl` are main sessions (`<bucket>` encodes the
  working directory, `sessionId` is a UUIDv7); `<root>/<bucket>/<stem>/<name>.jsonl` belong to the session
  `<stem>.jsonl` (task sub-agents `<AgentId>.jsonl`, advisors `__advisor.jsonl` / `__advisor.<slug>.jsonl`, seance
  consults); a sub-agent's own children nest the same way under `<stem>/<AgentId>/`.
- Discovery: regular files (never symlinks) whose name ends in `.jsonl` and whose path relative to the root has at
  least two components, within the depth limit of section 2. Every file or folder whose name starts with `.` is
  skipped (`omp gc --compress` stages copies in `.omp-gc-compress-*`; rewrite temps are `.<name>.jsonl.<id>.tmp`).
  Windows move-aside backups (`*.jsonl.<id>.bak`), archives (`*.jsonl.gz` under `<agent dir>/archive`, outside the
  root) and artifacts (`*.log`, `*.md`, `*.patch`, `draft.txt`) do not match. Collab replicas (`~/.omp/collab`),
  `omp render` working copies and sub-agents of `--no-session` runs are outside every root.

Writes are single-line appends or full rewrites through a temp file renamed over the transcript (a new identity:
section 5 resets the file). The title slot is overwritten in place without changing the file size.

#### Record classification

Every line is one JSON object, classified by `type`:

1. `title` — the title slot. Ignored entirely.
2. `session` — the header. Only the first one counts; later headers are ignored entirely.
3. `message` — by `message.role`: `user`, `developer`, `assistant`, `toolResult`; any other role (`bashExecution`,
   `pythonExecution`, `fileMention`, `custom`, `hookMessage`) only updates `lastLineMs`.
4. `model_usage` — a side model call.
5. `custom` with `customType == "session_exit"` — the omp process ended.
6. anything else (`model_change`, `thinking_level_change`, `compaction`, `branch_summary`, `custom_message`, other
   `custom`, `label`, …) — only `lastLineMs`.

Lines before the header are ignored. Entries other than the header without a string `id` (version-1 files from before
2026) or without a parseable `timestamp` are ignored entirely. When reading a file starts at an offset greater than 0
(section 5), its header is read first: the first line of type `session` among the first two lines of the file (the
title slot precedes it since 16.3).

#### Timing model

- An assistant `message` entry is appended once, at the end of its stream and before its tools run. Partial streaming
  output is never persisted; there is no record while a request is in flight.
- `message.timestamp` (Unix ms) is taken when the provider call starts, immediately before the request is built and
  sent; it is never rewritten (not on in-provider retries, not on copies). `message.duration` (ms, monotonic) runs from
  that instant to the end of the stream. `message.completedAt` (18.1+) is the wall clock when the message ended.
- Therefore `startMs = message.timestamp`; `endMs = startMs + duration` (truncated to whole milliseconds; `duration`
  has sub-millisecond digits) when `startMs` is known and `duration` is a finite number ≥ 0, else `completedAt`
  (truncated) when it is a finite number > 0, else the entry `timestamp`. The interval includes request building, time
  to first token and in-provider retries with their back-off; it excludes agent-level work before the call.
- The input entry (`user`, `developer`), or the tool result that completes a batch, is written a few milliseconds
  before the next request starts; it starts the in-flight estimate.

State: `headerMs` (nullable), `pendingToolCalls` (set of ids), `phase` (starts `idle`).

| Record | Action |
| --- | --- |
| header | `headerMs` = its `timestamp`; `project` from `cwd`; the header `timestamp` counts for `lastLineMs` |
| any entry whose `timestamp` is earlier than `headerMs - 2000` | copied history (forks, branches, imports, moves): ignore completely |
| `message` / `user` or `developer` | phase `generating`, `inFlightStartMs = ts`; `pendingToolCalls` cleared |
| `message` / `assistant` | response (below) |
| `message` / `toolResult` | remove `message.toolCallId` from `pendingToolCalls`. If `message.toolName == "yield"` and `message.isError` is not `true` → phase `idle` (a sub-agent's final yield ends its run). Else if phase is `tools` and `pendingToolCalls` is empty → phase `generating`, `inFlightStartMs = ts`. |
| `model_usage` | side-call response (below); no phase change |
| `custom` / `session_exit` | phase `idle`, `pendingToolCalls` cleared |
| anything else | only `lastLineMs` |

`message` / `assistant` (`m` = `message`):
1. Skip the entry (only `lastLineMs`) unless `m.model`, `m.provider` and `m.api` are strings and `m.usage` is an
   object (omp's own statistics apply the same guard).
2. `stamp` = `m.timestamp` truncated to an integer when it is a finite number > 0, else the entry `timestamp` (ms).
   `key = "omp:" + id + ":" + stamp` (decimal). If the key was first seen in another file, ignore the entry: no
   response and no phase change (it still counts for `lastLineMs`). A repeat in the same file is an update (4.4).
3. `stopReason` = `m.stopReason`; a missing or non-string value counts as `aborted`.
4. When at least one of `usage.input`, `usage.output`, `usage.cacheRead`, `usage.cacheWrite` is > 0, emit a response:
   `startMs = m.timestamp` when it is a finite number > 0 (else null), `endMs` as in the timing model, `final = true`,
   `model = m.model`, tokens below. When `stopReason` is `error` or `aborted`, `startMs = null` (totals only: failed
   and interrupted requests carry partial or no output). Messages whose four fields are all 0 (abort placeholders, the
   synthetic message that closes a resumed interrupted turn) produce no response.
5. Phase, also when step 4 emitted nothing: `stopReason` `error` or `aborted` → `idle`, `pendingToolCalls` cleared.
   Else when `m.content` has blocks of type `toolCall` with a string `id` and `stopReason` is `toolUse`, `stop` or
   `length` → `tools`, `pendingToolCalls` = those ids (omp runs tool calls under `stop` too; under `length` it writes
   skipped results and continues). Else when `stopReason == "stop"` and `m.stopDetails.type == "pause_turn"` →
   `generating`, `inFlightStartMs = endMs` (omp samples again). Otherwise `idle`.

`model_usage` (18.1+) records side calls billed to the session (`purpose`: `auto-thinking`, `job-widget`, `ttsr`,
`memory`, `find`, `task-completion-probe`, `auto-prewalk`, `auto-graph`, `unexpected-stop`, …). Same guard as step 1
on the entry's own `model`, `provider`, `api` and `usage`; `key = "omp:" + id + ":" + <entry timestamp ms>`,
deduplicated like step 2. When one of the four token fields is > 0, emit `startMs = null`, `endMs = ts`,
`final = true`, `model = model`. These responses never set the agent's `model`.

Verification target (once real files are available): per file, the token sums equal what `omp stats` stores for it (it
reads exactly the assistant messages and `model_usage` entries), and each response's speed equals omp's own
`output * 1000 / duration`.

#### Tokens

`usage` is omp's normalized `Usage` (every provider maps into it). Missing or non-numeric fields are 0, negatives clamp
to 0.

| Field | Value |
| --- | --- |
| `input` | `usage.input + usage.cacheRead + usage.cacheWrite` |
| `cachedInput` | `usage.cacheRead` |
| `cacheWriteInput` | `usage.cacheWrite` |
| `output` | `usage.output` |
| `reasoning` | `usage.reasoningTokens` (absent → 0) |

`usage.input` is non-cached input only; `usage.output` already includes thinking and tool-call arguments, and
`reasoningTokens` is a subset of it (absent when the provider does not report it). `usage.totalTokens` and
`usage.orchestration` are not used (see approximations).

#### Metadata

- `project` = basename of the header `cwd` (split on `/` and `\`); null before the header is read.
- `model` = `message.model` of the latest assistant response created or updated in this file.
- `isSubagent` = the file's path relative to its root has three or more components (it lives inside a session's
  artifacts folder: task sub-agents, advisors, seance consults). The header's `parentSession` is not used (forks and
  branched main sessions carry it too).
- `lastLineMs` = the latest parseable `timestamp` of the header and of every entry that is not copied history,
  including entries ignored by deduplication and entries without usage.

#### Deduplication and copied history

- The key combines the entry `id` with the message's own `timestamp`; both survive every copy omp makes, and omp's
  statistics deduplicate on the same pair (first file wins). The shared dedupe set (3.4) applies.
- Copies with a fresh header: `/fork`, `/tree` branches, `--fork`, seance consults, the move to a sibling file when
  another process owns the session, `persistCopy`. Copied entries keep their `id` and `timestamp` while the new header
  gets the current time, so the copied-history rule removes them even when the source file is gone. A header
  timestamp is set only when a session identity is created, never by lazy materialization or rewrites.
- Imports: `/resume @claude` converts a Claude Code transcript into a new omp session, keeping each record's original
  timestamp and Claude's usage under new entry ids (`/resume @codex` imports no usage). Those API calls are already
  counted under `claudeCode` with other keys; the copied-history rule removes them.
- Copies without a fresh header: forks and moves also copy the source's artifacts folder in the background, which
  duplicates its sub-agent transcripts byte for byte. Only the dedupe set removes them; the copies are extra idle
  agents without responses of their own.
- Rewrites of the same file (migrations, `/rename` without a title slot, discarding a failed retry turn, recovery,
  `omp gc --compress` on macOS, which replaces the file): section 5 resets the file and the same keys merge as
  same-file updates. A response removed by a rewrite stays recorded.
- Resume appends to the same file.

#### Known approximations

- `usage.orchestration` (provider-side orchestration tokens: billed and part of `totalTokens`, but not part of the
  conversation) is not counted, so normalized `input + output` can be lower than `totalTokens`.
- Error and aborted responses count in totals only; their partial streams never contribute speed.
- In-flight state is inferred from neighbouring records. A brand-new session has no file until its first assistant
  message completes, so the first request of every new session is invisible while in flight (it appears with its full
  timing when it ends).
- Agent-level retries (overload, rate limit, network) start a new request without writing a record: the phase stays
  `idle` during the back-off and the retried request; its response still has exact timing.
- Interrupting while tools run writes aborted tool results and ends the turn without a final record: the phase shows
  `generating` until the 300-second staleness rule.
- In-provider retries keep the original `timestamp`, so their back-off lowers the measured speed. Providers that run
  tools inside one stream (Cursor exec channel, Devin, GitLab Duo workflows) include that tool time in `duration`.
- Calls omp does not record are not counted (compaction summaries, titles, branch summaries, unpersisted refusals and
  empty error turns, every side call before 18.1); totals match `omp stats`, not the provider bill.
- GitLab Duo writes no `duration`; before 18.1 its responses end at the entry timestamp (a few ms after the stream).
- Sub-agents nested deeper than the depth limit are not tracked.
- Derived from the omp source (v18.12.6, evidence in `spec/research/omp.md`); not yet verified against real session
  files.

### 4.8 Antigravity

Google Antigravity (the `agy` CLI, which replaced Gemini CLI for personal accounts on 2026-06-18; the 2.x desktop app;
the Antigravity IDE; IDE extensions) runs a local language server that keeps one SQLite database per conversation.
Every model call is a `PLANNER_RESPONSE` step row that carries its own usage and timestamps. Evidence:
`spec/research/antigravity.md`.

#### Format eras

| Era | Conversation store | Effect on these rules |
| --- | --- | --- |
| Antigravity IDE 1.x, agy before 1.0.7 | `conversations/<id>.pb`, `implicit/<id>.pb`: encrypted protobuf | out of scope (never decrypted) |
| agy 1.0.7 – 1.4.4, desktop app and IDE 2.x (since about June 2026) | `conversations/<id>.db`: SQLite (WAL) with protobuf blobs | read (below) |
| forks in agy 1.2.17 | both `<id>.pb` (at fork time) and `<id>.db` (once the fork continues) | only the `.db` |
| newer builds | also `brain/<id>/.system_generated/logs/transcript.jsonl` | out of scope (no usage) |

The database schema (`PRAGMA user_version = 1`, 7 tables) and the protobuf field numbers below are unchanged from agy
1.0.7 to 1.4.4; the IDE writes the same schema (verified at 2.2.1).

#### Stores and discovery

- Stores (section 2): `~/.gemini/antigravity-cli` (CLI), `~/.gemini/antigravity` (desktop app, IDE extensions, IDE
  1.x), `~/.gemini/antigravity-ide` (IDE 2.x). A store is the language server's `--app_data_dir`; no environment
  variable moves it. A store folder whose name ends in `-backup` (`antigravity-backup` holds a copy of `antigravity`)
  is never read, not even when the user setting names it. A user setting that names a folder called `conversations`
  is used as that folder directly.
- Discovery: regular files (never symlinks) directly inside `<store>/conversations/` whose name is a UUID (8-4-4-4-12
  hexadecimal digits, any case) followed by `.db`. Not sessions: `*.db-wal`, `*.db-shm`, `*.pb`, `implicit/`,
  `*.trajectory.json` (sidecars of third-party exporters), `conversation_summaries.db` (in the store folder), `brain/`,
  `annotations/`, `log/`.
- Each database is one agent: agent id = absolute path of the `.db`; conversation id = its file name without `.db`. It
  counts as one file for discovery statistics and the 20 000-file limit.
- Gemini CLI (4.6) never reads below these stores, so nothing is counted twice between the two sources.

#### Opening and querying

Each database is opened with the rules of 4.5: read-only (`mode=ro`, never written), busy timeout 2 s, short read
transactions; when the header says WAL (bytes 18 and 19 are both 2) and no `-wal` file exists (a closed
conversation), open with `immutable=1`; otherwise open `mode=ro`, falling back to `immutable=1` only when no `-wal`
file exists; an immutable connection is dropped on the next change of the `.db` or as soon as a `-wal` appears. Never
create a file. A database that cannot be opened or queried contributes nothing and is retried on its next change; one
without a `steps` table with the columns below carries the warning `unsupportedFormat` and contributes nothing (no agent).

Only these queries run (`?` = the cursors of 5.4, `-1` for a complete read). The size guards keep large blobs unread:
`gen_metadata` rows embed the whole prompt and reach hundreds of MB, so a row whose `size` column exceeds 1 MiB is
never read, even when its blob would be small.

```sql
SELECT idx, step_type, status, CASE WHEN length(metadata) <= 1048576 THEN metadata END FROM steps WHERE idx > ? ORDER BY idx, rowid
SELECT idx, CASE WHEN typeof(size) = 'integer' AND size <= 1048576 AND length(data) <= 1048576 THEN data END FROM gen_metadata WHERE idx > ? ORDER BY idx, rowid
SELECT idx, CASE WHEN length(data) <= 1048576 THEN data END FROM executor_metadata WHERE idx > ? ORDER BY idx, rowid
SELECT CASE WHEN length(data) <= 1048576 THEN data END FROM trajectory_metadata_blob ORDER BY rowid LIMIT 1
```

`step_payload` (prompts, responses, file contents) and the other blob columns are never read. NULL integers count as
0; a NULL blob (absent or too large) is skipped without counting as malformed. The tables have no primary key: rows
with the same `idx` are processed in `rowid` order, the later one replacing the earlier.

#### Protobuf wire format

Blobs are protobuf messages, decoded by a small hand-written walker (no protobuf library). A message is a sequence of
fields; each starts with a varint key, field number `key >> 3`, wire type `key & 7`:

| Wire type | Payload |
| --- | --- |
| 0 | varint: at most 10 bytes, 7 bits each, least significant group first |
| 1 | 8 bytes (fixed64, double) |
| 2 | a varint length, then that many bytes (string, bytes, nested message, packed repeated scalars) |
| 5 | 4 bytes (fixed32, float) |

Field number 0, wire types 3, 4 (groups), 6 and 7, a varint longer than 10 bytes, and a field that runs past the end of
its buffer make the blob malformed. Unknown fields of every valid wire type are skipped, and so is a known field
whose wire type differs from the one its type uses (except packed repeated scalars). proto3 omits default values:
a missing number is 0, a missing string is empty, a missing message is absent. A non-repeated field that occurs more
than once takes its last occurrence; where one value of a repeated message field is needed, its first occurrence is
used. Repeated scalars (`step_indices`) are accepted packed (wire type 2) and unpacked (wire type 0). Varints are
unsigned 64-bit; `int32` and `int64` fields reinterpret them as signed 64-bit (negative `int32` values are 10-byte
varints). Strings are UTF-8 (invalid sequences become U+FFFD). A `Timestamp` is `{1: seconds (int64), 2: nanos
(int32)}`; it is valid when `seconds > 0` and then equals `seconds * 1000 + nanos div 1 000 000` ms (nanos outside
0 … 999 999 999 count as 0). A blob that is malformed, including any nested message these rules read, counts as one
malformed line and is ignored.

Fields used (all others are ignored):

| Message (column) | Fields (number) |
| --- | --- |
| `CortexStepMetadata` (`steps.metadata`) | `created_at` 1, `viewable_at` 6, `finished_generating_at` 7, `completed_at` 8, `model_usage` 9, `last_completed_chunk_at` 22, `started_at` 32 (all `Timestamp` except 9) |
| `ModelUsageStats` | `input_tokens` 2, `output_tokens` 3, `cache_read_tokens` 5, `message_id` 7 (string), `thinking_output_tokens` 9, `response_output_tokens` 10, `response_id` 11 (string) |
| `CortexStepGeneratorMetadata` (`gen_metadata.data`) | `chat_model` 1, `step_indices` 2 (repeated uint32) |
| `ChatModelMetadata` | `response_model` 19 (string), `model_display_name` 21 (string) |
| `ExecutorMetadata` (`executor_metadata.data`) | `termination_reason` 1 (enum), `last_step_idx` 3 (int32), `cascade_config` 10 → `planner_config` 1 → `model_name` 28 (string) |
| `CortexTrajectoryMetadata` (`trajectory_metadata_blob.data`) | `workspaces` 1 (repeated) → `workspace_folder_absolute_uri` 1 (string); `created_at` 2; `parent_conversation_id` 5 (string); `workspace_uris` 7 (repeated string); `subagent_spec` 8; `nesting_depth` 17 (int32) |

Column enums: `step_type` 14 = `USER_INPUT`, 15 = `PLANNER_RESPONSE`; every other value is a tool or system step (for
example 8 `VIEW_FILE`, 21 `RUN_COMMAND`, 127 `INVOKE_SUBAGENT`). `status` 1 `PENDING`, 2 `RUNNING`, 3 `DONE`,
4 `INVALID`, 5 `CLEARED`, 6 `CANCELED`, 7 `ERROR`, 8 `GENERATING`, 9 `WAITING`, 11 `QUEUED`, 12 `INTERRUPTED`.
*Open* statuses are 1, 2, 8, 9 and 11; every other value is *terminal*. `ModelUsageStats.model` and
`CortexStepMetadata.generator_model` are enums whose current values are placeholders; they are not used.

#### Step rows

The trajectory metadata gives `createdMs` (= `created_at`), `project`, `isSubagent` and the parent link (6.7); while
it is missing, every read retries it.

Rows of `steps` are processed in `idx` order:
1. Skip a row whose `metadata` is NULL. Skip a row and count one malformed line when its `metadata` does not decode or
   a token field of its `model_usage` exceeds 10 000 000.
2. If `createdMs` is known, `created_at` is valid and `created_at < createdMs - 2000`, the row is copied history (a
   fork): ignore it completely.
3. Every valid timestamp of the row's metadata counts for `lastLineMs`. The row with the highest `idx` processed so far
   is the *latest row* (a re-read row replaces its earlier version).
4. When `step_type == 15`, the status is terminal, `model_usage` is present and one of its five token fields is > 0,
   the row is a response:
   - `key` = `model_usage.response_id` when non-empty, else `model_usage.message_id` when non-empty, else
     `"antigravity:" + conversation id + ":" + idx`. If the key was first seen in another database, no response.
   - `endMs` = `finished_generating_at`, else `last_completed_chunk_at`, else `completed_at`; `startMs` = `created_at`
     (null when invalid). When none of the three is valid, `endMs` = the latest valid timestamp of the row and
     `startMs = null`; a row without any valid timestamp gives no response.
   - Status 6, 7 or 12 (cancelled, error, interrupted) → `startMs = null` (totals only).
   - `final = true`, `model` as in Metadata, tokens below. A later version of the row is an update (4.4).

A `PLANNER_RESPONSE` row with an open status is in flight and produces no response yet, even when it already carries
usage.

#### Phase

After every read (the startup read included) the phase is a function of the rows read so far:
1. No latest row → `idle`.
2. An `executor_metadata` row with `termination_reason != 0` and `last_step_idx >=` the latest row's `idx` (the run that
   contains it has ended) → `idle`.
3. Otherwise by the latest row:

| Latest row | Phase |
| --- | --- |
| `PLANNER_RESPONSE`, open status | `generating`, `inFlightStartMs = created_at` |
| `PLANNER_RESPONSE`, terminal status | `idle` (a response with tool calls is followed by its tool rows at once) |
| `USER_INPUT`, status 6, 7 or 12 | `idle` |
| `USER_INPUT`, any other status | `generating`, `inFlightStartMs = created_at` (the model call follows at once) |
| other step, open status | `tools` |
| other step, `DONE` | `generating`, `inFlightStartMs = completed_at`, else `created_at` (the next model call) |
| other step, any other terminal status | `idle` |

An invalid timestamp makes `inFlightStartMs` null. The staleness rule (6.7) applies.

#### Timing model

- A `PLANNER_RESPONSE` step is created when the planner starts the model call and is filled while the stream arrives;
  `finished_generating_at` marks the end of generation. The duration therefore includes time to first token and the
  planner's retries of the same call (`retry_infos`), as with Gemini CLI.
- The matching `gen_metadata` row's `time_to_first_token + streaming_duration` should be close to `endMs - startMs`; it
  is not used (and agy ≤ 1.1.17's `chat_start_metadata.created_at` neither).

#### Tokens

`model_usage` of the planner row (`ModelUsageStats`); missing fields are 0.

| Field | Value |
| --- | --- |
| `input` | `input_tokens + cache_read_tokens` |
| `cachedInput` | `cache_read_tokens` |
| `cacheWriteInput` | 0 |
| `output` | `output_tokens`; when it is 0, `thinking_output_tokens + response_output_tokens` |
| `reasoning` | `thinking_output_tokens` |

Unlike Gemini CLI's `usageMetadata`, `input_tokens` excludes cached input, and `output_tokens` already includes
thinking (`output_tokens == thinking_output_tokens + response_output_tokens` on every audited real row).
`cache_write_tokens` (4) is deprecated and 0.

#### Metadata

- `model` of a response: `chat_model.response_model` of the `gen_metadata` row with the lowest `idx` whose
  `step_indices` contains the row's `idx`, else that row's `chat_model.model_display_name`; else the `model_name` of
  the `executor_metadata` row with the smallest `last_step_idx >=` the row's `idx` (ties: lowest `idx`); else null.
  Empty strings count as missing. The model is resolved from the rows read so far; when a later read changes it, the
  response is updated (4.4). Serving variants (`-exp-b`) and routing labels (`gemini-pro-default`) are shown as they
  are.
- Agent `model` = the model of the response with the highest `idx` that has one.
- `project` = basename of the first `workspace_uris` entry, else of the first `workspaces` entry's
  `workspace_folder_absolute_uri`, else null. A URI (a scheme of two or more characters, such as `file:` or
  `vscode-remote:`) loses its scheme and authority (`//host`) and is percent-decoded as UTF-8 (invalid escapes stay as
  they are) before splitting on `/` and `\`; `file:///c%3A/Users/dev/projects/orbit` gives `orbit`.
- `isSubagent` = `parent_conversation_id` non-empty, or `nesting_depth > 0`, or `subagent_spec` present.
- `lastLineMs` = the latest valid timestamp among the processed rows' metadata and `createdMs`.

#### Deduplication and copied history

- The shared dedupe set (3.4) applies to response keys: the first database that emits a key owns it. This removes the
  same conversation found in two stores (some machines hold byte copies in `antigravity` and `antigravity-ide`) and
  history copied by `/fork`.
- The copied-history rule (step 2) also drops forked rows that have no response id.
- `/rewind` and cleared steps never remove counted responses.

#### Known approximations

- Antigravity timing semantics are not yet verified against real data: the meaning of `created_at` and
  `finished_generating_at` for planner rows is inferred from field names, Windsurf-era semantics and one sanitized
  timing sample (1.65 s and 2.36 s for two short answers).
- Whether a planner row with status `GENERATING` is written before its stream ends is unknown; if it is not, the
  in-flight phase comes only from the user-input and finished-tool rules.
- Error, cancelled and interrupted planner responses count in totals only.
- Model calls that are not planner steps (context checkpoints, titles, summaries, side questions) exist only in
  `gen_metadata`, which is not scanned for responses, and usage of failed retry attempts (`retry_infos[].usage`) is
  not counted: totals are lower than Antigravity's own `/usage`.
- Usage written to a row after it reached a terminal status is seen only by the next complete read (startup, history).
- Legacy encrypted conversations, transcript-only conversations, the Gemini macOS app's Antigravity integration and
  the Python SDK runtime contribute nothing.
- Derived from public reverse-engineering projects and the protobuf descriptors of agy 1.0.10 – 1.4.4 (evidence in
  `spec/research/antigravity.md`); not yet verified against real conversation databases.

## 5. Reading files

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

### 5.1 zstd frame streams (DeepSeek Harness)
`.zstd` files are read frame by frame: from the current offset, determine the compressed size of the next frame
(zstd `findFrameCompressedSize`); if the frame is not complete yet, stop and wait for more bytes. Decode each complete
frame (at most 64 MiB of output per frame, else the frame counts as malformed and is skipped), feed the decoded bytes
to the line reader, and advance the offset past the frame. A frame that fails to decode is skipped and counted as one
malformed line. Skippable frames are skipped. When the bytes at the offset are not a frame at all (unknown magic or
an invalid block header), count one malformed line and resynchronize at the next zstd frame or skippable-frame magic
after the offset; if none is present yet, keep the offset and retry when the file grows. Files that start at their
end (older files) start at the current size; files over 256 MiB are not seeded at all (they start at their end with the `partialHistory` warning), because a frame
stream cannot be entered in the middle.

### 5.2 SQLite sources (OpenCode)
Change detection watches `opencode.db` and `opencode.db-wal` (file watcher plus the 1 s fallback). On a change, read
new rows with `time_created > cursor` (indexed) plus the current versions of rows that are still in flight or were
updated recently (`id IN (...)`), and sessions whose `time_updated` advanced. Startup reads sessions with
`time_updated >= todayStartMs - 15 min` completely. The source counts as one "file" for discovery statistics.

### 5.3 Compressed Codex sessions (`.jsonl.zst`)
Codex compresses finished rollouts in place (`rollout-….jsonl` becomes `rollout-….jsonl.zst`, written once). Such a
file is one zstd stream (usually one frame; its decompressed size can exceed 1 GB). Read it with streaming
decompression from offset 0 with bounded buffers: any number of frames, skippable frames skipped, no per-frame output
limit, the decoded bytes fed to the line reader (section 5 line rules). A decoding error stops the file there and
counts one malformed line; what was parsed before stays. The file is never tailed: when its size, modification time
or identity changes, reset it (parser state, never the dedupe set) and read it again from the start. Startup follows
the same modification-time rule as other files; the 256 MiB tail rule does not apply. A session compressed while it
is tracked shows up as a new file whose response keys are already owned by the `.jsonl` (3.4), so nothing counts
twice. Reading or re-reading a whole compressed file (or a legacy Gemini `.json`) while running happens off the
live loop, so live updates never pause; its results are applied in one step when the read completes.

### 5.4 Antigravity conversation databases
Real users have hundreds of small conversation databases (one audit: 381); only recently changed ones are opened.

- Startup: a database's modification time is the newer of `<id>.db` and `<id>.db-wal`; with that time it takes part
  in the startup reading order of section 5 (and of section 8). Databases modified within `todayStartMs - 15 min` or
  later are read completely (seeded). Older databases are not opened; the first time one changes while running, it is
  read completely off the live loop (like a whole compressed file, 5.3) and its results are applied in one step.
- Change detection: the file watcher on every `conversations` folder, plus the 1-second fallback, which stats the
  `.db` and `.db-wal` of databases modified in the last 2 minutes, plus rediscovery every 10 seconds (new databases are
  read completely). A change of either file marks the database dirty. The language server writes the WAL continuously
  while a conversation is open, so a dirty database is read at most once every 500 ms.
- Incremental read, one short transaction: `steps` rows with `idx` above the highest `idx` read so far, plus the current
  versions of the rows last seen with an open status (`idx IN (…)`); `gen_metadata` and `executor_metadata` rows above
  their own `idx` cursors; the trajectory metadata until it is found. Then the phase (4.8) is evaluated. A new identity
  of the `.db` file resets the database (parser state, never the dedupe set) and reads it completely.
- Connections: a connection idle for 60 seconds is closed, and at most 16 stay open (the least recently changed is
  closed first). Reopening follows the open rules of 4.8.

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
- `responseCompleted` — the first emission of a key (4.4) whose `endMs >= nowMs - 120 000`, carrying source,
  project, model, output, total, duration and speed. Older responses that appear later (a whole-file source such as a
  legacy Gemini `.json`, a `.jsonl.zst` read for the first time while running, or an older Antigravity database read
  on its first change, 5.4) update totals silently.
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
- Gemini CLI: a file at `<chats>/<dir>/<name>` (where `<chats>` is the directory named `chats`) has as parent the
  tracked Gemini file directly inside `<chats>` whose header `sessionId`, with every character outside
  `[A-Za-z0-9_-]` replaced by `_`, equals `<dir>`; several candidates → prefer `.jsonl` over `.json`, then the
  earliest header `startTime`, then ordinal path. Nested sub-agents therefore attach to the main session; v0.33 – v0.36
  sub-agents (directly in `chats/`) have no parent.
- omp: a file `<dir>/<name>.jsonl` whose path relative to its root has three or more components has the parent
  `<dir>.jsonl` when that file is a tracked agent (a nested sub-agent attaches to the sub-agent that started it).
- Antigravity: the tracked Antigravity database whose conversation id equals this database's
  `parent_conversation_id` (compared case-insensitively); several (copies in two stores) → prefer the one in the same
  `conversations` folder, then the ordinally smallest path. Sub-agents (browser, `invoke_subagent`, image generation)
  are separate conversations; parallel conversations of the agent manager and battle-mode forks are independent agents.
- Self references and cycles resolve to null.

List:
1. Staleness first: a phase older than 300 seconds without a new line is reported as `idle`.
2. Base set: agents whose `lastLineMs` is within the last 10 minutes, or whose phase is `generating`. An agent whose
   file no longer exists (deleted, or replaced by its `.jsonl.zst`) leaves the list as soon as that is noticed; its
   responses stay in every total.
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
| `< 1 000 000 000` | `1.28M` / `12.8M` / `128M` |
| else | `1.28B` / `12.8B` / `128B` (three significant digits, more digits only above 999B) |

A value that rounds up to the next unit uses that unit (`999 700` → `1.00M`, `999 700 000` → `1.00B`). Token counts in
summaries use the same compact form (`1.2M`, `384k`, `2.49B`).
## 8. Daily history

Daily statistics are computed from the complete logs and kept permanently, so they survive tools deleting their own
logs (Claude Code removes transcripts after 30 days by default). Only aggregates are stored — never requests, keys in
plain text, prompts or content.

Scope: every discovered file of every enabled source with no date filter and no file-count limit (Codex `.jsonl.zst`,
all Gemini roots with legacy `.json`, every omp root including profiles, and every Antigravity conversation database
of every store included), plus Codex `archived_sessions` (the sibling of the sessions root) and the whole OpenCode
database. Parsing uses exactly the rules of section 4 (same state machines, dedupe, copied-history rules) over whole
files and complete database reads, in startup order (5; an Antigravity database's time is the newer of its `.db` and
`-wal`, 5.4).

Per local calendar day (date of `endMs`, `YYYY-MM-DD` in the computer's time zone at computation time) and source:
`requests`, `input`, `cachedInput`, `cacheWriteInput`, `output`, `reasoning`, `measuredOutput` and `measuredMs`
(sum of output and widened duration of measured responses, 3.3), `unfinalized` (count), `agents` (distinct agent ids
with at least one response), and per model `requests` and `output`. The day's average speed is
`measuredOutput / (measuredMs / 1000)`. `peak` is stored only for days observed live: it is the day's overall
`peakToday` (all sources together), repeated on every source row of that day; a UI shows it only for all sources and
combines rows with max, never sum.

Incremental cache: for each file keep its identity (path, size, modification time; for an Antigravity database also
the size and modification time of its `-wal`), the 64-bit FNV-1a hashes of the
response keys it owns, and its per-day contributions. On launch and periodically (every 10 minutes and at midnight),
unchanged files reuse their contributions; new or changed files are re-parsed completely, skipping keys owned by any
other file (ownership follows startup order); files that no longer exist keep their contributions. Daily totals are
the sums over files. The history store is versioned: a parser change bumps the version and triggers a full
recomputation that keeps previously stored days for which no logs exist any more. "Clear history" (settings) deletes
the store and recomputes from the current logs.
