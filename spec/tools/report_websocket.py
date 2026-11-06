#!/usr/bin/env python3
"""Reference implementation of the WebSocket transport of live reporting (METRICS 11.9, REPORTING-WEBSOCKET).

Standard library only. It implements:

- what a client takes from a discovery document's `websocket` object, and the connection URL;
- the signed handshake and the `report` message text;
- strict parsing of the service's messages (`ready`, `ack`, `error`) and handshake rejections;
- the stream scheduler of METRICS 11.9 as a simulator, with the latency percentiles;
- the `websocket` part of spec/fixtures/report-vectors.json (written by report_reference.py --write, checked by it).
"""

from __future__ import annotations

import json
import math
import re

import report_reference as ref
from execution_modes_reference import supported

WS_PATH = "/v1/reports/ws"
SUBPROTOCOL = "agent-speed-report.v1"
EXTENSION = "reportWebSocket"
SIGNATURE_HEADERS = ("X-AgentSpeed-Device", "X-AgentSpeed-Timestamp", "X-AgentSpeed-Nonce", "X-AgentSpeed-Signature")
# name: (low, high, default)
STREAM_RANGES = {"minIntervalMs": (10, 1000, 50), "maxInFlight": (1, 64, 8), "ackTimeoutMs": (500, 10_000, 2000),
                 "maxMessageBytes": (32_768, 65_536, 65_536), "pingIntervalSeconds": (10, 60, 20),
                 "pongTimeoutSeconds": (5, 30, 10)}
DEFAULT_STREAM = {key: default for key, (_, _, default) in STREAM_RANGES.items()}
MAX_DEPTH = 32
BACKOFF_MS = (500, 1000, 2000, 4000, 8000, 16_000, 30_000)
CONNECT_MS = 10_000
CLOSING_MS = 1000
STALE_MS = 1000
STABLE_MS = 60_000
LATENCY_SAMPLES = 512
SHA_RE = re.compile(r"^[0-9a-f]{64}$")
CODE_RE = re.compile(r"^[a-z][a-z0-9_]{0,47}$")
MAX_SEQ = 9007199254740991


# ------------------------------------------------------------------------------------------------- discovery

def stream_settings(document) -> dict | None:
    """METRICS 11.9 Availability: the stream settings, or None when the transport is unavailable."""
    if not isinstance(document, dict) or not supported(document.get("extensions"), EXTENSION):
        return None
    given = document.get("websocket")
    if not isinstance(given, dict) or given.get("path") != WS_PATH or given.get("subprotocol") != SUBPROTOCOL:
        return None
    settings = {}
    for key, (low, high, default) in STREAM_RANGES.items():
        if key not in given:
            settings[key] = default
            continue
        value = given[key]
        if not ref._type_ok(value, "integer") or not low <= value <= high:
            return None
        settings[key] = value
    if settings["pongTimeoutSeconds"] >= settings["pingIntervalSeconds"]:
        return None
    return settings


def websocket_url(api: str) -> str:
    """`api` as parse_discovery returns it: https → wss (http → ws for the loopback test service) plus the path."""
    scheme, rest = api.split("://", 1)
    return {"https": "wss", "http": "ws"}[scheme] + "://" + rest + WS_PATH


# ------------------------------------------------------------------------------------------------- messages

def handshake_headers(d: int, api: str, device_id: str, *, timestamp=None, nonce=None, api_key=None,
                      version="0.0.0") -> dict:
    """The signed GET of the upgrade (REPORTING-WEBSOCKET 4): the v1 signature over the WebSocket path, empty body."""
    headers = ref.signed_headers(d, "GET", api + WS_PATH, device_id, b"", timestamp=timestamp, nonce=nonce,
                                 api_key=api_key, version=version)
    headers["Sec-WebSocket-Protocol"] = SUBPROTOCOL
    return headers


def json_string(text: str) -> str:
    """A JSON string literal as both apps write it: `"` and `\\` escaped, control characters as \\b \\f \\n \\r \\t or
    \\u00xx (lower-case hex), everything else, `/` and non-ASCII included, as is."""
    escapes = {'"': '\\"', "\\": "\\\\", "\b": "\\b", "\f": "\\f", "\n": "\\n", "\r": "\\r", "\t": "\\t"}
    return '"' + "".join(escapes.get(ch) or (f"\\u{ord(ch):04x}" if ord(ch) < 0x20 else ch) for ch in text) + '"'


def report_message(headers: dict, body: bytes) -> str:
    """REPORTING-WEBSOCKET 5.2: one text message carrying the four signature headers and the body as a JSON string."""
    lowered = {key.lower(): value for key, value in headers.items()}
    fields = ",".join(f"{json_string(name)}:{json_string(lowered[name.lower()])}" for name in SIGNATURE_HEADERS)
    return '{"type":"report","headers":{' + fields + '},"body":' + json_string(body.decode("utf-8")) + "}"


def realtime(source_mode: str, sent_at_ms: int, age_ms: int | None) -> dict:
    """METRICS 11.9 `realtime`: sent_at_ms is the corrected wall clock of sentAt."""
    return {"sourceMode": source_mode,
            "observedAt": None if age_ms is None else ref.format_instant(sent_at_ms - age_ms),
            "observationAgeMs": age_ms,
            "sourceResolutionMs": 1000 if source_mode == "log" else None}


class InvalidMessage(ValueError):
    pass


def _depth(value, level=1) -> int:
    if isinstance(value, dict):
        return max([level] + [_depth(item, level + 1) for item in value.values()])
    if isinstance(value, list):
        return max([level] + [_depth(item, level + 1) for item in value])
    return level - 1


def _constant(name):
    raise InvalidMessage(f"{name} is not JSON")


def strict_object(data: bytes, max_bytes: int) -> dict:
    if len(data) > max_bytes:
        raise InvalidMessage("too large")
    try:
        value = json.loads(data.decode("utf-8"), object_pairs_hook=ref._no_duplicates, parse_constant=_constant)
        ref._check_surrogates(value)
    except (UnicodeDecodeError, json.JSONDecodeError, ref.StrictJsonError) as error:
        raise InvalidMessage(str(error)) from error
    if not isinstance(value, dict):
        raise InvalidMessage("not an object")
    if _depth(value) > MAX_DEPTH:
        raise InvalidMessage("too deep")
    return value


def _require(value, kind, check=lambda v: True):
    if not ref._type_ok(value, kind) or not check(value):
        raise InvalidMessage(f"bad {kind}")
    return value


def _instant(value) -> str:
    return _require(value, "string", lambda v: ref.SENT_AT_RE.match(v) is not None)


def _run(value) -> tuple[str, int]:
    run = _require(value, "object")
    return (_require(run.get("id"), "string", lambda v: 0 < len(v) <= 64),
            _require(run.get("seq"), "integer", lambda v: 1 <= v <= MAX_SEQ))


def parse_service_message(data: bytes, max_bytes: int = 65_536) -> dict:
    """A service message as the apps see it; raises InvalidMessage (the connection then counts as close 1002)."""
    message = strict_object(data, max_bytes)
    kind = message.get("type")
    if not isinstance(kind, str):
        raise InvalidMessage("type")
    if kind == "ready":
        if message.get("protocol") != "agent-speed-report":
            raise InvalidMessage("protocol")
        for key in ("version", "transportVersion"):
            if not ref._type_ok(message.get(key), "integer") or message[key] != 1:
                raise InvalidMessage(key)
        _instant(message.get("serverTime"))
        return {"type": "ready", "limits": {key: _require(message.get(key), "integer") for key in STREAM_RANGES}}
    if kind == "ack":
        run_id, seq = _run(message.get("run"))
        status = _require(message.get("status"), "string", lambda v: v in ("accepted", "duplicate"))
        digest = _require(message.get("bodySha256"), "string", lambda v: SHA_RE.match(v) is not None)
        _instant(message.get("receivedAt"))
        _instant(message.get("acceptedAt"))
        _require(message.get("processingMs"), "integer", lambda v: 0 <= v <= 900_000)
        rank = message.get("rank")
        if rank is not None:
            _require(rank, "integer", lambda v: 1 <= v <= 100_000_000)
        return {"type": "ack", "runId": run_id, "seq": seq, "status": status, "bodySha256": digest, "rank": rank}
    if kind == "error":
        if "run" not in message:
            raise InvalidMessage("run")
        run_id, seq = (None, None) if message["run"] is None else _run(message["run"])
        status = _require(message.get("httpStatus"), "integer", lambda v: 400 <= v <= 599)
        error = _require(message.get("error"), "object")
        code = _require(error.get("code"), "string", lambda v: CODE_RE.match(v) is not None)
        retry = error.get("retryAfterSeconds")
        if retry is not None:
            _require(retry, "integer", lambda v: 1 <= v <= 86_400)
        server_time = error.get("serverTime")
        if server_time is not None:
            _require(server_time, "integer", lambda v: v >= 0)
        fatal = _require(message.get("fatal"), "boolean")
        return {"type": "error", "runId": run_id, "seq": seq, "httpStatus": status, "code": code,
                "retryAfterSeconds": retry, "serverTime": server_time, "fatal": fatal}
    return {"type": "other"}


