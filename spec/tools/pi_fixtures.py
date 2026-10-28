#!/usr/bin/env python3
"""pi session fixtures (METRICS 4.13), used by generate_fixtures.py.

  python3 spec/tools/pi_fixtures.py OUT_DIR

OUT_DIR/pi/agent plays the role of `~/.pi/agent`: the root is pi/agent/sessions. Files are laid out exactly as pi
writes them, including one extension run sub-session under a session's own folder. The returned expectations have the
shape of spec/fixtures/parser-expected.json (`order`, `files`, `mtimes`) plus `discover` (the session files discovery
must find), `traces` (phase checkpoints after a given physical line of a file) and `tailReads` (a read that starts at
a byte offset, which must still take the header from the file start). Expected values are written by hand,
independently of reference.py.

They are written to their own file (`pi-expected.json`) rather than into the shared `parser-expected.json`, because
that file is read by both platforms' conformance tests and the macOS reader does not exist yet.
"""
import json
import os
import sys
from datetime import datetime, timezone

DAY = "2026-10-20"
HOME = "/projects"

A_ID = "019a0001-6f44-7000-8000-000000000001"
A_NAME = "2026-10-20T10-10-00-000Z_%s.jsonl" % A_ID
A_REL = "pi/agent/sessions/-projects-pulsar/%s" % A_NAME

B_ID = "019a0002-6f44-7000-8000-000000000002"
B_REL = "pi/agent/sessions/-projects-pulsar/2026-10-20T10-20-00-000Z_%s.jsonl" % B_ID

C_ID = "019a0001-6f44-7000-8000-0000000000aa"
C_REL = "pi/agent/sessions/-projects-pulsar/%s/%s/run-0/session.jsonl" % (A_NAME[:-len(".jsonl")], C_ID)

D_ID = "019a0003-6f44-7000-8000-000000000003"
D_REL = "pi/agent/sessions/-projects-nova/2026-10-20T09-00-00-000Z_%s.jsonl" % D_ID

MALFORMED = b"{this is not json"
BAD_UTF8 = b'{"type":"message","id":"\xff\xfe","message":{"role":"user"}}'


def ms(clock, day=DAY):
    hour, minute, rest = clock.split(":")
    second, milli = rest.split(".")
    dt = datetime(int(day[0:4]), int(day[5:7]), int(day[8:10]), int(hour), int(minute), int(second), int(milli) * 1000,
                  tzinfo=timezone.utc)
    return int(dt.timestamp() * 1000)


def iso(value):
    dt = datetime.fromtimestamp(value / 1000, tz=timezone.utc)
    return dt.strftime("%Y-%m-%dT%H:%M:%S.") + "%03dZ" % (value % 1000)


def usage(inp, out, cache_read=0, cache_write=0, cache_write_1h=None, reasoning=None):
    u = {"input": inp, "output": out, "cacheRead": cache_read, "cacheWrite": cache_write,
         "totalTokens": inp + out + cache_read + cache_write}
    if cache_write_1h is not None:
        u["cacheWrite1h"] = cache_write_1h
    if reasoning is not None:
        u["reasoning"] = reasoning
    return u


def text(value):
    return [{"type": "text", "text": value}]


def call(call_id):
    return [{"type": "toolCall", "id": call_id, "name": "read", "arguments": {}}]


