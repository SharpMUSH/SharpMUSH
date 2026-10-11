"""Local tests for smoke protocol/fixture helpers; Docker checks run in the smoke workflow."""

import base64
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import ssl
import struct
import subprocess
import tempfile
import threading
import unittest
from unittest.mock import patch

import smoke_native_tls as smoke


def client_frame(stream):
    first, second = smoke._read_exact(stream, 2)
    if not second & 128:
        raise AssertionError("Client frames must be masked")
    size = second & 127
    if size == 126:
        size = struct.unpack("!H", smoke._read_exact(stream, 2))[0]
    elif size == 127:
        size = struct.unpack("!Q", smoke._read_exact(stream, 8))[0]
    mask = smoke._read_exact(stream, 4)
    payload = smoke._read_exact(stream, size)
    return first & 15, bytes(value ^ mask[i % 4] for i, value in enumerate(payload))


def server_frame(payload, *, opcode=1, final=True):
    first = opcode | (128 if final else 0)
    if len(payload) < 126:
        return bytes([first, len(payload)]) + payload
    return bytes([first, 126]) + struct.pack("!H", len(payload)) + payload


@unittest.skipUnless(shutil.which("openssl"), "OpenSSL required for disposable test certificates")
class SmokeTlsProtocolTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="test-sharpmush-smoke-")
        cls.certificates = Path(cls.temporary.name) / "certificates"
        # Unit tests never run sudo, even when the test suite itself is in CI.
        with patch.dict(os.environ, {"GITHUB_ACTIONS": "false"}):
            cls.ca = smoke.generate_certificates(cls.certificates)

    @classmethod
    def tearDownClass(cls):
        smoke.cleanup_certificates(cls.certificates)
        cls.temporary.cleanup()

    @contextmanager
    def server(self, handler, *, allow_handshake_failure=False):
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(self.certificates / "fullchain.pem", self.certificates / "privkey.pem")
        listener = socket.socket()
        listener.bind(("127.0.0.1", 0))
        listener.listen(1)
        listener.settimeout(5)
        errors = []

        def serve():
            try:
                with listener:
                    raw, _ = listener.accept()
                    raw.settimeout(5)
                    with raw:
                        try:
                            with context.wrap_socket(raw, server_side=True) as stream:
                                handler(stream)
                        except ssl.SSLError:
                            if not allow_handshake_failure:
                                raise
            except BaseException as error:
                errors.append(error)

        thread = threading.Thread(target=serve, daemon=True)
        thread.start()
        try:
            yield listener.getsockname()[1]
        finally:
            thread.join(7)
            listener.close()
            self.assertFalse(thread.is_alive(), "Local fixture server did not finish")
            if errors:
                raise errors[0]

    def test_certificate_fixture_keeps_authority_keys_outside_mount(self):
        self.assertEqual({p.name for p in self.certificates.iterdir()}, {"ca.pem", "fullchain.pem", "privkey.pem"})
        authority = smoke._authority(self.certificates)
        result = subprocess.run(
            ["openssl", "verify", "-CAfile", str(self.ca), "-untrusted", str(authority / "issuer.pem"),
             str(authority / "leaf.pem")], text=True, capture_output=True,
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_generation_refuses_existing_material(self):
        with self.assertRaises(ValueError):
            smoke.generate_certificates(self.certificates)

    def test_verified_tls_carries_who_and_declines_telnet_options(self):
        def handler(stream):
            self.assertEqual(stream.recv(4096), b"WHO\r\n")
            stream.sendall(b"\xff\xfd\x18Player Name\r\nThere are no players connected.\r\n")
            self.assertEqual(stream.recv(3), b"\xff\xfc\x18")

        with self.server(handler) as port:
            with smoke._connect_tls(port, self.ca) as stream:
                self.assertTrue(stream.getpeercert(binary_form=True))
                smoke._telnet_who(stream)

    def test_verified_tls_rejects_wrong_hostname(self):
        with self.server(lambda stream: None, allow_handshake_failure=True) as port:
            with self.assertRaises(ssl.SSLCertVerificationError):
                smoke._connect_tls(port, self.ca, hostname="wrong-host.invalid")

    def test_verified_tls_rejects_untrusted_root(self):
        with self.server(lambda stream: None, allow_handshake_failure=True) as port:
            with socket.create_connection(("127.0.0.1", port), timeout=5) as raw:
                with self.assertRaises(ssl.SSLCertVerificationError):
                    ssl.create_default_context().wrap_socket(raw, server_hostname="localhost")

    def test_websocket_upgrade_control_frames_and_fragmented_seq_output(self):
        def handler(stream):
            request = bytearray()
            while not request.endswith(b"\r\n\r\n"):
                request.extend(smoke._read_exact(stream, 1))
            self.assertTrue(request.startswith(b"GET /ws HTTP/1.1\r\n"))
            key = next(line.split(":", 1)[1].strip() for line in request.decode().split("\r\n")
                       if line.lower().startswith("sec-websocket-key:"))
            accept = base64.b64encode(hashlib.sha1((key + smoke.WS_GUID).encode()).digest()).decode()
            stream.sendall(("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\n"
                            f"Connection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n").encode())
            self.assertEqual(client_frame(stream), (1, b'{"type":"hello"}'))
            self.assertEqual(client_frame(stream), (1, b"WHO"))
            output = json.dumps({"type": "seq", "seq": 1, "data": "There are no players connected."}).encode()
            stream.sendall(server_frame(b'{"type":"resumeToken","token":"test-only"}')
                           + server_frame(b"ping", opcode=9)
                           + server_frame(output[:10], final=False)
                           + server_frame(output[10:], opcode=0))
            self.assertEqual(client_frame(stream), (10, b"ping"))
            self.assertEqual(client_frame(stream), (8, struct.pack("!H", 1000)))

        with self.server(handler) as port:
            smoke.websocket_who(port, self.ca)

    def test_renewal_replaces_leaf_but_preserves_root_and_key_permissions(self):
        before = (self.certificates / "fullchain.pem").read_bytes()
        root = self.ca.read_bytes()
        key = self.certificates / "privkey.pem"
        key.chmod(0o640)
        smoke.renew_certificates(self.certificates)
        self.assertNotEqual(before, (self.certificates / "fullchain.pem").read_bytes())
        self.assertEqual(root, self.ca.read_bytes())
        self.assertEqual(key.stat().st_mode & 0o777, 0o640)
        with self.server(lambda stream: None) as port:
            with smoke._connect_tls(port, self.ca):
                pass


class SmokeTlsSafetyTests(unittest.TestCase):
    def test_fragmented_telnet_commands_and_subnegotiation(self):
        reader = smoke._TelnetReader()
        self.assertEqual(reader.feed(b"a\xff"), (b"a", b""))
        self.assertEqual(reader.feed(b"\xfb\x56b\xff\xfa\x18hidden\xff"), (b"b", b"\xff\xfe\x56"))
        self.assertEqual(reader.feed(b"\xf0c\xff\xff"), (b"c\xff", b""))

    def test_host_environment_cannot_select_remote_daemon_or_real_compose_file(self):
        result = subprocess.CompletedProcess([], 0, "", "")
        with patch.dict(os.environ, {"DOCKER_HOST": "tcp://production.invalid:2375",
                                     "COMPOSE_FILE": "production.yml", "AWS_SECRET_ACCESS_KEY": "do-not-forward"}):
            with patch.object(smoke.subprocess, "run", return_value=result) as run:
                smoke._run(["docker", "context", "show"])
        forwarded = run.call_args.kwargs["env"]
        for secret in ("DOCKER_HOST", "COMPOSE_FILE", "AWS_SECRET_ACCESS_KEY"):
            self.assertNotIn(secret, forwarded)

    def test_rejects_non_smoke_project_without_running_docker(self):
        with patch.object(smoke, "_run") as run:
            with self.assertRaises(ValueError):
                smoke._validate_isolation(["docker", "compose", "--project-name", "production"], Path("/unused"))
            run.assert_not_called()

    def test_rejects_public_listener_or_writable_certificate_mount(self):
        directory = Path("/tmp/generated-certificates")
        project = "smush-smoke-123-1"
        baseline = {"name": project, "services": {"connectionserver": {
            "ports": [{"published": "4203", "host_ip": "127.0.0.1"}],
            "volumes": [{"target": smoke.TLS_TARGET, "type": "bind", "source": str(directory), "read_only": True}],
        }}}
        for mutation in ("public", "writable"):
            model = json.loads(json.dumps(baseline))
            service = model["services"]["connectionserver"]
            if mutation == "public":
                service["ports"][0]["host_ip"] = "0.0.0.0"
            else:
                service["volumes"][0]["read_only"] = False
            with self.subTest(mutation=mutation), patch.object(smoke, "_assert_generated"):
                with patch.object(smoke, "_run", side_effect=[
                    subprocess.CompletedProcess([], 0, "default\n", ""),
                    subprocess.CompletedProcess([], 0, json.dumps(model), ""),
                ]):
                    with self.assertRaises(ValueError):
                        smoke._validate_isolation(["docker", "compose", "--project-name", project], directory)

    def test_permission_helper_refuses_sudo_outside_ci(self):
        with patch.object(smoke, "_assert_generated"), patch.dict(os.environ, {"GITHUB_ACTIONS": "false"}):
            with patch.object(smoke, "_run") as run:
                with self.assertRaises(ValueError):
                    smoke.prepare_certificate_permissions(Path("/tmp/generated-certificates"))
                run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
