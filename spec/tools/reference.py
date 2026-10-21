#!/usr/bin/env python3
"""Independent reference implementation of spec/METRICS.md, used to cross-check the Swift and C# cores.

  python3 spec/tools/reference.py --check-fixtures
  python3 spec/tools/reference.py --dump-today [--codex PATH] [--claude PATH] [--dsh PATH] [--gemini PATH]
  python3 spec/tools/reference.py --dump-history [--utc] [...]
  python3 spec/tools/reference.py --write-history-fixture

The dump format matches the `--dump-today` output of both platform probes. Conversation content is never printed.
"""
import argparse
import sqlite3
import subprocess
import json
import os
import re
import shutil
import sys
import tempfile
import time
from collections import OrderedDict
from datetime import datetime, timedelta, timezone

MAX_LINE = 32 * 1024 * 1024
DEDUPE_LIMIT = 65_536
WINDOW_MS = 3000
TS_RE = re.compile(r"^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2}):(\d{2})(?:\.(\d+))?(Z|[+-]\d{2}:?\d{2})?$")
TOKEN_FIELDS = ("input", "cachedInput", "cacheWriteInput", "output", "reasoning")
GEMINI_LEGACY_MAX = 64 * 1024 * 1024
GEMINI_SANITIZE = re.compile(r"[^A-Za-z0-9_-]")
GEMINI_CANCELLED = "Request cancelled."


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
        self.is_subagent = source == "claudeCode" and "subagents" in [p for p in re.split(r"[\\/]", path) if p]
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
        self.parent_thread_id = None
        self.streaming = False
        self.saw_modern = False
        self.prev_legacy = None
        # claude
        self.precise = False
        # deepseek, gemini
        self.parent_session = None
        # gemini
        self.marker = None
        self.seen_calls = set()
        if source == "gemini":
            parts = [p for p in re.split(r"[\\/]", path) if p]
            nested = len(parts) >= 3 and parts[-3] == "chats"
            self.source_decided = True
            self.is_subagent = nested
            self.parent_session = parts[-2] if nested else None
            self.project = gemini_project(path)


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
        subagent = source.get("subagent") if isinstance(source, dict) else None
        spawn = subagent.get("thread_spawn") if isinstance(subagent, dict) else None
        state.parent_thread_id = payload.get("parent_thread_id") or (
            spawn.get("parent_thread_id") if isinstance(spawn, dict) else None)
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
        state.source = "claudeDesktop" if obj["entrypoint"].startswith("claude-desktop") else "claudeCode"
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


ZSTD_MAGIC = 0xFD2FB528
SKIPPABLE_MAGIC = re.compile(rb"[\x50-\x5f]\x2a\x4d\x18")


def zstd_frame_size(data, pos):
    """Compressed size of the frame starting at pos, or None when it is not complete yet (zstd format spec)."""
    if len(data) - pos < 8:
        return None
    magic = int.from_bytes(data[pos:pos + 4], "little")
    if magic & 0xFFFFFFF0 == 0x184D2A50:
        size = 8 + int.from_bytes(data[pos + 4:pos + 8], "little")
        return size if len(data) - pos >= size else None
    if magic != ZSTD_MAGIC:
        raise ValueError("not a zstd frame")
    fhd = data[pos + 4]
    single, fcs_flag, checksum, dict_flag = (fhd >> 5) & 1, fhd >> 6, (fhd >> 2) & 1, fhd & 3
    header = 1 + (0 if single else 1) + [0, 1, 2, 4][dict_flag] + [1 if single else 0, 2, 4, 8][fcs_flag]
    cur = pos + 4 + header
    while True:
        if len(data) - cur < 3:
            return None
        bh = int.from_bytes(data[cur:cur + 3], "little")
        last, kind, size = bh & 1, (bh >> 1) & 3, bh >> 3
        cur += 3 + (1 if kind == 1 else size)
        if kind == 3:
            raise ValueError("reserved block type")
        if last:
            break
    cur += 4 if checksum else 0
    return cur - pos if cur <= len(data) else None


def zstd_lines(path, start_offset=0):
    data = open(path, "rb").read()
    pos, pending, malformed = start_offset, b"", 0
    while pos < len(data):
        try:
            size = zstd_frame_size(data, pos)
        except ValueError:
            malformed += 1
            candidates = [i for i in [data.find(b"\x28\xb5\x2f\xfd", pos + 1)] if i >= 0]
            skippable = SKIPPABLE_MAGIC.search(data, pos + 1)
            if skippable:
                candidates.append(skippable.start())
            if not candidates:
                break
            pos = min(candidates)
            continue
        if size is None:
            break
        frame = data[pos:pos + size]
        pos += size
        if int.from_bytes(frame[:4], "little") != ZSTD_MAGIC:
            continue
        out = subprocess.run(["zstd", "-dc", "-q"], input=frame, capture_output=True)
        if out.returncode != 0 or len(out.stdout) > 64 * 1024 * 1024:
            malformed += 1
            continue
        pending += out.stdout
        *complete, pending = pending.split(b"\n")
        for raw in complete:
            if raw.strip() and len(raw) <= MAX_LINE:
                yield raw
    if malformed:
        yield ("malformed", malformed)


