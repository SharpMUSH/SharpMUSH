import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

SCRIPT = Path(__file__).with_name("run_owned.py")

# One attempt: records its number, how many processes of earlier attempts are still alive, then
# starts a child that ignores SIGTERM and a double-forked daemon in its own session, all logging
# progress, and either exits or runs until stopped.
FIXTURE = r"""
d=$1
n=$(( $(cat "$d/attempts" 2>/dev/null || echo 0) + 1 ))
echo "$n" > "$d/attempts"
alive=0
for p in $(cat "$d/pids" 2>/dev/null); do kill -0 "$p" 2>/dev/null && alive=$((alive + 1)); done
echo "$alive" > "$d/alive-at-attempt-$n"
echo $$ >> "$d/pids"
sh -c 'trap "" TERM; echo $$ >> "$1/pids"; while :; do echo tick >> "$1/child"; sleep 0.05; done' child "$d" &
( setsid sh -c 'echo $$ >> "$1/pids"; while :; do echo tick >> "$1/daemon"; sleep 0.05; done' daemon "$d" & )
while [ "$(wc -l < "$d/pids")" -lt $((n * 3)) ]; do sleep 0.02; done
[ "$n" -ge "${SUCCEED_ON:-99}" ] && exit "${EXIT_WITH:-0}"
[ -n "$FAIL_WITH" ] && exit "$FAIL_WITH"
while :; do sleep 0.05; done
"""


def alive(pid):
	try:
		os.kill(pid, 0)
	except ProcessLookupError:
		return False
	return Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[0] != "Z"


class RunOwnedTests(unittest.TestCase):
	def setUp(self):
		self.dir = Path(tempfile.mkdtemp())
		self.fixture = self.dir / "fixture.sh"
		self.fixture.write_text(FIXTURE)

	def start(self, *args, env=None):
		return subprocess.Popen(
			[sys.executable, SCRIPT, *args, "--", "sh", self.fixture, self.dir],
			stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, env={**os.environ, **(env or {})})

	def run_owned(self, *args, env=None):
		proc = self.start(*args, env=env)
		output, _ = proc.communicate(timeout=60)
		return proc.returncode, output

	def attempts(self):
		return int((self.dir / "attempts").read_text())

	def pids(self):
		return [int(p) for p in (self.dir / "pids").read_text().split()]

	def assert_tree_gone(self):
		self.assertEqual([p for p in self.pids() if alive(p)], [])
		sizes = [(self.dir / name).stat().st_size for name in ("child", "daemon")]
		time.sleep(0.3)
		self.assertEqual([(self.dir / name).stat().st_size for name in ("child", "daemon")], sizes)

	def test_timeout_stops_the_whole_tree_before_the_next_attempt(self):
		unrelated = subprocess.Popen(["sleep", "60"], start_new_session=True)
		self.addCleanup(unrelated.kill)

		status, output = self.run_owned("--attempts", "2", "--timeout-seconds", "1", "--grace-seconds", "0.5")

		self.assertEqual(status, 124, output)
		self.assertEqual(self.attempts(), 2)
		self.assertEqual((self.dir / "alive-at-attempt-2").read_text().strip(), "0")
		self.assert_tree_gone()
		self.assertIsNone(unrelated.poll())
		self.assertIn("attempt 1/2 timed out", output)
		self.assertIn("attempt 2/2 timed out", output)

	def test_success_is_not_retried(self):
		status, output = self.run_owned("--attempts", "3", "--timeout-seconds", "30", "--grace-seconds", "0.5", env={"SUCCEED_ON": "1"})

		self.assertEqual(status, 0, output)
		self.assertEqual(self.attempts(), 1)
		self.assert_tree_gone()

	def test_retry_that_succeeds_reports_success(self):
		status, output = self.run_owned("--attempts", "3", "--timeout-seconds", "30", "--grace-seconds", "0.5", env={"SUCCEED_ON": "2", "FAIL_WITH": "3"})

		self.assertEqual(status, 0, output)
		self.assertEqual(self.attempts(), 2)
		self.assertEqual((self.dir / "alive-at-attempt-2").read_text().strip(), "0")
		self.assertIn("attempt 1/3 exited with status 3", output)

	def test_last_failure_status_is_kept(self):
		status, output = self.run_owned("--attempts", "2", "--timeout-seconds", "30", "--grace-seconds", "0.5", env={"FAIL_WITH": "3"})

		self.assertEqual(status, 3, output)
		self.assertEqual(self.attempts(), 2)
		self.assert_tree_gone()

	def test_interruption_stops_the_tree_and_does_not_retry(self):
		proc = self.start("--attempts", "3", "--timeout-seconds", "30", "--grace-seconds", "0.5")
		deadline = time.monotonic() + 30
		while not ((self.dir / "pids").exists() and len(self.pids()) >= 3) and time.monotonic() < deadline:
			time.sleep(0.05)

		proc.send_signal(signal.SIGTERM)
		output, _ = proc.communicate(timeout=30)

		self.assertEqual(proc.returncode, 128 + signal.SIGTERM, output)
		self.assertEqual(self.attempts(), 1)
		self.assert_tree_gone()

	def test_cancellation_signals_during_cleanup_do_not_abandon_it(self):
		proc = self.start("--attempts", "3", "--timeout-seconds", "30", "--grace-seconds", "60")
		deadline = time.monotonic() + 30
		while not ((self.dir / "pids").exists() and len(self.pids()) >= 3) and time.monotonic() < deadline:
			time.sleep(0.05)

		proc.send_signal(signal.SIGINT)
		time.sleep(0.5)
		proc.send_signal(signal.SIGTERM)
		output, _ = proc.communicate(timeout=30)

		self.assertEqual(proc.returncode, 128 + signal.SIGINT, output)
		self.assertEqual(self.attempts(), 1)
		self.assert_tree_gone()


if __name__ == "__main__":
	unittest.main()
