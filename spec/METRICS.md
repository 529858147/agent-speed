# Agent Speed — Measurement Specification

This document is the single source of truth for how Agent Speed reads agent transcripts and turns them into
tokens-per-second. The macOS (Swift) and Windows (C#) implementations MUST follow it exactly and MUST pass the
shared fixtures in `spec/fixtures/`.

## 1. Vocabulary

| Term | Meaning |
| --- | --- |
| Source | `codex`, `claudeCode`, `claudeDesktop`, `deepseek` (DeepSeek Harness, `dsh`), `opencode`, `gemini` (Gemini CLI) |
| Agent | One transcript file. Agent id = absolute file path. Sub-agents have their own files and are separate agents. |
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

`~` is `HOME` on macOS and `%USERPROFILE%` on Windows. Claude Code and Claude Desktop share one root; each file is
assigned to a source by its `entrypoint` field (section 4.1). Ordinary Claude Desktop chat conversations are not stored
locally and are out of scope.

Discovery: recursive enumeration of `*.jsonl` (DeepSeek: `session.v<N>.jsonl.zstd` or `session.v<N>.jsonl`; Gemini CLI:
fixed depths, see 4.6), maximum depth 6, never follow symlinks, at most 20 000 files per root.
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

### 4.5 OpenCode

OpenCode (2.x) stores everything in one SQLite database in WAL mode. It is opened read-only (never written, no
`immutable` flag), with a busy timeout of 2 s, and every read is a short transaction so OpenCode's checkpoints are
never blocked. A database without the tables `session_v2` and `session_message` is reported as unsupported (warning
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
- Gemini CLI: a file at `<chats>/<dir>/<name>` (where `<chats>` is the directory named `chats`) has as parent the
  tracked Gemini file directly inside `<chats>` whose header `sessionId`, with every character outside
  `[A-Za-z0-9_-]` replaced by `_`, equals `<dir>`; several candidates → prefer `.jsonl` over `.json`, then the
  earliest header `startTime`, then ordinal path. Nested sub-agents therefore attach to the main session; v0.33 – v0.36
  sub-agents (directly in `chats/`) have no parent.
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
## 8. Daily history

Daily statistics are computed from the complete logs and kept permanently, so they survive tools deleting their own
logs (Claude Code removes transcripts after 30 days by default). Only aggregates are stored — never requests, keys in
plain text, prompts or content.

Scope: every discovered file of every enabled source with no date filter (all Gemini roots, legacy `.json` included),
plus Codex `archived_sessions` (the sibling of the sessions root) and the whole OpenCode database. Parsing uses exactly the rules of section 4 (same state
machines, dedupe, copied-history rules) over whole files, in startup order (5).

Per local calendar day (date of `endMs`, `YYYY-MM-DD` in the computer's time zone at computation time) and source:
`requests`, `input`, `cachedInput`, `cacheWriteInput`, `output`, `reasoning`, `measuredOutput` and `measuredMs`
(sum of output and widened duration of measured responses, 3.3), `unfinalized` (count), `agents` (distinct agent ids
with at least one response), and per model `requests` and `output`. The day's average speed is
`measuredOutput / (measuredMs / 1000)`; `peak` is stored only for days observed live (the day's `peakToday`).

Incremental cache: for each file keep its identity (path, size, modification time), the 64-bit FNV-1a hashes of the
response keys it owns, and its per-day contributions. On launch and periodically (every 10 minutes and at midnight),
unchanged files reuse their contributions; new or changed files are re-parsed completely, skipping keys owned by any
other file (ownership follows startup order); files that no longer exist keep their contributions. Daily totals are
the sums over files. The history store is versioned: a parser change bumps the version and triggers a full
recomputation that keeps previously stored days for which no logs exist any more. "Clear history" (settings) deletes
the store and recomputes from the current logs.
