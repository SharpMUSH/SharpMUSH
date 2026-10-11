"""No Docker daemon or network is used by these isolation-contract checks."""

import copy
import os
from pathlib import Path
import unittest
from unittest.mock import patch

from smoke_deployment import clean_environment, compose_prefix, isolated_model, project_name, test_caddyfile


class DeploymentSmokeTests(unittest.TestCase):
    def test_compose_config_includes_backup_profile_and_ignores_deployment_env(self):
        command = compose_prefix("smush-smoke-123-1", Path("/tmp/fixture"))
        self.assertEqual(command[command.index("--profile") + 1], "backup")
        self.assertEqual(command[command.index("--env-file") + 1], "/tmp/fixture/empty.env")
        self.assertEqual(command[command.index("--project-name") + 1], "smush-smoke-123-1")

    def model(self):
        services = {name: {"container_name": "production-" + name, "restart": "unless-stopped",
                           "ports": [{"target": 80, "published": "80"}],
                           "environment": {}, "volumes": [], "image": "original"}
                    for name in ("nats", "renderer", "connectionserver", "sharpmush-server", "caddy", "backup")}
        services["backup"]["environment"] = {
            "RESTIC_REPOSITORY": "cloud:production", "RESTIC_PASSWORD": "not-a-secret",
            "B2_ACCOUNT_ID": "not-real", "B2_ACCOUNT_KEY": "not-real",
            "RESTIC_BACKUP_SOURCES": "/data/backup /data/lightning.plugins",
        }
        services["backup"]["volumes"] = [{"type": "volume", "source": "app-data", "target": "/data", "read_only": True}]
        services["caddy"]["volumes"] = [{"target": "/etc/caddy/Caddyfile", "source": "/production/Caddyfile"}]
        for name in ("renderer", "connectionserver", "sharpmush-server"):
            services[name]["build"] = {"context": "/repo"}
        services["connectionserver"]["environment"]["ConnectionServer__TelnetSslPort"] = "4203"
        services["connectionserver"]["volumes"] = [{"type": "bind", "source": "/tmp/fixture/certificates",
                                                    "target": "/run/sharpmush-tls", "read_only": True}]
        return {"services": services, "volumes": {"app-data": {"name": "production-data"}},
                "networks": {"sharpmush-net": {"name": "production-net"}, "renderer-egress": {}}}

    def isolate(self, source):
        return isolated_model(source, project="smush-smoke-123-1", workdir=Path("/tmp/fixture"),
                              image_tags={n: "local-" + n for n in ("renderer", "connectionserver", "sharpmush-server")},
                              ports={"plaintext": 14201, "tls": 14203, "https": 14443})

    def test_only_loopback_ports_are_published(self):
        model = self.isolate(self.model())
        mappings = [p for s in model["services"].values() for p in s.get("ports", [])]
        self.assertEqual(len(mappings), 3)
        self.assertEqual({p["host_ip"] for p in mappings}, {"127.0.0.1"})
        self.assertEqual({p["target"] for p in mappings}, {4201, 4203, 443})

    def test_all_networks_and_resources_are_isolated(self):
        model = self.isolate(self.model())
        self.assertTrue(all(n["internal"] for name, n in model["networks"].items() if name != "smoke-ingress"))
        self.assertEqual({name for name, service in model["services"].items()
                          if "smoke-ingress" in service.get("networks", {})}, {"caddy", "connectionserver"})
        self.assertNotIn("internal", model["networks"]["smoke-ingress"])
        for resources in (model["networks"], model["volumes"]):
            self.assertTrue(all(r["name"].startswith("smush-smoke-123-1_") for r in resources.values()))
        self.assertFalse(any("container_name" in s for s in model["services"].values()))
        self.assertTrue(all(s["restart"] == "no" for s in model["services"].values()))

    def test_rejects_external_volumes_and_networks(self):
        for category, name in (("volumes", "app-data"), ("networks", "sharpmush-net")):
            source = self.model()
            source[category][name]["external"] = True
            with self.subTest(category=category), self.assertRaises(AssertionError):
                self.isolate(source)

    def test_service_changes_require_isolation_review(self):
        source = self.model()
        source["services"]["unexpected-egress"] = {}
        with self.assertRaises(AssertionError):
            self.isolate(source)

    def test_images_are_local_and_cannot_be_pulled(self):
        model = self.isolate(self.model())
        for name in ("renderer", "connectionserver", "sharpmush-server"):
            service = model["services"][name]
            self.assertEqual(service["image"], "local-" + name)
            self.assertEqual(service["pull_policy"], "never")
            self.assertNotIn("build", service)

    def test_explicit_missing_bind_guard_survives_compose_json_round_trip(self):
        model = self.isolate(self.model())
        certificate = model["services"]["connectionserver"]["volumes"][0]
        self.assertIs(certificate["bind"]["create_host_path"], False)
        source = self.model()
        source["services"]["connectionserver"]["volumes"] = []
        with self.assertRaises(AssertionError):
            self.isolate(source)

    def test_restic_uses_only_local_repository_and_password_file(self):
        source = self.model()
        before = copy.deepcopy(source)
        backup = self.isolate(source)["services"]["backup"]
        self.assertEqual(source, before)
        environment = backup["environment"]
        self.assertEqual(environment["RESTIC_REPOSITORY"], "/repo")
        self.assertEqual(environment["RESTIC_PASSWORD_FILE"], "/run/smoke-restic-password")
        for key in ("RESTIC_PASSWORD", "B2_ACCOUNT_ID", "B2_ACCOUNT_KEY"):
            self.assertNotIn(key, environment)
        self.assertEqual(environment["RESTIC_BACKUP_SOURCES"], before["services"]["backup"]["environment"]["RESTIC_BACKUP_SOURCES"])
        self.assertTrue(next(v for v in backup["volumes"] if v["target"] == "/data")["read_only"])

    def test_caddy_keeps_production_routes_but_uses_only_manual_tls(self):
        original = "{$SHARPMUSH_DOMAIN} {\n handle /ws { reverse_proxy connectionserver:4202 }\n}\n"
        adapted = test_caddyfile(original)
        self.assertIn("https://localhost:443", adapted)
        self.assertIn("tls /run/smoke-tls/fullchain.pem /run/smoke-tls/privkey.pem", adapted)
        self.assertIn(original.split("\n", 1)[1], adapted)
        with self.assertRaises(AssertionError):
            test_caddyfile("changed shape")

    def test_daemon_and_deployment_environment_are_not_inherited(self):
        with patch.dict(os.environ, {"PATH": "/bin", "DOCKER_HOST": "tcp://production:2376",
                                     "DOCKER_CONTEXT": "prod", "COMPOSE_FILE": "prod.yml",
                                     "AWS_ACCESS_KEY_ID": "not-real", "RESTIC_PASSWORD": "not-real"}, clear=True):
            self.assertEqual(clean_environment(), {"PATH": "/bin"})

    def test_only_github_actions_can_choose_a_fixture_project(self):
        with patch.dict(os.environ, {}, clear=True), self.assertRaises(RuntimeError):
            project_name()
        with patch.dict(os.environ, {"GITHUB_ACTIONS": "true", "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1"}, clear=True):
            self.assertEqual(project_name(), "smush-smoke-123-1")
        with patch.dict(os.environ, {"GITHUB_ACTIONS": "true", "GITHUB_RUN_ID": "../production", "GITHUB_RUN_ATTEMPT": "1"}, clear=True), self.assertRaises(RuntimeError):
            project_name()


if __name__ == "__main__":
    unittest.main()
