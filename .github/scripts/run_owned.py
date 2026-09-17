#!/usr/bin/env python3
"""Run a command with retries, stopping every process an attempt started before the next one begins.

Each attempt gets its own session. This process is the child subreaper (Linux), so anything an
attempt leaves behind, including double-forked daemons in sessions of their own, stays among its
descendants. When an attempt ends for any reason, every remaining descendant is sent SIGTERM, then
SIGKILL after the grace period, and reaped. Processes outside that tree are never signalled.

Exit status: 0 once an attempt succeeds; otherwise the last attempt's status, or 124 when it timed
out; 128+N when this runner was stopped by signal N (no further attempt is started); 125 when an
attempt's processes could not all be stopped.
"""

import argparse
import ctypes
import os
import signal
import subprocess
import sys
import time

PR_SET_CHILD_SUBREAPER = 36
KILL_WAIT_SECONDS = 10
INTERRUPTED_GRACE_SECONDS = 5
INTERRUPTS = (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)


class Interrupted(Exception):
	def __init__(self, signum):
		super().__init__(signum)
		self.signum = signum


def log(message, level=None):
	prefix = f"::{level}::" if level else ""
	print(f"{prefix}run_owned: {message}", file=sys.stderr, flush=True)


def interrupt(signum, _frame):
	raise Interrupted(signum)


def descendants():
	"""Descendants of this process, zombies included until reaped, by walking the parent links in /proc."""
	children = {}
	for entry in os.listdir("/proc"):
		if not entry.isdigit():
			continue
		try:
			with open(f"/proc/{entry}/stat") as stat:
				parent = int(stat.read().rsplit(")", 1)[1].split()[1])
		except OSError:
			continue
		children.setdefault(parent, []).append(int(entry))
	found, pending = [], [os.getpid()]
	while pending:
		for child in children.get(pending.pop(), []):
			found.append(child)
			pending.append(child)
	return found


def reap():
	while True:
		try:
			pid, _ = os.waitpid(-1, os.WNOHANG)
		except ChildProcessError:
			return
		if pid == 0:
			return


def signal_all(pids, signum):
	for pid in pids:
		try:
			os.kill(pid, signum)
		except ProcessLookupError:
			pass  # exited since it was listed; the reap loop collects it


def wait_until_gone(seconds):
	deadline = time.monotonic() + seconds
	while True:
		reap()
		remaining = descendants()
		if not remaining or time.monotonic() >= deadline:
			return remaining
		time.sleep(0.05)


def kill_until_gone(seconds):
	"""SIGKILLs every descendant, including any forked since the last look, until none remain or time runs out."""
	deadline = time.monotonic() + seconds
	while True:
		reap()
		remaining = descendants()
		if not remaining or time.monotonic() >= deadline:
			return remaining
		signal_all(remaining, signal.SIGKILL)
		time.sleep(0.05)


def stop_owned(grace_seconds):
	"""Stops every descendant; returns the pids that survived SIGKILL."""
	started = time.monotonic()
	reap()
	owned = descendants()
	if not owned:
		return []
	signal_all(owned, signal.SIGTERM)
	remaining = wait_until_gone(grace_seconds)
	if remaining:
		log(f"{len(remaining)} process(es) outlived SIGTERM; sending SIGKILL: {remaining}")
		remaining = kill_until_gone(KILL_WAIT_SECONDS)
	log(f"stopped {len(owned)} process(es) in {time.monotonic() - started:.1f}s")
	return remaining


def run_attempt(command, timeout_seconds):
	"""Returns the attempt's exit status, or None when it timed out."""
	proc = subprocess.Popen(command, start_new_session=True)
	try:
		return proc.wait(timeout=timeout_seconds)
	except subprocess.TimeoutExpired:
		return None


def main():
	parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
	parser.add_argument("--attempts", type=int, required=True)
	timeout = parser.add_mutually_exclusive_group(required=True)
	timeout.add_argument("--timeout-minutes", type=float)
	timeout.add_argument("--timeout-seconds", type=float)
	parser.add_argument("--grace-seconds", type=float, default=30)
	parser.add_argument("command", nargs=argparse.REMAINDER)
	args = parser.parse_args()
	command = args.command[1:] if args.command[:1] == ["--"] else args.command
	if not command or args.attempts < 1:
		parser.error("an attempt count of at least 1 and a command after -- are required")
	timeout_seconds = args.timeout_seconds if args.timeout_seconds is not None else args.timeout_minutes * 60

	if ctypes.CDLL(None, use_errno=True).prctl(PR_SET_CHILD_SUBREAPER, 1, 0, 0, 0) != 0:
		log(f"cannot become child subreaper: {os.strerror(ctypes.get_errno())}", "error")
		return 125
	for signum in INTERRUPTS:
		signal.signal(signum, interrupt)

	status = 1
	attempt = 0
	try:
		for attempt in range(1, args.attempts + 1):
			log(f"attempt {attempt}/{args.attempts} started")
			result = run_attempt(command, timeout_seconds)
			if result is None:
				log(f"attempt {attempt}/{args.attempts} timed out after {timeout_seconds:g}s; stopping its processes", "warning")
				status = 124
			else:
				# A command killed by signal N reports -N; a shell reports 128+N.
				status = 128 - result if result < 0 else result
				log(f"attempt {attempt}/{args.attempts} exited with status {status}")

			survivors = stop_owned(args.grace_seconds)
			if survivors:
				log(f"attempt {attempt}/{args.attempts} left processes that could not be stopped: {survivors}", "error")
				return 125
			if status == 0:
				return 0
		return status
	except Interrupted as interrupted:
		# A cancelled job gets SIGINT, then SIGTERM 7.5s later, then is killed: finish inside that window.
		for signum in INTERRUPTS:
			signal.signal(signum, signal.SIG_IGN)
		log(f"attempt {attempt}/{args.attempts} interrupted by {signal.Signals(interrupted.signum).name}; stopping its processes", "warning")
		stop_owned(min(args.grace_seconds, INTERRUPTED_GRACE_SECONDS))
		return 128 + interrupted.signum


if __name__ == "__main__":
	sys.exit(main())
