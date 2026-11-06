"""The WebSocket endpoint of the reference server (REPORTING-WEBSOCKET, METRICS 11.9), standard library only.

RFC 6455 framing for one upgraded connection of report_server.py: the signed upgrade is checked by
ReferenceService.handle (it answers 101 with the device id), then the session sends `ready`, answers every `report`
message with `ack` or `error` through the same service rules as POST /v1/reports, keeps one connection per device
(a newer one closes the older with 4009), binds the connection to the first accepted run (another run is
`run_conflict`), answers Pings and sends its own. Test switches make it hold or drop answers and lie in `ready`.
"""

from __future__ import annotations

import base64
import hashlib
import json
import queue
import struct
import threading
import time

import report_reference as ref
from report_websocket import SIGNATURE_HEADERS, SUBPROTOCOL, InvalidMessage, strict_object

GUID = b"258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
TEXT, BINARY, CLOSE, PING, PONG, CONTINUATION = 0x1, 0x2, 0x8, 0x9, 0xA, 0x0


def accept_key(key: str) -> str:
    return base64.b64encode(hashlib.sha1(key.encode("ascii") + GUID).digest()).decode("ascii")


def upgrade_problem(headers: dict) -> str | None:
    """Why a GET is not a usable WebSocket upgrade for this endpoint, or None."""
    lowered = {k.lower(): v for k, v in headers.items()}
    if "websocket" not in lowered.get("upgrade", "").lower():
        return "Upgrade: websocket is required"
    if "upgrade" not in [token.strip().lower() for token in lowered.get("connection", "").split(",")]:
        return "Connection: Upgrade is required"
    if lowered.get("sec-websocket-version", "").strip() != "13":
        return "Sec-WebSocket-Version 13 is required"
    try:
        if len(base64.b64decode(lowered.get("sec-websocket-key", ""), validate=True)) != 16:
            return "Sec-WebSocket-Key is not 16 bytes"
    except ValueError:
        return "Sec-WebSocket-Key is not base64"
    offered = [token.strip() for token in lowered.get("sec-websocket-protocol", "").split(",")]
    if SUBPROTOCOL not in offered:
        return f"Sec-WebSocket-Protocol {SUBPROTOCOL} is required"
    return None


class Closed(Exception):
    pass


