#!/usr/bin/env python3
"""Ordinary game-command latency on a disposable SharpMUSH, with wiki reads off.

Starts the parity harness's world the parity harness's way: a private PennMUSH builds tools/parity/world,
dumps it, and a private SharpMUSH stack (nats-server, Server, ConnectionServer renderer, SocketServer, all
on free ports) imports that flatfile. Nothing else is touched. Then three logged-in sessions send a fixed
open-loop mix of look, say, pose and @set; each command is followed by a `think` sentinel on the same
connection, and its latency is from its scheduled send to the sentinel's arrival, so queueing behind a slow
command counts. The server's resident set, page faults and /metrics are sampled each second. Two phases
run: the first right after server start (the OS page cache is not dropped), then a warm one. Every process
started is stopped at the end.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
import threading
import time
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
sys.path.insert(0, str(REPO / "tools" / "parity"))
from parity import runner, servers, world  # noqa: E402
from parity.session import Session  # noqa: E402

PLAYERS = [("Alice", "alicepass"), ("Bob", "bobpass"), ("Wiz", "wizpass")]
MIX = ["look", "say measuring", "pose measures", "@set me=COMMANDLOAD:{n}"]


def server_pid(dotnet_run_pid: int) -> int | None:
    """The engine itself: `dotnet run` starts it as a child that runs SharpMUSH.Server."""
    for entry in Path("/proc").iterdir():
        if not entry.name.isdigit():
            continue
        try:
            stat = (entry / "stat").read_text()
            ppid = int(stat[stat.rindex(")") + 2:].split()[1])
            cmdline = (entry / "cmdline").read_bytes()
        except (OSError, ValueError):
            continue
        if ppid == dotnet_run_pid and b"SharpMUSH.Server" in cmdline:
            return int(entry.name)
    return None


def proc_sample(pid: int) -> dict:
    status = (Path("/proc") / str(pid) / "status").read_text()
    fields = dict(line.split(":", 1) for line in status.splitlines() if ":" in line)
    stat = (Path("/proc") / str(pid) / "stat").read_text()
    after = stat[stat.rindex(")") + 2:].split()
    kb = lambda name: int(fields[name].strip().split()[0]) * 1024  # noqa: E731
    return {"rss": kb("VmRSS"), "hwm": kb("VmHWM"), "minflt": int(after[7]), "majflt": int(after[9])}


def scrape(port: int) -> str:
    with urllib.request.urlopen(f"http://127.0.0.1:{port}/metrics", timeout=5) as response:
        return response.read().decode("utf-8", errors="replace")


def series(text: str, name: str) -> dict[str, float]:
    """Every sample of `name`, by its label set; a sample line is `name{labels} value timestamp`."""
    found = {}
    for line in text.splitlines():
        if line.startswith(name) and not line.startswith("#"):
            head, value = line.split()[0], line.split()[1]
            if head == name or head.startswith(name + "{"):
                found[head] = float(value)
    return found


def counter(before: str, after: str, name: str) -> float:
    return sum(series(after, name).values()) - sum(series(before, name).values())


def histogram(before: str, after: str, name: str) -> dict:
    """This phase's count, sum and p50/p95/p99 as bucket upper bounds, from the cumulative histograms either side."""
    def buckets(text: str) -> dict[float, float]:
        result = {}
        for head, value in series(text, name + "_bucket").items():
            bound = re.search(r'le="([^"]+)"', head).group(1)
            result[float("inf") if bound == "+Inf" else float(bound)] = value
        return result
    start, end = buckets(before), buckets(after)
    phase = sorted((bound, end[bound] - start.get(bound, 0.0)) for bound in end)
    count = counter(before, after, name + "_count")
    result = {"count": count, "sum_ms": counter(before, after, name + "_sum")}
    for q in (0.50, 0.95, 0.99):
        result[f"p{int(q * 100)}_le_ms"] = next((bound for bound, seen in phase if count and seen >= q * count), None)
    return result