class Session:
    """The physical lines of one session file."""

    def __init__(self, session_id, created, cwd):
        self.lines = [{"type": "session", "version": 3, "id": session_id, "timestamp": iso(created), "cwd": cwd}]

    def entry(self, entry_type, entry_id, when, **fields):
        self.lines.append(dict({"type": entry_type, "id": entry_id, "parentId": None, "timestamp": iso(when)}, **fields))
        return len(self.lines)

    def message(self, entry_id, when, message):
        obj = {"type": "message", "parentId": None, "timestamp": iso(when), "message": message}
        if entry_id is not None:
            obj["id"] = entry_id
        self.lines.append(obj)
        return len(self.lines)

    def user(self, entry_id, when, value="go"):
        return self.message(entry_id, when, {"role": "user", "content": value, "timestamp": when})

    def assistant(self, entry_id, when, started, stop, usage_obj, duration=None, model="claude-opus-4-5",
                  content=None):
        message = {"role": "assistant", "content": content if content is not None else text("done"),
                   "api": "anthropic-messages", "provider": "anthropic", "model": model,
                   "timestamp": started, "usage": usage_obj}
        if stop is not _MISSING:
            message["stopReason"] = stop
        if duration is not None:
            message["durationMs"] = duration
        return self.message(entry_id, when, message)

    def tool_result(self, entry_id, when, call_id, usage_obj=None):
        message = {"role": "toolResult", "toolCallId": call_id, "toolName": "read", "content": text("ok"),
                   "isError": False, "timestamp": when}
        if usage_obj is not None:
            message["usage"] = usage_obj
        return self.message(entry_id, when, message)

    def raw(self, raw_bytes):
        self.lines.append(raw_bytes)
        return len(self.lines)

    def encoded(self):
        out = []
        for line in self.lines:
            out.append(line if isinstance(line, bytes) else json.dumps(line, separators=(",", ":")).encode("utf-8"))
        return b"\n".join(out) + b"\n"


class _Missing:
    pass


_MISSING = _Missing()


def resp(entry_id, stamp, start, end, inp, cached, cache_write, out, reasoning, model, provider="anthropic"):
    return {"key": "pi:%s:%d" % (entry_id, stamp), "startMs": start, "endMs": end, "input": inp, "cachedInput": cached,
            "cacheWriteInput": cache_write, "cacheWrite1hInput": 0, "output": out, "reasoning": reasoning,
            "model": model, "provider": provider, "fast": False, "final": True}


