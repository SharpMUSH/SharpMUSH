"""Exercise the restore shell in deploy/README.md against disposable directories.

Run from the repository root:
python3 -m unittest discover -s .github/scripts -p test_restore_instructions.py -v
This does not run Docker, Restic, LMDB, or plugin assemblies.
"""

import hashlib
import os
from pathlib import Path
import re
import shlex
import shutil
import stat
import subprocess
import tempfile
import unittest


README = Path(__file__).resolve().parents[2] / "deploy" / "README.md"
COPY_NAME = "20261011T060000Z"


def restore_command():
    text = README.read_text(encoding="utf-8")
    section = text.split("# 3. Put it in place.", 1)[1]
    match = re.search(r"--entrypoint sh sharpmush-server -c '\n(.*?)'", section, re.S)
    if match is None:
        raise AssertionError("The documented restore shell command was not found")
    return match.group(1), section[match.end():]


def host_commands():
    text = README.read_text(encoding="utf-8").split("**To restore for real**", 1)[1]
    match = re.search(r"```bash\n(.*?)\n```", text, re.S)
    if match is None:
        raise AssertionError("The documented host restore commands were not found")
    return match.group(1)


def inventory(root):
    """Compare contents and modes, including hidden files and empty directories."""
    result = {}
    for path in sorted(root.rglob("*")):
        mode = stat.S_IMODE(path.stat().st_mode)
        digest = hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None
        result[str(path.relative_to(root))] = (mode, digest)
    return result


