#!/usr/bin/env python3
"""Independent reference implementation of spec/METRICS.md, used to cross-check the Swift and C# cores.

  python3 spec/tools/reference.py --check-fixtures
  python3 spec/tools/reference.py --dump-today [--codex PATH] [--claude PATH]

The dump format matches the `--dump-today` output of both platform probes. Conversation content is never printed.
"""
import argparse
import json
import os
import re
import sys
import time
from collections import OrderedDict
from datetime import datetime, timedelta, timezone

MAX_LINE = 32 * 1024 * 1024
DEDUPE_LIMIT = 65_536
WINDOW_MS = 3000
TS_RE = re.compile(r"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:?\d{2})?$")
TOKEN_FIELDS = ("input", "cachedInput", "cacheWriteInput", "output", "reasoning")


def parse_ts(value):
    if not isinstance(value, str):
        return None
    m = TS_RE.match(value.strip())
    if not m:
        return None
    y, mo, d, h, mi, s, frac, tz = m.groups()
    try:
        dt = datetime(int(y), int(mo), int(d), int(h), int(mi), int(s), tzinfo=timezone.utc)
    except ValueError:
        return None
    ms = int((frac or "0")[:3].ljust(3, "0"))
    if tz and tz != "Z":
        sign = 1 if tz[0] == "+" else -1
        digits = tz[1:].replace(":", "")
        dt -= sign * timedelta(hours=int(digits[:2]), minutes=int(digits[2:]))
    return int(dt.timestamp()) * 1000 + ms


def num(value):
    if isinstance(value, bool):
        return 0
    if isinstance(value, (int, float)):
        return max(0, int(value))
    return 0


def basename(path):
    if not isinstance(path, str) or not path:
        return None
    parts = [p for p in re.split(r"[\\/]", path) if p]
    return parts[-1] if parts else None


class Deduper:
    def __init__(self, limit=DEDUPE_LIMIT):
        self.owners = OrderedDict()
        self.limit = limit

    def owner(self, key):
        return self.owners.get(key)

    def claim(self, key, owner):
        self.owners[key] = owner
        while len(self.owners) > self.limit:
            self.owners.popitem(last=False)


class FileState:
    def __init__(self, path, source):
        self.path = path
        self.source = source
        self.source_decided = False
        self.project = None
        self.model = None
        self.is_subagent = "subagents" in [p for p in re.split(r"[\\/]", path) if p]
        self.phase = "idle"
        self.in_flight = None
        self.last_line = None
        self.pending = None
        self.malformed = 0
        self.responses = OrderedDict()
        # codex
        self.file_created = None
        self.my_agent_path = "/root"
        self.thread_id = None
        self.streaming = False
        self.saw_modern = False
        self.prev_legacy = None
        # claude
        self.precise = False


def emit(state, key, start, end, tokens, model, final=True):
    state.responses[key] = dict(key=key, startMs=start, endMs=end, model=model, final=final, **tokens)


def codex_tokens(usage):
    usage = usage if isinstance(usage, dict) else {}
    return dict(input=num(usage.get("input_tokens")), cachedInput=num(usage.get("cached_input_tokens")),
                cacheWriteInput=num(usage.get("cache_write_input_tokens")), output=num(usage.get("output_tokens")),
                reasoning=num(usage.get("reasoning_output_tokens")))