def write_fixtures(out_dir):
    def path_of(rel):
        return os.path.join(out_dir, rel.replace("/", os.sep))

    for rel in (A_REL, B_REL, C_REL, D_REL):
        os.makedirs(os.path.dirname(path_of(rel)), exist_ok=True)

    mtimes = {}
    ordered = []
    traces = []

    def writer(rel, mtime):
        mtimes[rel] = mtime
        return path_of(rel)

    def write(rel, session, mtime):
        with open(writer(rel, mtime), "wb") as fh:
            fh.write(session.encoded())
        ordered.append(rel)

    def put(rel, content, mtime):
        os.makedirs(os.path.dirname(path_of(rel)) or ".", exist_ok=True)
        with open(writer(rel, mtime), "wb") as fh:
            fh.write(content)

    def trace(rel, after_line, phase, in_flight):
        traces.append(dict(file=rel, afterLine=after_line, phase=phase, inFlightStartMs=in_flight))

    # ---- Session A: one measured response of every shape, side usages and ignored entries.
    a = Session(A_ID, ms("10:10:00.000"), HOME + "/pulsar")
    a.entry("model_change", "a1ac0001", ms("10:10:00.100"), provider="anthropic", modelId="claude-opus-4-5")
    a.entry("thinking_level_change", "a1ac0002", ms("10:10:00.200"), thinkingLevel="max")
    line = a.user("a1aa0001", ms("10:10:10.000"))
    trace(A_REL, line, "generating", ms("10:10:10.000"))

    # The measured shape: message.timestamp is the request start, durationMs the stream's own duration.
    line = a.assistant("a1ab0001", ms("10:10:20.000"), ms("10:10:10.500"), "toolUse",
                       usage(1000, 200, 100, 50, reasoning=30), duration=9500, content=call("call-1"))
    trace(A_REL, line, "tools", None)

    # The tool result answers the last pending call: the next request is in flight.
    line = a.tool_result("a1ac0003", ms("10:10:21.000"), "call-1")
    trace(A_REL, line, "generating", ms("10:10:21.000"))

    line = a.assistant("a1ab0002", ms("10:10:30.000"), ms("10:10:21.500"), "stop", usage(1200, 300), duration=8500)
    trace(A_REL, line, "idle", None)

    # No durationMs: the entry timestamp ends the response.
    a.assistant("a1ab0003", ms("10:10:40.000"), ms("10:10:35.000"), "stop", usage(1300, 150))
    # Error and aborted keep their totals and count no interval.
    a.assistant("a1ab0004", ms("10:10:50.000"), ms("10:10:45.000"), "error", usage(1400, 10), duration=5000)
    a.assistant("a1ab0005", ms("10:11:00.000"), ms("10:10:55.000"), "aborted", usage(1450, 5), duration=5000)
    # `length` completes normally, and a durationMs of 0 ends at the start.
    a.assistant("a1ab0006", ms("10:11:10.000"), ms("10:11:05.000"), "length", usage(1500, 400), duration=0)
    # A missing and a non-string stopReason count as aborted.
    a.assistant("a1ab0007", ms("10:11:20.000"), ms("10:11:15.000"), _MISSING, usage(1600, 20), duration=5000)
    a.assistant("a1ab0008", ms("10:11:30.000"), ms("10:11:25.000"), 7, usage(1650, 25), duration=5000)
    # All four counters at 0: no response at all.
    a.assistant("a1ab0009", ms("10:11:40.000"), ms("10:11:35.000"), "stop", usage(0, 0), duration=5000)

    # Side usages: totals only. The `usage` entry names its own model; compaction and branch summaries do not.
    a.entry("usage", "a1ad0001", ms("10:11:50.000"), kind="cache_warm", provider="anthropic",
            model="claude-haiku-4-5", usage=usage(5000, 0))
    a.entry("compaction", "a1ad0002", ms("10:12:00.000"), summary="…", firstKeptEntryId="a1aa0001", tokensBefore=50000,
            usage=usage(2000, 300))
    a.entry("branch_summary", "a1ad0003", ms("10:12:10.000"),
            **{"fromId": "a1aa0001", "summary": "branch", "usage": usage(3000, 200)})
    # A tool result with its own usage is nested model work a tool performed.
    a.tool_result("a1ac0004", ms("10:12:20.000"), "call-2", usage_obj=usage(700, 50))

    # Entries that never affect measurement.
    a.entry("custom", "a1ae0001", ms("10:12:30.000"), customType="pi.virtual-model-state", data={"count": 1})
    a.entry("custom_message", "a1ae0002", ms("10:12:40.000"), customType="notes", content="remember", display=True)
    a.entry("label", "a1ae0003", ms("10:12:50.000"), targetId="a1aa0001", label="checkpoint")
    a.entry("session_info", "a1ae0004", ms("10:13:00.000"), name="Nightly refactor")
    a.entry("context_edit", "a1ae0005", ms("10:13:10.000"), targetId="a1aa0001", replacement=None)
    # Two malformed lines: one is not JSON at all, the other is invalid UTF-8.
    a.raw(MALFORMED)
    a.raw(BAD_UTF8)
    # An entry without an id: the empty string takes its place in the key.
    a.assistant(None, ms("10:13:30.000"), ms("10:13:25.000"), "stop", usage(1700, 90), duration=5000)
    line = a.user("a1aa0002", ms("10:14:00.000"))
    trace(A_REL, line, "generating", ms("10:14:00.000"))
    write(A_REL, a, ms("10:14:00.000"))

    # ---- Session B: a /clone. Its header is fresh; copied entries keep their ids and message timestamps.
    b = Session(B_ID, ms("10:20:00.000"), HOME + "/pulsar")
    # Copied verbatim: far older than the header minus 2 s, so the copy cutoff skips it.
    b.assistant("a1ab0001", ms("10:10:20.000"), ms("10:10:10.500"), "toolUse", usage(1000, 200, 100, 50, reasoning=30),
                duration=9500, content=call("call-1"))
    # Copied with a rewritten entry timestamp inside the cutoff window: the key is already owned by A (METRICS 3.4).
    b.assistant("a1ab0002", ms("10:19:59.500"), ms("10:10:21.500"), "stop", usage(1200, 300), duration=8500)
    b.assistant("b2ab0001", ms("10:20:10.000"), ms("10:20:05.000"), "stop", usage(2000, 120), duration=5000)
    write(B_REL, b, ms("10:20:10.000"))

    # ---- Session C: an extension run sub-session below A's folder; it attaches to A and counts on its own.
    c = Session(C_ID, ms("10:30:00.000"), HOME + "/pulsar")
    line = c.entry("session_info", "c3ae0001", ms("10:30:00.100"), name="subagent-scout-019a0001-1")
    trace(C_REL, line, "idle", None)
    line = c.user("c3aa0001", ms("10:30:05.000"))
    trace(C_REL, line, "generating", ms("10:30:05.000"))
    line = c.assistant("c3ab0001", ms("10:30:15.000"), ms("10:30:05.500"), "stop", usage(900, 250), duration=9500)
    trace(C_REL, line, "idle", None)
    write(C_REL, c, ms("10:30:15.000"))

    # ---- Session D: a second project bucket, older than the rest.
    d = Session(D_ID, ms("09:00:00.000"), HOME + "/nova")
    d.user("d4aa0001", ms("09:00:05.000"))
    d.assistant("d4ab0001", ms("09:00:15.000"), ms("09:00:05.500"), "stop", usage(800, 120), duration=9500)
    write(D_REL, d, ms("09:00:15.000"))

    # ---- Discovery decoys: none of these is a session file.
    head = json.dumps(a.lines[0], separators=(",", ":")).encode("utf-8") + b"\n"
    put("pi/agent/sessions/stray.jsonl", head, ms("10:40:00.000"))
    put("pi/agent/sessions/-projects-pulsar/ollama.json", b"{}\n", ms("10:40:01.000"))
    put("pi/agent/sessions/-projects-pulsar/notes/draft.jsonl", head, ms("10:40:02.000"))
    put("pi/agent/sessions/.hidden/session.jsonl", head, ms("10:40:03.000"))
    put("pi/agent/sessions/-projects-pulsar/%s.jsonl.bak" % A_NAME[:-len(".jsonl")], head, ms("10:40:04.000"))

    # ---- A read of session D that starts after its two first lines: the header must still be probed. The offset is
    # the newline that ends line 2, so reading begins on line 3.
    d_lines = [json.dumps(line, separators=(",", ":")).encode("utf-8") + b"\n" for line in d.lines]
    tail_reads = [dict(file=D_REL, offset=len(d_lines[0]) + len(d_lines[1]) - 1, project="nova", phase="idle",
                       inFlightStartMs=None, lastLineMs=ms("09:00:15.000"),
                       responseKeys=["pi:d4ab0001:%d" % ms("09:00:05.500")])]

    order = sorted(ordered, key=lambda rel: (mtimes[rel], rel))
    result = {
        "_comment": "pi: the folder `pi/agent` plays `~/.pi/agent`, so the only automatic root is pi/agent/sessions. "
                    "Session A carries one measured response of every shape (durationMs, entry-timestamp fallback, "
                    "error, aborted, a missing and a non-string stopReason, a zero-usage placeholder, a durationMs of "
                    "0), three side usages that count in totals only plus a tool result with usage, entries that never "
                    "affect measurement, one entry without an id and two malformed lines. Session B is a clone: the "
                    "copied entry older than the header minus 2 s is skipped by the copy cutoff, the copied entry "
                    "inside that window is deduplicated by key, and only B's own response counts. Session C is an "
                    "extension run sub-session; it attaches to A (`parentFile`) and counts as its own agent. "
                    "`tailReads` start reading at byte `offset` (after two lines), so the header must still be read "
                    "first. This file is separate from parser-expected.json because only the Windows tests read it "
                    "until the macOS reader exists (METRICS 4.13).",
        "order": order,
        "mtimes": mtimes,
        "discover": sorted([A_REL, B_REL, C_REL, D_REL]),
        "files": {
            A_REL: dict(
                source="pi", project="pulsar", model="claude-opus-4-5", isSubagent=False, parentFile=None,
                phase="generating", inFlightStartMs=ms("10:14:00.000"), lastLineMs=ms("10:14:00.000"),
                malformedLines=2,
                responses=[
                    resp("a1ab0001", ms("10:10:10.500"), ms("10:10:10.500"), ms("10:10:20.000"), 1150, 100, 50, 200, 30,
                         "claude-opus-4-5"),
                    resp("a1ab0002", ms("10:10:21.500"), ms("10:10:21.500"), ms("10:10:30.000"), 1200, 0, 0, 300, 0,
                         "claude-opus-4-5"),
                    resp("a1ab0003", ms("10:10:35.000"), ms("10:10:35.000"), ms("10:10:40.000"), 1300, 0, 0, 150, 0,
                         "claude-opus-4-5"),
                    resp("a1ab0004", ms("10:10:45.000"), None, ms("10:10:50.000"), 1400, 0, 0, 10, 0, "claude-opus-4-5"),
                    resp("a1ab0005", ms("10:10:55.000"), None, ms("10:11:00.000"), 1450, 0, 0, 5, 0, "claude-opus-4-5"),
                    resp("a1ab0006", ms("10:11:05.000"), ms("10:11:05.000"), ms("10:11:05.000"), 1500, 0, 0, 400, 0,
                         "claude-opus-4-5"),
                    resp("a1ab0007", ms("10:11:15.000"), None, ms("10:11:20.000"), 1600, 0, 0, 20, 0, "claude-opus-4-5"),
                    resp("a1ab0008", ms("10:11:25.000"), None, ms("10:11:30.000"), 1650, 0, 0, 25, 0, "claude-opus-4-5"),
                    resp("a1ad0001", ms("10:11:50.000"), None, ms("10:11:50.000"), 5000, 0, 0, 0, 0,
                         "claude-haiku-4-5"),
                    resp("a1ad0002", ms("10:12:00.000"), None, ms("10:12:00.000"), 2000, 0, 0, 300, 0,
                         "claude-opus-4-5", provider=None),
                    resp("a1ad0003", ms("10:12:10.000"), None, ms("10:12:10.000"), 3000, 0, 0, 200, 0,
                         "claude-opus-4-5", provider=None),
                    resp("a1ac0004", ms("10:12:20.000"), None, ms("10:12:20.000"), 700, 0, 0, 50, 0, "claude-opus-4-5",
                         provider=None),
                    resp("", ms("10:13:25.000"), ms("10:13:25.000"), ms("10:13:30.000"), 1700, 0, 0, 90, 0,
                         "claude-opus-4-5"),
                ]),
            B_REL: dict(
                source="pi", project="pulsar", model="claude-opus-4-5", isSubagent=False, parentFile=None,
                phase="idle", inFlightStartMs=None, lastLineMs=ms("10:20:10.000"), malformedLines=0,
                responses=[
                    resp("b2ab0001", ms("10:20:05.000"), ms("10:20:05.000"), ms("10:20:10.000"), 2000, 0, 0, 120, 0,
                         "claude-opus-4-5"),
                ]),
            C_REL: dict(
                source="pi", project="pulsar", model="claude-opus-4-5", isSubagent=True, parentFile=A_REL,
                phase="idle", inFlightStartMs=None, lastLineMs=ms("10:30:15.000"), malformedLines=0,
                responses=[
                    resp("c3ab0001", ms("10:30:05.500"), ms("10:30:05.500"), ms("10:30:15.000"), 900, 0, 0, 250, 0,
                         "claude-opus-4-5"),
                ]),
            D_REL: dict(
                source="pi", project="nova", model="claude-opus-4-5", isSubagent=False, parentFile=None,
                phase="idle", inFlightStartMs=None, lastLineMs=ms("09:00:15.000"), malformedLines=0,
                responses=[
                    resp("d4ab0001", ms("09:00:05.500"), ms("09:00:05.500"), ms("09:00:15.000"), 800, 0, 0, 120, 0,
                         "claude-opus-4-5"),
                ]),
        },
        "traces": traces,
        "tailReads": tail_reads,
    }

    for rel, when in mtimes.items():
        os.utime(path_of(rel), (when / 1000, when / 1000))
    with open(os.path.join(out_dir, "pi-expected.json"), "w", encoding="utf-8") as fh:
        json.dump(result, fh, indent=2, ensure_ascii=False)
        fh.write("\n")
    return result


def main():
    if len(sys.argv) != 2:
        print(__doc__.strip())
        sys.exit(2)
    print(json.dumps(write_fixtures(sys.argv[1]), indent=2))


if __name__ == "__main__":
    main()
