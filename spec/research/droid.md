# Factory Droid — research notes

Requested in GitHub issue #33 (droid CLI 0.250.0 on Windows 10, paths and fields checked by the reporter). Factory's
CLI coding agent `droid` and the Factory Desktop app are closed source. No Droid data exists on the project's own
machines; the rules of METRICS 4.16 are derived from the issue, Factory's documentation and changelog, public
recordings of real sessions in the test data of third-party readers, and the code of those readers (read as
references only, never copied). Every fixture in `spec/fixtures/droid` is hand-written and synthetic.

Sources:

- Issue #33 (the field list and the `custom:<name>-[<gateway>]-0` ids).
- Factory docs (docs.factory.ai, now redirecting to docs.factory.com): settings, models, BYOK, droid exec, analytics
  API, and the CLI changelog (entries quoted below by version).
- tokscale (`junhoyeo/tokscale` at `39ecb83ab045`, `crates/tokscale-core/src/sessions/droid.rs`): client `droid`.
- superbasedapp/observer (`f80c1a36`, `internal/adapter/droid/`, real recordings under
  `testdata/factory/sessions`: droid on Linux and Windows and Factory Desktop on Windows, with `.bak` files).
- entireio/cli (`6870b7b1`, `cmd/entire/cli/agent/factoryaidroid/`: sub-agent sessions, token block notes).
- inference-sh/agentprotocol (`790173f6`, `transcript/droid/`: a recorded harness session of droid 0.226 with a
  `/compress` successor).
