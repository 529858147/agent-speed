#!/usr/bin/env python3
"""Generates the shared parser and engine fixtures in spec/fixtures.

Usage: python3 spec/tools/generate_fixtures.py
"""
import json
import math
import os
import subprocess
import sys
from datetime import datetime, timezone

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gemini_fixtures  # noqa: E402

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures"))
T0 = int(datetime(2026, 10, 20, 10, 0, 0, tzinfo=timezone.utc).timestamp() * 1000)


def ts(ms):
    return datetime.fromtimestamp(ms / 1000, tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.") + f"{ms % 1000:03d}Z"


def at(minutes, seconds, millis=0):
    return T0 + minutes * 60_000 + seconds * 1000 + millis


def write_jsonl(rel, rows):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for row in rows:
            f.write(row if isinstance(row, str) else json.dumps(row, ensure_ascii=False, separators=(",", ":")))
            f.write("\n")


def codex(ms, kind, payload):
    return {"timestamp": ts(ms), "type": kind, "payload": payload}


def cx(i, c, o, r, cw=None):
    u = {"input_tokens": i, "cached_input_tokens": c, "output_tokens": o,
         "reasoning_output_tokens": r, "total_tokens": i + o}
    if cw is not None:
        u["cache_write_input_tokens"] = cw
    return u


def resp(key, start, end, inp, cached, cw, out, reasoning, model, final=True):
    return {"key": key, "startMs": start, "endMs": end, "input": inp, "cachedInput": cached,
            "cacheWriteInput": cw, "output": out, "reasoning": reasoning, "model": model, "final": final}


files = {}
order = []


def expect(rel, **kw):
    order.append(rel)
    files[rel] = kw


# --- Codex, modern format -----------------------------------------------------
rel = "codex/sessions/2026/10/20/rollout-2026-10-20T10-00-00-modern.jsonl"
u1 = cx(10000, 8000, 200, 50, 0)
u2 = cx(10400, 10000, 330, 0, 0)
cum2 = cx(20400, 18000, 530, 50, 0)
write_jsonl(rel, [
    codex(at(0, 0), "session_meta", {"id": "thread-main", "session_id": "sess-1", "timestamp": ts(at(0, 0)),
                                     "cwd": "/Users/dev/projects/rocket", "originator": "codex_cli_rs",
                                     "cli_version": "0.163.0", "source": "cli"}),
    codex(at(0, 0, 100), "turn_context", {"cwd": "/Users/dev/projects/rocket", "model": "gpt-6.1-sol"}),
    codex(at(0, 1), "event_msg", {"type": "task_started", "turn_id": "turn-1"}),
    codex(at(0, 1), "event_msg", {"type": "user_message", "message": "Ship it", "images": []}),
    codex(at(0, 1, 10), "response_item", {"type": "message", "role": "user",
                                          "content": [{"type": "input_text", "text": "Ship it"}]}),
    codex(at(0, 3), "response_item", {"type": "reasoning", "summary": [], "encrypted_content": "x"}),
    codex(at(0, 5), "response_item", {"type": "function_call", "name": "shell", "arguments": "{}",
                                      "call_id": "call-1"}),
    codex(at(0, 5, 10), "token_usage_record", {"thread_id": "thread-main", "session_id": "sess-1",
                                               "response_id": "resp-1", "usage": u1,
                                               "turn_token_usage": u1, "thread_token_usage": u1}),
    codex(at(0, 5, 12), "event_msg", {"type": "token_count",
                                      "info": {"total_token_usage": u1, "last_token_usage": u1}}),
    codex(at(0, 7, 500), "response_item", {"type": "function_call_output", "call_id": "call-1", "output": "ok"}),
    codex(at(0, 9), "response_item", {"type": "message", "role": "assistant",
                                      "content": [{"type": "output_text", "text": "Done"}]}),
    codex(at(0, 10, 500), "token_usage_record", {"thread_id": "thread-main", "session_id": "sess-1",
                                                 "response_id": "resp-2", "usage": u2,
                                                 "turn_token_usage": u2, "thread_token_usage": cum2}),
    codex(at(0, 10, 502), "event_msg", {"type": "token_count",
                                        "info": {"total_token_usage": cum2, "last_token_usage": u2}}),
    codex(at(0, 10, 600), "token_usage_record", {"thread_id": "thread-main", "session_id": "sess-1",
                                                 "response_id": "resp-2", "usage": u2}),
    "this line is not json",
    codex(at(0, 10, 700), "event_msg", {"type": "task_complete", "turn_id": "turn-1"}),
])
expect(rel, source="codex", project="rocket", model="gpt-6.1-sol", isSubagent=False, parentFile=None,
       phase="idle", inFlightStartMs=None, lastLineMs=at(0, 10, 700), malformedLines=1,
       responses=[
           resp("resp-1", at(0, 1, 10), at(0, 5, 10), 10000, 8000, 0, 200, 50, "gpt-6.1-sol"),
           resp("resp-2", at(0, 7, 500), at(0, 10, 500), 10400, 10000, 0, 330, 0, "gpt-6.1-sol"),
       ])

# --- Codex, sub-agent with copied parent history ------------------------------
rel = "codex/sessions/2026/10/20/rollout-2026-10-20T10-00-20-subagent.jsonl"
u3 = cx(5000, 0, 120, 40)
write_jsonl(rel, [
    codex(at(0, 20), "session_meta", {"id": "thread-worker", "session_id": "sess-1", "timestamp": ts(at(0, 20)),
                                      "forked_from_id": "thread-main", "parent_thread_id": "thread-main",
                                      "cwd": "/Users/dev/projects/rocket", "thread_source": "subagent",
                                      "agent_path": "/root/worker",
                                      "source": {"subagent": {"thread_spawn": {"parent_thread_id": "thread-main",
                                                                               "depth": 1,
                                                                               "agent_path": "/root/worker"}}}}),
    codex(at(0, 20), "session_meta", {"id": "thread-main", "session_id": "sess-1", "timestamp": ts(at(0, 0)),
                                      "cwd": "/Users/dev/projects/rocket", "source": "cli"}),
    codex(at(0, 1, 10), "response_item", {"type": "message", "role": "user",
                                          "content": [{"type": "input_text", "text": "Ship it"}]}),
    codex(at(0, 5, 10), "token_usage_record", {"thread_id": "thread-main", "session_id": "sess-1",
                                               "response_id": "resp-1", "usage": u1}),
    codex(at(0, 7, 0), "token_usage_record", {"thread_id": "thread-main", "session_id": "sess-1",
                                              "response_id": "resp-old-copy", "usage": u1}),
    codex(at(0, 20, 100), "turn_context", {"cwd": "/Users/dev/projects/rocket", "model": "gpt-6.1-sol"}),
    codex(at(0, 20, 500), "response_item", {"type": "agent_message", "author": "/root/worker",
                                            "recipient": "/root", "content": []}),
    codex(at(0, 21), "response_item", {"type": "agent_message", "author": "/root", "recipient": "/root/worker",
                                       "content": [{"type": "input_text", "text": "Task"}]}),
    codex(at(0, 22), "response_item", {"type": "reasoning", "summary": []}),
    codex(at(0, 23, 900), "response_item", {"type": "custom_tool_call", "name": "exec", "input": "ls",
                                            "call_id": "call-9"}),
    codex(at(0, 24), "token_usage_record", {"thread_id": "thread-worker", "session_id": "sess-1",
                                            "response_id": "resp-3", "usage": u3}),
    codex(at(0, 30), "response_item", {"type": "custom_tool_call_output", "call_id": "call-9", "output": "a b"}),
    codex(at(0, 31), "response_item", {"type": "reasoning", "summary": []}),
    codex(at(0, 32), "event_msg", {"type": "token_count", "info": {"total_token_usage": u3, "last_token_usage": u3}}),
    codex(at(0, 40), "response_item", {"type": "agent_message", "author": "/root", "recipient": "/root/worker",
                                       "content": [{"type": "input_text", "text": "Next"}]}),
    codex(at(0, 45), "response_item", {"type": "reasoning", "summary": []}),
    codex(at(0, 47), "token_usage_record", {"thread_id": "thread-worker", "session_id": "sess-1",
                                            "response_id": "resp-4", "usage": cx(5200, 5000, 60, 10)}),
])
expect(rel, source="codex", project="rocket", model="gpt-6.1-sol", isSubagent=True,
       parentFile="codex/sessions/2026/10/20/rollout-2026-10-20T10-00-00-modern.jsonl", phase="tools", inFlightStartMs=None, lastLineMs=at(0, 47), malformedLines=0,
       responses=[resp("resp-3", at(0, 21), at(0, 24), 5000, 0, 0, 120, 40, "gpt-6.1-sol"),
                  resp("resp-4", at(0, 40), at(0, 47), 5200, 5000, 0, 60, 10, "gpt-6.1-sol")])

# --- Codex, legacy token_count only -------------------------------------------
rel = "codex/sessions/2026/10/20/rollout-2026-10-20T10-01-00-legacy.jsonl"
tA = cx(19515, 7040, 698, 368)
tB = cx(40000, 19000, 1000, 400)
lastB = cx(20485, 11960, 302, 32)
write_jsonl(rel, [
    codex(at(1, 0), "session_meta", {"id": "thread-legacy", "timestamp": ts(at(1, 0)),
                                     "cwd": "/Users/dev/projects/legacy-app", "originator": "codex_vscode"}),
    codex(at(1, 0, 100), "turn_context", {"cwd": "/Users/dev/projects/legacy-app", "model": "gpt-5.5"}),
    codex(at(1, 0, 500), "event_msg", {"type": "task_started", "turn_id": "t-1"}),
    codex(at(1, 0, 500), "event_msg", {"type": "user_message", "message": "hi"}),
    codex(at(1, 0, 600), "event_msg", {"type": "token_count", "info": None}),
    codex(at(1, 2), "response_item", {"type": "reasoning", "summary": []}),
    codex(at(1, 4), "event_msg", {"type": "token_count", "info": {"total_token_usage": tA, "last_token_usage": tA}}),
    codex(at(1, 4, 1), "event_msg", {"type": "token_count", "info": {"total_token_usage": tA, "last_token_usage": tA}}),
    codex(at(1, 6), "response_item", {"type": "function_call_output", "call_id": "c", "output": "x"}),
    codex(at(1, 9), "event_msg", {"type": "token_count", "info": {"total_token_usage": tB, "last_token_usage": lastB}}),
    codex(at(1, 9, 100), "event_msg", {"type": "task_complete", "turn_id": "t-1"}),
])
expect(rel, source="codex", project="legacy-app", model="gpt-5.5", isSubagent=False, parentFile=None,
       phase="idle", inFlightStartMs=None, lastLineMs=at(1, 9, 100), malformedLines=0,
       responses=[
           resp("codex-legacy:thread-legacy:20213", at(1, 0, 500), at(1, 4), 19515, 7040, 0, 698, 368, "gpt-5.5"),
           resp("codex-legacy:thread-legacy:41000", at(1, 6), at(1, 9), 20485, 11960, 0, 302, 32, "gpt-5.5"),
       ])


# --- Codex, compressed session (.jsonl.zst) -------------------------------------
# Two zstd frames split in the middle of a line, with a skippable frame between them.
rel = "codex/sessions/2026/10/20/rollout-2026-10-20T10-01-20-compressed.jsonl.zst"
z1 = cx(6000, 5000, 150, 40, 0)
z2 = cx(6300, 6000, 210, 0, 0)
zcum = cx(12300, 11000, 360, 40, 0)
zrows = [
    codex(at(1, 20), "session_meta", {"id": "thread-zst", "session_id": "sess-z", "timestamp": ts(at(1, 20)),
                                      "cwd": "/Users/dev/projects/comet", "originator": "codex_cli_rs",
                                      "cli_version": "0.170.0", "source": "cli"}),
    codex(at(1, 20, 100), "turn_context", {"cwd": "/Users/dev/projects/comet", "model": "gpt-6.1-sol-mini"}),
    codex(at(1, 21), "event_msg", {"type": "task_started", "turn_id": "turn-z"}),
    codex(at(1, 21), "event_msg", {"type": "user_message", "message": "Tidy up", "images": []}),
    codex(at(1, 21, 10), "response_item", {"type": "message", "role": "user",
                                           "content": [{"type": "input_text", "text": "Tidy up"}]}),
    codex(at(1, 24), "response_item", {"type": "function_call", "name": "shell", "arguments": "{}",
                                       "call_id": "call-z"}),
    codex(at(1, 24, 10), "token_usage_record", {"thread_id": "thread-zst", "session_id": "sess-z",
                                                "response_id": "resp-z1", "usage": z1,
                                                "turn_token_usage": z1, "thread_token_usage": z1}),
    codex(at(1, 24, 12), "event_msg", {"type": "token_count", "info": {"total_token_usage": z1, "last_token_usage": z1}}),
    codex(at(1, 25, 500), "response_item", {"type": "function_call_output", "call_id": "call-z", "output": "ok"}),
    codex(at(1, 27), "response_item", {"type": "message", "role": "assistant",
                                       "content": [{"type": "output_text", "text": "Tidied"}]}),
    codex(at(1, 28, 500), "token_usage_record", {"thread_id": "thread-zst", "session_id": "sess-z",
                                                 "response_id": "resp-z2", "usage": z2,
                                                 "turn_token_usage": z2, "thread_token_usage": zcum}),
    codex(at(1, 28, 502), "event_msg", {"type": "token_count", "info": {"total_token_usage": zcum, "last_token_usage": z2}}),
    codex(at(1, 28, 700), "event_msg", {"type": "task_complete", "turn_id": "turn-z"}),
]
ztext = "".join(json.dumps(r, ensure_ascii=False, separators=(",", ":")) + "\n" for r in zrows).encode("utf-8")
zsplit = ztext.index(b"resp-z1")
zpath = os.path.join(ROOT, rel)
os.makedirs(os.path.dirname(zpath), exist_ok=True)
with open(zpath, "wb") as f:
    f.write(subprocess.run(["zstd", "-q", "-c"], input=ztext[:zsplit], check=True, capture_output=True).stdout)
    f.write((0x184D2A50).to_bytes(4, "little") + (4).to_bytes(4, "little") + b"skip")
    f.write(subprocess.run(["zstd", "-q", "-c"], input=ztext[zsplit:], check=True, capture_output=True).stdout)
expect(rel, source="codex", project="comet", model="gpt-6.1-sol-mini", isSubagent=False, parentFile=None,
       phase="idle", inFlightStartMs=None, lastLineMs=at(1, 28, 700), malformedLines=0,
       responses=[
           resp("resp-z1", at(1, 21, 10), at(1, 24, 10), 6000, 5000, 0, 150, 40, "gpt-6.1-sol-mini"),
           resp("resp-z2", at(1, 25, 500), at(1, 28, 500), 6300, 6000, 0, 210, 0, "gpt-6.1-sol-mini"),
       ])

# --- Claude Code CLI ----------------------------------------------------------
def claude(kind, ms, entry="cli", cwd="/Users/dev/projects/nova", session="s-cli", sidechain=False, **extra):
    row = {"parentUuid": None, "isSidechain": sidechain, "userType": "external", "cwd": cwd,
           "sessionId": session, "version": "2.1.300", "entrypoint": entry, "type": kind}
    row.update(extra)
    row["timestamp"] = ts(ms)
    return row


def cl_usage(i, cc, cr, o, thinking=None):
    u = {"input_tokens": i, "cache_creation_input_tokens": cc, "cache_read_input_tokens": cr, "output_tokens": o,
         "service_tier": "standard"}
    if thinking is not None:
        u["output_tokens_details"] = {"thinking_tokens": thinking}
    return u


def assistant(ms, mid, content, usage, stop, model="claude-opus-5-5", **kw):
    return claude("assistant", ms, requestId="req_" + mid, uuid="uuid-" + mid + str(ms),
                  message={"id": mid, "type": "message", "role": "assistant", "model": model,
                           "content": [{"type": c} for c in content], "stop_reason": stop, "usage": usage}, **kw)


def user(ms, content, **kw):
    return claude("user", ms, uuid="u" + str(ms), message={"role": "user", "content": content}, **kw)


rel = "claude/projects/-Users-dev-projects-nova/s-cli.jsonl"
write_jsonl(rel, [
    {"type": "permission-mode", "permissionMode": "default", "sessionId": "s-cli"},
    user(at(2, 0), "Build the launch pad"),
    claude("api-request", at(2, 0, 50), id="api-1", params={"model": "claude-opus-5-5"}),
    claude("attachment", at(2, 0, 60), attachment={"type": "todo"}),
    assistant(at(2, 4), "msg_A", ["thinking"], cl_usage(2, 1000, 20000, 150, 60), None),
    assistant(at(2, 6, 50), "msg_A", ["tool_use"], cl_usage(2, 1000, 20000, 300, 60), "tool_use"),
    user(at(2, 9), [{"type": "tool_result", "tool_use_id": "t1", "content": "ok"}]),
    assistant(at(2, 12), "msg_B", ["text"], cl_usage(5, 200, 21000, 240), "end_turn"),
    assistant(at(2, 12, 500), "msg_SYN", ["text"], cl_usage(0, 0, 0, 0), "stop_sequence", model="<synthetic>"),
    claude("system", at(2, 13), subtype="turn_duration"),
    user(at(2, 20), "Now polish it"),
    user(at(2, 25), [{"type": "text", "text": "[Request interrupted by user]"}]),
])
expect(rel, source="claudeCode", project="nova", model="claude-opus-5-5", isSubagent=False, parentFile=None,
       phase="idle", inFlightStartMs=None, lastLineMs=at(2, 25), malformedLines=0,
       responses=[
           resp("msg_A", at(2, 0, 50), at(2, 6, 50), 21002, 20000, 1000, 300, 60, "claude-opus-5-5"),
           resp("msg_B", at(2, 9), at(2, 12), 21205, 21000, 200, 240, 0, "claude-opus-5-5"),
       ])

# --- Claude Code sub-agent ----------------------------------------------------
rel = "claude/projects/-Users-dev-projects-nova/s-cli/subagents/agent-abc.jsonl"
write_jsonl(rel, [
    user(at(2, 7), "Search the codebase", sidechain=True, agentId="abc"),
    assistant(at(2, 10), "msg_D", ["tool_use"], cl_usage(4, 500, 0, 90), "tool_use", sidechain=True, agentId="abc"),
    user(at(2, 11), [{"type": "tool_result", "tool_use_id": "t2", "content": "found"}], sidechain=True,
         agentId="abc"),
    assistant(at(2, 14), "msg_E", ["thinking"], cl_usage(2, 0, 600, 8), None, sidechain=True, agentId="abc"),
    assistant(at(2, 15), "msg_E", ["tool_use"], cl_usage(2, 0, 600, 8), None, sidechain=True, agentId="abc"),
    user(at(2, 16), [{"type": "tool_result", "tool_use_id": "t3", "content": "done"}], sidechain=True,
         agentId="abc"),
])
expect(rel, source="claudeCode", project="nova", model="claude-opus-5-5", isSubagent=True,
       parentFile="claude/projects/-Users-dev-projects-nova/s-cli.jsonl",
       phase="generating", inFlightStartMs=at(2, 16), lastLineMs=at(2, 16), malformedLines=0,
       responses=[resp("msg_D", at(2, 7), at(2, 10), 504, 0, 500, 90, 0, "claude-opus-5-5"),
                  resp("msg_E", at(2, 11), at(2, 15), 602, 600, 0, 8, 0, "claude-opus-5-5", final=False)])

# --- Claude Desktop, resumed session that copies msg_B ------------------------
rel = "claude/projects/-Users-dev-projects-orbit/s-desk.jsonl"
d = dict(entry="claude-desktop", cwd="/Users/dev/projects/orbit", session="s-desk")
write_jsonl(rel, [
    {"type": "custom-title", "customTitle": "Orbit", "sessionId": "s-desk"},
    user(at(2, 9), [{"type": "tool_result", "tool_use_id": "t1", "content": "ok"}],
         **dict(d, entry="claude-desktop-3p")),
    assistant(at(2, 12), "msg_B", ["text"], cl_usage(5, 200, 21000, 240), "end_turn", **d),
    user(at(3, 0), "Plan the orbit", **d),
    assistant(at(3, 5), "msg_C", ["tool_use"], cl_usage(3, 0, 30000, 500), "tool_use", **d),
])
expect(rel, source="claudeDesktop", project="orbit", model="claude-opus-5-5", isSubagent=False, parentFile=None,
       phase="tools", inFlightStartMs=None, lastLineMs=at(3, 5), malformedLines=0,
       responses=[resp("msg_C", at(3, 0), at(3, 5), 30003, 30000, 0, 500, 0, "claude-opus-5-5")])


# --- DeepSeek Harness (dsh): zstd frame streams ------------------------------


def write_zstd(rel, frames):
    path = os.path.join(ROOT, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        for rows in frames:
            text = "".join((r if isinstance(r, str) else json.dumps(r, ensure_ascii=False, separators=(",", ":"))) + "\n"
                           for r in rows)
            f.write(subprocess.run(["zstd", "-q", "-c", "--no-content-size"], input=text.encode("utf-8"),
                                   check=True, capture_output=True).stdout)


def dsh(kind, seq, ms, data, **extra):
    row = {"type": kind, "seq": seq, "time": ms, "data": data}
    row.update(extra)
    return row


def dsh_usage(i, o, cr, cw):
    return {"inputTokens": i, "outputTokens": o, "cacheReadTokens": cr, "cacheWriteTokens": cw,
            "totalTokens": i + o + cr}


def dsh_message(seq, ms, mid, blocks, model, usage):
    return dsh("assistant/message", seq, ms, {
        "turn": 1, "step": seq,
        "message": {"role": "assistant", "content": [{"type": b} for b in blocks],
                    "source": {"kind": "model", "provider": "deepseek-account", "model": model}, "id": mid},
        "usage": usage, "stream": [{"type": "chunk", "time": ms - 100, "chunk": {"type": "usage", "usage": usage}}]},
        surfaceOp="append")


rel = "dsh/sessions/--Users-dev-projects-quasar--/session-dsh-main/session.v4.jsonl.zstd"
write_zstd(rel, [
    [{"type": "session", "version": 4, "id": "session-dsh-main", "createdAt": at(4, 0),
      "cwd": "/Users/dev/projects/quasar", "isSeeded": False, "delegationDepth": 0, "agentPreset": "standard"}],
    [dsh("permission/preset", 1, at(4, 0, 10), {"preset": "default"}), dsh("turn/start", 2, at(4, 1), {"turn": 1})],
    [dsh("step/start", 3, at(4, 1), {"turn": 1, "step": 1}),
     dsh("user/message", 4, at(4, 1, 5), {"content": [{"type": "text", "text": "Fix the build"}], "role": "user"}),
     dsh("request/header", 5, at(4, 1, 10), {"header": {"config": {"provider": "deepseek-account",
                                                                    "model": "deepseek-flash"}}})],
    [dsh("session-log-deepseek/delivery-accepted", 6, at(4, 1, 400), {"throughSeq": 5})],
    [dsh_message(7, at(4, 5), "dsm-1", ["reasoning", "tool-call"], "deepseek-flash", dsh_usage(9000, 400, 0, 0))],
    [dsh("tool/call", 8, at(4, 5, 2), {"callId": "c1", "name": "bash"}),
     dsh("tool/result", 9, at(4, 6), {"message": {"role": "tool", "toolCallId": "c1"}}),
     dsh("step/end", 10, at(4, 6), {"turn": 1, "step": 1})],
    [dsh("step/start", 11, at(4, 6, 10), {"turn": 1, "step": 2})],
    [dsh_message(12, at(4, 8, 10), "dsm-2", ["text"], "deepseek-flash", dsh_usage(120, 300, 9216, 64)),
     dsh_message(13, at(4, 8, 20), "dsm-2", ["text"], "deepseek-flash", dsh_usage(120, 300, 9216, 64))],
    [dsh("step/end", 14, at(4, 8, 25), {"turn": 1, "step": 2}),
     dsh("turn/end", 15, at(4, 8, 30), {"turn": 1, "reason": {"kind": "completed"}}),
     "this line is not json"],
])
expect(rel, source="deepseek", project="quasar", model="deepseek-flash", isSubagent=False, parentFile=None,
       phase="idle", inFlightStartMs=None, lastLineMs=at(4, 8, 30), malformedLines=1,
       responses=[
           resp("dsm-1", at(4, 1), at(4, 5), 9000, 0, 0, 400, 0, "deepseek-flash"),
           resp("dsm-2", at(4, 6, 10), at(4, 8, 10), 9400, 9216, 64, 300, 0, "deepseek-flash"),
       ])

rel = "dsh/sessions/--Users-dev-projects-quasar--/session-dsh-sub/session.v4.jsonl.zstd"
write_zstd(rel, [
    [{"type": "session", "version": 4, "id": "session-dsh-sub", "createdAt": at(4, 10),
      "cwd": "/Users/dev/projects/quasar", "parentSession": "session-dsh-main", "isSeeded": True,
      "origin": "subagent", "delegationDepth": 1, "agentPreset": "explore"}],
    [dsh("step/start", 3, at(4, 1), {"turn": 1, "step": 1}),
     dsh_message(7, at(4, 5), "dsm-1", ["reasoning", "tool-call"], "deepseek-flash", dsh_usage(9000, 400, 0, 0))],
    [dsh("step/start", 20, at(4, 11), {"turn": 1, "step": 1}),
     dsh("request/header", 21, at(4, 11, 5), {"header": {"config": {"model": "deepseek-pro"}}})],
    [dsh_message(22, at(4, 14), "dss-1", ["tool-call"], "deepseek-pro", dsh_usage(500, 240, 4000, 0))],
    [dsh("tool/call", 23, at(4, 14, 5), {"callId": "c9", "name": "grep"}),
     dsh("tool/result", 24, at(4, 15), {"message": {"role": "tool", "toolCallId": "c9"}})],
    [dsh("step/start", 25, at(4, 15, 10), {"turn": 1, "step": 2})],
])
expect(rel, source="deepseek", project="quasar", model="deepseek-pro", isSubagent=True,
       parentFile="dsh/sessions/--Users-dev-projects-quasar--/session-dsh-main/session.v4.jsonl.zstd",
       phase="generating", inFlightStartMs=at(4, 15, 10), lastLineMs=at(4, 15, 10), malformedLines=0,
       responses=[resp("dss-1", at(4, 11), at(4, 14), 4500, 4000, 0, 240, 0, "deepseek-pro")])


# --- OpenCode: SQLite database -------------------------------------------------
import sqlite3

opencode_rel = "opencode/opencode.db"
oc_path = os.path.join(ROOT, opencode_rel)
os.makedirs(os.path.dirname(oc_path), exist_ok=True)
if os.path.exists(oc_path):
    os.remove(oc_path)
oc = sqlite3.connect(oc_path)
oc.executescript("""
CREATE TABLE session_v2 (id text PRIMARY KEY, project_id text NOT NULL, parent_id text, slug text NOT NULL,
  directory text NOT NULL, version text NOT NULL, time_created integer NOT NULL, time_updated integer NOT NULL);
CREATE TABLE session_message (id text PRIMARY KEY, session_id text NOT NULL, type text NOT NULL, seq integer NOT NULL,
  time_created integer NOT NULL, time_updated integer NOT NULL, data text NOT NULL);
CREATE INDEX session_message_time_created_idx ON session_message (time_created);
""")


def oc_tokens(i, o, r, cr, cw):
    return {"input": i, "output": o, "reasoning": r, "cache": {"read": cr, "write": cw}}


def oc_row(rid, sid, kind, seq, created, updated, data):
    oc.execute("INSERT INTO session_message VALUES (?,?,?,?,?,?,?)",
               (rid, sid, kind, seq, created, updated, json.dumps(data, separators=(",", ":"))))


oc.execute("INSERT INTO session_v2 VALUES (?,?,?,?,?,?,?,?)",
           ("ses_main", "prj", None, "main", "/Users/dev/projects/nebula", "2.0.32", at(5, 0), at(5, 48)))
oc.execute("INSERT INTO session_v2 VALUES (?,?,?,?,?,?,?,?)",
           ("ses_sub", "prj", "ses_main", "sub", "/Users/dev/projects/nebula", "2.0.32", at(5, 20), at(5, 30)))
opus = {"id": "claude-opus-5-5", "providerID": "anthropic"}
sonnet = {"id": "claude-sonnet-5-5", "providerID": "anthropic"}
oc_row("u1", "ses_main", "user", 1, at(5, 1), at(5, 1), {"text": "Plan it", "time": {"created": at(5, 1)}})
oc_row("msg_oc1", "ses_main", "assistant", 2, at(5, 1, 10), at(5, 5, 20),
       {"time": {"created": at(5, 1, 10), "streamed": at(5, 5, 10), "completed": at(5, 5, 20)}, "model": opus,
        "finish": "tool-calls", "tokens": oc_tokens(4, 300, 100, 1000, 200), "content": [{"type": "tool"}]})
oc_row("msg_oc2", "ses_main", "assistant", 3, at(5, 40), at(5, 42, 10),
       {"time": {"created": at(5, 40), "streamed": at(5, 42), "completed": at(5, 42, 10)}, "model": opus,
        "finish": "stop", "tokens": oc_tokens(2, 150, 0, 1200, 0), "content": [{"type": "text"}]})
oc_row("cmp_1", "ses_main", "compaction", 4, at(5, 43), at(5, 47),
       {"time": {"created": at(5, 43), "completed": at(5, 47)}, "model": {"id": "claude-haiku-5"},
        "tokens": oc_tokens(5000, 800, 0, 0, 0), "status": "done"})
oc_row("idle1", "ses_main", "idle", 5, at(5, 48), at(5, 48), {"outcome": "completed", "time": {"created": at(5, 48)}})
oc_row("msg_copy", "ses_sub", "assistant", 1, at(5, 20), at(5, 20),
       {"time": {"created": at(5, 1, 10), "streamed": at(5, 5, 10), "completed": at(5, 5, 20)}, "model": opus,
        "finish": "tool-calls", "tokens": oc_tokens(4, 300, 100, 1000, 200)})
oc_row("msg_oc3", "ses_sub", "assistant", 2, at(5, 21), at(5, 24, 5),
       {"time": {"created": at(5, 21), "streamed": at(5, 24), "completed": at(5, 24, 5)}, "model": sonnet,
        "finish": "tool-calls", "tokens": oc_tokens(3, 90, 30, 0, 500)})
oc_row("msg_oc4", "ses_sub", "assistant", 3, at(5, 30), at(5, 30),
       {"time": {"created": at(5, 30)}, "model": sonnet, "content": []})
oc.commit()
oc.close()
opencode_expected = {
    "opencode:ses_main": dict(source="opencode", project="nebula", model="claude-opus-5-5", isSubagent=False,
                              parentId=None, phase="idle", inFlightStartMs=None, lastLineMs=at(5, 48),
                              responses=[
                                  resp("msg_oc1", at(5, 1, 10), at(5, 5, 10), 1204, 1000, 200, 400, 100,
                                       "claude-opus-5-5"),
                                  resp("msg_oc2", at(5, 40), at(5, 42), 1202, 1200, 0, 150, 0, "claude-opus-5-5"),
                                  resp("cmp_1", at(5, 43), at(5, 47), 5000, 0, 0, 800, 0, "claude-haiku-5"),
                              ]),
    "opencode:ses_sub": dict(source="opencode", project="nebula", model="claude-sonnet-5-5", isSubagent=True,
                             parentId="opencode:ses_main", phase="generating", inFlightStartMs=at(5, 30),
                             lastLineMs=at(5, 30),
                             responses=[resp("msg_oc3", at(5, 21), at(5, 24), 503, 0, 500, 120, 30,
                                             "claude-sonnet-5-5")]),
}

# --- Gemini CLI -----------------------------------------------------------------
# Modification times: git does not keep them, so tests that depend on startup order (history) apply these first.
mtimes = {}
for rel in order:
    when = int(math.ceil((files[rel]["lastLineMs"] + 1000) / 1000) * 1000)
    mtimes[rel] = max(when, max(mtimes.values(), default=0) + 1000)
    os.utime(os.path.join(ROOT, rel), (mtimes[rel] / 1000, mtimes[rel] / 1000))
gemini = gemini_fixtures.write_fixtures(ROOT)
assert min(gemini["mtimes"].values()) > max(mtimes.values())
order += gemini["order"]
files.update(gemini["files"])
mtimes.update(gemini["mtimes"])

with open(os.path.join(ROOT, "parser-expected.json"), "w", encoding="utf-8") as f:
    json.dump({"_comment": "Process files in `order` with one shared dedupe set, then the OpenCode database. Paths are "
                           "relative to this folder. `mtimes` (Unix ms) give the modification times that produce "
                           "`order`; apply them to a copy of this folder before computing daily history. Gemini "
                           "`traces` give the phase after feeding a .jsonl file up to and including physical line "
                           "`afterLine` (after the files before it in `order`).",
               "order": order, "files": files, "mtimes": mtimes,
               "opencode": {"database": opencode_rel, "agents": opencode_expected},
               "gemini": {"discover": gemini["discover"], "traces": gemini["traces"]}}, f, indent=2,
              ensure_ascii=False)
    f.write("\n")

# --- Engine cases -------------------------------------------------------------
B = 1_000_000_000_000
DAY = B - 3_600_000


def er(key, agent, source, model, start, end, out, inp=0, final=True):
    return {"key": key, "agentId": agent, "source": source, "model": model, "startMs": start, "endMs": end,
            "input": inp, "output": out, "final": final}


def agent(aid, source, model, phase="idle", in_flight=None, last=None, parent=None):
    return {"id": aid, "source": source, "model": model, "phase": phase, "inFlightStartMs": in_flight,
            "lastLineMs": last, "parentId": parent}


r1 = er("r1", "a1", "codex", "m1", B, B + 4000, 200, 10000)
cases = [
    {"name": "single completed response",
     "agents": [agent("a1", "codex", "m1", last=B + 4000)], "responses": [r1],
     "queries": [{"nowMs": B + 4000, "liveOutput": 50.0, "liveTotal": 2550.0, "estimatedShare": 0.0},
                 {"nowMs": B + 5000, "liveOutput": 200 * 2000 / 4000 / 3, "liveTotal": 1700.0, "estimatedShare": 0.0},
                 {"nowMs": B + 7000, "liveOutput": 0.0, "liveTotal": 0.0, "estimatedShare": 0.0}]},
    {"name": "in-flight estimate from the agent's recent speed",
     "agents": [agent("a1", "codex", "m1", "generating", B + 4500, B + 4500)], "responses": [r1],
     "queries": [{"nowMs": B + 6000, "liveOutput": 125 / 3, "liveTotal": 2125.0, "estimatedShare": 0.6}]},
    {"name": "stale in-flight agents are not estimated",
     "agents": [agent("a1", "codex", "m1", "generating", B + 4500, B + 4500)], "responses": [r1],
     "queries": [{"nowMs": B + 4500 + 300_001, "liveOutput": 0.0, "liveTotal": 0.0, "estimatedShare": 0.0}]},
    {"name": "parallel agents are summed",
     "agents": [agent("a1", "codex", "m1", last=B + 4000), agent("a2", "claudeCode", "m2", last=B + 3000)],
     "responses": [er("r1", "a1", "codex", "m1", B, B + 4000, 200),
                   er("r2", "a2", "claudeCode", "m2", B + 1000, B + 3000, 100)],
     "queries": [{"nowMs": B + 3000, "liveOutput": 250 / 3, "liveTotal": 250 / 3, "estimatedShare": 0.0}]},
    {"name": "durations shorter than 500 ms are widened backwards",
     "agents": [agent("a1", "codex", "m1", last=B + 1100)],
     "responses": [er("r1", "a1", "codex", "m1", B + 1000, B + 1100, 10)],
     "queries": [{"nowMs": B + 1100, "liveOutput": 10 / 3, "liveTotal": 10 / 3, "estimatedShare": 0.0},
                 {"nowMs": B + 3700, "liveOutput": 8 / 3, "liveTotal": 8 / 3, "estimatedShare": 0.0}]},
    {"name": "unknown or implausible intervals never count as speed",
     "agents": [agent("a1", "codex", "m1", last=B + 960_000)],
     "responses": [er("r1", "a1", "codex", "m1", None, B + 2000, 500),
                   er("r2", "a1", "codex", "m1", B, B + 960_000, 500)],
     "queries": [{"nowMs": B + 2000, "liveOutput": 0.0, "liveTotal": 0.0, "estimatedShare": 0.0},
                 {"nowMs": B + 960_000, "liveOutput": 0.0, "liveTotal": 0.0, "estimatedShare": 0.0}]},
    {"name": "model fallback when the agent has no history",
     "agents": [agent("a1", "codex", "m1", last=B + 4000), agent("a3", "claudeCode", "m1", "generating", B + 5000,
                                                                  B + 5000)],
     "responses": [r1],
     "queries": [{"nowMs": B + 6000, "liveOutput": 100 / 3, "liveTotal": (2550 + 2550) / 3, "estimatedShare": 0.5}]},
    {"name": "source fallback when the model is unknown",
     "agents": [agent("a1", "codex", "m1", last=B + 4000), agent("a4", "codex", "m9", "generating", B + 5000,
                                                                  B + 5000)],
     "responses": [r1],
     "queries": [{"nowMs": B + 6000, "liveOutput": 100 / 3, "liveTotal": (2550 + 2550) / 3, "estimatedShare": 0.5}]},
    {"name": "no estimate without any reference speed",
     "agents": [agent("a1", "codex", "m1", last=B + 4000), agent("a5", "claudeDesktop", "m7", "generating",
                                                                  B + 5000, B + 5000)],
     "responses": [r1],
     "queries": [{"nowMs": B + 6000, "liveOutput": 50 / 3, "liveTotal": 2550 / 3, "estimatedShare": 0.0}]},
    {"name": "recent speed uses the last three responses",
     "agents": [agent("a1", "codex", "m1", "generating", B + 7000, B + 7000)],
     "responses": [er("r1", "a1", "codex", "m1", B, B + 1000, 100),
                   er("r2", "a1", "codex", "m1", B + 2000, B + 3000, 50),
                   er("r3", "a1", "codex", "m1", B + 4000, B + 5000, 50),
                   er("r4", "a1", "codex", "m1", B + 6000, B + 7000, 50)],
     "queries": [{"nowMs": B + 9000, "liveOutput": 50.0, "liveTotal": 50.0, "estimatedShare": 100 / 150}]},
    {"name": "estimates never overlap the agent's own measured responses",
     "agents": [agent("a1", "claudeCode", "m1", "generating", B, B + 2000)],
     "responses": [er("r1", "a1", "claudeCode", "m1", B, B + 2000, 100)],
     "queries": [{"nowMs": B + 3000, "liveOutput": 50.0, "liveTotal": 50.0, "estimatedShare": 1 / 3}]},
    {"name": "unmeasured responses are estimated from a measured reference",
     "agents": [agent("a1", "claudeCode", "m1", last=B + 4000), agent("a2", "claudeCode", "m1", "tools", None, B + 3000)],
     "responses": [er("r1", "a1", "claudeCode", "m1", B, B + 4000, 200),
                   er("r2", "a2", "claudeCode", "m1", B + 1000, B + 3000, 8, final=False)],
     "queries": [{"nowMs": B + 3000, "liveOutput": 250 / 3, "liveTotal": 250 / 3, "estimatedShare": 0.4}]},
    {"name": "agents are listed in parent groups",
     "agents": [agent("P1", "claudeDesktop", "m1", "idle", None, B),
                agent("C1", "claudeDesktop", "m1", "generating", None, B + 3000, "P1"),
                agent("C2", "claudeDesktop", "m1", "generating", None, B + 3000, "P1"),
                agent("P2", "codex", "m2", "generating", None, B + 3000),
                agent("R", "codex", "m2", "idle", None, B - 900_000),
                agent("C3", "codex", "m2", "generating", None, B + 2000, "R"),
                agent("D", "claudeCode", "m3", "idle", None, B - 1000),
                agent("Q", "codex", "m2", "idle", None, B - 700_000)],
     "responses": [er("r1", "P2", "codex", "m2", B, B + 3000, 300),
                   er("r2", "C1", "claudeDesktop", "m1", B, B + 3000, 150),
                   er("r3", "C2", "claudeDesktop", "m1", B, B + 3000, 90)],
     "queries": [{"nowMs": B + 3000, "liveOutput": 180.0, "liveTotal": 180.0, "estimatedShare": 0.0,
                  "agentsOrder": ["P2", "P1", "C1", "C2", "R", "C3", "D"],
                  "agentParents": {"C1": "P1", "C2": "P1", "C3": "R"}}]},
    {"name": "today totals, average, peak and history",
     "todayStartMs": DAY,
     "agents": [agent("a1", "codex", "m1", last=B + 4000)],
     "responses": [er("r0", "a1", "codex", "m1", DAY - 5000, DAY - 1, 999, 999),
                   er("r1", "a1", "codex", "m1", B, B + 4000, 200, 10000),
                   er("r2", "a1", "codex", "m1", B + 2000, B + 3000, 300, 0),
                   er("r3", "a1", "codex", "m1", B + 5000, B + 6000, 8, 0, final=False)],
     "queries": [{"nowMs": B + 10_000, "liveOutput": 0.0, "liveTotal": 0.0, "estimatedShare": 0.0,
                  "todayRequests": 3, "todayOutput": 508, "todayInput": 10000, "averageToday": 100.0,
                  "todayUnfinalized": 1,
                  "peakTodayOutput": 150.0,
                  "historyMeasuredOutput": {str(B): 50.0, str(B + 1000): 50.0, str(B + 2000): 350.0,
                                            str(B + 3000): 50.0, str(B + 4000): 0.0}}]},
]
with open(os.path.join(ROOT, "engine-cases.json"), "w", encoding="utf-8") as f:
    json.dump({"_comment": "Times are Unix ms. Unless a case sets todayStartMs, use 0. Compare floats with 1e-6 "
                           "absolute tolerance. historyMeasuredOutput maps bucket start ms to tok/s.",
               "cases": cases}, f, indent=2)
    f.write("\n")
print("fixtures written to", ROOT)