def codex_line(state, obj, dedupe):
    ts = parse_ts(obj.get("timestamp"))
    if ts is None:
        return
    kind = obj.get("type")
    payload = obj.get("payload") if isinstance(obj.get("payload"), dict) else {}
    if kind == "session_meta":
        if state.file_created is not None:
            return
        state.file_created = ts
        state.last_line = ts if state.last_line is None else max(state.last_line, ts)
        state.project = basename(payload.get("cwd")) or state.project
        state.my_agent_path = payload.get("agent_path") or "/root"
        source = payload.get("source")
        state.is_subagent = state.is_subagent or payload.get("thread_source") == "subagent" or (
            isinstance(source, dict) and "subagent" in source)
        state.thread_id = payload.get("id") or state.thread_id
        return
    if state.file_created is not None and ts < state.file_created - 2000:
        return
    state.last_line = ts if state.last_line is None else max(state.last_line, ts)
    ptype = payload.get("type")
    if kind == "turn_context":
        state.model = payload.get("model") or state.model
        state.project = basename(payload.get("cwd")) or state.project
        return
    if kind == "token_usage_record":
        state.saw_modern = True
        thread = payload.get("thread_id") or state.thread_id or state.path
        key = payload.get("response_id") or "codex:%s:%d" % (thread, ts)
        if dedupe.owner(key) is not None:
            return
        dedupe.claim(key, state.path)
        emit(state, key, state.pending, ts, codex_tokens(payload.get("usage")), state.model)
        after_usage(state, ts)
        return
    if kind == "event_msg":
        if ptype == "token_count":
            if state.saw_modern and state.streaming:
                after_usage(state, ts)
            else:
                legacy(state, ts, payload, dedupe)
        elif ptype in ("task_complete", "turn_aborted"):
            state.phase, state.pending, state.streaming, state.in_flight = "idle", None, False, None
        elif ptype in ("task_started", "user_message"):
            input_line(state, ts)
        return
    if kind == "response_item":
        role = payload.get("role")
        if ptype == "reasoning" or (isinstance(ptype, str) and ptype.endswith("_call")) or (
                ptype == "message" and role == "assistant"):
            state.streaming = True
            state.phase = "generating"
            state.in_flight = state.pending if state.pending is not None else ts
        elif (isinstance(ptype, str) and ptype.endswith("_output")) or (
                ptype == "message" and role in ("user", "developer", "system")):
            input_line(state, ts)
        elif ptype == "agent_message" and payload.get("recipient") == state.my_agent_path:
            if not state.streaming:
                state.pending = ts


def input_line(state, ts):
    if not state.streaming:
        state.pending = ts
        state.phase = "generating"
        state.in_flight = state.pending


def after_usage(state, ts):
    state.pending, state.streaming, state.phase, state.in_flight = ts, False, "tools", None


def legacy(state, ts, payload, dedupe):
    info = payload.get("info")
    if state.saw_modern or not isinstance(info, dict):
        return
    total = info.get("total_token_usage")
    last = info.get("last_token_usage")
    if not isinstance(total, dict):
        return
    fields = ("input_tokens", "cached_input_tokens", "cache_write_input_tokens", "output_tokens",
              "reasoning_output_tokens", "total_tokens")
    if state.prev_legacy is None:
        usage = last if isinstance(last, dict) else total
    else:
        delta = {f: num(total.get(f)) - num(state.prev_legacy.get(f)) for f in fields}
        if delta["total_tokens"] == 0:
            return
        usage = (last if isinstance(last, dict) else {}) if any(v < 0 for v in delta.values()) else delta
    state.prev_legacy = total
    thread = state.thread_id or state.path
    key = "codex-legacy:%s:%d" % (thread, num(total.get("total_tokens")))
    if dedupe.owner(key) is not None:
        return
    dedupe.claim(key, state.path)
    emit(state, key, state.pending, ts, codex_tokens(usage), state.model)
    after_usage(state, ts)


def is_interrupt(message):
    content = message.get("content") if isinstance(message, dict) else None
    marker = "[Request interrupted by user"
    if isinstance(content, str):
        return content.startswith(marker)
    if isinstance(content, list):
        return any(isinstance(b, dict) and b.get("type") == "text" and isinstance(b.get("text"), str)
                   and b["text"].startswith(marker) for b in content)
    return False


def claude_tokens(usage):
    usage = usage if isinstance(usage, dict) else {}
    inp, cc, cr = num(usage.get("input_tokens")), num(usage.get("cache_creation_input_tokens")), num(
        usage.get("cache_read_input_tokens"))
    details = usage.get("output_tokens_details") if isinstance(usage.get("output_tokens_details"), dict) else {}
    return dict(input=inp + cc + cr, cachedInput=cr, cacheWriteInput=cc, output=num(usage.get("output_tokens")),
                reasoning=num(details.get("thinking_tokens")))