def retry_after_header(value) -> int | None:
    """A Retry-After header as the apps read it: delay seconds only (an HTTP date counts as absent)."""
    if value is None or not re.fullmatch(r"[0-9]{1,9}", value.strip()):
        return None
    return int(value.strip())


def classify_rejection(status: int, body: bytes | None, retry_after: str | None) -> dict:
    """A handshake answered without 101 (METRICS 11.9): the v1 error code (None for a plain failure) and the wait."""
    header = retry_after_header(retry_after)
    code, wait = None, None
    if body:
        try:
            error = strict_object(body, 65_536).get("error")
            if isinstance(error, dict) and isinstance(error.get("code"), str) and CODE_RE.match(error["code"]):
                code = error["code"]
                given = error.get("retryAfterSeconds")
                wait = given if ref._type_ok(given, "integer") and given >= 1 else None
        except InvalidMessage:
            pass
    if code is None:
        by_status = {401: "signature_invalid", 403: "forbidden", 404: "unknown_device", 410: "gone",
                     413: "payload_too_large", 429: "rate_limited"}
        if status in by_status:
            code = by_status[status]
        elif status == 503 and header is not None:
            code = "rate_limited"
        elif 400 <= status < 500:
            code = "bad_request"
    return {"code": code, "retryAfterSeconds": wait if wait is not None else header}


def percentile(samples: list[int], p: int) -> int | None:
    """Nearest rank: the smallest sample with at least p % of the samples at or below it."""
    if not samples:
        return None
    ordered = sorted(samples)
    return ordered[max(1, math.ceil(p * len(ordered) / 100)) - 1]


# ------------------------------------------------------------------------------------------------- scheduler

COUNTERS = ("built", "accepted", "duplicate", "rejected", "stale", "saturated", "unknown", "unmatched")
STRIKES = ("rejectStrikes", "nonceStrikes", "signatureStrikes", "runConflicts", "oversize")


