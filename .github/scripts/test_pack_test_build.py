"""Check the isolated CI artifact includes the shipped-plugin test dependencies."""

from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest


PACK_SCRIPT = Path(__file__).with_name("pack-test-build.sh")
PROJECTS = (
    "SharpMUSH.Tests", "SharpMUSH.Tests.BUnit", "SharpMUSH.Tests.ScenePlugin",
    "SharpMUSH.Tests.DiscordPlugin", "SharpMUSH.Tests.WorldPlugin", "SharpMUSH.Tests.Integration",
)


class TestBuildPackagingTests(unittest.TestCase):
    def test_unit_archive_preserves_real_server_plugin_output_layout(self):
        with tempfile.TemporaryDirectory(prefix="test-build-package-") as directory:
            root = Path(directory)
            subprocess.run(["git", "init", "-q", str(root)], check=True)
            for project in (*PROJECTS, "SharpMUSH.Client"):
                (root / project / "bin/Debug/net11.0").mkdir(parents=True)
            payloads = {}
            for plugin in ("discord", "lsp", "mcp", "scene", "world"):
                base = Path("SharpMUSH.Server/bin/Debug/net11.0/plugins") / plugin
                payloads[base / "plugin.json"] = '{"id":"' + plugin + '"}'
                payloads[base / (plugin + ".dll")] = "built assembly payload: " + plugin
            for path, contents in payloads.items():
                (root / path).parent.mkdir(parents=True, exist_ok=True)
                (root / path).write_text(contents, encoding="utf-8")
            archives = root / "archives"
            subprocess.run(["bash", str(PACK_SCRIPT), str(archives)], cwd=root, check=True)
            extracted = root / "isolated-unit-job"
            with tarfile.open(archives / "SharpMUSH.Tests.tar.gz") as archive:
                archive.extractall(extracted, filter="data")
            for path, contents in payloads.items():
                self.assertTrue((extracted / path).is_file(), f"Unit artifact omitted {path}")
                self.assertEqual((extracted / path).read_text(encoding="utf-8"), contents)
            # The regression uses only the downloaded unit archive, with no server output on disk.
            self.assertFalse((extracted / "SharpMUSH.Server/bin/Debug/net11.0/SharpMUSH.Server.dll").exists())


if __name__ == "__main__":
    unittest.main()