def claude_line(state, obj, dedupe):
    ts = parse_ts(obj.get("timestamp"))
    if ts is None:
        return
    state.last_line = ts if state.last_line is None else max(state.last_line, ts)
    if not state.source_decided and isinstance(obj.get("entrypoint"), str):
        state.source = "claudeDesktop" if obj["entrypoint"] == "claude-desktop" else "claudeCode"
        state.source_decided = True
    state.project = basename(obj.get("cwd")) or state.project
    if obj.get("isSidechain") is True:
        state.is_subagent = True
    kind = obj.get("type")
    if kind == "api-request":
        state.pending, state.precise, state.phase, state.in_flight = ts, True, "generating", ts
    elif kind == "user":
        if is_interrupt(obj.get("message")):
            state.phase, state.pending, state.precise, state.in_flight = "idle", None, False, None
        else:
            if not state.precise:
                state.pending = ts
            state.phase, state.in_flight = "generating", state.pending
    elif kind == "assistant":
        message = obj.get("message") if isinstance(obj.get("message"), dict) else {}
        if message.get("model") == "<synthetic>":
            return
        key = message.get("id") or obj.get("requestId") or obj.get("uuid")
        if not isinstance(key, str) or not key:
            return
        owner = dedupe.owner(key)
        if owner is not None and owner != state.path:
            return
        tokens = claude_tokens(message.get("usage"))
        stop = message.get("stop_reason")
        resp = state.responses.get(key)
        if resp is None:
            dedupe.claim(key, state.path)
            emit(state, key, state.pending, ts, tokens, message.get("model"), stop is not None)
            state.pending, state.precise = None, False
            resp = state.responses[key]
        else:
            resp["endMs"] = max(resp["endMs"], ts)
            for f in TOKEN_FIELDS:
                resp[f] = max(resp[f], tokens[f])
            resp["model"] = message.get("model") or resp["model"]
            resp["final"] = resp["final"] or stop is not None
        state.model = resp["model"] or state.model
        content = message.get("content") if isinstance(message.get("content"), list) else []
        has_tool_use = any(isinstance(b, dict) and b.get("type") == "tool_use" for b in content)
        if stop == "tool_use" or (stop is None and has_tool_use):
            state.phase, state.in_flight = "tools", None
        elif stop is None or stop == "pause_turn":
            state.phase, state.in_flight = "generating", resp["startMs"] if resp["startMs"] is not None else ts
        else:
            state.phase, state.in_flight = "idle", None


def read_lines(path, start_offset=0):
    with open(path, "rb") as f:
        f.seek(start_offset)
        data = f.read()
    if start_offset:
        nl = data.find(b"\n")
        data = data[nl + 1:] if nl >= 0 else b""
    end = data.rfind(b"\n")
    if end < 0:
        return
    for raw in data[:end].split(b"\n"):
        if len(raw) > MAX_LINE or not raw.strip():
            continue
        yield raw


def process_file(state, dedupe, start_offset=0):
    handler = codex_line if state.source == "codex" else claude_line
    for raw in read_lines(state.path, start_offset):
        try:
            obj = json.loads(raw)
        except (ValueError, UnicodeDecodeError):
            state.malformed += 1
            continue
        if not isinstance(obj, dict):
            state.malformed += 1
            continue
        handler(state, obj, dedupe)


# ----------------------------------------------------------------------------- engine

def interval(resp):
    start, end = resp.get("startMs"), resp["endMs"]
    if start is None or end < start or end - start > 15 * 60_000:
        return None
    return (min(start, end - 500), end)


def overlap(a0, a1, b0, b1):
    return max(0, min(a1, b1) - max(a0, b0))


def speed_of(responses):
    out = tot = dur = 0.0
    for r in responses:
        iv = interval(r)
        out += r["output"]
        tot += r["output"] + r["input"]
        dur += (iv[1] - iv[0]) / 1000
    return (out / dur, tot / dur) if dur > 0 else None


def recent(valid, now, predicate, limit, horizon):
    picked = [r for r in valid if predicate(r) and r["endMs"] >= now - horizon]
    picked.sort(key=lambda r: r["endMs"], reverse=True)
    return speed_of(picked[:limit]) if picked else None


def is_final(resp):
    return resp.get("final", True)


def estimated_speed(valid, now, agent):
    return (recent(valid, now, lambda r: r["agentId"] == agent["id"], 3, 600_000)
            or recent(valid, now, lambda r: r.get("model") == agent.get("model") and agent.get("model"), 10, 1_800_000)
            or recent(valid, now, lambda r: r["source"] == agent["source"], 10, 1_800_000))