class StreamScheduler:
    """METRICS 11.9 with instant writes. Every action is appended to `log` as `<t> <action>`."""

    def __init__(self, config: dict):
        self.settings = {key: config.get(key, default) for key, default in DEFAULT_STREAM.items()}
        self.limits = dict(self.settings)
        self.heartbeat_ms = config.get("heartbeatSeconds", ref.DEFAULT_LIMITS["heartbeatSeconds"]) * 1000
        self.available = config.get("available", True)
        self.log: list[str] = []
        self.counters = dict.fromkeys(COUNTERS, 0)
        self.strikes = dict.fromkeys(STRIKES, 0)
        self.latency: list[int] = []
        self.reporting, self.network, self.paused = True, True, False
        self.mode, self.last_active, self.prev_tick = "quiet", None, None
        self.conn, self.deadline, self.ready_at = "closed", None, None
        self.awaiting: list[dict] = []
        self.failures, self.reconnect_at, self.rate_limited_until = 0, 0, 0
        self.stop, self.mismatched, self.waiting_settings = None, False, False
        self.last_delivered, self.wake_at = None, None
        self.new_run(None)

    # -- helpers

    def say(self, t, action):
        self.log.append(f"{t} {action}")

    def new_run(self, t):
        self.seq, self.window_start, self.observed, self.dirty = 0, t, None, False
        self.last_build, self.last_activity = None, None

    def discard(self, t):
        self.window_start, self.observed = t, None

    def end(self, t, action=None):
        if action:
            self.say(t, action)
        for report in self.awaiting:
            self.say(t, f"dropped {report['seq']} unknown")
            self.counters["unknown"] += 1
        self.awaiting = []
        self.conn, self.deadline, self.ready_at = "closed", None, None

    def failure(self, t):
        self.failures += 1
        self.reconnect_at = t + BACKOFF_MS[min(self.failures, len(BACKOFF_MS)) - 1]
        self.say(t, f"backoff {self.reconnect_at}")

    def wait(self, t, seconds):
        seconds = 60 if seconds is None else min(max(seconds, 5), 3600)
        self.rate_limited_until = max(self.rate_limited_until, t + seconds * 1000)
        self.say(t, f"wait {self.rate_limited_until}")

    def halt(self, t, reason):
        self.stop = reason
        self.say(t, f"stop {reason}")
        if self.conn != "closed":
            self.end(t, "abort")

    def react(self, t, name):
        self.say(t, f"react {name}")

    def ready_to_send(self) -> bool:
        return self.reporting and self.network and not self.paused and self.stop is None and self.available

    def build(self, t, activity):
        self.seq += 1
        age = None if self.observed is None else t - self.observed
        self.say(t, f"build {self.seq} {activity} {t - self.window_start} {'-' if age is None else age}")
        self.counters["built"] += 1
        report = {"seq": self.seq, "observed": self.observed, "written": t}
        self.window_start, self.observed, self.dirty = t, None, False
        self.last_build, self.last_activity = t, activity
        if age is not None and age > STALE_MS:
            self.say(t, f"dropped {report['seq']} stale")
            self.counters["stale"] += 1
            return
        self.awaiting.append(report)
        self.say(t, f"write {report['seq']}")

    def leave(self, t):
        if len(self.awaiting) < self.limits["maxInFlight"]:
            self.build(t, "leaving")
        self.conn, self.deadline = "closing", t + CLOSING_MS
        if not self.awaiting:
            self.end(t, "close 1000")

    # -- the tick

    def tick(self, t, active=False, changed=False, observed=None, events=()):
        user_leave = False
        for event in events:
            if event == "pause":
                user_leave |= not self.paused
                self.paused = True
            elif event == "resume":
                self.paused = False
            elif event == "networkOff":
                self.network = False
                if self.conn != "closed":
                    self.end(t, "abort")
            elif event == "networkOn":
                self.network = True
            elif event in ("off", "quit"):
                user_leave |= self.reporting
                self.reporting = False
        if self.prev_tick is not None and t - self.prev_tick > ref.GAP_MS:
            self.discard(t)
            if self.conn != "closed":
                self.end(t, "abort")
        self.prev_tick = t
        if self.window_start is None:
            self.window_start = t
        previous = self.mode
        if active:
            self.last_active = t
        since = None if self.last_active is None else t - self.last_active
        self.mode = ("quiet" if since is None or since >= ref.QUIET_MS
                     else "active" if since < ref.HOLD_MS else "idle")
        if changed or self.mode != self.last_activity:
            self.dirty = True
        if changed and observed is not None:
            self.observed = observed if self.observed is None else min(self.observed, observed)
        if self.conn == "closing" and t >= self.deadline:
            self.end(t, "abort")
        if user_leave and self.conn == "open" and previous != "quiet" and t >= self.rate_limited_until:
            self.leave(t)
        if not self.ready_to_send():
            if self.conn == "open":
                if self.awaiting:
                    self.conn, self.deadline = "closing", t + CLOSING_MS
                else:
                    self.end(t, "close 1000")
            elif self.conn in ("connecting", "opening"):
                self.end(t, "abort")
            self.discard(t)
            return
        if self.mode == "quiet":
            if previous != "quiet" and self.conn == "open":
                self.leave(t)
            elif self.conn in ("connecting", "opening"):
                self.end(t, "abort")
            self.discard(t)
            return
        if self.conn == "closed":
            if t >= self.reconnect_at and t >= self.rate_limited_until and not self.waiting_settings:
                self.conn, self.deadline = "connecting", t + CONNECT_MS
                self.say(t, "connect")
            self.discard(t)
            return
        if self.conn in ("connecting", "opening"):
            if t >= self.deadline:
                self.end(t, "abort")
                self.failure(t)
            self.discard(t)
            return
        if self.conn == "closing":
            self.discard(t)
            return
        if any(t > report["written"] + self.limits["ackTimeoutMs"] for report in self.awaiting):
            self.end(t, "abort")
            self.failure(t)
            self.discard(t)
            return
        due = self.dirty and (self.last_build is None or t - self.last_build >= self.limits["minIntervalMs"])
        heartbeat_from = self.ready_at if self.last_build is None else self.last_build
        due |= t - heartbeat_from >= self.heartbeat_ms
        if due:
            if len(self.awaiting) < self.limits["maxInFlight"]:
                self.build(t, self.mode)
            else:
                self.discard(t)
                self.dirty = False
                self.counters["saturated"] += 1
                self.say(t, "saturated")
        elif self.dirty:
            at = self.last_build + self.limits["minIntervalMs"]
            if self.wake_at != at:
                self.wake_at = at
                self.say(t, f"wake {at}")

    # -- the connection's events

    def opened(self, t):
        if self.conn == "connecting":
            self.conn, self.deadline = "opening", t + CONNECT_MS

    def ready(self, t, limits):
        if self.conn != "opening":
            return
        if limits == self.settings:
            self.conn, self.deadline, self.ready_at, self.mismatched = "open", None, t, False
            self.limits = dict(limits)
            self.discard(t)
            self.dirty, self.last_build = True, None
            return
        self.end(t, "close 1000")
        if self.mismatched:
            self.halt(t, "mismatch")
        else:
            self.mismatched, self.waiting_settings = True, True
            self.react(t, "refreshDiscovery")

    def settings_changed(self, t, settings):
        if settings is None:
            if self.waiting_settings:
                self.waiting_settings = False
                self.halt(t, "mismatch")
            else:
                self.available = False
            return
        self.settings, self.available, self.waiting_settings = {**DEFAULT_STREAM, **settings}, True, False

    def retry(self, t):
        self.stop, self.mismatched, self.waiting_settings = None, False, False
        self.strikes = dict.fromkeys(STRIKES, 0)
        self.failures, self.reconnect_at = 0, t

    def ack(self, t, seq, status, matches=True):
        if self.conn not in ("open", "closing"):
            return
        report = next((r for r in self.awaiting if r["seq"] == seq), None) if matches else None
        if report is None:
            self.counters["unmatched"] += 1
            return
        self.awaiting.remove(report)
        self.say(t, f"delivered {seq} {status}")
        self.counters[status] += 1
        self.last_delivered = t
        if report["observed"] is not None:
            self.latency = (self.latency + [t - report["observed"]])[-LATENCY_SAMPLES:]
        self.strikes = dict.fromkeys(STRIKES, 0)
        if t - self.ready_at >= STABLE_MS:
            self.failures = 0
        if self.conn == "closing" and not self.awaiting:
            self.end(t, "close 1000")

    def error(self, t, seq, code, status, retry_after=None, fatal=False):
        if self.conn not in ("open", "closing"):
            return
        report = next((r for r in self.awaiting if r["seq"] == seq), None) if seq is not None else None
        if report is not None:
            self.awaiting.remove(report)
            self.say(t, f"dropped {seq} rejected")
            self.counters["rejected"] += 1
        self.react_to(t, code, status, retry_after)
        if fatal and self.conn != "closed":
            self.end(t, "abort")
            self.failure(t)
        elif self.conn == "closing" and not self.awaiting:
            self.end(t, "close 1000")

    def rejected(self, t, status, code=None, retry_after=None):
        if self.conn != "connecting":
            return
        self.end(t)
        if code is None:
            code = classify_rejection(status, None, None if retry_after is None else str(retry_after))["code"]
        if not self.react_to(t, code, status, retry_after):
            self.failure(t)

    def closed(self, t, code):
        if self.conn == "closed":
            return
        was = self.conn
        self.end(t)
        if was == "closing":
            return
        if code == 4001:
            self.halt(t, "apiKey")
        elif code == 4003:
            self.halt(t, "forbidden")
        elif code == 4004:
            self.react(t, "reRegister")
            self.failure(t)
        elif code == 4009:
            self.halt(t, "replaced")
        elif code == 4410:
            self.halt(t, "gone")
        elif code in (4429, 4503):
            self.wait(t, None)
        elif code in (1002, 1003, 1007):
            self.halt(t, "incompatible")
        elif code == 1009:
            self.react(t, "refreshDiscovery")
            self.strikes["oversize"] += 1
            if self.strikes["oversize"] >= 3:
                self.halt(t, "incompatible")
            else:
                self.failure(t)
        else:
            self.failure(t)

    def react_to(self, t, code, status, retry_after) -> bool:
        """The reaction table; True when it ended the attempt (a failure, a wait, a stop or a fresh start)."""
        strikes = self.strikes
        if code == "timestamp_out_of_window":
            self.react(t, "correctClock")
            return False
        if code == "nonce_replayed":
            strikes["nonceStrikes"] += 1
            if strikes["nonceStrikes"] >= 3:
                self.halt(t, "nonce")
                return True
            return False
        if code == "signature_invalid":
            self.react(t, "recheckRegistration")
            strikes["signatureStrikes"] += 1
            if strikes["signatureStrikes"] >= 3:
                self.halt(t, "keyMismatch")
                return True
            if self.conn != "closed":
                self.end(t, "close 1000")
            self.failure(t)
            return True
        if code in ("api_key_required", "api_key_invalid"):
            self.halt(t, "apiKey")
            return True
        if code == "forbidden":
            self.halt(t, "forbidden")
            return True
        if code == "unknown_device":
            self.react(t, "reRegister")
            if self.conn != "closed":
                self.end(t, "abort")
            self.failure(t)
            return True
        if code == "username_mismatch":
            self.react(t, "rename")
            return False
        if code == "seq_conflict":
            if self.conn != "closed":
                self.end(t, "abort")
            self.react(t, "newRun")
            self.new_run(t)
            self.reconnect_at = t
            return True
        if code == "run_conflict":
            if self.conn != "closed":
                self.end(t, "close 1000")
            strikes["runConflicts"] += 1
            if strikes["runConflicts"] >= 2:
                self.halt(t, "clientState")
            else:
                self.reconnect_at = t
            return True
        if code == "version_unsupported":
            self.react(t, "refreshDiscovery")
            self.halt(t, "upgrade")
            return True
        if code == "payload_too_large":
            self.react(t, "refreshDiscovery")
            strikes["oversize"] += 1
            if strikes["oversize"] >= 3:
                self.halt(t, "incompatible")
                return True
            return False
        if code == "report_stale":
            return False
        if code == "rate_limited" or (status == 503 and retry_after is not None):
            if self.conn != "closed":
                self.end(t, "abort")
            self.wait(t, retry_after)
            return True
        if code == "gone":
            self.halt(t, "gone")
            return True
        if status is not None and 400 <= status < 500:
            strikes["rejectStrikes"] += 1
            if strikes["rejectStrikes"] >= 3:
                self.halt(t, "rejected")
                return True
            return False
        if self.conn != "closed":
            self.end(t, "abort")
        self.failure(t)
        return True

    def state(self) -> dict:
        return {"connection": self.conn, "seq": self.seq, "failures": self.failures, "stop": self.stop,
                "lastDelivered": self.last_delivered}


def _expand(events: list) -> list:
    expanded = []
    for event in events:
        if event["type"] == "tick" and "until" in event:
            step = event.get("step", 500)
            for at in range(event["at"], event["until"] + 1, step):
                expanded.append({**{k: v for k, v in event.items() if k not in ("until", "step")}, "at": at})
        else:
            expanded.append(event)
    return sorted(expanded, key=lambda e: e["at"])