def read_lines(path, start_offset=0, max_lines=None):
    with open(path, "rb") as f:
        f.seek(start_offset)
        data = f.read()
    if start_offset:
        nl = data.find(b"\n")
        data = data[nl + 1:] if nl >= 0 else b""
    end = data.rfind(b"\n")
    if end < 0:
        return
    for count, raw in enumerate(data[:end].split(b"\n"), start=1):
        if max_lines is not None and count > max_lines:
            return
        if len(raw) > MAX_LINE or not raw.strip():
            continue
        yield raw


def zst_stream_lines(path):
    """A compressed Codex session (METRICS 5.3): decompressed as one stream, always from the start."""
    proc = subprocess.Popen(["zstd", "-dc", "-q", path], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    pending = b""
    try:
        for chunk in iter(lambda: proc.stdout.read(1 << 20), b""):
            pending += chunk
            *complete, pending = pending.split(b"\n")
            for raw in complete:
                if raw.strip() and len(raw) <= MAX_LINE:
                    yield raw
            if len(pending) > MAX_LINE:
                pending = b""
    finally:
        proc.stdout.close()
        if proc.wait() != 0:
            yield ("malformed", 1)


def process_file(state, dedupe, start_offset=0, max_lines=None):
    if state.source == "gemini" and state.path.endswith(".json"):
        return gemini_legacy(state, dedupe)
    handler = {"codex": codex_line, "deepseek": dsh_event, "gemini": gemini_record}.get(state.source, claude_line)
    if state.path.endswith(".zstd"):
        lines = zstd_lines(state.path, start_offset)
    elif state.path.endswith(".jsonl.zst"):
        lines = zst_stream_lines(state.path)
    else:
        lines = read_lines(state.path, start_offset, max_lines)
    for raw in lines:
        if isinstance(raw, tuple):
            state.malformed += raw[1]
            continue
        try:
            obj = json.loads(raw)
        except (ValueError, UnicodeDecodeError):
            state.malformed += 1
            continue
        if not isinstance(obj, dict):
            state.malformed += 1
            continue
        handler(state, obj, dedupe)


def resolve_parents(states):
    """Maps agent id (path) to its parent agent id per METRICS 6.7."""
    by_path = {s.path: s for s in states}
    by_thread, by_session = {}, {}
    for s in sorted(states, key=lambda s: (s.file_created or 0, s.path)):
        if s.source == "codex" and s.thread_id and s.thread_id not in by_thread:
            by_thread[s.thread_id] = s.path
        if s.source == "deepseek" and s.thread_id and s.thread_id not in by_session:
            by_session[s.thread_id] = s.path
    gemini_mains = {}
    for s in sorted((s for s in states if s.source == "gemini" and s.thread_id),
                    key=lambda s: (0 if s.path.endswith(".jsonl") else 1, s.file_created or 0, s.path)):
        parts = re.split(r"[\\/]", s.path)
        if len(parts) >= 2 and parts[-2] == "chats":
            gemini_mains.setdefault((os.sep.join(parts[:-1]), GEMINI_SANITIZE.sub("_", s.thread_id)), s.path)
    parents = {}
    for s in states:
        parent = None
        if s.source == "codex":
            parent = by_thread.get(s.parent_thread_id) if s.parent_thread_id else None
        elif s.source == "deepseek":
            parent = by_session.get(s.parent_session) if s.parent_session else None
        elif s.source == "gemini":
            chats = os.sep.join(re.split(r"[\\/]", s.path)[:-2])
            parent = gemini_mains.get((chats, s.parent_session)) if s.parent_session else None
        else:
            parts = re.split(r"[\\/]", s.path)
            if len(parts) >= 3 and parts[-2] == "subagents":
                candidate = s.path[:len(s.path) - len(parts[-1]) - len(parts[-2]) - 2] + ".jsonl"
                parent = candidate if candidate in by_path else None
        parents[s.path] = parent if parent != s.path else None
    for path in list(parents):
        seen, cur = set(), path
        while cur is not None and cur not in seen:
            seen.add(cur)
            cur = parents.get(cur)
        if cur is not None:
            parents[path] = None
    return parents


def dsh_event(state, obj, dedupe):
    kind = obj.get("type")
    if kind == "event" and isinstance(obj.get("event"), dict):
        return dsh_event(state, obj["event"], dedupe)
    if kind == "snapshot":
        if isinstance(obj.get("header"), dict):
            dsh_header(state, obj["header"])
        for record in obj.get("records") or []:
            if isinstance(record, dict):
                dsh_event(state, record, dedupe)
        return
    if kind == "session":
        dsh_header(state, obj)
        return
    ts = obj.get("time")
    if isinstance(ts, bool) or not isinstance(ts, (int, float)):
        return
    ts = int(ts)
    if state.file_created is not None and ts < state.file_created - 2000:
        return
    state.last_line = ts if state.last_line is None else max(state.last_line, ts)
    data = obj.get("data") if isinstance(obj.get("data"), dict) else {}
    if kind == "request/header":
        config = (data.get("header") or {}).get("config") if isinstance(data.get("header"), dict) else None
        if isinstance(config, dict) and isinstance(config.get("model"), str) and config["model"]:
            state.model = config["model"]
    elif kind == "step/start":
        state.pending, state.phase, state.in_flight = ts, "generating", ts
    elif kind == "turn/end":
        state.phase, state.pending, state.in_flight = "idle", None, None
    elif kind == "assistant/message":
        message = data.get("message") if isinstance(data.get("message"), dict) else {}
        key = message.get("id") or "dsh:%s:%s" % (state.thread_id, obj.get("seq"))
        if dedupe.owner(key) is not None:
            return
        dedupe.claim(key, state.path)
        usage = data.get("usage")
        if isinstance(usage, dict):
            cr, cw = num(usage.get("cacheReadTokens")), num(usage.get("cacheWriteTokens"))
            tokens = dict(input=num(usage.get("inputTokens")) + cr + cw, cachedInput=cr, cacheWriteInput=cw,
                          output=num(usage.get("outputTokens")), reasoning=0)
            source = message.get("source") if isinstance(message.get("source"), dict) else {}
            model = source.get("model") or state.model
            emit(state, key, state.pending, ts, tokens, model)
            state.model = model or state.model
        content = message.get("content") if isinstance(message.get("content"), list) else []
        tool = any(isinstance(b, dict) and b.get("type") == "tool-call" for b in content)
        state.pending, state.phase, state.in_flight = None, ("tools" if tool else "idle"), None


def dsh_header(state, header):
    if state.file_created is not None:
        return
    created = header.get("createdAt")
    state.file_created = int(created) if isinstance(created, (int, float)) and not isinstance(created, bool) else 0
    state.thread_id = header.get("id") or state.thread_id
    state.project = basename(header.get("cwd")) or state.project
    parent = header.get("parentSession")
    depth = header.get("delegationDepth")
    state.parent_session = parent if isinstance(parent, str) and parent else None
    state.is_subagent = state.is_subagent or header.get("origin") == "subagent" or bool(state.parent_session) or (
        isinstance(depth, (int, float)) and not isinstance(depth, bool) and depth > 0)


def opencode_states(db_path, dedupe):
    """Parses an OpenCode database (METRICS 4.5); returns one state per session, keyed like agent ids."""
    db = sqlite3.connect("file:%s?mode=ro" % db_path, uri=True, timeout=2)
    try:
        tables = {r[0] for r in db.execute("select name from sqlite_master where type='table'")}
        if not {"session_v2", "session_message"} <= tables:
            return []
        sessions = {r[0]: r for r in db.execute(
            "select id, parent_id, directory, time_created from session_v2")}
        states = {}
        for sid, parent, directory, created in sessions.values():
            st = FileState("opencode:" + sid, "opencode")
            st.project = basename(directory)
            st.is_subagent = parent is not None
            st.parent_session = ("opencode:" + parent) if parent in sessions else None
            st.file_created = created
            states[sid] = st
        rows = db.execute("select id, session_id, type, time_updated, data from session_message "
                          "order by session_id, seq")
        for rid, sid, kind, updated, data in rows:
            st = states.get(sid)
            if st is None:
                continue
            try:
                d = json.loads(data)
            except ValueError:
                st.malformed += 1
                continue
            if not isinstance(d, dict):
                st.malformed += 1
                continue
            times = d.get("time") if isinstance(d.get("time"), dict) else {}
            if kind == "assistant":
                created = times.get("created")
                if isinstance(created, (int, float)) and created < st.file_created - 2000:
                    continue
                st.last_line = updated if st.last_line is None else max(st.last_line, updated)
                model = (d.get("model") or {}).get("id") if isinstance(d.get("model"), dict) else None
                st.model = model or st.model
                if "streamed" not in times:
                    st.phase, st.in_flight = "generating", created
                    continue
                tokens = d.get("tokens")
                if isinstance(tokens, dict) and dedupe.owner(rid) in (None, st.path):
                    dedupe.claim(rid, st.path)
                    emit(st, rid, created, times["streamed"], opencode_tokens(tokens), model)
                st.phase = "tools" if d.get("finish") == "tool-calls" else "idle"
                st.in_flight = None
            else:
                st.last_line = updated if st.last_line is None else max(st.last_line, updated)
                if kind == "compaction" and isinstance(d.get("tokens"), dict):
                    if dedupe.owner(rid) in (None, st.path):
                        dedupe.claim(rid, st.path)
                        model = (d.get("model") or {}).get("id") if isinstance(d.get("model"), dict) else None
                        emit(st, rid, times.get("created"), times.get("completed", updated),
                             opencode_tokens(d["tokens"]), model)
                elif kind == "idle":
                    st.phase, st.in_flight = "idle", None
        return list(states.values())
    finally:
        db.close()


def opencode_tokens(t):
    cache = t.get("cache") if isinstance(t.get("cache"), dict) else {}
    cr, cw, r = num(cache.get("read")), num(cache.get("write")), num(t.get("reasoning"))
    return dict(input=num(t.get("input")) + cr + cw, cachedInput=cr, cacheWriteInput=cw,
                output=num(t.get("output")) + r, reasoning=r)


def gemini_tokens(t):
    return dict(input=num(t.get("input")) + num(t.get("tool")), cachedInput=num(t.get("cached")), cacheWriteInput=0,
                output=num(t.get("output")) + num(t.get("thoughts")), reasoning=num(t.get("thoughts")))


def gemini_content(content):
    """(has visible non-thought text, has a functionCall part)."""
    if isinstance(content, str):
        return bool(content.strip()), False
    if isinstance(content, dict):
        content = [content]
    text = call = False
    if isinstance(content, list):
        for part in content:
            if isinstance(part, str):
                text = text or bool(part.strip())
            elif isinstance(part, dict):
                if isinstance(part.get("functionCall"), dict):
                    call = True
                value = part.get("text")
                if isinstance(value, str) and value.strip() and part.get("thought") is not True:
                    text = True
    return text, call


def gemini_touch(state, ts):
    if ts is not None:
        state.last_line = ts if state.last_line is None else max(state.last_line, ts)


def gemini_idle(state):
    state.phase, state.pending, state.marker, state.in_flight = "idle", None, None, None


def gemini_generating(state, start, marker):
    state.phase, state.pending, state.marker, state.in_flight = "generating", start, marker, start


def gemini_model(msg):
    return msg.get("model") if isinstance(msg.get("model"), str) and msg.get("model") else None


def gemini_update(state, resp, end, tokens, model):
    resp.update(tokens)
    resp["endMs"] = max(resp["endMs"], end)
    resp["model"] = model or resp["model"]
    state.model = resp["model"] or state.model


def gemini_record(state, obj, dedupe):
    """One line of a Gemini CLI session (METRICS 4.6)."""
    if isinstance(obj.get("$rewindTo"), str):
        gemini_idle(state)
    elif isinstance(obj.get("$patch"), dict):
        removed = obj["$patch"].get("removeIds")
        if state.phase == "generating" and isinstance(removed, list) and state.marker in removed:
            gemini_idle(state)
    elif isinstance(obj.get("id"), str):
        gemini_message(state, obj, dedupe)
    elif isinstance(obj.get("$set"), dict):
        gemini_set(state, obj["$set"], dedupe)
    elif isinstance(obj.get("sessionId"), str) and isinstance(obj.get("projectHash"), str):
        gemini_header(state, obj, dedupe)


def gemini_header(state, obj, dedupe):
    state.thread_id = obj["sessionId"]
    if obj.get("kind") == "subagent":
        state.is_subagent = True
    started = parse_ts(obj.get("startTime"))
    if state.file_created is None:
        state.file_created = started
    gemini_touch(state, started)
    gemini_touch(state, parse_ts(obj.get("lastUpdated")))
    if isinstance(obj.get("messages"), list):
        for msg in obj["messages"]:
            if isinstance(msg, dict) and isinstance(msg.get("id"), str):
                gemini_message(state, msg, dedupe)


def gemini_set(state, values, dedupe):
    gemini_touch(state, parse_ts(values.get("lastUpdated")))
    if values.get("kind") == "subagent":
        state.is_subagent = True
    if isinstance(values.get("sessionId"), str):
        state.thread_id = values["sessionId"]
    if isinstance(values.get("messages"), list):
        gemini_checkpoint(state, values["messages"], dedupe)
    if isinstance(values.get("summary"), str) and values["summary"]:
        gemini_idle(state)


def gemini_checkpoint(state, messages, dedupe):
    ids = set()
    for msg in messages:
        if not isinstance(msg, dict) or not isinstance(msg.get("id"), str):
            continue
        ids.add(msg["id"])
        if msg.get("type") != "gemini" or not isinstance(msg.get("tokens"), dict):
            continue
        key, ts = msg["id"], parse_ts(msg.get("timestamp"))
        if ts is None or dedupe.owner(key) not in (None, state.path):
            continue
        tokens, model = gemini_tokens(msg["tokens"]), gemini_model(msg)
        resp = state.responses.get(key)
        if resp is None:
            dedupe.claim(key, state.path)
            emit(state, key, None, ts, tokens, model)
            state.model = model or state.model
        else:
            gemini_update(state, resp, ts, tokens, model)
    if state.phase == "generating" and state.marker not in ids:
        gemini_idle(state)


def gemini_message(state, msg, dedupe):
    ts = parse_ts(msg.get("timestamp"))
    if ts is None:
        return
    gemini_touch(state, ts)
    calls = msg.get("toolCalls") if isinstance(msg.get("toolCalls"), list) else []
    calls = [c for c in calls if isinstance(c, dict) and isinstance(c.get("id"), str)]
    call_ts = [t for t in (parse_ts(c.get("timestamp")) for c in calls) if t is not None]
    for t in call_ts:
        gemini_touch(state, t)
    for th in msg.get("thoughts") if isinstance(msg.get("thoughts"), list) else []:
        if isinstance(th, dict):
            gemini_touch(state, parse_ts(th.get("timestamp")))
    kind = msg.get("type")
    if kind == "user":
        gemini_generating(state, ts, msg["id"])
        return
    if kind == "error" or (kind == "info" and msg.get("content") == GEMINI_CANCELLED):
        gemini_idle(state)
        return
    if kind != "gemini":
        return
    key = msg["id"]
    if dedupe.owner(key) not in (None, state.path):
        return
    if isinstance(msg.get("tokens"), dict):
        tokens, model = gemini_tokens(msg["tokens"]), gemini_model(msg)
        resp = state.responses.get(key)
        if resp is None:
            start = None if call_ts and ts >= min(call_ts) else state.pending
            dedupe.claim(key, state.path)
            emit(state, key, start, ts, tokens, model)
            state.model = model or state.model
            has_text, has_call = gemini_content(msg.get("content"))
            state.phase = "tools" if has_call or not has_text else "idle"
            state.pending, state.marker, state.in_flight = None, None, None
        else:
            gemini_update(state, resp, ts, tokens, model)
    new_calls = [c for c in calls if c["id"] not in state.seen_calls]
    state.seen_calls.update(c["id"] for c in new_calls)
    if not new_calls:
        return
    if any(c.get("name") == "complete_task" and c.get("status") == "success" for c in new_calls):
        gemini_idle(state)
    elif all(c.get("status") == "cancelled" for c in new_calls):
        gemini_idle(state)
    else:
        times = [t for t in (parse_ts(c.get("timestamp")) for c in new_calls) if t is not None]
        gemini_generating(state, max(times) if times else ts, key)


def gemini_legacy(state, dedupe):
    """A legacy (<= v0.38) whole-document session file: replayed from the start on every change."""
    if os.path.getsize(state.path) > GEMINI_LEGACY_MAX:
        state.partial_history = True
        return
    try:
        with open(state.path, "rb") as f:
            obj = json.loads(f.read())
    except (ValueError, UnicodeDecodeError):
        state.malformed += 1
        return
    if isinstance(obj, dict) and isinstance(obj.get("sessionId"), str):
        gemini_header(state, obj, dedupe)
    else:
        state.malformed += 1


_gemini_projects = {}


def gemini_project(path):
    parts = re.split(r"[\\/]", path)
    if len(parts) >= 3 and parts[-2] == "chats":
        project_dir = os.sep.join(parts[:-2])
    elif len(parts) >= 4 and parts[-3] == "chats":
        project_dir = os.sep.join(parts[:-3])
    else:
        return None
    if project_dir in _gemini_projects:
        return _gemini_projects[project_dir]
    project = None
    try:
        with open(os.path.join(project_dir, ".project_root"), encoding="utf-8") as f:
            project = basename(f.read().strip())
    except OSError:
        try:
            registry = os.path.join(os.path.dirname(os.path.dirname(project_dir)), "projects.json")
            with open(registry, encoding="utf-8") as f:
                projects = json.load(f).get("projects")
            slug = os.path.basename(project_dir)
            if isinstance(projects, dict):
                project = next((basename(k) for k, v in projects.items() if v == slug), None)
        except (OSError, ValueError, AttributeError):
            project = None
    _gemini_projects[project_dir] = project
    return project


def gemini_roots(env=None, platform=None):
    env = os.environ if env is None else env
    platform = sys.platform if platform is None else platform
    home = env.get("GEMINI_CLI_HOME") or os.path.expanduser("~")
    found = [os.path.join(home, ".gemini", "tmp")]
    if platform == "darwin":
        found.append(os.path.join(home, ".cache", ".gemini", "tmp"))
    return found


def discover_gemini(root, limit=20_000):
    found = []
    if not os.path.isdir(root):
        return found
    for project in sorted(os.listdir(root)):
        chats = os.path.join(root, project, "chats")
        if os.path.islink(os.path.join(root, project)) or os.path.islink(chats) or not os.path.isdir(chats):
            continue
        for name in os.listdir(chats):
            full = os.path.join(chats, name)
            if os.path.islink(full):
                continue
            if os.path.isdir(full):
                for sub in os.listdir(full):
                    nested = os.path.join(full, sub)
                    if sub.endswith((".jsonl", ".json")) and os.path.isfile(nested) and not os.path.islink(nested):
                        found.append(nested)
            elif name.endswith((".jsonl", ".json")) and os.path.isfile(full):
                found.append(full)
    found.sort(key=lambda p: os.path.getmtime(p), reverse=True)
    return found[:limit]


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


def agent_list(responses, agents, now):
    by_id = {a["id"]: dict(a) for a in agents}
    for a in by_id.values():
        if a["phase"] == "generating" and a.get("lastLineMs") is not None and now - a["lastLineMs"] > 300_000:
            a["phase"] = "idle"
    live = {aid: live_rate([r for r in responses if r["agentId"] == aid], [a], now)[0] for aid, a in by_id.items()}
    chosen = {aid for aid, a in by_id.items()
              if a["phase"] == "generating" or (a.get("lastLineMs") is not None and now - a["lastLineMs"] <= 600_000)}
    for aid in list(chosen):
        cur = by_id[aid].get("parentId")
        while cur in by_id and cur not in chosen:
            chosen.add(cur)
            cur = by_id[cur].get("parentId")
    parent = {aid: (by_id[aid].get("parentId") if by_id[aid].get("parentId") in chosen else None) for aid in chosen}
    children = {}
    for aid in chosen:
        children.setdefault(parent[aid], []).append(aid)

    def members(aid):
        out = [aid]
        for c in children.get(aid, []):
            out += members(c)
        return out

    def last(aid):
        return by_id[aid].get("lastLineMs") or 0

    def sibling_key(aid):
        return (0 if by_id[aid]["phase"] == "generating" else 1, -live[aid], -last(aid), aid)

    def group_key(root):
        m = members(root)
        return (0 if any(by_id[x]["phase"] == "generating" for x in m) else 1, -sum(live[x] for x in m),
                -max(last(x) for x in m), root)

    order = []

    def walk(aid):
        order.append(aid)
        for c in sorted(children.get(aid, []), key=sibling_key):
            walk(c)

    for root in sorted(children.get(None, []), key=group_key):
        walk(root)
    return order, {aid: p for aid, p in parent.items() if p is not None}


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
    states = []
    for rel in expected["order"]:
        exp = expected["files"][rel]
        state = FileState(os.path.join(root, rel), fixture_source(rel))
        process_file(state, dedupe)
        states.append(state)
        got = dict(source=state.source, project=state.project, model=state.model, isSubagent=state.is_subagent,
                   phase=state.phase, inFlightStartMs=state.in_flight, lastLineMs=state.last_line,
                   malformedLines=state.malformed, responses=list(state.responses.values()))
        for k, v in exp.items():
            if k != "parentFile" and got[k] != v:
                failures.append("%s: %s expected %r got %r" % (rel, k, v, got[k]))
    oc = expected.get("opencode")
    if oc:
        for st in opencode_states(os.path.join(root, oc["database"]), dedupe):
            exp = oc["agents"].get(st.path)
            if exp is None:
                failures.append("opencode: unexpected agent %s" % st.path)
                continue
            got = dict(source=st.source, project=st.project, model=st.model, isSubagent=st.is_subagent,
                       parentId=st.parent_session, phase=st.phase, inFlightStartMs=st.in_flight,
                       lastLineMs=st.last_line, responses=list(st.responses.values()))
            for k, v in exp.items():
                if got[k] != v:
                    failures.append("%s: %s expected %r got %r" % (st.path, k, v, got[k]))
    parents = resolve_parents(states)
    for rel in expected["order"]:
        parent = parents[os.path.join(root, rel)]
        got = os.path.relpath(parent, root) if parent else None
        if got != expected["files"][rel]["parentFile"]:
            failures.append("%s: parentFile expected %r got %r" % (rel, expected["files"][rel]["parentFile"], got))
    gm = expected.get("gemini")
    if gm:
        groot = os.path.join(root, "gemini", "tmp")
        found = sorted(os.path.relpath(p, root).replace(os.sep, "/") for p in discover_gemini(groot))
        if found != gm["discover"]:
            failures.append("gemini discover: expected %r got %r" % (gm["discover"], found))
        for t in gm["traces"]:
            trace_dedupe = Deduper()
            for rel in expected["order"]:
                if rel == t["file"]:
                    break
                process_file(FileState(os.path.join(root, rel), fixture_source(rel)), trace_dedupe)
            state = FileState(os.path.join(root, t["file"]), "gemini")
            process_file(state, trace_dedupe, max_lines=t["afterLine"])
            if (state.phase, state.in_flight) != (t["phase"], t["inFlightStartMs"]):
                failures.append("%s line %d: expected %s/%r got %s/%r" % (
                    t["file"], t["afterLine"], t["phase"], t["inFlightStartMs"], state.phase, state.in_flight))
        if gemini_roots({"GEMINI_CLI_HOME": "/x"}, "linux") != [os.path.join("/x", ".gemini", "tmp")]:
            failures.append("gemini roots: GEMINI_CLI_HOME not honoured")
        if len(gemini_roots({"GEMINI_CLI_HOME": "/x"}, "darwin")) != 2:
            failures.append("gemini roots: macOS sandbox root missing")
    hist_path = os.path.join(root, "history-expected.json")
    if os.path.exists(hist_path):
        got = fixture_history(root, expected)
        want = json.load(open(hist_path, encoding="utf-8"))["days"]
        if json.loads(json.dumps(got)) != want:
            failures.append("history: expected %s got %s" % (json.dumps(want)[:300], json.dumps(got)[:300]))
    cases = json.load(open(os.path.join(root, "engine-cases.json"), encoding="utf-8"))["cases"]
    for case in cases:
        for q in case["queries"]:
            out, tot, share = live_rate(case["responses"], case["agents"], q["nowMs"])
            for name, val in (("liveOutput", out), ("liveTotal", tot), ("estimatedShare", share)):
                if abs(val - q[name]) > 1e-6:
                    failures.append("%s @%d: %s expected %r got %r" % (case["name"], q["nowMs"], name, q[name], val))
            if "agentsOrder" in q:
                order, parent_map = agent_list(case["responses"], case["agents"], q["nowMs"])
                if order != q["agentsOrder"] or parent_map != q["agentParents"]:
                    failures.append("%s: agents %r %r" % (case["name"], order, parent_map))
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


def fixture_source(rel):
    for prefix, source in (("codex/", "codex"), ("dsh/", "deepseek"), ("gemini/", "gemini")):
        if rel.startswith(prefix):
            return source
    return "claudeCode"


def fixture_history(root, expected):
    """Daily history over a copy of the fixtures whose modification times follow `mtimes` (git does not keep them)."""
    with tempfile.TemporaryDirectory() as tmp:
        copy = os.path.join(tmp, "fixtures")
        shutil.copytree(root, copy)
        for rel, ms in expected.get("mtimes", {}).items():
            os.utime(os.path.join(copy, rel), (ms / 1000, ms / 1000))
        return daily_history(collect_all(os.path.join(copy, "codex", "sessions"), os.path.join(copy, "claude", "projects"),
                                         os.path.join(copy, "dsh", "sessions"), os.path.join(copy, "opencode", "opencode.db"),
                                         [os.path.join(copy, "gemini", "tmp")]), tz=timezone.utc)


def write_history_fixture():
    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "fixtures"))
    expected = json.load(open(os.path.join(root, "parser-expected.json"), encoding="utf-8"))
    with open(os.path.join(root, "history-expected.json"), "w", encoding="utf-8") as f:
        json.dump({"_comment": "Daily history (METRICS 8) over all fixtures, grouped in UTC, after setting each file's "
                               "modification time from parser-expected.json `mtimes`. Generated by "
                               "spec/tools/reference.py --write-history-fixture; platforms must match exactly.",
                   "days": fixture_history(root, expected)}, f, indent=2, ensure_ascii=False)
        f.write("\n")


