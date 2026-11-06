#!/usr/bin/env python3
"""Writes spec/fixtures/model-attribution-cases.json (METRICS 4.1 step 4, 11.3, 11.4 Attribution).

Every expectation below is written by hand. `spec/tools/reference.py --check-fixtures` checks them with the reference
parser and the bundled price table; both apps run the same cases through their parser, engine and report ledger.

Usage: python3 spec/tools/model_attribution_cases.py
"""
import json
import os
from datetime import datetime, timezone

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures", "model-attribution-cases.json")
T0 = int(datetime(2026, 10, 25, 23, 0, 0, tzinfo=timezone.utc).timestamp() * 1000)
OPUS_5, OPUS_55, OPUS_48 = "claude-opus-5", "claude-opus-5-5", "claude-opus-4-8"


def ts(ms):
    return datetime.fromtimestamp(ms / 1000, tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.") + f"{ms % 1000:03d}Z"


def at(seconds):
    return T0 + round(seconds * 1000)


def line(kind, sec, session, sidechain=False, **extra):
    row = {"parentUuid": None, "isSidechain": sidechain, "userType": "external", "cwd": "/Users/dev/projects/nova",
           "sessionId": session, "version": "2.1.309", "entrypoint": "cli", "type": kind}
    row.update(extra)
    row["timestamp"] = ts(at(sec))
    return row


def assistant(sec, session, mid, model, blocks, output, stop, **extra):
    message = {"id": mid, "type": "message", "role": "assistant"}
    if model is not None:
        message["model"] = model
    message.update(content=[{"type": b} for b in blocks], stop_reason=stop,
                   usage={"input_tokens": 4, "cache_read_input_tokens": 2000, "output_tokens": output})
    return line("assistant", sec, session, requestId="req_" + mid, uuid="uuid-%s-%d" % (mid, at(sec)), message=message,
                **extra)


def user(sec, session, content, **extra):
    return line("user", sec, session, uuid="u-%s-%d" % (session, at(sec)), message={"role": "user", "content": content},
                **extra)


def tool_result(sec, session, **extra):
    return user(sec, session, [{"type": "tool_result", "tool_use_id": "t-%d" % at(sec), "content": "ok"}], **extra)


def api_request(sec, session, model, **extra):
    return line("api-request", sec, session, id="api-%s-%d" % (session, at(sec)), params={"model": model}, **extra)


def text(rows):
    return [json.dumps(r, ensure_ascii=False, separators=(",", ":")) for r in rows]


def resp(key, model, output, report_id):
    return {"key": key, "model": model, "output": output, "reportModelId": report_id}


def agent(source, is_subagent, model, report_id, responses):
    return {"source": source, "isSubagent": is_subagent, "model": model, "reportModelId": report_id,
            "responses": responses}


def ledger(model_id, requests, output, source="claudeCode"):
    return {"modelId": model_id, "source": source, "requests": requests, "output": output}


SWITCH = "claude/projects/-Users-dev-projects-nova/s-switch.jsonl"
s = "s-switch"
switch_case = {
    "name": "a session that switches from claude-opus-5 to claude-opus-5-5 keeps every response on its own model",
    "files": [{"path": SWITCH, "lines": text([
        user(0, s, "Plan the release"),
        api_request(0.05, s, OPUS_5),
        assistant(2, s, "msg_old_1", OPUS_5, ["thinking"], 10, None, requestedModel=OPUS_5),
        assistant(4, s, "msg_old_1", OPUS_5, ["text"], 120, "end_turn", requestedModel=OPUS_5),
        user(10, s, "<command-name>/model</command-name>", isMeta=True),
        line("system", 10.1, s, subtype="local_command", content="Set model to claude-opus-5-5"),
        user(20, s, "Now ship it"),
        api_request(20.05, s, OPUS_55),
        assistant(22, s, "msg_new_1", OPUS_55, ["thinking"], 8, None, requestedModel=OPUS_55, advisorModel=OPUS_5),
        assistant(25, s, "msg_new_1", OPUS_55, ["tool_use"], 300, "tool_use", requestedModel=OPUS_55,
                  advisorModel=OPUS_5),
        tool_result(26, s),
        assistant(28, s, "msg_new_2", OPUS_55, ["text"], 50, "end_turn", requestedModel=OPUS_55, advisorModel=OPUS_5),
        assistant(28.5, s, "msg_synthetic", "<synthetic>", ["text"], 0, "stop_sequence"),
        user(40, s, "One more thing"),
        assistant(43, s, "msg_no_model", None, ["text"], 5, "end_turn"),
    ])}],
    "expect": {SWITCH: agent("claudeCode", False, OPUS_55, OPUS_55, [
        resp("msg_old_1", OPUS_5, 120, OPUS_5),
        resp("msg_new_1", OPUS_55, 300, OPUS_55),
        resp("msg_new_2", OPUS_55, 50, OPUS_55),
        resp("msg_no_model", None, 5, "unknown"),
    ])},
    "ledger": [ledger(OPUS_5, 1, 120), ledger(OPUS_55, 2, 350), ledger("unknown", 1, 5)],
}

PARENT = "claude/projects/-Users-dev-projects-nova/s-parent.jsonl"
CHILD = "claude/projects/-Users-dev-projects-nova/s-parent/subagents/agent-a1.jsonl"
p = "s-parent"
subagent_case = {
    "name": "a sub-agent on claude-opus-5-5 under a claude-opus-5 parent: each file keeps its own model",
    "files": [
        {"path": PARENT, "lines": text([
            user(0, p, "Review the module"),
            assistant(3, p, "msg_parent_1", OPUS_5, ["tool_use"], 40, "tool_use", requestedModel=OPUS_5),
            tool_result(30, p),
            assistant(33, p, "msg_parent_2", OPUS_5, ["text"], 60, "end_turn", requestedModel=OPUS_5),
        ])},
        {"path": CHILD, "lines": text([
            user(4, p, "Search the module", sidechain=True, agentId="a1"),
            assistant(6, p, "msg_child_1", OPUS_55, ["thinking"], 8, None, sidechain=True, agentId="a1",
                      advisorModel=OPUS_5),
            assistant(8, p, "msg_child_1", OPUS_55, ["tool_use"], 90, "tool_use", sidechain=True, agentId="a1",
                      advisorModel=OPUS_5),
            tool_result(9, p, sidechain=True, agentId="a1"),
            assistant(12, p, "msg_child_2", OPUS_55, ["text"], 30, "end_turn", sidechain=True, agentId="a1",
                      advisorModel=OPUS_5),
            assistant(12.5, p, "msg_child_synthetic", "<synthetic>", ["text"], 0, "stop_sequence", sidechain=True,
                      agentId="a1"),
        ])},
    ],
    "expect": {
        PARENT: agent("claudeCode", False, OPUS_5, OPUS_5, [
            resp("msg_parent_1", OPUS_5, 40, OPUS_5),
            resp("msg_parent_2", OPUS_5, 60, OPUS_5),
        ]),
        CHILD: agent("claudeCode", True, OPUS_55, OPUS_55, [
            resp("msg_child_1", OPUS_55, 90, OPUS_55),
            resp("msg_child_2", OPUS_55, 30, OPUS_55),
        ]),
    },
    "ledger": [ledger(OPUS_5, 2, 100), ledger(OPUS_55, 2, 120)],
}

OLD = "claude/projects/-Users-dev-projects-nova/s-old.jsonl"
NEW = "claude/projects/-Users-dev-projects-nova/s-resumed.jsonl"
o, n = "s-old", "s-resumed"
resume_case = {
    "name": "a resumed session copies claude-opus-5 history: the copy is ignored and the new file reports only its "
            "own claude-opus-5-5 responses",
    "files": [
        {"path": OLD, "lines": text([
            user(0, o, "Start the migration"),
            assistant(3, o, "msg_r1", OPUS_5, ["tool_use"], 70, "tool_use"),
            tool_result(4, o),
            assistant(6, o, "msg_r2", OPUS_5, ["text"], 30, "end_turn"),
        ])},
        {"path": NEW, "lines": text([
            user(0, n, "Start the migration"),
            assistant(3, n, "msg_r1", OPUS_5, ["tool_use"], 70, "tool_use"),
            tool_result(4, n),
            assistant(6, n, "msg_r2", OPUS_5, ["text"], 30, "end_turn"),
            user(60, n, "Continue"),
            assistant(63, n, "msg_r3", OPUS_55, ["text"], 80, "end_turn", requestedModel=OPUS_55),
        ])},
    ],
    "expect": {
        OLD: agent("claudeCode", False, OPUS_5, OPUS_5, [
            resp("msg_r1", OPUS_5, 70, OPUS_5),
            resp("msg_r2", OPUS_5, 30, OPUS_5),
        ]),
        NEW: agent("claudeCode", False, OPUS_55, OPUS_55, [resp("msg_r3", OPUS_55, 80, OPUS_55)]),
    },
    "ledger": [ledger(OPUS_5, 2, 100), ledger(OPUS_55, 1, 80)],
}

FALLBACK = "claude/projects/-Users-dev-projects-nova/s-fallback/subagents/agent-f1.jsonl"
f = "s-fallback"
fallback_case = {
    "name": "a message that falls back to another model mid-stream: the response takes its latest line's model and "
            "each ledger increment goes to the model of that moment",
    "files": [{"path": FALLBACK, "lines": text([
        user(0, f, "Check the build", sidechain=True, agentId="f1"),
        assistant(2, f, "msg_fb", OPUS_55, ["thinking"], 8, None, sidechain=True, agentId="f1", advisorModel=OPUS_55),
        assistant(4, f, "msg_fb", OPUS_48, ["fallback"], 8, None, sidechain=True, agentId="f1", advisorModel=OPUS_55),
        assistant(9, f, "msg_fb", OPUS_48, ["tool_use"], 200, "tool_use", sidechain=True, agentId="f1",
                  advisorModel=OPUS_55),
    ])}],
    "expect": {FALLBACK: agent("claudeCode", True, OPUS_48, OPUS_48, [resp("msg_fb", OPUS_48, 200, OPUS_48)])},
    "ledger": [ledger(OPUS_55, 1, 8), ledger(OPUS_48, 0, 192)],
}

REPORT_IDS = [[m, OPUS_55] for m in (
    "claude-opus-5-5", "claude-opus-5.5", "Claude-Opus-5-5", "claude-opus-5-5[1m]", "anthropic/claude-opus-5.5",
    "us.anthropic.claude-opus-5-5-v1:0", "claude-opus-5-5@20261001", "claude-opus-5-5-20261001",
    "claude-opus-5-5-thinking-max", "claude-opus-5-5:thinking", "claude-opus-5-5-fast", "duo-chat-opus-5-5")] + [
    [m, OPUS_5] for m in ("claude-opus-5", "claude-opus-5[1m]", "anthropic/claude-opus-5", "claude-opus-5-20260601",
                          "claude-opus-5-thinking-max", "claude-opus-5-fast")] + [
    [m, "unknown"] for m in ("claude-opus-5-5-latest", "claude-opus-55", "claude-opus-5-6", "<synthetic>")]

COMMENT = (
    "METRICS 4.1 step 4 and 11.3-11.4 (Attribution), written by hand. `cases`: feed each file's `lines` in order to a "
    "Claude parser whose agent id ends with `path` (one dedupe set per case, files in the given order). Every upsert "
    "goes to an engine running live at the response's own endMs with the bundled price table (spec/assets/"
    "prices.json). `expect` gives each file's source, isSubagent and agent model, the agent model's report id (the id "
    "`models[].generating` uses), and its responses in first-emission order with model, output and report id; "
    "`ledger` gives the report ledger's requests and output per (model id, source). `reportIds`: [model, report id] "
    "with no provider, bundled table. Generated by spec/tools/model_attribution_cases.py; checked by "
    "spec/tools/reference.py --check-fixtures.")


def main():
    doc = {"_comment": COMMENT, "cases": [switch_case, subagent_case, resume_case, fallback_case], "reportIds": REPORT_IDS}
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2, ensure_ascii=False)
        f.write("\n")


if __name__ == "__main__":
    main()