def simulate_stream(case: dict) -> dict:
    """Runs a METRICS 11.9 scheduler case; wake-ups the scheduler asks for become ticks without new data."""
    scheduler = StreamScheduler(case.get("config", {}))
    events = _expand(case["events"])
    tick_times = {event["at"] for event in events if event["type"] == "tick"}
    last_active = False

    def wake_until(limit):
        nonlocal last_active
        while scheduler.wake_at is not None and scheduler.wake_at < limit:
            at, scheduler.wake_at = scheduler.wake_at, None
            if at not in tick_times:
                scheduler.tick(at, active=last_active)

    for event in events:
        t = event["at"]
        wake_until(t)
        if scheduler.wake_at == t and event["type"] == "tick":
            scheduler.wake_at = None
        kind = event["type"]
        if kind == "tick":
            last_active = event.get("active", False)
            observed = t - event["observedAgo"] if "observedAgo" in event else None
            scheduler.tick(t, last_active, event.get("changed", False), observed, event.get("events", ()))
        elif kind == "open":
            scheduler.opened(t)
        elif kind == "ready":
            scheduler.ready(t, {**scheduler.settings, **event.get("limits", {})})
        elif kind == "ack":
            scheduler.ack(t, event["seq"], event.get("status", "accepted"), event.get("sha", "match") == "match")
        elif kind == "error":
            scheduler.error(t, event.get("seq"), event["code"], event["httpStatus"], event.get("retryAfter"),
                            event.get("fatal", False))
        elif kind == "close":
            scheduler.closed(t, event["code"])
        elif kind == "reject":
            scheduler.rejected(t, event["status"], event.get("code"), event.get("retryAfter"))
        elif kind == "settings":
            scheduler.settings_changed(t, event.get("settings"))
        elif kind == "retry":
            scheduler.retry(t)
        else:
            raise ValueError(f"unknown event {kind}")
    wake_until(math.inf)
    return {"log": scheduler.log, "counters": scheduler.counters, "latency": scheduler.latency,
            "state": scheduler.state()}


# ------------------------------------------------------------------------------------------------- vectors

def _ticks(at, until, **attributes):
    return {"at": at, "until": until, "type": "tick", **attributes}


def _tick(at, **attributes):
    return {"at": at, "type": "tick", **attributes}


def _open_ready(at, **limits):
    ready = {"at": at + 10, "type": "ready"}
    if limits:
        ready["limits"] = limits
    return [{"at": at, "type": "open"}, ready]


ACTIVE = {"active": True}
CHANGED = {"active": True, "changed": True}

