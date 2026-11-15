"""One-click Caddy routing for telnet over WebSocket. Needs Docker; opt in with SHARPMUSH_CADDY_TESTS=1."""
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
UPSTREAMS = ('socketserver:4202', 'sharpmush-server:8080')
GUARD_LIFTED = '# The owner finished /setup; the guard is off.\n'
STUBS = r'''
systemctl() { :; }
curl() { echo 'Unexpected network call' >&2; exit 91; }
docker() { exit 92; }
'''

requires_caddy = unittest.skipUnless(os.environ.get('SHARPMUSH_CADDY_TESTS') == '1' and shutil.which('docker'),
                                     'set SHARPMUSH_CADDY_TESTS=1 with Docker available')


def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]


def replace_once(text, old, new):
    if old not in text:
        raise AssertionError(f'Expected {old!r} in the one-click config; update this test')
    return text.replace(old, new, 1)


class Backend(ThreadingHTTPServer):
    def __init__(self):
        super().__init__(('127.0.0.1', 0), Recorder)
        self.seen = []
        threading.Thread(target=self.serve_forever, daemon=True).start()

    def close(self):
        self.shutdown()
        self.server_close()


class Recorder(BaseHTTPRequestHandler):
    def do_GET(self):
        self.server.seen.append((self.path, self.headers.get('Upgrade')))
        self.send_response(200)
        self.send_header('Content-Length', '0')
        self.end_headers()

    def log_message(self, *args):
        pass


def write_stack(directory, port, upstream, guarded):
    for sub in ('guard', 'sites', 'state'):
        (directory / sub).mkdir()
    env = dict(os.environ, SHARPMUSH_DIR=str(directory), SHARPMUSH_DOMAIN=DOMAIN, SHARPMUSH_TELNET_HOST=TELNET,
               CLOUDFLARE_TUNNEL_TOKEN='', SHARPMUSH_GUARD='on')
    script = 'id() { echo 0; }\n' + SOURCE + '\n' + STUBS + '\narm_guard\ntelnet_tls\n'
    subprocess.run(['bash', '-c', script], env=env, check=True, capture_output=True, text=True, timeout=10)

    site = directory / 'sites/telnet.caddy'
    site.write_text(replace_once(site.read_text(), f'\n{TELNET} {{\n', f'\nhttp://{TELNET}:{port} {{\n'))
    if not guarded:
        (directory / 'guard/guard.caddy').write_text(GUARD_LIFTED)

    caddyfile = (ROOT / 'deploy/oneclick/Caddyfile').read_text()
    for name in UPSTREAMS:
        caddyfile = replace_once(caddyfile, name, name).replace(name, upstream)
    caddyfile = caddyfile.replace('/etc/caddy/', f'{directory}/')
    caddyfile = replace_once(caddyfile, '{\n\tservers {', '{\n\tauto_https off\n\tadmin off\n\tservers {')
    (directory / 'Caddyfile').write_text(caddyfile)


class Caddy:
    def __init__(self, directory, port):
        self.directory = directory
        self.port = port
        self.name = f'sharpmush-caddy-test-{uuid.uuid4().hex[:8]}'

    def start(self):
        subprocess.run(['docker', 'run', '-d', '--name', self.name, '--network', 'host',
                        '-v', f'{self.directory}:{self.directory}',
                        '-e', f'SHARPMUSH_SITE=http://{DOMAIN}:{self.port}',
                        IMAGE, 'caddy', 'run', '--config', f'{self.directory}/Caddyfile', '--adapter', 'caddyfile'],
                       check=True, capture_output=True, text=True, timeout=300)
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            try:
                with socket.create_connection(('127.0.0.1', self.port), timeout=1):
                    return
            except OSError:
                time.sleep(0.2)
        logs = subprocess.run(['docker', 'logs', self.name], capture_output=True, text=True).stderr
        raise AssertionError(f'Caddy did not start:\n{logs}')

    def stop(self):
        subprocess.run(['docker', 'rm', '-f', self.name], capture_output=True, timeout=60)


def status_of(port, host, path, websocket=True):
    lines = [f'GET {path} HTTP/1.1', f'Host: {host}']
    if websocket:
        lines += ['Connection: Upgrade', 'Upgrade: websocket', 'Sec-WebSocket-Version: 13',
                  'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==', 'Sec-WebSocket-Protocol: telnet']
    else:
        lines.append('Connection: close')
    with socket.create_connection(('127.0.0.1', port), timeout=10) as s:
        s.sendall(('\r\n'.join(lines) + '\r\n\r\n').encode('ascii'))
        return int(s.makefile('rb').readline().split()[1])


class Stack:
    guarded = True

    @classmethod
    def setUpClass(cls):
        directory = Path(tempfile.mkdtemp(prefix='sharpmush-caddy-'))
        cls.addClassCleanup(shutil.rmtree, directory, ignore_errors=True)
        cls.backend = Backend()
        cls.addClassCleanup(cls.backend.close)
        cls.port = free_port()
        write_stack(directory, cls.port, f'127.0.0.1:{cls.backend.server_address[1]}', cls.guarded)
        caddy = Caddy(directory, cls.port)
        cls.addClassCleanup(caddy.stop)
        caddy.start()

    def setUp(self):
        self.backend.seen.clear()

    def assert_reaches_telnet(self, host, path):
        self.backend.seen.clear()
        self.assertEqual(status_of(self.port, host, path), 200)
        self.assertEqual(self.backend.seen, [('/telnet', 'websocket')])

    def assert_reaches_nothing(self, host, path, websocket=True, status=None):
        self.backend.seen.clear()
        actual = status_of(self.port, host, path, websocket)
        if status is None:
            self.assertNotEqual(actual, 200)
        else:
            self.assertEqual(actual, status)
        self.assertEqual(self.backend.seen, [], f'{host}{path} reached the backend')


@requires_caddy
class WhileGuarded(Stack, unittest.TestCase):
    guarded = True

    def test_no_websocket_reaches_the_backend(self):
        for host, path in ((TELNET, '/'), (TELNET, '/telnet'), (TELNET, '/health'), (DOMAIN, '/telnet')):
            with self.subTest(url=f'{host}{path}'):
                self.assert_reaches_nothing(host, path)


@requires_caddy
class AfterSetup(Stack, unittest.TestCase):
    guarded = False

    def test_a_websocket_on_any_path_of_the_telnet_name_is_telnet(self):
        for path in ('/', '/anything'):
            with self.subTest(path=path):
                self.assert_reaches_telnet(TELNET, path)

    def test_the_portal_name_serves_telnet_on_its_own_path(self):
        self.assert_reaches_telnet(DOMAIN, '/telnet')

    def test_health_is_never_telnet(self):
        self.assert_reaches_nothing(TELNET, '/health', status=404)

    def test_a_plain_request_on_the_telnet_name_gets_the_notice(self):
        self.assert_reaches_nothing(TELNET, '/', websocket=False, status=404)


if __name__ == '__main__':
    unittest.main()
