# pi — research notes

Evidence for METRICS 4.13. The rules were derived from the shipped package
`@earendil-works/pi-coding-agent@1.3.0` (`package.json` `piConfig.configDir` = `.pi`), its own documentation
(`docs/session-format.md`, `docs/message-types.md`, `docs/sessions.md`, `docs/environment-variables.md`, packaged in
the same release; line numbers below are of that release's files) and the compiled behaviour
(`dist/core/agent-session.js`), then measured against 109 real session files and 7 real extension run sub-sessions
from a Windows machine (2026-08-13 … 2026-10-28, 8 107 assistant messages). Re-check with
`python3 spec/tools/reference.py --dump-today --pi <sessions folder>` against
`AgentSpeed.Probe --dump-today --pi <sessions folder>` through `spec/tools/compare_dumps.py`.

pi is the upstream coding agent by earendil works (`pi.dev`, npm `@earendil-works/pi-coding-agent`, previously
`@mariozechner/pi-coding-agent`). It is the ancestor of omp (`can1357/oh-my-pi`, METRICS 4.7): the two share the
JSONL session family, but pi has moved on independently — it writes no title slot, no `model_usage` entries and no
`serviceTier`, and its assistant messages carry `durationMs` instead of omp's `duration`/`ttft`/`completedAt`. The
omp research note called upstream pi out of scope while it had no request durations; that premise no longer holds.

## Decisions

- **Only the two layouts pi writes are sessions.** `<bucket>/<name>.jsonl` exactly two levels below the root, and
  `session.jsonl` three or more levels below it (`<bucket>/<name>/<uuid>/run-N/session.jsonl`, an extension's run
  sub-session, named `subagent-…` through a `session_info` entry in the real files). Everything else below the root —
  logs, drafts, work notes an extension writes next to a session — is ignored.
- **A run sub-session is its own agent and attaches to `<bucket>/<name>.jsonl`.** Measured: the 7 run sub-sessions
  share no assistant keys with their parent file, so nothing is counted twice and the parent link is path-derived
  (as omp's sub-agents are, 4.7).
- **`--session-dir`, `PI_CODING_AGENT_SESSION_DIR` and the `sessionDir` setting can point anywhere.** The first two
  are honoured as the automatic root (the environment variable is part of the root resolution); a `sessionDir` setting
  inside pi's own settings file is not read, exactly as omp does not follow its custom session markers (4.7). The
  user setting in Settings → Sources covers those folders.
- **`message.timestamp` and `durationMs` are the interval; the entry timestamp is the fallback.** pi writes the
  assistant entry when the stream ends, so `entryTimestamp − (started + durationMs)` is a few milliseconds (2 ms on a
  measured example). `durationMs` only exists in recent releases (42 of 8 107 real assistant messages), so the entry
  timestamp is the primary path, not the exception.
- **Entry `id` is not required.** Usually an 8-hex id (docs/session-format.md:44), it may be a full UUID, and v1 files
  have none; the key falls back to the empty string so such files still count (the entry timestamp and message
  timestamp keep the key unique).
- **Copied history (`/fork`, `/clone`) is skipped and deduplicated.** `/clone` "copies the active branch into a new
  session" (docs/sessions.md:28) and `/fork` creates a new session from an earlier user message (docs/sessions.md:27);
  the header of such a session carries `parentSession` (docs/session-format.md:72-75). Copied entries keep their
  original timestamps, so they fall before the new header by far more than 2 seconds and are skipped; the (id,
  timestamp) key additionally deduplicates them against the file that owns them (3.4).
- **`usage` is pi's own field set.** `input` is the part that was not read from cache, `output` includes thinking,
  `reasoning` is already part of `output` (docs/message-types.md:90) and must not be added again; `cacheWrite1h` is
  the subset of `cacheWrite` written with one-hour retention. The `cost` object pi writes is ignored: cost is always
  computed from this project's own price table (METRICS 9).
- **All four usage counters at 0 produce no response**, as for omp (4.7); aborted turns and extension placeholders
  would otherwise inflate request counts.
- **Error and aborted responses count in totals only.** `stopReason` is pi's own vocabulary
  (docs/message-types.md:134); a missing or non-string value counts as `aborted`, as for omp. `length` and `deferred`
  are terminal and complete normally.
- **Side usage counts in totals only and never sets the agent's model**, like omp's `model_usage`: a `usage` entry
  (any `kind`; `cache_warm` is pi's example, docs/session-format.md:115-121), a `compaction` or `branch_summary`
  entry carrying `usage` (docs/session-format.md:135, 160), and a `toolResult` message carrying `usage`
  (docs/message-types.md:177). Compaction, branch summaries and tool results name no model, so they are attributed to
  the session's current one.
- **`pending` assistant messages are never persisted** (docs/message-types.md:145), so live speed is inferred from
  neighbouring entries like omp's: a `user` message opens the in-flight window, `toolCall` blocks move the agent to
  `tools`, and the tool results that answer them (or an error) close it.
- **No execution modes.** `thinkingLevel` (docs/message-types.md:122-141) is a reasoning level, not a speed tier, and
  pi records no provider tier the way omp's `serviceTier` does. Nothing is fabricated for EXECUTION-MODES.
- **`session_info` names, `context_edit`, `label`, `custom`, `custom_message` and `model_change` do not affect
  measurement.** Names are presentation, edits change future context only (docs/session-format.md:174-181), and the
  model of a response is taken from the assistant message itself.

Worth revisiting with real files:

- Whether `/clone` re-ids entries. The dedupe and the 2-second cutoff both assume it does not; the measured files
  contain no clone (`parentSession` appears in none of the 109 headers).
- Nested runs (a run sub-session that starts another run) would attach to the outer session today.
- Whether pi keeps writing `durationMs` (adoption rate: 42 of 8 107 assistant messages so far) and whether omp's
  `duration`/`ttft`/`completedAt` ever reappear.

## Measurements on real files

| Fact | Value |
| --- | --- |
| Session files / extension run sub-sessions | 109 at depth 2, 7 at depth 5 (`<bucket>/<name>/<uuid>/run-0/session.jsonl`), nothing deeper |
| Entries | `session` 109, `model_change` 268, `thinking_level_change` 197, `message` 18 534, `custom` 105, `custom_message` 2, `compaction` 2 |
| Messages by role | assistant 8 107, toolResult 9 963 (2 with `usage`), user 831, system 3 |
| `stopReason` / `rawStopReason` | `toolUse`/`tool_calls` 7 057, `stop`/`stop` 715, `toolUse`/`completed` 156, `error`/null 136, `stop`/`completed` 21, `aborted`/null 21, `aborted`/`tool_calls` 1 |
| `durationMs` | 42 of 8 107 assistant messages; min 1 474, median 10 536, max 35 391 ms |
| Entry timestamp − (start + `durationMs`) | 2 ms on a measured example |
| Run sub-session keys shared with the parent | 0 |
| `parentSession` headers (forks, clones) | 0 |

Cross-check on those files (2026-10-28, one active session): `reference.py --dump-today --pi` and
`AgentSpeed.Probe --dump-today --pi` agree on all 56 responses — keys, tokens, intervals, models, providers and
API-equivalent cost — with `compare_dumps.py` reporting `missing=0 extra=0` (`requestCount`, which the Python dump
omits for every non-harness source, is the only field difference).

## Evidence

pi at `@earendil-works/pi-coding-agent@1.3.0` unless a path is named.

| Rule | Source |
| --- | --- |
| Config folder `.pi` | `package.json` `piConfig.configDir` |
| File location `<bucket>/<timestamp>_<session-id>.jsonl`, bucket encoding | `docs/session-format.md` 8-17 |
| Versions 1/2/3, automatic migration on load | `docs/session-format.md` 22-29 |
| Entry base: `id` 8-hex (UUID fallback), `parentId`, ISO `timestamp` | `docs/session-format.md` 31-45 |
| Entry timestamps ISO 8601, message timestamps Unix ms | `docs/session-format.md` 47; `docs/message-types.md` 5 |
| Header fields (`version`, `id`, `timestamp`, `cwd`, `parentSession`) | `docs/session-format.md` 54-75 |
| `message` entry types: system/user/assistant/toolResult/custom/bashExecution… | `docs/session-format.md` 78-113; `docs/message-types.md` 94-260 |
| `usage` entry: arbitrary `kind`, contributes to session totals | `docs/session-format.md` 115-121 |
| `compaction` / `branch_summary` `usage` contributes to totals | `docs/session-format.md` 128-163 |
| `custom`, `custom_message`, `label`, `session_info`, `context_edit` semantics | `docs/session-format.md` 164-207 |
| Assistant message fields (`api`, `provider`, `model`, `responseId`, `thinkingLevel`, `usage`, `stopReason`, `rawStopReason`, `timestamp`) | `docs/message-types.md` 122-141 |
| `Usage` fields and `reasoning` included in `output` | `docs/message-types.md` 67-90 |
| `pending` assistant messages are not persisted | `docs/message-types.md` 145 |
| Tool result `usage` = nested model work, contributes to statistics | `docs/message-types.md` 170-177 |
| Sessions under `~/.pi/agent/sessions`, grouped by working directory; `--session-dir`, `PI_CODING_AGENT_SESSION_DIR`, `sessionDir` | `docs/sessions.md` 48-52 |
| `/tree`, `/fork`, `/clone` behaviour | `docs/sessions.md` 22-36 |
| `PI_CODING_AGENT_DIR` (default `~/.pi/agent`), `PI_CODING_AGENT_SESSION_DIR`, `AI_AGENT=pi`, `PI_CODING_AGENT=true` | `docs/environment-variables.md` 9-16, 78-83 |
| `durationMs` written from the stream's own duration | `dist/core/agent-session.js` 965 |
| Real-file measurements above | the 109-session Windows corpus described above |