DSH_NAME = re.compile(r"^session\.v\d+\.jsonl(\.zstd)?$")


def discover(root, limit=20_000, deepseek=False, codex=False):
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
            wanted = DSH_NAME.match(name) if deepseek else name.endswith((".jsonl", ".jsonl.zst") if codex else ".jsonl")
            if wanted and not os.path.islink(path):
                found.append(path)
    found.sort(key=lambda p: os.path.getmtime(p), reverse=True)
    return found[:limit]


def dump_today(codex_root, claude_root, dsh_root, gemini_dirs=()):
    started = time.time()
    now = datetime.now().astimezone()
    today_start = int(now.replace(hour=0, minute=0, second=0, microsecond=0).timestamp() * 1000)
    files = ([(p, "codex") for p in discover(codex_root, codex=True)] + [(p, "claudeCode") for p in discover(claude_root)]
             + [(p, "deepseek") for p in discover(dsh_root, deepseek=True)]
             + [(p, "gemini") for d in gemini_dirs for p in discover_gemini(d)])
    files = [(os.path.getmtime(p), p, s) for p, s in files]
    files.sort(key=lambda t: (t[0], t[1]))
    dedupe = Deduper()
    rows, read_files, read_bytes = [], 0, 0
    for mtime, path, source in files:
        if mtime * 1000 < today_start - 15 * 60_000:
            continue
        size = os.path.getsize(path)
        if size > 256 * 1024 * 1024 and path.endswith(".zstd"):
            continue
        if source == "gemini" and path.endswith(".json") and size > GEMINI_LEGACY_MAX:
            continue
        offset = size - 64 * 1024 * 1024 if size > 256 * 1024 * 1024 and not path.endswith(".jsonl.zst") else 0
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


