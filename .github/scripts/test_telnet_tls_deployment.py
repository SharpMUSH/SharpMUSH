"""Native TLS deployment contract. Compose checks skip explicitly if the CLI is absent.

Uses only Python's standard library and never starts containers, reads deploy/.env,
or uses real certificate/private-key files. The deployment-impact workflow discovers it.
"""

import copy
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
DEPLOY = ROOT / "deploy"
BASES = ("docker-compose.prod.yml", "docker-compose.cloudflare.yml")
OVERLAY = "docker-compose.telnet-tls.yml"


class NativeTlsDocumentationTests(unittest.TestCase):
    def test_environment_example_is_opt_in(self):
        example = (DEPLOY / ".env.example").read_text()
        self.assertIn("# SHARPMUSH_TELNET_TLS_CERT_DIR=", example)
        self.assertFalse(any(
            line.startswith("SHARPMUSH_TELNET_TLS_CERT_DIR=")
            for line in example.splitlines()
        ))

    def test_guide_covers_both_bases_and_certificate_lifecycle(self):
        guide = (DEPLOY / "native-client-tls.md").read_text()
        for base in BASES:
            self.assertIn(f"-f {base} -f {OVERLAY}", guide)
        for required in (
            "1654:1654", "fullchain.pem", "privkey.pem", "create_host_path: false",
            "-verify_hostname", "-verify_return_error", "Plaintext 4201 stays enabled",
            "docker compose restart connectionserver", "WebSocket", "disconnect",
            "ExecStartPre", "ExecStart", "ExecStop", "operator-managed",
        ):
            with self.subTest(required=required):
                self.assertIn(required, guide)

    def test_stale_unimplemented_claim_is_removed(self):
        readme = (DEPLOY / "README.md").read_text()
        self.assertNotIn("configured but not yet implemented", readme)
        self.assertNotIn("nothing listens on it", readme)
        self.assertIn("native-client-tls.md", readme)
        self.assertIn("4203", readme)

    def test_documented_preflight_stops_before_compose_for_missing_directory(self):
        guide = (DEPLOY / "native-client-tls.md").read_text()
        blocks = re.findall(r"```bash\n(.*?)```", guide, re.S)
        preflight = next(block for block in blocks if 'test -d "$SHARPMUSH_TELNET_TLS_CERT_DIR"' in block)
        with tempfile.TemporaryDirectory(prefix="sharpmush-tls-preflight-") as temporary:
            directory = Path(temporary)
            missing = directory / "missing-certificates"
            log = directory / "compose-called"
            docker = directory / "docker"
            docker.write_text('#!/bin/sh\nprintf "called\\n" >> "$COMPOSE_TEST_LOG"\n')
            docker.chmod(0o755)
            environment = dict(os.environ)
            environment.update({
                "PATH": str(directory) + os.pathsep + os.environ.get("PATH", ""),
                "COMPOSE_TEST_LOG": str(log),
            })
            result = subprocess.run(
                ["bash", "-c", preflight.replace("/etc/sharpmush/telnet-tls", str(missing))],
                env=environment, text=True, capture_output=True, timeout=10,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(log.exists(), "Compose must not run before host-directory validation")
            self.assertFalse(missing.exists(), "A typo must not create a certificate directory")

    def test_overlay_requires_explicit_host_path_and_read_only_bind(self):
        # The Compose tests below check the actual parsed/merged model. These simple
        # source invariants also run on machines with no Docker installation.
        overlay = (DEPLOY / OVERLAY).read_text()
        for required in (
            "${SHARPMUSH_TELNET_TLS_CERT_DIR:?", "type: bind", "read_only: true",
            "create_host_path: false", 'ConnectionServer__TelnetSslPort: "4203"',
            "ASPNETCORE_Kestrel__Certificates__Default__Path: /run/sharpmush-tls/fullchain.pem",
            "ASPNETCORE_Kestrel__Certificates__Default__KeyPath: /run/sharpmush-tls/privkey.pem",
        ):
            with self.subTest(required=required):
                self.assertIn(required, overlay)


class NativeTlsComposeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not shutil.which("docker"):
            raise unittest.SkipTest("Docker CLI unavailable; Compose rendering was not run")
        result = subprocess.run(
            ["docker", "compose", "version"], capture_output=True, text=True, timeout=30
        )
        if result.returncode:
            raise unittest.SkipTest("Docker Compose unavailable; rendering was not run")
        print(result.stdout.strip())

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="sharpmush-tls-compose-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.certificates = self.root / "certificates"
        self.certificates.mkdir()
        self.env_file = self.root / "empty.env"
        self.env_file.write_text("")
        # --env-file prevents implicit deploy/.env loading. Only fake settings are
        # rendered, regardless of the caller's Compose environment.
        self.environment = {
            key: value for key, value in os.environ.items()
            if not key.startswith(("COMPOSE_", "SHARPMUSH_", "CLOUDFLARE_", "RESTIC_", "B2_"))
        }
        self.environment.update({
            "SHARPMUSH_DOMAIN": "mush.example.invalid",
            "CLOUDFLARE_TUNNEL_TOKEN": "test-only-not-a-real-token",
            "RESTIC_REPOSITORY": "", "RESTIC_PASSWORD": "",
            "B2_ACCOUNT_ID": "", "B2_ACCOUNT_KEY": "",
        })

    def compose(self, base, *, overlay=False, cert_dir=None):
        environment = dict(self.environment)
        if cert_dir is not None:
            environment["SHARPMUSH_TELNET_TLS_CERT_DIR"] = str(cert_dir)
        command = [
            "docker", "compose", "--project-name", "sharpmush-tls-config-test",
            "--env-file", str(self.env_file), "-f", str(DEPLOY / base),
        ]
        if overlay:
            command += ["-f", str(DEPLOY / OVERLAY)]
        return subprocess.run(
            command + ["config", "--format", "json"], env=environment,
            capture_output=True, text=True, timeout=30,
        )

    def model(self, base, **kwargs):
        result = self.compose(base, **kwargs)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(result.stdout)

    def test_bases_need_no_tls_settings(self):
        for base in BASES:
            with self.subTest(base=base):
                connection = self.model(base)["services"]["connectionserver"]
                self.assertNotIn("ConnectionServer__TelnetSslPort", connection["environment"])
                self.assertFalse(any(p["target"] == 4203 for p in connection["ports"]))
                self.assertTrue(any(p["target"] == 4201 for p in connection["ports"]))
                self.assertFalse(any(v["target"] == "/run/sharpmush-tls" for v in connection["volumes"]))

    def test_overlay_requires_nonempty_certificate_directory(self):
        for base in BASES:
            for directory in (None, ""):
                with self.subTest(base=base, directory=directory):
                    result = self.compose(base, overlay=True, cert_dir=directory)
                    self.assertNotEqual(result.returncode, 0)
                    self.assertIn("SHARPMUSH_TELNET_TLS_CERT_DIR", result.stderr)

    def test_overlay_adds_only_tls_configuration(self):
        for base in BASES:
            with self.subTest(base=base):
                baseline = self.model(base)
                model = self.model(base, overlay=True, cert_dir=self.certificates)
                connection = model["services"]["connectionserver"]
                environment = connection["environment"]
                self.assertEqual(environment["ConnectionServer__TelnetSslPort"], "4203")
                self.assertEqual(environment["ASPNETCORE_Kestrel__Certificates__Default__Path"],
                                 "/run/sharpmush-tls/fullchain.pem")
                self.assertEqual(environment["ASPNETCORE_Kestrel__Certificates__Default__KeyPath"],
                                 "/run/sharpmush-tls/privkey.pem")
                ports = [p for p in connection["ports"] if p["target"] == 4203]
                self.assertEqual(len(ports), 1)
                self.assertEqual(str(ports[0]["published"]), "4203")
                self.assertEqual(ports[0].get("protocol", "tcp"), "tcp")
                mounts = [v for v in connection["volumes"] if v["target"] == "/run/sharpmush-tls"]
                self.assertEqual(len(mounts), 1)
                self.assertEqual(mounts[0]["type"], "bind")
                self.assertEqual(mounts[0]["source"], str(self.certificates))
                self.assertIs(mounts[0]["read_only"], True)
                # Some Compose versions omit false fields from normalized JSON. The
                # source test above separately requires an explicit false in YAML.
                self.assertIs(mounts[0].get("bind", {}).get("create_host_path", False), False)

                # Removing exactly the overlay's additions restores the complete base
                # model: renderer socket, plaintext port, proxy trust and all other
                # services must be unchanged, including the web /ws infrastructure.
                stripped = copy.deepcopy(model)
                service = stripped["services"]["connectionserver"]
                for key in (
                    "ConnectionServer__TelnetSslPort",
                    "ASPNETCORE_Kestrel__Certificates__Default__Path",
                    "ASPNETCORE_Kestrel__Certificates__Default__KeyPath",
                ):
                    del service["environment"][key]
                service["ports"] = [p for p in service["ports"] if p["target"] != 4203]
                service["volumes"] = [v for v in service["volumes"] if v["target"] != "/run/sharpmush-tls"]
                self.assertEqual(stripped, baseline)


if __name__ == "__main__":
    unittest.main()