class RestoreInstructionsTests(unittest.TestCase):
    def setUp(self):
        self.scratch = tempfile.TemporaryDirectory(prefix="sharpmush restore ")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.source = self.root / "snapshot"
        self.target = self.root / "app-data"
        self.source.mkdir()
        self.target.mkdir()
        self.write(self.source, f"backup/{COPY_NAME}/data.mdb", b"saved world")
        self.write(self.source, "wiki-assets/image.png", b"saved image")
        self.write(self.source, "lightning.dataprotection-keys/key.xml", b"saved key")
        self.write(self.source, "lightning.plugins/example/plugin.json", b'{"id":"example"}')
        self.write(self.source, "lightning.plugins/example/Example.dll", b"fixture, never executed")
        self.write(self.source, "lightning.plugins/plugins.state.json", b'{"Disabled":["example"]}')
        self.write(self.source, "lightning.plugins/.uploads/pending/package.zip", b"pending upload")
        (self.source / "lightning.plugins/empty").mkdir()
        (self.source / "lightning.plugins/example/Example.dll").chmod(0o640)
        (self.source / "lightning.plugins/.uploads").chmod(0o750)
        self.write(self.target, "lightning/data.mdb", b"current world")
        self.write(self.target, "wiki-assets/image.png", b"current image")
        self.write(self.target, "lightning.dataprotection-keys/key.xml", b"current key")
        self.write(self.target, "lightning.plugins/newer/Newer.dll", b"stale plugin")
        self.write(self.target, "lightning.plugins/plugins.state.json", b'{"Disabled":[]}')

    @staticmethod
    def write(root, relative, content):
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def script(self, copy_name=COPY_NAME):
        script, _ = restore_command()
        # Only substitute the two fixed roots and selected copy. Refuse to run if an
        # edit leaves either production root behind; the shell must stay in this fixture.
        script = script.replace("source=/restore/data", "source=" + shlex.quote(str(self.source)))
        script = script.replace("target=/app/data", "target=" + shlex.quote(str(self.target)))
        script = script.replace("REPLACE_WITH_SELECTED_TIMESTAMP", copy_name)
        self.assertNotIn("/app/data", script)
        self.assertNotIn("/restore/data", script)
        return script

    def run_restore(self, copy_name=COPY_NAME):
        return subprocess.run(
            ["sh", "-c", self.script(copy_name)], cwd=self.root,
            capture_output=True, text=True, timeout=10, check=False,
        )

    def test_shell_syntax(self):
        result = subprocess.run(
            ["sh", "-n"], input=self.script(), text=True,
            capture_output=True, timeout=10, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_restores_complete_plugin_tree_and_replaces_stale_code(self):
        result = self.run_restore()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(inventory(self.source / "lightning.plugins"), inventory(self.target / "lightning.plugins"))
        self.assertEqual((self.target / "lightning/data.mdb").read_bytes(), b"saved world")
        self.assertEqual((self.target / "wiki-assets/image.png").read_bytes(), b"saved image")
        self.assertEqual((self.target / "lightning.dataprotection-keys/key.xml").read_bytes(), b"saved key")

    def test_missing_plugins_fails_before_changing_any_destination(self):
        shutil.rmtree(self.source / "lightning.plugins")
        before = inventory(self.target)
        result = self.run_restore()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("plugin", result.stderr.lower())
        self.assertEqual(inventory(self.target), before)

    def test_missing_world_fails_before_changing_any_destination(self):
        before = inventory(self.target)
        result = self.run_restore("not-a-snapshot")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(inventory(self.target), before)

    def test_incoming_copy_is_rejected_even_if_data_file_exists(self):
        self.write(self.source, "backup/.incoming-partial/data.mdb", b"unfinished world")
        before = inventory(self.target)
        result = self.run_restore(".incoming-partial")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(inventory(self.target), before)

    def test_missing_assets_fails_before_changing_any_destination(self):
        shutil.rmtree(self.source / "wiki-assets")
        before = inventory(self.target)
        result = self.run_restore()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(inventory(self.target), before)

    def test_explicitly_empty_plugin_source_clears_destination(self):
        shutil.rmtree(self.source / "lightning.plugins")
        (self.source / "lightning.plugins").mkdir()
        result = self.run_restore()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue((self.target / "lightning.plugins").is_dir())
        self.assertEqual(inventory(self.target / "lightning.plugins"), {})

    def test_restore_can_be_repeated_without_nesting_plugins(self):
        self.assertEqual(self.run_restore().returncode, 0)
        first = inventory(self.target)
        result = self.run_restore()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(inventory(self.target), first)

    def test_restart_is_conditional_on_successful_restore(self):
        _, after_command = restore_command()
        self.assertRegex(after_command, r"^\s*&&[ \t]*\\\n\s*docker compose -f docker-compose\.prod\.yml start connectionserver sharpmush-server")

    def test_host_restore_selects_production_file_explicitly(self):
        commands = re.findall(r"^[ \t]*docker compose[^\n]*", host_commands(), re.M)
        self.assertEqual(len(commands), 5)
        for command in commands:
            self.assertEqual(command.split()[:4],
                             ["docker", "compose", "-f", "docker-compose.prod.yml"])

    def test_host_commands_stop_at_each_failed_step(self):
        # The shell function replaces Docker entirely. It never runs container commands,
        # touches a volume, or executes the inner restore script.
        stub = r'''
docker() {
  if [ "$1" = compose ]; then
    shift
    [ "$1" = -f ] && [ "$2" = docker-compose.prod.yml ] || {
      echo "Unexpected Compose selection" >&2; return 99;
    }
    shift 2
    set -- compose "$@"
  fi
  case "$*" in
    "compose stop "*) step=stop ;;
    "volume create "*) step=volume ;;
    *"restic restore "*) step=restic ;;
    *"-c ls /restore/data/backup") step=inspect ;;
    *"--entrypoint sh sharpmush-server -c "*) step=replace ;;
    "compose start "*) step=restart ;;
    *) echo "Unexpected Docker command" >&2; return 99 ;;
  esac
  printf "%s\n" "$step" >> "$DOCKER_LOG"
  if [ "$step" = "$FAIL_STEP" ]; then return 23; fi
}
'''
        steps = ["stop", "volume", "restic", "inspect", "replace", "restart"]
        log = self.root / "docker-calls"
        for failure in [*steps, "none"]:
            with self.subTest(failure=failure):
                log.write_text("")
                result = subprocess.run(
                    ["sh", "-c", stub + host_commands()], cwd=self.root,
                    env={**os.environ, "DOCKER_LOG": str(log), "FAIL_STEP": failure},
                    capture_output=True, text=True, timeout=10, check=False,
                )
                expected = steps if failure == "none" else steps[:steps.index(failure) + 1]
                self.assertEqual(log.read_text().splitlines(), expected, result.stderr)
                self.assertEqual(result.returncode, 0 if failure == "none" else 23, result.stderr)


if __name__ == "__main__":
    unittest.main()