def collect_all(codex_root, claude_root, dsh_root, opencode_db, gemini_dirs=(), include_archived=True):
    """Parses every log completely (METRICS 8) and returns all responses with their source and agent."""
    files = [(p, "codex") for p in discover(codex_root, limit=10 ** 9, codex=True)]
    if include_archived:
        archived = os.path.join(os.path.dirname(codex_root.rstrip(os.sep)), "archived_sessions")
        files += [(p, "codex") for p in discover(archived, limit=10 ** 9, codex=True)]
    files += [(p, "claudeCode") for p in discover(claude_root, limit=10 ** 9)]
    files += [(p, "deepseek") for p in discover(dsh_root, limit=10 ** 9, deepseek=True)]
    files += [(p, "gemini") for d in gemini_dirs for p in discover_gemini(d, limit=10 ** 9)]
    files = sorted({(os.path.getmtime(p), p, s) for p, s in files}, key=lambda t: (t[0], t[1]))
    dedupe = Deduper(limit=10 ** 9)
    out = []
    for _, path, source in files:
        state = FileState(path, source)
        process_file(state, dedupe)
        out += [(state.source, path, r) for r in state.responses.values()]
    if opencode_db and os.path.exists(opencode_db):
        for st in opencode_states(opencode_db, dedupe):
            out += [(st.source, st.path, r) for r in st.responses.values()]
    return out


