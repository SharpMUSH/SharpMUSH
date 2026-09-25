"""A telnet session that turns a stream of MUSH output into per-command transcripts.

Output is attributed to commands without sleeps: after every step the runner sends a sentinel
(`think <token>`) on every open session and reads each stream until its token comes back. Both
servers process the commands of one connection in order, so everything printed before the token
belongs to the step (or to earlier queued work) and nothing later can be lost or mis-attributed.
"""
from __future__ import annotations

import socket
import time

IAC, DONT, DO, WONT, WILL, SB, SE = 255, 254, 253, 252, 251, 250, 240


class SessionError(RuntimeError):
    pass


def split_telnet(data: bytes) -> tuple[bytes, bytes]:
    """Remove telnet negotiation (IAC sequences) from raw bytes; keep everything else.

    Returns (text, rest): `rest` is an IAC sequence cut off at the end of `data`, to be completed
    by the next read. Stripped text is never stripped again, so an escaped 0xFF stays one byte.
    """
    out = bytearray()
    i, n = 0, len(data)
    while i < n:
        b = data[i]
        if b != IAC:
            out.append(b)
            i += 1
        elif i + 1 >= n:
            return bytes(out), data[i:]
        elif data[i + 1] == IAC:  # escaped 0xFF
            out.append(IAC)
            i += 2
        elif data[i + 1] in (DO, DONT, WILL, WONT):
            if i + 2 >= n:
                return bytes(out), data[i:]
            i += 3
        elif data[i + 1] == SB:
            j = data.find(bytes([IAC, SE]), i + 2)
            if j < 0:
                return bytes(out), data[i:]
            i = j + 2
        else:
            i += 2
    return bytes(out), b""


def strip_telnet(data: bytes) -> bytes:
    """`split_telnet` for a complete stream: an unfinished trailing sequence is dropped."""
    return split_telnet(data)[0]


class Session:
    def __init__(self, name: str, host: str, port: int, token_prefix: str, timeout: float = 20.0):
        self.name = name
        self._prefix = token_prefix
        self._timeout = timeout
        self._seq = 0
        self._text = b""     # received so far, telnet negotiation removed
        self._partial = b""  # an IAC sequence split across reads, still raw
        self._sock = socket.create_connection((host, port), timeout=timeout)
        self._sock.setblocking(True)

    def send(self, line: str) -> None:
        self._sock.sendall(line.encode("utf-8") + b"\r\n")

    def next_token(self) -> str:
        self._seq += 1
        return f"{self._prefix}{self.name}x{self._seq}"

    def _read_until(self, marker: bytes) -> bytes:
        deadline = time.monotonic() + self._timeout
        while True:
            idx = self._text.find(marker)
            if idx >= 0:
                consumed = self._text[:idx]
                self._text = self._text[idx + len(marker):]
                return consumed
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise SessionError(f"session {self.name}: timed out waiting for {marker!r}; "
                                   f"received so far: {self._text[-300:]!r}")
            self._sock.settimeout(remaining)
            try:
                chunk = self._sock.recv(65536)
            except socket.timeout:
                continue
            if not chunk:
                raise SessionError(f"session {self.name}: connection closed while waiting for {marker!r}")
            text, self._partial = split_telnet(self._partial + chunk)
            self._text += text

    def sync_start(self) -> str:
        """Queue the sentinel; pair with `sync_finish`. Sending to every session first lets
        cross-session output that is already in flight land before its sentinel."""
        token = self.next_token()
        self.send(f"think {token}")
        return token

    def sync_finish(self, token: str) -> str:
        before = self._read_until(token.encode() + b"\r\n")
        return before.decode("utf-8", errors="replace")

    # Before login `think` does not exist on either server, but INFO does and always ends with the
    # same marker line, so it is the pre-login sentinel. It also delimits the connect banner.
    INFO_END = b"### End INFO\r\n"

    def _info_sync(self) -> str:
        self.send("INFO")
        text = self._read_until(self.INFO_END).decode("utf-8", errors="replace")
        return text.split("### Begin INFO")[0]

    def read_banner(self) -> str:
        """Consume the connect screen and return it. The banner is site content (connect.txt),
        recorded for the report but never compared."""
        return self._info_sync()

    def sync_prelogin(self) -> str:
        """Everything printed since the last read, for a session that is not logged in."""
        return self._info_sync()

    def settle(self) -> str:
        """Wait for queued work (@wait, @trigger, @dolist...) that the step started: the sentinel
        goes through the command queue, behind whatever is already queued."""
        token = self.next_token()
        self.send(f"@wait 0=think {token}")
        return self._read_until(token.encode() + b"\r\n").decode("utf-8", errors="replace")

    def close(self) -> None:
        """QUIT and wait for the server to close the socket, so that the disconnect it announces
        to other sessions has been sent before anything else is read."""
        try:
            self.send("QUIT")
            deadline = time.monotonic() + self._timeout
            while time.monotonic() < deadline:
                self._sock.settimeout(max(0.1, deadline - time.monotonic()))
                try:
                    if not self._sock.recv(65536):
                        break
                except socket.timeout:
                    continue
        except OSError:
            pass  # the server already dropped the connection: nothing left to wait for
        finally:
            self._sock.close()