def live_rate(responses, agents, now):
    w0 = now - WINDOW_MS
    valid = [r for r in responses if interval(r) is not None and is_final(r)]
    unmeasured = [r for r in responses if interval(r) is not None and not is_final(r)]
    by_id = {a["id"]: a for a in agents}
    measured = [0.0, 0.0]
    for r in valid:
        s, e = interval(r)
        frac = overlap(w0, now, s, e) / (e - s)
        measured[0] += r["output"] * frac
        measured[1] += (r["output"] + r["input"]) * frac
    estimated = [0.0, 0.0]
    for r in unmeasured:
        agent = by_id.get(r["agentId"], {"id": r["agentId"], "model": r.get("model"), "source": r["source"]})
        sp = estimated_speed(valid, now, agent)
        if sp is None:
            continue
        s, e = interval(r)
        secs = overlap(w0, now, s, e) / 1000
        estimated[0] += sp[0] * secs
        estimated[1] += sp[1] * secs
    for a in agents:
        if a["phase"] != "generating" or a.get("inFlightStartMs") is None or a.get("lastLineMs") is None:
            continue
        if now - a["lastLineMs"] > 300_000:
            continue
        sp = estimated_speed(valid, now, a)
        if sp is None:
            continue
        own_ends = [r["endMs"] for r in responses if r["agentId"] == a["id"]]
        start = max([a["inFlightStartMs"]] + own_ends)
        secs = overlap(w0, now, start, now) / 1000
        estimated[0] += sp[0] * secs
        estimated[1] += sp[1] * secs
    out = (measured[0] + estimated[0]) / (WINDOW_MS / 1000)
    tot = (measured[1] + estimated[1]) / (WINDOW_MS / 1000)
    denom = measured[0] + estimated[0]
    return out, tot, (estimated[0] / denom if denom > 0 else 0.0)


