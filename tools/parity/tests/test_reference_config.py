"""Offline regression coverage for the private PennMUSH fixture configuration."""
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from parity.servers import PennMush


class ReferenceConfigurationTests(unittest.TestCase):
    def test_daily_pay_is_disabled_without_changing_starting_money_or_source(self):
        # Pin even an implicit/default paycheck: a future shipped template may omit the line.
        for paycheck in ("paycheck 50\n", "paycheck 100\n", ""):
            with self.subTest(paycheck=paycheck), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                source = root / "reference"
                (source / "src").mkdir(parents=True)
                (source / "src" / "netmud").touch()
                (source / "game").mkdir()
                original = "port 4201\nssl_port 4203\nstarting_money 150\n" + paycheck
                (source / "game" / "mushcnf.dst").write_text(original)
                for name in ("aliascnf.dst", "restrictcnf.dst", "namescnf.dst"):
                    (source / "game" / name).write_text("# fixture\n")
                work = root / "run"
                work.mkdir()
                server = PennMush(source, work)
                with patch("parity.servers.Process"), patch("parity.servers.wait_for"):
                    server.start()
                generated = (server.game / "mush.cnf").read_text()
                effective = dict(line.split(maxsplit=1) for line in generated.splitlines()
                                 if line.strip() and not line.startswith("#"))
                self.assertEqual(effective["paycheck"], "0")
                self.assertEqual(effective["starting_money"], "150")
                self.assertEqual(effective["port"], str(server.port))
                self.assertEqual(effective["ssl_port"], "0")
                self.assertEqual((source / "game" / "mushcnf.dst").read_text(), original)