# Hand-written from METRICS 11.9; simulate_stream must reproduce every expectation.
STREAM_CASES = [
    {"name": "a change is built at once; the next one within the minimum interval waits for it",
     "events": [_tick(0), _tick(1000, **CHANGED, observedAgo=20), *_open_ready(1140),
                _tick(1200, **CHANGED, observedAgo=5), _tick(1220, **CHANGED, observedAgo=3),
                {"at": 1230, "type": "ack", "seq": 1}, {"at": 1280, "type": "ack", "seq": 2}, _tick(1500, **ACTIVE)],
     "expect": {"log": ["1000 connect", "1200 build 1 active 50 5", "1200 write 1", "1220 wake 1250",
                        "1230 delivered 1 accepted", "1250 build 2 active 50 33", "1250 write 2",
                        "1280 delivered 2 accepted"],
                "counters": {"built": 2, "accepted": 2}, "latency": [35, 63],
                "state": {"connection": "open", "seq": 2, "failures": 0, "stop": None, "lastDelivered": 1280}}},
    {"name": "a burst inside the minimum interval is one report with the earliest observation; a duplicate is "
             "delivered, an answer with another hash is unmatched",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED, observedAgo=4),
                _tick(110, **CHANGED, observedAgo=5), _tick(120, **CHANGED, observedAgo=1),
                _tick(140, **CHANGED, observedAgo=2), {"at": 145, "type": "ack", "seq": 1, "sha": "other"},
                {"at": 146, "type": "ack", "seq": 1, "status": "duplicate"}, {"at": 170, "type": "ack", "seq": 2}],
     "expect": {"log": ["0 connect", "100 build 1 active 80 4", "100 write 1", "110 wake 150",
                        "146 delivered 1 duplicate", "150 build 2 active 50 45", "150 write 2",
                        "170 delivered 2 accepted"],
                "counters": {"built": 2, "accepted": 1, "duplicate": 1, "unmatched": 1}, "latency": [50, 65]}},
    {"name": "idle after 15 s, a heartbeat after heartbeatSeconds, leaving at quiet, and a new connection on activity",
     "config": {"heartbeatSeconds": 300},
     "events": [_tick(0, **CHANGED), {"at": 50, "type": "open"}, {"at": 100, "type": "ready"},
                _ticks(500, 15000, **ACTIVE), _ticks(15500, 619500), _tick(620000, **CHANGED),
                {"at": 520, "type": "ack", "seq": 1}, {"at": 30020, "type": "ack", "seq": 2},
                {"at": 330020, "type": "ack", "seq": 3}, {"at": 615030, "type": "ack", "seq": 4}],
     "expect": {"log": ["0 connect", "500 build 1 active 400 -", "500 write 1", "520 delivered 1 accepted",
                        "30000 build 2 idle 29500 -", "30000 write 2", "30020 delivered 2 accepted",
                        "330000 build 3 idle 300000 -", "330000 write 3", "330020 delivered 3 accepted",
                        "615000 build 4 leaving 285000 -", "615000 write 4", "615030 delivered 4 accepted",
                        "615030 close 1000", "620000 connect"],
                "counters": {"built": 4, "accepted": 4},
                "state": {"connection": "connecting", "seq": 4, "failures": 0, "stop": None,
                          "lastDelivered": 615030}}},
    {"name": "with every slot awaited the window is lost; the sequence goes on",
     "config": {"maxInFlight": 2},
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED, observedAgo=5),
                _tick(200, **CHANGED, observedAgo=5), _tick(300, **CHANGED, observedAgo=5),
                _tick(400, **CHANGED, observedAgo=5), {"at": 450, "type": "ack", "seq": 1},
                _tick(500, **CHANGED, observedAgo=5), {"at": 520, "type": "ack", "seq": 2},
                {"at": 540, "type": "ack", "seq": 3}],
     "expect": {"log": ["0 connect", "100 build 1 active 80 5", "100 write 1", "200 build 2 active 100 5",
                        "200 write 2", "300 saturated", "400 saturated", "450 delivered 1 accepted",
                        "500 build 3 active 100 5", "500 write 3", "520 delivered 2 accepted",
                        "540 delivered 3 accepted"],
                "counters": {"built": 3, "accepted": 3, "saturated": 2}, "latency": [355, 325, 45]}},
    {"name": "an unanswered report: the connection is dropped, the outcome stays unknown, nothing is resent",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED, observedAgo=0),
                _ticks(500, 3500, **ACTIVE), *_open_ready(3010), {"at": 3030, "type": "ack", "seq": 1},
                {"at": 3520, "type": "ack", "seq": 2}],
     "expect": {"log": ["0 connect", "100 build 1 active 80 0", "100 write 1", "2500 abort", "2500 dropped 1 unknown",
                        "2500 backoff 3000", "3000 connect", "3500 build 2 active 480 -", "3500 write 2",
                        "3520 delivered 2 accepted"],
                "counters": {"built": 2, "accepted": 1, "unknown": 1, "unmatched": 1}, "latency": [],
                "state": {"connection": "open", "seq": 2, "failures": 1, "stop": None, "lastDelivered": 3520}}},
    {"name": "failed attempts back off 0.5, 1 and 2 s; a heartbeat while active; a minute open with answers clears "
             "the failures",
     "events": [_ticks(0, 5000, **ACTIVE), {"at": 100, "type": "close", "code": 1006},
                {"at": 1100, "type": "close", "code": 1006}, {"at": 2600, "type": "close", "code": 1006},
                *_open_ready(5010), _ticks(5500, 65500, **ACTIVE), {"at": 5520, "type": "ack", "seq": 1},
                {"at": 65520, "type": "ack", "seq": 2}],
     "expect": {"log": ["0 connect", "100 backoff 600", "1000 connect", "1100 backoff 2100", "2500 connect",
                        "2600 backoff 4600", "5000 connect", "5500 build 1 active 480 -", "5500 write 1",
                        "5520 delivered 1 accepted", "65500 build 2 active 60000 -", "65500 write 2",
                        "65520 delivered 2 accepted"],
                "state": {"connection": "open", "seq": 2, "failures": 0, "stop": None, "lastDelivered": 65520}}},
    {"name": "Retry-After of a refused handshake wins, clamped to 5 s; a 429 without one waits 60 s",
     "events": [_ticks(0, 66000, **ACTIVE),
                {"at": 100, "type": "reject", "status": 429, "code": "rate_limited", "retryAfter": 2},
                {"at": 5600, "type": "reject", "status": 429}],
     "expect": {"log": ["0 connect", "100 wait 5100", "5500 connect", "5600 wait 65600", "66000 connect"],
                "state": {"connection": "connecting", "seq": 0, "failures": 0, "stop": None,
                          "lastDelivered": None}}},
    {"name": "a rate-limited report is dropped, the connection aborted and the wait observed",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED), _tick(200, **CHANGED),
                {"at": 250, "type": "error", "seq": 1, "code": "rate_limited", "httpStatus": 429, "retryAfter": 7},
                _ticks(500, 7500, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "200 build 2 active 100 -",
                        "200 write 2", "250 dropped 1 rejected", "250 abort", "250 dropped 2 unknown",
                        "250 wait 7250", "7500 connect"],
                "counters": {"built": 2, "rejected": 1, "unknown": 1}}},
    {"name": "seq_conflict starts a new run on a new connection",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED), {"at": 120, "type": "ack", "seq": 1},
                _tick(200, **CHANGED), _tick(300, **CHANGED),
                {"at": 320, "type": "error", "seq": 2, "code": "seq_conflict", "httpStatus": 409},
                _tick(500, **ACTIVE), *_open_ready(510), _tick(1000, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "120 delivered 1 accepted",
                        "200 build 2 active 100 -", "200 write 2", "300 build 3 active 100 -", "300 write 3",
                        "320 dropped 2 rejected", "320 abort", "320 dropped 3 unknown", "320 react newRun",
                        "500 connect", "1000 build 1 active 480 -", "1000 write 1"],
                "counters": {"built": 4, "accepted": 1, "rejected": 1, "unknown": 1},
                "state": {"connection": "open", "seq": 1, "failures": 0, "stop": None, "lastDelivered": 120}}},
    {"name": "run_conflict reconnects once; the second stops; the close after a fatal error is not acted on",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED),
                {"at": 150, "type": "error", "code": "run_conflict", "httpStatus": 409, "fatal": True},
                {"at": 160, "type": "close", "code": 1008}, _tick(500, **ACTIVE), *_open_ready(510),
                _tick(1000, **ACTIVE),
                {"at": 1050, "type": "error", "code": "run_conflict", "httpStatus": 409, "fatal": True},
                _tick(1500, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "150 close 1000",
                        "150 dropped 1 unknown", "500 connect", "1000 build 2 active 480 -", "1000 write 2",
                        "1050 close 1000", "1050 dropped 2 unknown", "1050 stop clientState"],
                "counters": {"built": 2, "unknown": 2},
                "state": {"connection": "closed", "seq": 2, "failures": 0, "stop": "clientState",
                          "lastDelivered": None}}},
    {"name": "replaced by another connection: stopped until Try Again",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED), {"at": 120, "type": "ack", "seq": 1},
                {"at": 300, "type": "close", "code": 4009}, _tick(500, **ACTIVE), {"at": 600, "type": "retry"},
                _tick(1000, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "120 delivered 1 accepted",
                        "300 stop replaced", "1000 connect"],
                "state": {"connection": "connecting", "seq": 1, "failures": 0, "stop": None, "lastDelivered": 120}}},
    {"name": "a ready with other limits: one forced refresh, then the refreshed settings; a second mismatch stops",
     "events": [_ticks(0, 3000, **ACTIVE), *_open_ready(10, maxInFlight=4),
                {"at": 800, "type": "settings", "settings": {"maxInFlight": 4}}, *_open_ready(1010, maxInFlight=4),
                {"at": 1520, "type": "ack", "seq": 1}, {"at": 2000, "type": "close", "code": 1001},
                *_open_ready(2510, maxInFlight=6), {"at": 2800, "type": "settings", "settings": {"maxInFlight": 4}},
                *_open_ready(3010, maxInFlight=6)],
     "expect": {"log": ["0 connect", "20 close 1000", "20 react refreshDiscovery", "1000 connect",
                        "1500 build 1 active 480 -", "1500 write 1", "1520 delivered 1 accepted", "2000 backoff 2500",
                        "2500 connect", "2520 close 1000", "2520 react refreshDiscovery", "3000 connect",
                        "3020 close 1000", "3020 stop mismatch"],
                "state": {"connection": "closed", "seq": 1, "failures": 1, "stop": "mismatch",
                          "lastDelivered": 1520}}},
    {"name": "pause sends leaving and closes; quit waits at most a second for the answers",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED), {"at": 120, "type": "ack", "seq": 1},
                _tick(500, **ACTIVE, events=["pause"]), {"at": 530, "type": "ack", "seq": 2}, _tick(1000, **ACTIVE),
                _tick(1500, **ACTIVE, events=["resume"]), *_open_ready(1510), _tick(2000, **ACTIVE),
                _tick(2500, **ACTIVE, events=["quit"]), _tick(3000, **ACTIVE), _tick(3500, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "120 delivered 1 accepted",
                        "500 build 2 leaving 400 -", "500 write 2", "530 delivered 2 accepted", "530 close 1000",
                        "1500 connect", "2000 build 3 active 480 -", "2000 write 3", "2500 build 4 leaving 500 -",
                        "2500 write 4", "3500 abort", "3500 dropped 3 unknown", "3500 dropped 4 unknown"],
                "counters": {"built": 4, "accepted": 2, "unknown": 2}}},
    {"name": "network off aborts at once without leaving; network on connects again",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED),
                _tick(500, **ACTIVE, events=["networkOff"]), _tick(1000, **ACTIVE),
                _tick(1500, **ACTIVE, events=["networkOn"])],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "500 abort", "500 dropped 1 unknown",
                        "1500 connect"],
                "counters": {"built": 1, "unknown": 1}}},
    {"name": "rejected reports strike until the third stops; an acknowledged report clears the strikes",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED), _tick(200, **CHANGED),
                _tick(300, **CHANGED), _tick(400, **CHANGED),
                {"at": 410, "type": "error", "seq": 1, "code": "bad_request", "httpStatus": 400},
                {"at": 420, "type": "ack", "seq": 2},
                {"at": 430, "type": "error", "seq": 3, "code": "payload_invalid", "httpStatus": 422},
                {"at": 440, "type": "error", "seq": 4, "code": "bad_request", "httpStatus": 400},
                _tick(500, **CHANGED), {"at": 510, "type": "error", "seq": 5, "code": "not_found", "httpStatus": 404}],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "200 build 2 active 100 -",
                        "200 write 2", "300 build 3 active 100 -", "300 write 3", "400 build 4 active 100 -",
                        "400 write 4", "410 dropped 1 rejected", "420 delivered 2 accepted", "430 dropped 3 rejected",
                        "440 dropped 4 rejected", "500 build 5 active 100 -", "500 write 5", "510 dropped 5 rejected",
                        "510 stop rejected", "510 abort"],
                "counters": {"built": 5, "accepted": 1, "rejected": 4}}},
    {"name": "a refused signature rechecks the registration and backs off; the third stops",
     "events": [_ticks(0, 3000, **ACTIVE), {"at": 100, "type": "reject", "status": 401},
                {"at": 1100, "type": "reject", "status": 401, "code": "signature_invalid"},
                {"at": 2600, "type": "reject", "status": 401}],
     "expect": {"log": ["0 connect", "100 react recheckRegistration", "100 backoff 600", "1000 connect",
                        "1100 react recheckRegistration", "1100 backoff 2100", "2500 connect",
                        "2600 react recheckRegistration", "2600 stop keyMismatch"],
                "state": {"connection": "closed", "seq": 0, "failures": 2, "stop": "keyMismatch",
                          "lastDelivered": None}}},
    {"name": "an observation older than 1 s is dropped before writing; a sleep drops the connection",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED, observedAgo=1500),
                _tick(200, **CHANGED, observedAgo=30), {"at": 220, "type": "ack", "seq": 2}, _tick(20000, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 1500", "100 dropped 1 stale", "200 build 2 active 100 30",
                        "200 write 2", "220 delivered 2 accepted", "20000 abort", "20000 connect"],
                "counters": {"built": 2, "accepted": 1, "stale": 1}, "latency": [50]}},
    {"name": "close codes: 4004 re-registers, 4429 waits 60 s, a fatal forbidden stops and its close is not acted on",
     "events": [_tick(0, **CHANGED), *_open_ready(10), {"at": 100, "type": "close", "code": 4004},
                _tick(500, **ACTIVE), _tick(1000, **ACTIVE), *_open_ready(1010),
                {"at": 1100, "type": "close", "code": 4429}, _tick(1500, **ACTIVE), _tick(61500, **ACTIVE),
                *_open_ready(61510),
                {"at": 61600, "type": "error", "code": "forbidden", "httpStatus": 403, "fatal": True},
                {"at": 61610, "type": "close", "code": 4003}],
     "expect": {"log": ["0 connect", "100 react reRegister", "100 backoff 600", "1000 connect", "1100 wait 61100",
                        "61500 connect", "61600 stop forbidden", "61600 abort"],
                "state": {"connection": "closed", "seq": 0, "failures": 1, "stop": "forbidden",
                          "lastDelivered": None}}},
    {"name": "10 s to the 101 and 10 s from it to ready",
     "events": [_ticks(0, 21000, **ACTIVE), {"at": 10600, "type": "open"}],
     "expect": {"log": ["0 connect", "10000 abort", "10000 backoff 10500", "10500 connect", "21000 abort",
                        "21000 backoff 22000"]}},
    {"name": "without the capability nothing connects; a refreshed document with it connects",
     "config": {"available": False},
     "events": [_ticks(0, 1000, **CHANGED), {"at": 1200, "type": "settings", "settings": {}},
                _tick(1500, **ACTIVE)],
     "expect": {"log": ["1500 connect"]}},
    {"name": "a clock correction keeps the connection; a fatal server error aborts and backs off",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED),
                {"at": 150, "type": "error", "seq": 1, "code": "timestamp_out_of_window", "httpStatus": 401},
                _tick(200, **CHANGED),
                {"at": 250, "type": "error", "code": "internal_error", "httpStatus": 500, "fatal": True},
                _tick(500, **ACTIVE), _tick(1000, **ACTIVE)],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "150 dropped 1 rejected",
                        "150 react correctClock", "200 build 2 active 100 -", "200 write 2", "250 abort",
                        "250 dropped 2 unknown", "250 backoff 750", "1000 connect"],
                "counters": {"built": 2, "rejected": 1, "unknown": 1}}},
    {"name": "a third replayed nonce stops",
     "events": [_tick(0, **CHANGED), *_open_ready(10), _tick(100, **CHANGED),
                {"at": 150, "type": "error", "seq": 1, "code": "nonce_replayed", "httpStatus": 401},
                _tick(200, **CHANGED),
                {"at": 250, "type": "error", "seq": 2, "code": "nonce_replayed", "httpStatus": 401},
                _tick(300, **CHANGED),
                {"at": 350, "type": "error", "seq": 3, "code": "nonce_replayed", "httpStatus": 401}],
     "expect": {"log": ["0 connect", "100 build 1 active 80 -", "100 write 1", "150 dropped 1 rejected",
                        "200 build 2 active 100 -", "200 write 2", "250 dropped 2 rejected", "300 build 3 active 100 -",
                        "300 write 3", "350 dropped 3 rejected", "350 stop nonce", "350 abort"],
                "counters": {"built": 3, "rejected": 3}}},
]


