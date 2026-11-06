#!/usr/bin/env python3
"""Writes spec/fixtures/work-signal-cases.json (METRICS 6.10). Expectations are written by hand below; only the
digests of the `digests` cases are computed here, with hashlib, independently of work_signals.py."""
import hashlib
import json
import os

T = 1_000_000_000_000
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures", "work-signal-cases.json")


def sha16(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()[:16]


def act(at, tool, dg, failed=None):
    a = {"at": T + at, "tool": tool, "digest": dg}
    if failed is not None:
        a["failed"] = failed
    return a


def sig(looping=None, repeating=None, novelty=None, failed=0, recovered=0):
    return {"looping": looping, "repeating": repeating, "novelty": novelty, "failed": failed, "recovered": recovered}


A, B, C, D = "a" * 16, "b" * 16, "c" * 16, "d" * 16
signals = [
    {"name": "the same failing call three times is looping", "nowMs": T + 40_000,
     "actions": [act(0, "Bash", A, True), act(10_000, "Bash", A, True), act(20_000, "Bash", A, True)],
     "expect": sig(looping=3, failed=3)},
    {"name": "two calls alternating three times with one failure is looping",
     "nowMs": T + 70_000,
     "actions": [act(0, "Edit", B, False), act(10_000, "Bash", A, True), act(20_000, "Edit", B, False),
                 act(30_000, "Bash", A, False), act(40_000, "Edit", B, False), act(50_000, "Bash", A, False)],
     "expect": sig(looping=3, failed=1, recovered=1)},
    {"name": "a block of three repeated three times, counted by its repeats",
     "nowMs": T + 90_000,
     "actions": [act(i * 10_000, t, d, f) for i, (t, d, f) in enumerate(
         [("Read", C, False), ("Edit", B, False), ("Bash", A, True)] * 3)],
     "expect": sig(looping=3, novelty=3 / 9, failed=3)},
    {"name": "repeats without a failure are only repeating",
     "nowMs": T + 50_000,
     "actions": [act(0, "wait_agent", D), act(10_000, "wait_agent", D), act(20_000, "wait_agent", D),
                 act(30_000, "wait_agent", D)],
     "expect": sig(repeating=4)},
    {"name": "a repetition older than 120 s is not shown",
     "nowMs": T + 200_000,
     "actions": [act(0, "Bash", A, True), act(10_000, "Bash", A, True), act(20_000, "Bash", A, True)],
     "expect": sig(failed=3)},
    {"name": "two repeats are not enough; a different digest breaks the run",
     "nowMs": T + 40_000,
     "actions": [act(0, "Bash", A, True), act(10_000, "Bash", B, True), act(20_000, "Bash", A, True),
                 act(30_000, "Bash", A, True)],
     "expect": sig(failed=4)},
    {"name": "novelty needs eight recent actions; a failure fixed by the same tool within five actions is recovered",
     "nowMs": T + 100_000,
     "actions": [act(0, "Read", "1" * 16), act(10_000, "Read", "2" * 16), act(20_000, "Bash", A, True),
                 act(30_000, "Edit", B, False), act(40_000, "Bash", C, False), act(50_000, "Read", "1" * 16),
                 act(60_000, "Grep", "3" * 16), act(70_000, "Bash", D, True), act(80_000, "Read", "4" * 16)],
     "expect": sig(novelty=8 / 9, failed=2, recovered=1)},
    {"name": "actions older than ten minutes leave every signal",
     "nowMs": T + 700_000,
     "actions": [act(0, "Bash", A, True), act(10_000, "Bash", A, True), act(20_000, "Bash", A, True),
                 act(690_000, "Read", B, False)],
     "expect": sig()},
    {"name": "a failure recovered only after more than five actions does not count",
     "nowMs": T + 80_000,
     "actions": [act(0, "Bash", A, True), act(10_000, "Read", "1" * 16), act(20_000, "Read", "2" * 16),
                 act(30_000, "Read", "3" * 16), act(40_000, "Read", "4" * 16), act(50_000, "Read", "5" * 16),
                 act(60_000, "Bash", B, False)],
     "expect": sig(failed=1)},
]

claude_input = {"command": "swift test", "description": "Run tests"}
claude_raw = json.dumps(claude_input, ensure_ascii=False, separators=(",", ":"))
codex_cmd = '{"cmd":"cargo test","yield_time_ms":10000}'
codex_patch = "*** Begin Patch\n*** Update File: src/lib.rs\n*** End Patch"
pi_args = {"path": "src/queue.ts"}
pi_raw = json.dumps(pi_args, separators=(",", ":"))

files = [
    {"name": "Claude: a tool_use repeated on a message's next line is one action; is_error marks the failure",
     "format": "claude",
     "lines": [
         [T, {"type": "assistant", "message": {"id": "m1", "content": [
             {"type": "thinking", "thinking": ""},
             {"type": "tool_use", "id": "t1", "name": "Bash", "input": claude_input}]}}],
         [T + 100, {"type": "assistant", "message": {"id": "m1", "content": [
             {"type": "tool_use", "id": "t1", "name": "Bash", "input": claude_input}]}}],
         [T + 5000, {"type": "user", "message": {"content": [
             {"type": "tool_result", "tool_use_id": "t1", "is_error": True, "content": "error: build failed"}]}}],
         [T + 6000, {"type": "assistant", "message": {"id": "m2", "content": [
             {"type": "tool_use", "id": "t2", "name": "Read", "input": {"file_path": "Package.swift"}}]}}],
         [T + 6500, {"type": "user", "message": {"content": [
             {"type": "tool_result", "tool_use_id": "t2", "content": "// swift-tools-version"}]}}],
     ],
     "expect": [{"at": T, "tool": "Bash", "digest": sha16(claude_raw), "failed": True},
                {"at": T + 6000, "tool": "Read", "digest": sha16('{"file_path":"Package.swift"}'),
                 "failed": False}]},
    {"name": "Codex: exit_code of the output's leading JSON object; an output without one stays unknown",
     "format": "codex",
     "lines": [
         [T, {"type": "response_item", "payload": {"type": "custom_tool_call", "call_id": "c1", "name": "exec",
                                                  "input": codex_cmd}}],
         [T + 4000, {"type": "response_item", "payload": {
             "type": "custom_tool_call_output", "call_id": "c1",
             "output": json.dumps([{"type": "input_text",
                                    "text": '{"chunk_id":"x","wall_time_seconds":3.9,"exit_code":101,'
                                            '"original_token_count":12,"output":"test failed"}'}])}}],
         [T + 5000, {"type": "response_item", "payload": {"type": "function_call", "call_id": "c2",
                                                         "name": "apply_patch", "arguments": codex_patch}}],
         [T + 5200, {"type": "response_item", "payload": {"type": "function_call_output", "call_id": "c2",
                                                         "output": "Success. Updated the following files"}}],
         [T + 6000, {"type": "response_item", "payload": {"type": "custom_tool_call", "call_id": "c3", "name": "exec",
                                                         "input": codex_cmd}}],
         [T + 9000, {"type": "response_item", "payload": {
             "type": "custom_tool_call_output", "call_id": "c3",
             "output": '{"chunk_id":"y","wall_time_seconds":2.0,"exit_code":0,"original_token_count":3,"output":"ok"}'}}],
     ],
     "expect": [{"at": T, "tool": "exec", "digest": sha16(codex_cmd), "failed": True},
                {"at": T + 5000, "tool": "apply_patch", "digest": sha16(codex_patch), "failed": None},
                {"at": T + 6000, "tool": "exec", "digest": sha16(codex_cmd), "failed": False}]},
    {"name": "Pi and omp: toolCall blocks and toolResult isError",
     "format": "pi",
     "lines": [
         [T, {"type": "message", "message": {"role": "assistant", "content": [
             {"type": "text", "text": "Reading"}, {"type": "toolCall", "id": "p1", "name": "read", "arguments": pi_args}]}}],
         [T + 900, {"type": "message", "message": {"role": "toolResult", "toolCallId": "p1", "toolName": "read",
                                                   "isError": True, "content": [{"type": "text", "text": "ENOENT"}]}}],
     ],
     "expect": [{"at": T, "tool": "read", "digest": sha16(pi_raw), "failed": True}]},
    {"name": "Factory Droid: tool_use in an assistant message line, tool_result in the next user message line",
     "format": "droid",
     "lines": [
         [T, {"type": "message", "id": "a1", "message": {"role": "assistant", "content": [
             {"type": "tool_use", "id": "toolu_d1", "name": "Execute", "input": claude_input}]}}],
         [T + 1500, {"type": "message", "id": "r1", "message": {"role": "user", "content": [
             {"type": "tool_result", "tool_use_id": "toolu_d1", "content": "2 failing", "is_error": True}]}}],
         [T + 2000, {"type": "message", "id": "a2", "message": {"role": "assistant", "content": [
             {"type": "tool_use", "id": "toolu_d2", "name": "Read", "input": {"file_path": "Package.swift"}}]}}],
     ],
     "expect": [{"at": T, "tool": "Execute", "digest": sha16(claude_raw), "failed": True},
                {"at": T + 2000, "tool": "Read", "digest": sha16('{"file_path":"Package.swift"}'), "failed": None}]},
]

cases = {
    "_comment": "METRICS 6.10. `digests`: input as stored -> first 16 hex digits of SHA-256. `signals`: actions "
                "(`failed` absent = unknown) and the signals at `nowMs`, written by hand. `files`: lines `[at, object]` "
                "of one file in a source family and the actions they give; object inputs are hashed as the compact "
                "JSON text the line stores.",
    "digests": [{"input": "swift test", "expect": sha16("swift test")},
                {"input": codex_cmd, "expect": sha16(codex_cmd)},
                {"input": {"path": "src/queue.ts"}, "expect": sha16(pi_raw)},
                {"input": {"command": "ls 文件"}, "expect": sha16('{"command":"ls 文件"}')}],
    "signals": signals,
    "files": files,
}
with open(OUT, "w", encoding="utf-8") as fh:
    json.dump(cases, fh, indent=1, ensure_ascii=False)
    fh.write("\n")
print("wrote", os.path.normpath(OUT))