- ccusage (`85ebca36`, `rust/adapters/droid`), getagentseal/codeburn (`800f650d`, `src/providers/droid.ts`),
  ain3sh/droid-session-explorer (`adfd3df4`), Factory-AI/droid-sdk-python (`aa6fd945`, the live protocol's types).

## Verdict

Feasible, with one structural limit: the transcript has no usage at all; usage exists only as cumulative counters
per session in a sidecar file that droid rewrites while the session runs. The transcript does time every model
call (the user or tool-result message before it and the assistant message it produced), so Agent Speed takes the
calls and their timing from the transcript and their tokens from the counters (METRICS 4.16).

| | Where | Confidence |
| --- | --- | --- |
| One call per assistant message, with its end time | `message` lines, ISO `timestamp` | recordings |
| Call start | the latest user line sent to the model (`visibility` absent or `llm_only`) | recordings |
| Tokens | sidecar `tokenUsage`, cumulative, the session's own calls only | issue, recordings, 4 readers agree |
| Model, effort | assistant `message.modelId`, `message.reasoningEffort` | issue, recordings |
| Project | `session_start.cwd` (the folder slug is lossy) | issue, recordings |
| Sub-agent parent | `session_start.callingSessionId` (+ `callingToolUseId`) | entireio's recorded e2e session |
| Fork / successor | `session_start.parent` (+ `forkedAtMessageId` for forks) | recordings (one fork, one successor) |

## Files

- `~/.factory/sessions/<slug>/<session id>.jsonl` (Windows `%USERPROFILE%\.factory\sessions`), the slug being the
  working directory with separators replaced by `-` (`D:\git\my-project` → `-D-git-my-project`). The session id is a
  UUID and equals the file stem and `session_start.id` in every recording. Factory Desktop writes the same tree.
- `<session id>.settings.json`: the sidecar; `<session id>.settings.json.bak`: the previous copy droid keeps (often
  identical). Other files in `~/.factory` (`settings.json` with BYOK API keys in plain text, `config.json`,
  `history.json`, `logs/`, `sessions-index.json` or `cache/session-discovery-index.json`, `auth.*`) are not needed and
  must not be read.
- No documented environment variable moves the folder. Third parties found `FACTORY_HOME_OVERRIDE` in the binary;
  most treat it as a replacement home with `.factory` under it, one as the `.factory` folder itself. Not used.

## Transcript (`session_start.version` 2 in every recording)

- `session_start`: `id`, `title`, `owner`, `version`, `cwd`, `hostId`, optional `organizationId`,
  `isSessionTitleManuallySet`, `sessionTitleAutoStage`; forks add `parent` and `forkedAtMessageId`; the successor of
  a `/compress` (0.226) has `parent` only; Task-tool workers have `callingSessionId`, `callingToolUseId` and a title
  `"<type>: <description>"`.
- `message`: `id`, `timestamp`, `parentId`, `message{role, content[], …}`. User lines carry `visibility`: absent (the
  user's text, tool results), `llm_only` (the per-turn context, id `context-<turn id>`, written just before the
  prompt), `user_only` (notices and hook records, never sent to the model). Assistant lines carry `modelId`,
  `reasoningEffort` and provider extras (`openaiEncryptedContent`, `chatCompletionReasoningContent`, …).
- Content blocks: `text`, `thinking {thinking, signature, signatureProvider, durationMs}`, `image`,
  `tool_use {id, name, input}` in assistant messages and `tool_result {tool_use_id, content (string), is_error}` in
  the next user message — Anthropic's shape for every provider. Ids `toolu_…` or `call_…`. Tool names seen: Read,
  LS, Glob, Grep, Execute, Create, Edit, MultiEdit, TodoWrite, Task, Skill, WebSearch, FetchUrl, AskUser,
  GenerateDroid, ExitSpecMode.
- `agent_turn_outcome {turnId, reason (completed, error, model_usage_exhausted), resultKind}`: no timestamp.
- `compaction_state {summaryText, summaryTokens, summaryKind, anchorMessage?, removedCount, …, timestamp}`: an
  in-place summary (auto-compaction, and `/compress` since 0.228: "/compress now summarizes your current session,
  and /handoff starts a summary-only successor").
- `todo_state {todos, messageIndex, timestamp}`.
- Timing check on three recordings: each assistant message follows the user line before it by the call's duration
  (1.5–16 s), and tool runs sit between an assistant `tool_use` message and the next user line.

## Sidecar

`model` (`auto` for Factory's router, then `effectiveFactoryRouterModel.modelId`), `reasoningEffort`,
`interactionMode`, `autonomyLevel`, `autonomyMode`, `specModeModel`, `providerLock` (the API dialect: `anthropic`,
`openai`, `generic-chat-completion-api`) and `providerLockTimestamp`, `assistantActiveTimeMs`, `archivedAt`,
`tokenUsage {inputTokens, outputTokens, cacheCreationTokens, cacheReadTokens, thinkingTokens, factoryCredits}`,
`inclusiveTokenUsage` (own + children), `childInclusiveTokenUsageBySessionId {child id: token block}`,
`lastCallTokenUsage {inputTokens, outputTokens, cacheReadTokens}`.

- Own calls only: a Task worker keeps its own block; `inclusiveTokenUsage` adds the workers (entireio, observer;
  changelog 0.123: "Mission Control token counts now include subagent tokens").
- `inputTokens` excludes cache reads: in the issue's sample cache reads (282 368) exceed input (53 858); in a BYOK
  recording the last call's `inputTokens` 3 131 + `cacheReadTokens` 23 040 make up the prompt.
- `outputTokens` includes reasoning and `thinkingTokens` is a breakdown, often estimated from text length
  (entireio's reading of droid's provider adapters; recordings show thinking 80 of output 203, 92 of 327).
  tokscale, ccusage and codeburn add thinking to output instead; Agent Speed follows the provider semantics.
- `factoryCredits` is Factory's billing unit ("Factory Standard Credits", model multipliers), not tokens; 0 for BYOK.
- `assistantActiveTimeMs` is the summed wall time of the session's turns, tool runs included: 13 974 ms against
  12.5 s of model calls plus 0.85 s of tools in one recording, 75 380 ms against a 75.23 s turn in another.
- History of the counters (changelog): 0.123 "Factory token usage is now persisted to session settings"; 0.168
  "Session token usage is now counted only on successful turns"; 0.247 "Usage totals now count every billed model
  attempt, including ones that were retried or refused".
- Inherited counters: in the 0.226 recording the `/compress` successor's counters started at its parent's (parent
  60/37 input/output, successor 70/42 after one 10/5 mock call, its `.bak` 60/37), while `assistantActiveTimeMs`
  restarted. Forks: unknown (the one recorded fork's parent had zero usage). Since 0.196 "Forking a session now keeps
  you in the original session", so a fork's parent can keep growing.
- When droid rewrites the sidecar (every call or every turn) is not documented; the issue says its modification
  time follows the transcript's.

## Models and effort

- Official ids as Factory lists them (`claude-opus-5-5`, `claude-sonnet-4-5-20250929`, `gpt-5.4`, `glm-5.3`,
  `kimi-k3`, …; retired ids such as `gpt-5-codex` stay in old logs); `-fast` variants are ids of their own.
- BYOK: `custom:<display name with whitespace runs as '-'>-<n>`, `<n>` counting entries of the same name from 0
  (droid 0.99 fixed ids changing between sessions). A bracket part is the user's own label (Factory's examples
  "Sonnet 4.5 [Bedrock]", "GPT-OSS 120b [Groq]"). Recorded: `custom:GPT-5.4-Mini-[OpenAI-BYOK]-0`,
  `custom:superbased-0`; in readers' fixtures also `custom:Kimi-K3-(free)-0`. A `customModels[].id` the user sets
  is used as is.
- `reasoningEffort`: `none`, `dynamic`, `off`, `minimal`, `low`, `medium`, `high`, `xhigh`, `max`, per model; an
  unsupported choice silently becomes `off` (Factory issue #4), so the value on the assistant message is the one used.

## Processes

Native single binary built with Bun (changelog 0.27.1 "Upgraded to bun 1.3.3"): `droid` / `droid.exe`. Installers:
`~/.local/bin/droid` (curl script), `%USERPROFILE%\bin\droid.exe` (PowerShell script), Homebrew cask `droid`, npm
`droid` with platform packages `@factory/cli-<os>-<arch>[-baseline]` (on Windows a Node shim starts `droid.exe`). The
scripts stop old copies with `pkill -x droid` / `Get-Process -Name droid`. The TUI starts a child
`droid exec --input-format stream-jsonrpc --output-format stream-jsonrpc`; Factory Desktop runs
`droid daemon --enable-child-ipc` and `droid exec` children from its bundle (`…\resources\bin\droid.exe`). Not
verified with a running droid.

## Open questions (to check with a real session)

1. Does a fork's or a `/handoff` successor's `tokenUsage` start at its parent's counters?
2. Is the sidecar rewritten after every call or only at the end of a turn, and before or after the transcript line?
3. Do Factory-routed (non-BYOK) Anthropic sessions also store `inputTokens` without cache reads?