def _stream_result(case: dict) -> dict:
    """The simulator's result restricted to what the case expects; unlisted counters are 0."""
    result = simulate_stream(case)
    expect = case["expect"]
    got = {"log": result["log"]}
    if "counters" in expect:
        got["counters"] = {key: value for key, value in result["counters"].items() if value}
    for key in ("latency", "state"):
        if key in expect:
            got[key] = result[key]
    return got


def check_stream_cases(cases: list) -> list[str]:
    failures = []
    for case in cases:
        got = _stream_result(case)
        if got != case["expect"]:
            failures.append(f"websocket.scheduler: {case['name']}: got {json.dumps(got)}")
    return failures


WS_TIME = 1792872000  # 2026-10-24T20:00:00Z (REPORTING-WEBSOCKET 14)
WS_BODY = {
    "schema": 1, "client": {"app": "AgentSpeed", "version": "0.0.0-test", "platform": "macOS", "osMajor": 27},
    "deviceId": "6f1c2a9e-3b4d-4e5f-8a7b-9c0d1e2f3a4b", "username": "demo-ws",
    "run": {"id": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 42}, "sentAt": "2026-10-24T20:00:00.000Z",
    "windowMs": 50, "activity": "active",
    "live": {"outputTokS": 100.0, "totalTokS": 100.0, "estimatedShare": 0.0,
             "agents": {"generating": 1, "tools": 0, "active": 1}},
    "peak": {"outputTokS": 100.0, "totalTokS": 100.0},
    "delta": {"requests": 1, "output": 5, "input": 0, "cachedInput": 0, "cacheWriteInput": 0, "reasoning": 0,
              "measuredOutput": 5, "measuredMs": 50},
    "title": {"level": 4, "metric": "output", "redline": 2000, "heat": 0.166},
    "realtime": {"sourceMode": "event", "observedAt": "2026-10-24T19:59:59.990Z", "observationAgeMs": 10,
                 "sourceResolutionMs": 0},
}


def _document(**changes) -> dict:
    document = {"protocol": "agent-speed-report", "versions": [1],
                "service": {"name": "Example", "terms": "https://example.com/terms",
                            "privacy": "https://example.com/privacy"},
                "api": "https://api.example.com/agent-speed",
                "extensions": {"reportWebSocket": 1},
                "websocket": {"path": WS_PATH, "subprotocol": SUBPROTOCOL}}
    for key, value in changes.items():
        if value is None:
            document.pop(key, None)
        else:
            document[key] = value
    return document


def _ws(**changes) -> dict:
    return {"path": WS_PATH, "subprotocol": SUBPROTOCOL, **changes}


FULL_STREAM = {"minIntervalMs": 50, "maxInFlight": 8, "ackTimeoutMs": 2000, "maxMessageBytes": 65536,
               "pingIntervalSeconds": 20, "pongTimeoutSeconds": 10}

DISCOVERY_CASES = [
    ("the capability with only the required fields gets the defaults", _document(), DEFAULT_STREAM),
    ("every field given", _document(websocket=_ws(minIntervalMs=100, maxInFlight=4, ackTimeoutMs=5000,
                                                   maxMessageBytes=32768, pingIntervalSeconds=30,
                                                   pongTimeoutSeconds=29)),
     {"minIntervalMs": 100, "maxInFlight": 4, "ackTimeoutMs": 5000, "maxMessageBytes": 32768,
      "pingIntervalSeconds": 30, "pongTimeoutSeconds": 29}),
    ("unknown fields are ignored", _document(websocket=_ws(compression="none", minIntervalMs=10)),
     {**DEFAULT_STREAM, "minIntervalMs": 10}),
    ("no capability", _document(extensions=None), None),
    ("the capability as true", _document(extensions={"reportWebSocket": True}), None),
    ("the capability as a string", _document(extensions={"reportWebSocket": "1"}), None),
    ("another capability version", _document(extensions={"reportWebSocket": 2}), None),
    ("the capability as null", _document(extensions={"reportWebSocket": None}), None),
    ("extensions not an object", _document(extensions=[1]), None),
    ("the capability without the websocket object", _document(websocket=None), None),
    ("websocket not an object", _document(websocket="wss://api.example.com/ws"), None),
    ("another path", _document(websocket=_ws(path="/v1/reports/ws/")), None),
    ("a path that replaces the base path is not accepted", _document(websocket=_ws(path="/agent-speed/v1/reports/ws")),
     None),
    ("another subprotocol", _document(websocket=_ws(subprotocol="agent-speed-report.v2")), None),
    ("minIntervalMs below its range", _document(websocket=_ws(minIntervalMs=9)), None),
    ("minIntervalMs above its range", _document(websocket=_ws(minIntervalMs=1001)), None),
    ("maxInFlight 0", _document(websocket=_ws(maxInFlight=0)), None),
    ("maxInFlight 65", _document(websocket=_ws(maxInFlight=65)), None),
    ("ackTimeoutMs 499", _document(websocket=_ws(ackTimeoutMs=499)), None),
    ("maxMessageBytes 32767", _document(websocket=_ws(maxMessageBytes=32767)), None),
    ("pingIntervalSeconds 61", _document(websocket=_ws(pingIntervalSeconds=61)), None),
    ("pongTimeoutSeconds equal to pingIntervalSeconds", _document(websocket=_ws(pingIntervalSeconds=20,
                                                                                pongTimeoutSeconds=20)), None),
    ("a number given as a string", _document(websocket=_ws(maxInFlight="8")), None),
    ("a number given as a boolean", _document(websocket=_ws(maxInFlight=True)), None),
    ("other extensions stay independent", _document(extensions={"reportWebSocket": 1, "executionModes": 1}),
     DEFAULT_STREAM),
]

URL_CASES = [
    ("https://api.example.com/agent-speed", "wss://api.example.com/agent-speed/v1/reports/ws"),
    ("https://api.example.com", "wss://api.example.com/v1/reports/ws"),
    ("https://api.example.com:8443/radar/api", "wss://api.example.com:8443/radar/api/v1/reports/ws"),
    ("http://127.0.0.1:8787", "ws://127.0.0.1:8787/v1/reports/ws"),
]