def percentiles(values: list[float]) -> dict:
    values = sorted(values)
    at = lambda p: values[min(len(values) - 1, max(0, int(-(-p * len(values) // 1)) - 1))] if values else None  # noqa: E731
    return {"count": len(values), "p50": at(0.50), "p95": at(0.95), "p99": at(0.99), "max": values[-1] if values else None}


def drive(session: Session, rate: float, seconds: float, phase: str, out: list, lock: threading.Lock, offset: float) -> None:
    start = time.monotonic() + offset
    n = 0
    while True:
        due = start + n / rate
        if due - start >= seconds:
            return
        delay = due - time.monotonic()
        if delay > 0:
            time.sleep(delay)
        command = MIX[n % len(MIX)].format(n=n)
        session.send(command)
        session.sync_finish(session.sync_start())
        latency = (time.monotonic() - due) * 1000
        with lock:
            out.append({"phase": phase, "session": session.name, "command": command.split()[0], "latency_ms": latency})
        n += 1


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True, help="scratch directory for the disposable servers")
    parser.add_argument("--pennmush", type=Path, required=True)
    parser.add_argument("--seconds", type=float, default=600)
    parser.add_argument("--rate", type=float, default=10, help="commands per second, over all sessions")
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    work = args.work / time.strftime("run-%Y%m%d-%H%M%S")
    work.mkdir(parents=True)
    metrics_port = servers.free_port()
    os.environ["Metrics__Port"] = str(metrics_port)
    penn = servers.PennMush(args.pennmush, work)
    sharp = servers.SharpMush(REPO, work, REPO / "tools" / "parity" / ".cache", args.dotnet, build=False)
    try:
        penn.start()
        target = runner.Target("PennMUSH", "127.0.0.1", penn.port, "LOADp")
        setup, _ = world.load_setup(REPO / "tools" / "parity" / "world" / "setup.mush")
        world.run_setup(target, setup)
        dump = penn.game / "data" / "outdb.gz"
        servers.wait_for(dump.exists, "PennMUSH to write outdb.gz", 30, [penn.proc])
        flatfile = work / "world.db"
        penn.dump_flatfile(flatfile)
        penn.stop()
        started = time.monotonic()
        sharp.start(flatfile)
        engine = next(p for p in sharp.procs if p.name == "server")
        pid = None
        servers.wait_for(lambda: server_pid(engine.popen.pid) is not None, "the engine process", 30, [engine])
        pid = server_pid(engine.popen.pid)
        sessions = []
        for name, password in PLAYERS:
            session = Session(name, "127.0.0.1", sharp.port, "LOADs")
            session.read_banner()
            session.send(f"connect {name} {password}")
            session.sync_finish(session.sync_start())
            sessions.append(session)
        ready = time.monotonic() - started
        samples, results = [], []
        lock = threading.Lock()
        summary = {"engine_ready_seconds": ready, "rate_per_second": args.rate, "sessions": len(sessions), "phases": {}}
        for phase in ("cold", "warm"):
            stop = threading.Event()
            base = proc_sample(pid)
            before = scrape(metrics_port)

            def sample() -> None:
                t0 = time.monotonic()
                while not stop.is_set():
                    s = proc_sample(pid)
                    samples.append({"phase": phase, "seconds": round(time.monotonic() - t0, 1), **s})
                    stop.wait(1)

            sampler = threading.Thread(target=sample, daemon=True)
            sampler.start()
            phase_results: list = []
            threads = [threading.Thread(target=drive, args=(s, args.rate / len(sessions), args.seconds, phase, phase_results, lock, i / args.rate))
                       for i, s in enumerate(sessions)]
            for t in threads:
                t.start()
            for t in threads:
                t.join()
            stop.set()
            sampler.join()
            after = scrape(metrics_port)
            end = proc_sample(pid)
            results.extend(phase_results)
            (args.output / f"metrics-{phase}-before.txt").write_text(before, encoding="utf-8")
            (args.output / f"metrics-{phase}-after.txt").write_text(after, encoding="utf-8")
            by_command = {}
            for r in phase_results:
                by_command.setdefault(r["command"], []).append(r["latency_ms"])
            summary["phases"][phase] = {
                "label": "first run after server start, page cache not dropped" if phase == "cold" else "warm: second phase on the same server",
                "commands": percentiles([r["latency_ms"] for r in phase_results]),
                "by_command": {k: percentiles(v) for k, v in by_command.items()},
                "peak_rss_bytes": end["hwm"], "rss_bytes": end["rss"],
                "minor_faults": end["minflt"] - base["minflt"], "major_faults": end["majflt"] - base["majflt"],
                "writer_wait": histogram(before, after, "sharpmush_storage_writer_wait_milliseconds"),
                "writer_hold": histogram(before, after, "sharpmush_storage_writer_hold_milliseconds"),
                "read_transaction": histogram(before, after, "sharpmush_storage_read_duration_milliseconds"),
                "writer_queued_at_end": sum(series(after, "sharpmush_storage_writer_queued").values()),
                "reads_active_at_end": sum(series(after, "sharpmush_storage_reads_active").values()),
                "gc_pause_seconds": counter(before, after, "dotnet_gc_pause_time_seconds_total"),
                "allocated_bytes": counter(before, after, "dotnet_gc_heap_total_allocated_bytes_total"),
                "gc_collections": counter(before, after, "dotnet_gc_collections_total"),
            }
        for s in sessions:
            s.close()
        (args.output / "commands.jsonl").write_text("".join(json.dumps(r) + "\n" for r in results), encoding="utf-8")
        (args.output / "samples.jsonl").write_text("".join(json.dumps(s) + "\n" for s in samples), encoding="utf-8")
        (args.output / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
        print(json.dumps(summary, indent=2))
    finally:
        sharp.stop()
        penn.stop()


if __name__ == "__main__":
    main()
