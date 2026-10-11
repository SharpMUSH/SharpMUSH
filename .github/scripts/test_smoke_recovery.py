"""Offline checks for the recovery harness; no Docker or application is run."""

import hashlib
import json
from pathlib import Path
import subprocess
import struct
import tempfile
import unittest
import zipfile
import zlib

from smoke_recovery import documented_restore, package_fixture, png_fixture


class RecoverySmokeHelperTests(unittest.TestCase):
    def test_png_fixture_has_valid_checksums_and_one_rgba_pixel(self):
        data = png_fixture()
        self.assertEqual(data[:8], b"\x89PNG\r\n\x1a\n")
        offset, chunks = 8, {}
        while offset < len(data):
            size = struct.unpack("!I", data[offset:offset + 4])[0]
            kind = data[offset + 4:offset + 8]
            payload = data[offset + 8:offset + 8 + size]
            crc = struct.unpack("!I", data[offset + 8 + size:offset + 12 + size])[0]
            self.assertEqual(crc, zlib.crc32(kind + payload))
            chunks[kind] = payload
            offset += 12 + size
        self.assertEqual(offset, len(data))
        self.assertEqual(struct.unpack("!2I5B", chunks[b"IHDR"]), (1, 1, 8, 6, 0, 0, 0))
        self.assertEqual(zlib.decompress(chunks[b"IDAT"]), b"\0\xff\0\0\xff")
        self.assertEqual(chunks[b"IEND"], b"")

    def test_extracts_the_documented_shell_without_production_root_rewrites(self):
        readme = Path(__file__).resolve().parents[2] / "deploy" / "README.md"
        script = documented_restore(readme, "20261011-064900-123")
        self.assertIn("world_copy=20261011-064900-123", script)
        self.assertIn("source=/restore/data", script)
        self.assertIn("target=/app/data", script)
        self.assertIn('rm -rf "$target/lightning.plugins"', script)
        result = subprocess.run(["sh", "-n"], input=script, text=True,
                                capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 0, result.stderr)

    def test_rejects_unfinished_or_unsafe_copy_names(self):
        # The input is rejected before the file is read.
        for name in ("latest", ".incoming-test", "../escape", "", "garbage", "20261011;id"):
            with self.subTest(name=name), self.assertRaises(RuntimeError):
                documented_restore(Path("not-read"), name)

    def test_package_has_flat_files_real_hashes_and_matching_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            (folder / "CommandOnlyPlugin.dll").write_bytes(b"Fixture shape only; never executed")
            (folder / "CommandOnlyPlugin.deps.json").write_text("{}")
            result = package_fixture(folder, "command-only", "CommandOnlyPlugin", folder / "plugin.zip")
            with zipfile.ZipFile(result) as package:
                self.assertEqual(sorted(package.namelist()), ["CommandOnlyPlugin.deps.json",
                                 "CommandOnlyPlugin.dll", "package.yaml", "plugin.json"])
                manifest = package.read("package.yaml").decode()
                for name in ("CommandOnlyPlugin.deps.json", "CommandOnlyPlugin.dll", "plugin.json"):
                    self.assertIn(hashlib.sha256(package.read(name)).hexdigest(), manifest)
                plugin = json.loads(package.read("plugin.json"))
                self.assertEqual(plugin["id"], "command-only")
                self.assertEqual(plugin["entry"], "CommandOnlyPlugin.dll")
                self.assertIn("package: command-only", manifest)

    def test_missing_compiled_assembly_cannot_be_packaged(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            with self.assertRaises(RuntimeError):
                package_fixture(folder, "command-only", "CommandOnlyPlugin", folder / "plugin.zip")


if __name__ == "__main__":
    unittest.main()