ENCODING_CASES = [
    '{"a":"plain"}',
    '{"name":"say \\"hi\\"","path":"C:\\\\Users\\\\dev"}',
    '{"slash":"a/b","text":"小明的 Mac — 速蹬","line":"x\\ny"}',
    '{"emoji":"🚀","sep":"\u2028"}',
]

MESSAGE_CASES = [
    ("a ready", {"type": "ready", "protocol": "agent-speed-report", "version": 1, "transportVersion": 1,
                 "serverTime": "2026-10-24T20:00:00.000Z", **FULL_STREAM},
     {"type": "ready", "limits": FULL_STREAM}),
    ("a ready with another transport version", {"type": "ready", "protocol": "agent-speed-report", "version": 1,
                                                "transportVersion": 2, "serverTime": "2026-10-24T20:00:00.000Z",
                                                **FULL_STREAM}, "invalid"),
    ("a ready without serverTime", {"type": "ready", "protocol": "agent-speed-report", "version": 1,
                                    "transportVersion": 1, **FULL_STREAM}, "invalid"),
    ("a ready with a limit as a string", {"type": "ready", "protocol": "agent-speed-report", "version": 1,
                                          "transportVersion": 1, "serverTime": "2026-10-24T20:00:00.000Z",
                                          **FULL_STREAM, "maxInFlight": "8"}, "invalid"),
    ("a ready with other limits is valid (the scheduler compares them)",
     {"type": "ready", "protocol": "agent-speed-report", "version": 1, "transportVersion": 1,
      "serverTime": "2026-10-24T20:00:00.000Z", **FULL_STREAM, "minIntervalMs": 5},
     {"type": "ready", "limits": {**FULL_STREAM, "minIntervalMs": 5}}),
    ("the ack of REPORTING-WEBSOCKET 14",
     {"type": "ack", "run": {"id": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 42}, "status": "accepted",
      "bodySha256": "5432dab6e9dfe089930177ace227082abec9824e2ed9c9b0fa902f37bb4502e3",
      "receivedAt": "2026-10-24T20:00:00.021Z", "acceptedAt": "2026-10-24T20:00:00.028Z", "processingMs": 7},
     {"type": "ack", "runId": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 42, "status": "accepted",
      "bodySha256": "5432dab6e9dfe089930177ace227082abec9824e2ed9c9b0fa902f37bb4502e3", "rank": None}),
    ("a duplicate with a rank",
     {"type": "ack", "run": {"id": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 43}, "status": "duplicate",
      "bodySha256": "0" * 64, "receivedAt": "2026-10-24T20:00:00.021Z", "acceptedAt": "2026-10-24T20:00:00.028Z",
      "processingMs": 0, "rank": 3, "note": "ignored"},
     {"type": "ack", "runId": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 43, "status": "duplicate",
      "bodySha256": "0" * 64, "rank": 3}),
    ("an ack with another status", {"type": "ack", "run": {"id": "x", "seq": 1}, "status": "queued",
                                    "bodySha256": "0" * 64, "receivedAt": "2026-10-24T20:00:00.021Z",
                                    "acceptedAt": "2026-10-24T20:00:00.028Z", "processingMs": 0}, "invalid"),
    ("an ack with an upper-case hash", {"type": "ack", "run": {"id": "x", "seq": 1}, "status": "accepted",
                                        "bodySha256": "A" * 64, "receivedAt": "2026-10-24T20:00:00.021Z",
                                        "acceptedAt": "2026-10-24T20:00:00.028Z", "processingMs": 0}, "invalid"),
    ("an ack without processingMs", {"type": "ack", "run": {"id": "x", "seq": 1}, "status": "accepted",
                                     "bodySha256": "0" * 64, "receivedAt": "2026-10-24T20:00:00.021Z",
                                     "acceptedAt": "2026-10-24T20:00:00.028Z"}, "invalid"),
    ("an ack with seq 0", {"type": "ack", "run": {"id": "x", "seq": 0}, "status": "accepted", "bodySha256": "0" * 64,
                           "receivedAt": "2026-10-24T20:00:00.021Z", "acceptedAt": "2026-10-24T20:00:00.028Z",
                           "processingMs": 0}, "invalid"),
    ("an ack with rank 0", {"type": "ack", "run": {"id": "x", "seq": 1}, "status": "accepted", "bodySha256": "0" * 64,
                            "receivedAt": "2026-10-24T20:00:00.021Z", "acceptedAt": "2026-10-24T20:00:00.028Z",
                            "processingMs": 0, "rank": 0}, "invalid"),
    ("an ack time without milliseconds", {"type": "ack", "run": {"id": "x", "seq": 1}, "status": "accepted",
                                          "bodySha256": "0" * 64, "receivedAt": "2026-10-24T20:00:00Z",
                                          "acceptedAt": "2026-10-24T20:00:00.028Z", "processingMs": 0}, "invalid"),
    ("an error for a report", {"type": "error", "run": {"id": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 42},
                               "httpStatus": 429, "error": {"code": "rate_limited", "retryAfterSeconds": 5},
                               "fatal": False},
     {"type": "error", "runId": "c3d4e5f6-a7b8-4c9d-8e0f-1a2b3c4d5e6f", "seq": 42, "httpStatus": 429,
      "code": "rate_limited", "retryAfterSeconds": 5, "serverTime": None, "fatal": False}),
    ("an error of the connection", {"type": "error", "run": None, "httpStatus": 401,
                                    "error": {"code": "timestamp_out_of_window", "serverTime": 1792872010,
                                              "message": "clock"}, "fatal": True},
     {"type": "error", "runId": None, "seq": None, "httpStatus": 401, "code": "timestamp_out_of_window",
      "retryAfterSeconds": None, "serverTime": 1792872010, "fatal": True}),
    ("an error without run", {"type": "error", "httpStatus": 400, "error": {"code": "bad_request"}, "fatal": False},
     "invalid"),
    ("an error with a 3xx status", {"type": "error", "run": None, "httpStatus": 302, "error": {"code": "moved"},
                                    "fatal": False}, "invalid"),
    ("an error without fatal", {"type": "error", "run": None, "httpStatus": 500, "error": {"code": "internal"}},
     "invalid"),
    ("an error code with upper case", {"type": "error", "run": None, "httpStatus": 400,
                                       "error": {"code": "Bad_Request"}, "fatal": False}, "invalid"),
    ("another message type is ignored", {"type": "notice", "text": "maintenance at 22:00"}, {"type": "other"}),
    ("no type", {"run": None}, "invalid"),
]

RAW_MESSAGE_CASES = [
    ("a duplicate member name", '{"type":"ack","type":"ready"}', "invalid"),
    ("an array", '[{"type":"ready"}]', "invalid"),
    ("NaN", '{"type":"error","run":null,"httpStatus":NaN,"error":{"code":"x"},"fatal":false}', "invalid"),
    ("a lone surrogate", '{"type":"notice","text":"\\ud800"}', "invalid"),
    ("a byte order mark", '\ufeff{"type":"notice"}', "invalid"),
    ("trailing text", '{"type":"notice"} {}', "invalid"),
    ("deeper than 32 levels", '{"type":"notice","x":' + '[' * 32 + ']' * 32 + '}', "invalid"),
    ("32 levels", '{"type":"notice","x":' + '[' * 31 + ']' * 31 + '}', {"type": "other"}),
]

REJECTION_CASES = [
    (401, '{"error":{"code":"timestamp_out_of_window","serverTime":1792872400}}', None,
     {"code": "timestamp_out_of_window", "retryAfterSeconds": None}),
    (401, None, None, {"code": "signature_invalid", "retryAfterSeconds": None}),
    (401, '<html>401</html>', None, {"code": "signature_invalid", "retryAfterSeconds": None}),
    (403, None, None, {"code": "forbidden", "retryAfterSeconds": None}),
    (404, '{"error":{"code":"unknown_device"}}', None, {"code": "unknown_device", "retryAfterSeconds": None}),
    (404, None, None, {"code": "unknown_device", "retryAfterSeconds": None}),
    (410, None, None, {"code": "gone", "retryAfterSeconds": None}),
    (413, None, None, {"code": "payload_too_large", "retryAfterSeconds": None}),
    (429, '{"error":{"code":"rate_limited","retryAfterSeconds":30}}', "12", {"code": "rate_limited",
                                                                             "retryAfterSeconds": 30}),
    (429, None, "12", {"code": "rate_limited", "retryAfterSeconds": 12}),
    (429, None, None, {"code": "rate_limited", "retryAfterSeconds": None}),
    (429, None, "Sat, 24 Oct 2026 20:00:00 GMT", {"code": "rate_limited", "retryAfterSeconds": None}),
    (503, None, "20", {"code": "rate_limited", "retryAfterSeconds": 20}),
    (503, None, None, {"code": None, "retryAfterSeconds": None}),
    (502, None, None, {"code": None, "retryAfterSeconds": None}),
    (400, None, None, {"code": "bad_request", "retryAfterSeconds": None}),
    (426, '{"error":{"code":"upgrade_required"}}', None, {"code": "upgrade_required", "retryAfterSeconds": None}),
    (301, None, None, {"code": None, "retryAfterSeconds": None}),
]