class Session:
    """One open report connection. `options` holds the test switches of report_server.py."""

    def __init__(self, handler, service: ref.ReferenceService, lock: threading.Lock, device_id: str,
                 authorization: str | None, registry: dict, options: dict):
        self.handler, self.service, self.lock = handler, service, lock
        self.device_id, self.authorization, self.registry = device_id, authorization, registry
        self.options = options
        self.settings = service.websocket
        self.write_lock = threading.Lock()
        self.inbox: queue.Queue = queue.Queue()
        self.closed = threading.Event()
        self.run_id: str | None = None
        self.unknown_types = 0
        self.answers = 0
        self.last_pong = time.monotonic()

    # -- frames

    def send(self, opcode: int, payload: bytes = b"") -> None:
        header = bytes([0x80 | opcode])
        size = len(payload)
        if size < 126:
            header += bytes([size])
        elif size < 65_536:
            header += bytes([126]) + struct.pack("!H", size)
        else:
            header += bytes([127]) + struct.pack("!Q", size)
        with self.write_lock:
            try:
                self.handler.wfile.write(header + payload)
                self.handler.wfile.flush()
            except OSError:
                self.closed.set()

    def send_json(self, value: dict) -> None:
        self.send(TEXT, json.dumps(value, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))

    def close(self, code: int, reason: str = "") -> None:
        if not self.closed.is_set():
            self.send(CLOSE, struct.pack("!H", code) + reason.encode("utf-8")[:120])
            self.closed.set()
            self.service.log(f"ws: closed {self.device_id[:8]} with {code} {reason}".rstrip())

    def _read_exact(self, size: int) -> bytes:
        data = self.handler.rfile.read(size)
        if len(data) != size:
            raise Closed()
        return data

    def _read_frame(self) -> tuple[bool, int, bytes]:
        first, second = self._read_exact(2)
        if first & 0x70:
            raise ValueError("reserved bits set (no extension was negotiated)")
        if not second & 0x80:
            raise ValueError("client frames must be masked")
        size = second & 0x7F
        if size == 126:
            size = struct.unpack("!H", self._read_exact(2))[0]
        elif size == 127:
            size = struct.unpack("!Q", self._read_exact(8))[0]
        if size > self.settings["maxMessageBytes"]:
            raise OverflowError()
        mask = self._read_exact(4)
        data = bytes(b ^ mask[i % 4] for i, b in enumerate(self._read_exact(size)))
        return bool(first & 0x80), first & 0x0F, data

    def _reader(self) -> None:
        parts: list[bytes] = []
        started = 0.0
        try:
            while not self.closed.is_set():
                fin, opcode, data = self._read_frame()
                if opcode == PING:
                    self.send(PONG, data)
                elif opcode == PONG:
                    self.last_pong = time.monotonic()
                elif opcode == CLOSE:
                    code = struct.unpack("!H", data[:2])[0] if len(data) >= 2 else 1005
                    if not self.closed.is_set():
                        self.send(CLOSE, data[:2])
                        self.closed.set()
                    self.service.log(f"ws: {self.device_id[:8]} closed with {code}")
                elif opcode == BINARY or (opcode == CONTINUATION and not parts):
                    self.close(1003, "text messages only")
                elif opcode in (TEXT, CONTINUATION):
                    if opcode == TEXT:
                        parts, started = [], time.monotonic()
                    parts.append(data)
                    if sum(map(len, parts)) > self.settings["maxMessageBytes"]:
                        self.close(1009, "message too large")
                    elif time.monotonic() - started > 10:
                        self.close(1008, "message took too long")
                    elif fin:
                        self.inbox.put((b"".join(parts), time.time(), time.monotonic()))
                        parts = []
                else:
                    self.close(1002, "unknown opcode")
        except OverflowError:
            self.close(1009, "message too large")
        except ValueError as error:
            self.close(1002, str(error))
        except (Closed, OSError):
            self.closed.set()

    # -- messages

    def ready(self) -> None:
        limits = {**self.settings, **self.options.get("readyLimits", {})}
        self.send_json({"type": "ready", "protocol": "agent-speed-report", "version": 1, "transportVersion": 1,
                        "serverTime": ref.format_instant(int(time.time() * 1000)), **limits})

    def answer(self, data: bytes, received_wall: float, received_mono: float) -> None:
        try:
            message = strict_object(data, self.settings["maxMessageBytes"])
        except InvalidMessage:
            self.close(1007 if not _utf8(data) else 1008, "invalid message")
            return
        if message.get("type") != "report":
            self.unknown_types += 1
            self.send_json({"type": "error", "run": None, "httpStatus": 400,
                            "error": {"code": "bad_request", "message": "unknown message type"}, "fatal": False})
            if self.unknown_types >= 3:
                self.close(1008, "bad_request")
            return
        self.unknown_types = 0
        headers, body = message.get("headers"), message.get("body")
        if (not isinstance(headers, dict) or not isinstance(body, str)
                or any(not isinstance(headers.get(name), str) for name in SIGNATURE_HEADERS)
                or len({k.lower() for k in headers if k.lower().startswith("x-agentspeed-")}) != len(
                    [k for k in headers if k.lower().startswith("x-agentspeed-")])):
            self.send_json({"type": "error", "run": None, "httpStatus": 400,
                            "error": {"code": "bad_request", "message": "headers or body malformed"}, "fatal": False})
            return
        body_bytes = body.encode("utf-8")
        run = None
        try:
            parsed = json.loads(body)
            if isinstance(parsed, dict) and isinstance(parsed.get("run"), dict):
                run = {"id": parsed["run"].get("id"), "seq": parsed["run"].get("seq")}
        except ValueError:
            parsed = None
        if headers["X-AgentSpeed-Device"] != self.device_id:
            self.error(run, 400, {"code": "device_mismatch"})
            return
        if self.run_id is not None and run is not None and run["id"] != self.run_id:
            self.error(run, 409, {"code": "run_conflict"}, fatal=True)
            self.close(1008, "run_conflict")
            return
        if self.registry.get(self.device_id) is not self:
            self.close(4009, "connection_replaced")
            return
        call = {name: headers[name] for name in SIGNATURE_HEADERS}
        if self.authorization:
            call["Authorization"] = self.authorization
        with self.lock:
            status, _, payload = self.service.handle("POST", self.service.api_path + "/v1/reports", call, body_bytes,
                                                     channel="websocket")
        self.answers += 1
        if status == 200:
            if self.run_id is None and run is not None:
                self.run_id = run["id"]
            ack = {"type": "ack", "run": run, "status": payload["status"], "bodySha256": ref.sha256_hex(body_bytes),
                   "receivedAt": ref.format_instant(int(received_wall * 1000)),
                   "acceptedAt": ref.format_instant(int(time.time() * 1000)),
                   "processingMs": int((time.monotonic() - received_mono) * 1000)}
            drop = self.options.get("dropAnswers", 0)
            if drop and self.answers % drop == 0:
                self.service.log(f"ws: dropped the answer to seq {run and run['seq']} (test switch)")
                return
            delay = self.options.get("answerDelayMs", 0)
            if delay:
                time.sleep(delay / 1000)
            self.send_json(ack)
        else:
            self.error(run, status, payload["error"])

    def error(self, run, status: int, error: dict, fatal: bool = False) -> None:
        safe_run = run if run and isinstance(run.get("id"), str) and isinstance(run.get("seq"), int) else None
        self.send_json({"type": "error", "run": safe_run, "httpStatus": status, "error": error, "fatal": fatal})

    # -- the connection

    def run(self) -> None:
        previous = self.registry.get(self.device_id)
        self.registry[self.device_id] = self
        if previous is not None and previous is not self:
            previous.close(4009, "connection_replaced")
        self.service.log(f"ws: {self.device_id[:8]} connected")
        self.ready()
        threading.Thread(target=self._reader, daemon=True).start()
        ping_every, pong_within = self.settings["pingIntervalSeconds"], self.settings["pongTimeoutSeconds"]
        next_ping = time.monotonic() + ping_every
        ping_sent = None
        try:
            while not self.closed.is_set():
                try:
                    self.answer(*self.inbox.get(timeout=0.25))
                except queue.Empty:
                    pass
                now = time.monotonic()
                if ping_sent is not None and self.last_pong >= ping_sent:
                    ping_sent = None
                if ping_sent is not None and now - ping_sent > pong_within:
                    self.service.log(f"ws: {self.device_id[:8]} missed a Pong")
                    self.closed.set()
                elif now >= next_ping:
                    ping_sent, next_ping = now, now + ping_every
                    self.send(PING, b"agent-speed")
        finally:
            if self.registry.get(self.device_id) is self:
                del self.registry[self.device_id]


def _utf8(data: bytes) -> bool:
    try:
        data.decode("utf-8")
        return True
    except UnicodeDecodeError:
        return False