def daily_history(responses, tz=None):
    days = {}
    for source, agent, r in responses:
        date = datetime.fromtimestamp(r["endMs"] / 1000, tz=tz or datetime.now().astimezone().tzinfo)
        key = (date.strftime("%Y-%m-%d") if tz else datetime.fromtimestamp(r["endMs"] / 1000).strftime("%Y-%m-%d"),
               source)
        d = days.setdefault(key, dict(requests=0, input=0, cachedInput=0, cacheWriteInput=0, output=0, reasoning=0,
                                      measuredOutput=0, measuredMs=0, unfinalized=0, agents=set(), models={}))
        d["requests"] += 1
        for f in TOKEN_FIELDS:
            d[f] += r[f]
        iv = interval(r)
        if iv is not None and is_final(r):
            d["measuredOutput"] += r["output"]
            d["measuredMs"] += iv[1] - iv[0]
        if not is_final(r):
            d["unfinalized"] += 1
        d["agents"].add(agent)
        m = d["models"].setdefault(r.get("model") or "", [0, 0])
        m[0] += 1
        m[1] += r["output"]
    rows = []
    for (date, source), d in sorted(days.items()):
        rows.append(OrderedDict([("date", date), ("source", source)] + [(k, d[k]) for k in (
            "requests", "input", "cachedInput", "cacheWriteInput", "output", "reasoning", "measuredOutput",
            "measuredMs", "unfinalized")] + [("agents", len(d["agents"])),
                                             ("models", {k: {"requests": v[0], "output": v[1]}
                                                         for k, v in sorted(d["models"].items())})]))
    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check-fixtures", action="store_true")
    parser.add_argument("--dump-today", action="store_true")
    parser.add_argument("--dump-history", action="store_true")
    parser.add_argument("--opencode", default=None)
    parser.add_argument("--utc", action="store_true", help="group days in UTC instead of local time")
    parser.add_argument("--codex", default=None)
    parser.add_argument("--claude", default=None)
    parser.add_argument("--dsh", default=None)
    parser.add_argument("--gemini", action="append", default=None, help="Gemini CLI tmp folder (repeatable)")
    parser.add_argument("--write-history-fixture", action="store_true")
    args = parser.parse_args()
    home = os.path.expanduser("~")
    codex_root = args.codex or (os.path.join(os.environ["CODEX_HOME"], "sessions") if os.environ.get("CODEX_HOME")
                                else os.path.join(home, ".codex", "sessions"))
    claude_root = args.claude or (os.path.join(os.environ["CLAUDE_CONFIG_DIR"], "projects")
                                  if os.environ.get("CLAUDE_CONFIG_DIR") else os.path.join(home, ".claude", "projects"))
    dsh_root = args.dsh or (os.path.join(os.environ["DSH_HOME"], "sessions") if os.environ.get("DSH_HOME")
                            else os.path.join(home, ".dsh", "sessions"))
    opencode_db = args.opencode or os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.join(home, ".local", "share"),
                                                "opencode", "opencode.db")
    gemini_dirs = args.gemini or gemini_roots()
    if args.write_history_fixture:
        write_history_fixture()
        return
    if args.dump_history:
        rows = daily_history(collect_all(codex_root, claude_root, dsh_root, opencode_db, gemini_dirs),
                             tz=timezone.utc if args.utc else None)
        for row in rows:
            print(json.dumps(row, ensure_ascii=False, separators=(",", ":")))
        return
    if args.check_fixtures:
        check_fixtures()
    elif args.dump_today:
        dump_today(codex_root, claude_root, dsh_root, gemini_dirs)
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
