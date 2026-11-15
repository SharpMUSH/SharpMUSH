"""The one-click Caddy routing for telnet over WebSocket, run through a real Caddy.

Opt-in (SHARPMUSH_CADDY_TESTS=1) because it needs Docker and the caddy:2-alpine image the one-click stack runs.
install.sh writes the guard and the telnet name's site exactly as it does on a server; the stack's Caddyfile is
used as shipped, with only its upstreams pointed at a stub that records what reaches it.
"""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import threading
import time
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[2]
SOURCE = (ROOT / 'deploy/oneclick/install.sh').read_text().split('\ncase "${1:-install}" in')[0]
IMAGE = 'caddy:2-alpine'
DOMAIN = 'mush.example.test'
TELNET = 'telnet.mush.example.test'
STUBS = r'''
systemctl() { :; }
curl() { echo 'Unexpected network call' >&2; exit 91; }
docker() { exit 92; }
'''


def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]


class Backend(ThreadingHTTPServer):
    """Stands in for socketserver and the main server: records each request that reaches it."""

    def __init__(self):
        self.seen = []
        super().__init__(('127.0.0.1', 0), BackendHandler)


class BackendHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.server.seen.append((self.path, self.headers.get('Upgrade')))
        self.send_response(200)
        self.send_header('Content-Length', '7')
        self.end_headers()
        self.wfile.write(b'backend')

    def log_message(self, *args):
        pass


@unittest.skipUnless(os.environ.get('SHARPMUSH_CADDY_TESTS') == '1' and shutil.which('docker'),
                     'set SHARPMUSH_CADDY_TESTS=1 with Docker available')
class TelnetRoutingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.dir = Path(tempfile.mkdtemp(prefix='sharpmush-caddy-'))
        cls.backend = Backend()
        threading.Thread(target=cls.backend.serve_forever, daemon=True).start()
        cls.port = free_port()
        cls.container = f'sharpmush-caddy-test-{uuid.uuid4().hex[:8]}'
        cls.install_files()
        cls.write_caddyfile()

    @classmethod
    def tearDownClass(cls):
        cls.stop_caddy()
        cls.backend.shutdown()
        shutil.rmtree(cls.dir, ignore_errors=True)

    @classmethod
    def install_files(cls):
        """arm_guard and telnet_tls, as install.sh runs them in Let's Encrypt mode with a telnet name."""
        for sub in ('guard', 'sites', 'state'):
            (cls.dir / sub).mkdir()
        env = dict(os.environ, SHARPMUSH_DIR=str(cls.dir), SHARPMUSH_DOMAIN=DOMAIN, SHARPMUSH_TELNET_HOST=TELNET,
                   CLOUDFLARE_TUNNEL_TOKEN='', SHARPMUSH_GUARD='on')
        script = 'id() { echo 0; }\n' + SOURCE + '\n' + STUBS + '\narm_guard\ntelnet_tls\n'
        subprocess.run(['bash', '-c', script], env=env, check=True, capture_output=True, text=True, timeout=10)
        sites = cls.dir / 'sites/telnet.caddy'
        text = sites.read_text()
        if f'\n{TELNET} {{\n' not in text:
            raise AssertionError(f'install.sh did not write a site for {TELNET}:\n{text}')
        # Plain HTTP on a test port: the routing is under test, not the certificate.
        sites.write_text(text.replace(f'\n{TELNET} {{\n', f'\nhttp://{TELNET}:{cls.port} {{\n'))
        cls.code = (cls.dir / 'state/setup-code').read_text().strip()

    @classmethod
    def write_caddyfile(cls):
        text = (ROOT / 'deploy/oneclick/Caddyfile').read_text()
        upstream = f'127.0.0.1:{cls.backend.server_address[1]}'
        for name in ('socketserver:4202', 'sharpmush-server:8080'):
            if name not in text:
                raise AssertionError(f'The one-click Caddyfile no longer names {name}; update this test')
            text = text.replace(name, upstream)
        text = text.replace('/etc/caddy/', f'{cls.dir}/')
        text = text.replace('{\n\tservers {', '{\n\tauto_https off\n\tadmin off\n\tservers {', 1)
        (cls.dir / 'Caddyfile').write_text(text)

    @classmethod
    def start_caddy(cls):
        subprocess.run(['docker', 'run', '-d', '--name', cls.container, '--network', 'host',
                        '-v', f'{cls.dir}:{cls.dir}', '-e', f'SHARPMUSH_SITE=http://{DOMAIN}:{cls.port}',
                        IMAGE, 'caddy', 'run', '--config', f'{cls.dir}/Caddyfile', '--adapter', 'caddyfile'],
                       check=True, capture_output=True, text=True, timeout=300)
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            try:
                with socket.create_connection(('127.0.0.1', cls.port), timeout=1):
                    return
            except OSError:
                time.sleep(0.2)
        logs = subprocess.run(['docker', 'logs', cls.container], capture_output=True, text=True).stderr
        raise AssertionError(f'Caddy did not start:\n{logs}')

    @classmethod
    def stop_caddy(cls):
        subprocess.run(['docker', 'rm', '-f', cls.container], capture_output=True, timeout=60)

    def request(self, host, path, upgrade=True, cookie=None):
        headers = [f'GET {path} HTTP/1.1', f'Host: {host}', 'Connection: close' if not upgrade else 'Connection: Upgrade']
        if upgrade:
            headers += ['Upgrade: websocket', 'Sec-WebSocket-Version: 13',
                        'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==', 'Sec-WebSocket-Protocol: telnet']
        if cookie:
            headers.append(f'Cookie: {cookie}')
        with socket.create_connection(('127.0.0.1', self.port), timeout=10) as s:
            s.sendall(('\r\n'.join(headers) + '\r\n\r\n').encode('ascii'))
            status = s.makefile('rb').readline().decode('iso-8859-1')
        return int(status.split()[1])

    def test_guard_then_open(self):
        # Two phases in one test: Caddy reads the guard at start, as the stack's reload does.
        self.start_caddy()
        try:
            for host, path in ((TELNET, '/'), (TELNET, '/telnet'), (TELNET, '/health'), (DOMAIN, '/telnet')):
                with self.subTest(guarded=f'{host}{path}'):
                    self.backend.seen.clear()
                    status = self.request(host, path)
                    self.assertNotEqual(status, 200, f'{host}{path} reached the backend while guarded')
                    self.assertEqual(self.backend.seen, [], f'{host}{path} reached the backend while guarded')
        finally:
            self.stop_caddy()

        (self.dir / 'guard/guard.caddy').write_text('# The owner finished /setup; the guard is off.\n')
        self.start_caddy()
        try:
            for host, path in ((TELNET, '/'), (TELNET, '/anything'), (DOMAIN, '/telnet')):
                with self.subTest(open=f'{host}{path}'):
                    self.backend.seen.clear()
                    self.assertEqual(self.request(host, path), 200)
                    self.assertEqual(self.backend.seen, [('/telnet', 'websocket')])
            with self.subTest(open='health is never telnet'):
                self.backend.seen.clear()
                self.assertEqual(self.request(TELNET, '/health'), 404)
                self.assertEqual(self.backend.seen, [])
            with self.subTest(open='a plain request on the telnet name'):
                self.backend.seen.clear()
                self.assertEqual(self.request(TELNET, '/', upgrade=False), 404)
                self.assertEqual(self.backend.seen, [])
        finally:
            self.stop_caddy()


if __name__ == '__main__':
    unittest.main()