LATENCY_CASES = [
    ([], None, None),
    ([40], 40, 40),
    ([30, 10, 20], 20, 30),
    (list(range(1, 101)), 50, 95),
    ([5] * 19 + [900], 5, 5),
    ([5] * 18 + [700, 900], 5, 700),
]


def build_websocket_vectors(keys: dict) -> dict:
    d = int(keys["device"]["d"], 16)
    api = "https://api.example.com/agent-speed"
    device = WS_BODY["deviceId"]
    handshake_nonce, report_nonce = ref.b64url(bytes(range(16, 32))), ref.b64url(bytes(range(16)))
    handshake = handshake_headers(d, api, device, timestamp=WS_TIME, nonce=handshake_nonce, version="0.0.0-test")
    body = ref.compact_json(WS_BODY)
    report_headers = ref.signed_headers(d, "POST", api + "/v1/reports", device, body, timestamp=WS_TIME,
                                        nonce=report_nonce, version="0.0.0-test")
    fixed = {"X-AgentSpeed-Device": device, "X-AgentSpeed-Timestamp": str(WS_TIME),
             "X-AgentSpeed-Nonce": report_nonce, "X-AgentSpeed-Signature": "S" * 86}
    return {
        "_comment": "METRICS 11.9 and REPORTING-WEBSOCKET vectors, written by spec/tools/report_reference.py --write from "
                    "spec/tools/report_websocket.py. Scheduler expectations are hand-written; counters not listed "
                    "are 0. Signatures are deterministic here; apps verify instead of comparing bytes.",
        "discovery": [{"name": name, "document": document, "expect": expect}
                      for name, document, expect in DISCOVERY_CASES],
        "url": [{"api": api_url, "expect": url} for api_url, url in URL_CASES],
        "handshake": {"method": "GET", "url": websocket_url(api), "authority": "api.example.com",
                      "path": "/agent-speed" + WS_PATH,
                      "headers": {name: handshake[name] for name in (*SIGNATURE_HEADERS, "Sec-WebSocket-Protocol")},
                      "canonical": ref.canonical_string("GET", "api.example.com", "/agent-speed" + WS_PATH,
                                                        str(WS_TIME), handshake_nonce, device, b"")},
        "report": {"body": body.decode("utf-8"), "bodySha256": ref.sha256_hex(body),
                   "headers": {name: report_headers[name] for name in SIGNATURE_HEADERS},
                   "canonical": ref.canonical_string("POST", "api.example.com", "/agent-speed/v1/reports",
                                                     str(WS_TIME), report_nonce, device, body),
                   "message": report_message(report_headers, body)},
        "encoding": [{"body": text, "headers": fixed, "message": report_message(fixed, text.encode("utf-8"))}
                     for text in ENCODING_CASES],
        "realtime": [
            {"sourceMode": "log", "sentAtMs": WS_TIME * 1000, "observationAgeMs": 10,
             "expect": realtime("log", WS_TIME * 1000, 10)},
            {"sourceMode": "log", "sentAtMs": WS_TIME * 1000 + 5, "observationAgeMs": None,
             "expect": realtime("log", WS_TIME * 1000 + 5, None)},
            {"sourceMode": "mixed", "sentAtMs": WS_TIME * 1000, "observationAgeMs": 1000,
             "expect": realtime("mixed", WS_TIME * 1000, 1000)},
        ],
        "messages": [{"name": name, "text": ref.compact_json(message).decode("utf-8"), "expect": expect}
                     for name, message, expect in MESSAGE_CASES]
                    + [{"name": name, "text": text, "expect": expect} for name, text, expect in RAW_MESSAGE_CASES]
                    + [{"name": "larger than maxMessageBytes", "text": '{"type":"notice","pad":"' + "x" * 32_768 + '"}',
                        "maxMessageBytes": 32_768, "expect": "invalid"}],
        "rejections": [{"status": status, "body": body_text, "retryAfter": header, "expect": expect}
                       for status, body_text, header, expect in REJECTION_CASES],
        "backoff": [BACKOFF_MS[min(failures, len(BACKOFF_MS)) - 1] for failures in range(1, 10)],
        "latency": [{"samples": samples, "p50": p50, "p95": p95} for samples, p50, p95 in LATENCY_CASES],
        "scheduler": STREAM_CASES,
    }


def check_websocket_vectors(vectors: dict, keys: dict) -> list[str]:
    failures = []
    for case in vectors["discovery"]:
        got = stream_settings(case["document"])
        if got != case["expect"]:
            failures.append(f"websocket.discovery: {case['name']}: got {got}")
        if ref.parse_discovery(case["document"], "https://example.com/.well-known/agent-speed-report").get(
                "websocket") != case["expect"]:
            failures.append(f"websocket.discovery: {case['name']}: parse_discovery differs")
    for case in vectors["url"]:
        if websocket_url(case["api"]) != case["expect"]:
            failures.append(f"websocket.url: {case['api']}")
    pub = ref.parse_jwk(keys["device"]["jwk"])
    handshake = vectors["handshake"]
    signature = ref.b64url_decode(handshake["headers"]["X-AgentSpeed-Signature"])
    if not ref.verify(pub, handshake["canonical"].encode("utf-8"), signature):
        failures.append("websocket.handshake: the signature does not verify")
    if ref.authority_of(handshake["url"].replace("wss://", "https://")) != handshake["authority"]:
        failures.append("websocket.handshake: authority")
    report = vectors["report"]
    body = report["body"].encode("utf-8")
    if ref.sha256_hex(body) != report["bodySha256"] or json.loads(report["message"])["body"] != report["body"]:
        failures.append("websocket.report: body or message differs")
    if not ref.verify(pub, report["canonical"].encode("utf-8"),
                      ref.b64url_decode(report["headers"]["X-AgentSpeed-Signature"])):
        failures.append("websocket.report: the signature does not verify")
    tampered = report["canonical"].replace(report["bodySha256"], ref.sha256_hex(body.replace(b'"output":5', b'"output":6')))
    if ref.verify(pub, tampered.encode("utf-8"), ref.b64url_decode(report["headers"]["X-AgentSpeed-Signature"])):
        failures.append("websocket.report: a changed body still verifies")
    for case in vectors["encoding"]:
        message = report_message(case["headers"], case["body"].encode("utf-8"))
        if message != case["message"] or json.loads(message)["body"] != case["body"]:
            failures.append(f"websocket.encoding: {case['body']!r}")
    for case in vectors["realtime"]:
        if realtime(case["sourceMode"], case["sentAtMs"], case["observationAgeMs"]) != case["expect"]:
            failures.append(f"websocket.realtime: {case}")
    for case in vectors["messages"]:
        try:
            got = parse_service_message(case["text"].encode("utf-8"), case.get("maxMessageBytes", 65_536))
        except InvalidMessage:
            got = "invalid"
        if got != case["expect"]:
            failures.append(f"websocket.messages: {case['name']}: got {got}")
    for case in vectors["rejections"]:
        body = None if case["body"] is None else case["body"].encode("utf-8")
        got = classify_rejection(case["status"], body, case["retryAfter"])
        if got != case["expect"]:
            failures.append(f"websocket.rejections: {case}: got {got}")
    if vectors["backoff"] != [500, 1000, 2000, 4000, 8000, 16000, 30000, 30000, 30000]:
        failures.append("websocket.backoff")
    for case in vectors["latency"]:
        if (percentile(case["samples"], 50), percentile(case["samples"], 95)) != (case["p50"], case["p95"]):
            failures.append(f"websocket.latency: {case['samples'][:5]}…")
    failures += check_stream_cases(vectors["scheduler"])
    return failures
