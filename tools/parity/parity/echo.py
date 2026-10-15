"""A local HTTP site for `@http` cases: it answers with one line describing the request it received.

Both servers send to the same site, so a difference in that line is a difference in the request.
Only what the MUSH chooses is described (method, Content-Type, Authorization, `X-` headers, body);
headers each HTTP library adds on its own (User-Agent, Accept, Host, Content-Length) are left out.
A path starting `/auth` answers 401 to a request with no Authorization, as a site asking for a
login does: PennMUSH's curl sends `userpass` only after that, SharpMUSH at once.
"""
from __future__ import annotations

import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class _Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def _answer(self) -> None:
        length = int(self.headers.get("Content-Length") or 0)
        body = self.rfile.read(length).decode("utf-8", errors="replace") if length else ""
        auth = self.headers.get("Authorization")
        if self.path.startswith("/auth") and not auth:
            self._send(401, "login needed", {"WWW-Authenticate": 'Basic realm="parity"'})
            return
        own = sorted(f"{k.lower()}={v}" for k, v in self.headers.items() if k.lower().startswith("x-"))
        line = " ".join([self.command, f"type={self.headers.get('Content-Type') or '-'}", f"auth={auth or '-'}",
                         *own, f"body={body or '-'}"])
        self._send(200, line, {})

    def _send(self, status: int, text: str, headers: dict) -> None:
        data = text.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        for k, v in headers.items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(data)

    do_GET = do_POST = do_PUT = do_DELETE = do_PATCH = do_OPTIONS = do_HEAD = _answer

    def log_message(self, *args) -> None:  # the transcripts are the record
        pass


class EchoSite:
    def __init__(self) -> None:
        self._server = ThreadingHTTPServer(("127.0.0.1", 0), _Handler)
        self.url = f"http://127.0.0.1:{self._server.server_address[1]}"
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)

    def start(self) -> "EchoSite":
        self._thread.start()
        return self

    def stop(self) -> None:
        self._server.shutdown()
        self._server.server_close()