def buckets(responses, first, last):
    values = {}
    for r in responses:
        iv = interval(r)
        if iv is None or not is_final(r):
            continue
        s, e = iv
        k = (s // 1000) * 1000
        while k < e:
            if first <= k <= last:
                values[k] = values.get(k, 0.0) + r["output"] * overlap(k, k + 1000, s, e) / (e - s)
            k += 1000
    return values


def today_stats(responses, today_start, now):
    today = [r for r in responses if r["endMs"] >= today_start]
    valid = [r for r in today if interval(r) is not None and is_final(r)]
    sp = speed_of(valid)
    b = buckets(today, today_start, now)
    peak = 0.0
    for k in b:
        peak = max(peak, sum(b.get(k - i * 1000, 0.0) for i in range(3)) / 3)
    return dict(requests=len(today), output=sum(r["output"] for r in today),
                unfinalized=sum(1 for r in today if not is_final(r)),
                input=sum(r["input"] for r in today), average=sp[0] if sp else None, peak=peak, buckets=b)


# ----------------------------------------------------------------------------- commands

def check_fixtures():
    root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures")
    expected = json.load(open(os.path.join(root, "parser-expected.json"), encoding="utf-8"))
    dedupe = Deduper()
    failures = []
    for rel in expected["order"]:
        exp = expected["files"][rel]
        source = "codex" if rel.startswith("codex/") else "claudeCode"
        state = FileState(os.path.join(root, rel), source)
        process_file(state, dedupe)
        got = dict(source=state.source, project=state.project, model=state.model, isSubagent=state.is_subagent,
                   phase=state.phase, inFlightStartMs=state.in_flight, lastLineMs=state.last_line,
                   malformedLines=state.malformed, responses=list(state.responses.values()))
        for k, v in exp.items():
            if got[k] != v:
                failures.append("%s: %s expected %r got %r" % (rel, k, v, got[k]))
    cases = json.load(open(os.path.join(root, "engine-cases.json"), encoding="utf-8"))["cases"]
    for case in cases:
        for q in case["queries"]:
            out, tot, share = live_rate(case["responses"], case["agents"], q["nowMs"])
            for name, val in (("liveOutput", out), ("liveTotal", tot), ("estimatedShare", share)):
                if abs(val - q[name]) > 1e-6:
                    failures.append("%s @%d: %s expected %r got %r" % (case["name"], q["nowMs"], name, q[name], val))
            if "todayRequests" in q:
                st = today_stats(case["responses"], case.get("todayStartMs", 0), q["nowMs"])
                checks = (("todayRequests", st["requests"]), ("todayOutput", st["output"]),
                          ("todayInput", st["input"]), ("averageToday", st["average"]),
                          ("peakTodayOutput", st["peak"]), ("todayUnfinalized", st["unfinalized"]))
                for name, val in checks:
                    if abs((val or 0) - q[name]) > 1e-6:
                        failures.append("%s: %s expected %r got %r" % (case["name"], name, q[name], val))
                for k, v in q["historyMeasuredOutput"].items():
                    if abs(st["buckets"].get(int(k), 0.0) - v) > 1e-6:
                        failures.append("%s: bucket %s expected %r got %r" % (case["name"], k, v,
                                                                              st["buckets"].get(int(k))))
    if failures:
        print("\n".join(failures))
        sys.exit(1)
    print("reference implementation matches all fixtures (%d files, %d engine cases)"
          % (len(expected["order"]), len(cases)))


def discover(root, limit=20_000):
    found = []
    if not os.path.isdir(root):
        return found
    base_depth = root.rstrip(os.sep).count(os.sep)
    for dirpath, dirnames, filenames in os.walk(root, followlinks=False):
        if dirpath.count(os.sep) - base_depth >= 6:
            dirnames[:] = []
        dirnames[:] = [d for d in dirnames if not os.path.islink(os.path.join(dirpath, d))]
        for name in filenames:
            path = os.path.join(dirpath, name)
            if name.endswith(".jsonl") and not os.path.islink(path):
                found.append(path)
    found.sort(key=lambda p: os.path.getmtime(p), reverse=True)
    return found[:limit]


def dump_today(codex_root, claude_root):
    started = time.time()
    now = datetime.now().astimezone()
    today_start = int(now.replace(hour=0, minute=0, second=0, microsecond=0).timestamp() * 1000)
    files = [(p, "codex") for p in discover(codex_root)] + [(p, "claudeCode") for p in discover(claude_root)]
    files = [(os.path.getmtime(p), p, s) for p, s in files]
    files.sort(key=lambda t: (t[0], t[1]))
    dedupe = Deduper()
    rows, read_files, read_bytes = [], 0, 0
    for mtime, path, source in files:
        if mtime * 1000 < today_start - 15 * 60_000:
            continue
        size = os.path.getsize(path)
        offset = size - 64 * 1024 * 1024 if size > 256 * 1024 * 1024 else 0
        state = FileState(path, source)
        process_file(state, dedupe, offset)
        read_files += 1
        read_bytes += size - offset
        for r in state.responses.values():
            if r["endMs"] >= today_start:
                rows.append(OrderedDict([("key", r["key"]), ("source", state.source), ("agentId", path),
                                         ("model", r["model"]), ("startMs", r["startMs"]), ("endMs", r["endMs"]),
                                         ("input", r["input"]), ("cachedInput", r["cachedInput"]),
                                         ("cacheWriteInput", r["cacheWriteInput"]), ("output", r["output"]),
                                         ("reasoning", r["reasoning"]), ("final", r["final"])]))
    rows.sort(key=lambda r: (r["endMs"], r["key"]))
    for r in rows:
        print(json.dumps(r, ensure_ascii=False, separators=(",", ":")))
    counts = {}
    for r in rows:
        counts[r["source"]] = counts.get(r["source"], 0) + 1
    print(json.dumps({"summary": {"counts": counts, "output": sum(r["output"] for r in rows),
                                  "input": sum(r["input"] for r in rows), "filesRead": read_files,
                                  "bytesRead": read_bytes, "seedMs": int((time.time() - started) * 1000)}}))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-fixtures", action="store_true")
    parser.add_argument("--dump-today", action="store_true")
    parser.add_argument("--codex", default=None)
    parser.add_argument("--claude", default=None)
    args = parser.parse_args()
    home = os.path.expanduser("~")
    codex_root = args.codex or (os.path.join(os.environ["CODEX_HOME"], "sessions") if os.environ.get("CODEX_HOME")
                                else os.path.join(home, ".codex", "sessions"))
    claude_root = args.claude or (os.path.join(os.environ["CLAUDE_CONFIG_DIR"], "projects")
                                  if os.environ.get("CLAUDE_CONFIG_DIR") else os.path.join(home, ".claude", "projects"))
    if args.check_fixtures:
        check_fixtures()
    elif args.dump_today:
        dump_today(codex_root, claude_root)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
