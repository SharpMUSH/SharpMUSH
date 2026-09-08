"""Watchtower scope policy: the socket owner is never an unattended-update target.

Recreating `connectionserver` closes every telnet and /ws kernel socket it holds, so labelling it
`watchtower.enable=true` turns a routine image publish into an unannounced disconnect for everyone
logged in — players see the engine's restart notices and then their client drops. Engine and
renderer replacement keeps those sockets, so those two are the ones that may update themselves.
"""
import re
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]

COMPOSE_FILES = [
    "docker-compose.yml",
    "docker-compose-hub.yml",
    "deploy/docker-compose.prod.yml",
    "deploy/docker-compose.cloudflare.yml",
]

LABEL = "com.centurylinklabs.watchtower.enable"

# The socket owner is the Compose service named `connectionserver` (it kept that name so existing
# proxy routes keep working; its image is sharpmush-socketserver).
SOCKET_OWNER = "connectionserver"
AUTO_UPDATABLE = {"renderer", "sharpmush-server"}


def services(text):
    """Map each service name to its block of the file, without needing a YAML parser."""
    lines = text.splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if line.rstrip() == "services:") + 1
    except StopIteration:
        raise AssertionError("compose file has no top-level services: block")
    blocks, current = {}, None
    for line in lines[start:]:
        if line.strip() and not line.startswith(" "):
            break  # A sibling top-level key (volumes:, networks:) ends the services block.
        name = re.match(r"^  (?P<name>[A-Za-z0-9._-]+):\s*$", line)
        if name:
            current = name["name"]
            blocks[current] = []
        elif current is not None:
            blocks[current].append(line)
    return {name: "\n".join(body) for name, body in blocks.items()}


def watchtower_label(block):
    match = re.search(rf"^\s*-\s*{re.escape(LABEL)}=(?P<value>\S+)\s*$", block, re.MULTILINE)
    return match["value"] if match else None


class WatchtowerScopeTests(unittest.TestCase):
    def compose(self, name):
        return services((REPO / name).read_text(encoding="utf-8"))

    def test_socket_owner_is_excluded_from_watchtower(self):
        for name in COMPOSE_FILES:
            with self.subTest(compose=name):
                block = self.compose(name)[SOCKET_OWNER]
                self.assertEqual(watchtower_label(block), "false",
                                 f"{name}: replacing {SOCKET_OWNER} disconnects every live client, "
                                 "so it must not be an unattended update target")

    def test_socket_preserving_services_do_update_themselves(self):
        for name in COMPOSE_FILES:
            services_in_file = self.compose(name)
            for service in AUTO_UPDATABLE & services_in_file.keys():
                with self.subTest(compose=name, service=service):
                    self.assertEqual(watchtower_label(services_in_file[service]), "true",
                                     f"{name}: {service} can be replaced without dropping sockets, "
                                     "so it should update unattended")

    def test_every_compose_file_declares_the_socket_owner(self):
        # Guards the parser itself: a rename that silently produced no match would make the
        # policy assertions above vacuous.
        for name in COMPOSE_FILES:
            with self.subTest(compose=name):
                self.assertIn(SOCKET_OWNER, self.compose(name))


if __name__ == "__main__":
    unittest.main()
