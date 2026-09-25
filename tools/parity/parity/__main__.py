"""One-command differential run: `tools/parity/run.sh` (see tools/parity/README.md)."""
from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

from . import compare as cmp
from . import coverage, report, runner, scenario, servers, world

HERE = Path(__file__).resolve().parent.parent          # tools/parity
REPO = HERE.parent.parent


def find_pennmush(explicit: str | None) -> Path:
    cands = [explicit, os.environ.get("PENNMUSH_DIR"), str(REPO / "pennmush")]
    common = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=REPO, capture_output=True, text=True)
    if common.returncode == 0:  # a git worktree keeps the gitignored pennmush/ in the main checkout
        cands.append(str((REPO / common.stdout.strip()).resolve().parent / "pennmush"))
    for c in cands:
        if c and (Path(c) / "src" / "netmud").exists():
            return Path(c)
    raise SystemExit("No built PennMUSH found. Pass --pennmush-dir or set PENNMUSH_DIR to a checkout where "
                     "`./configure && make` has produced src/netmud. Looked in: " + ", ".join(c for c in cands if c))


def default_dotnet() -> str:
    for c in (os.environ.get("DOTNET"), os.path.join(os.environ.get("DOTNET_ROOT", ""), "dotnet")):
        if c and Path(c).exists():
            return c
    return "dotnet"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="parity", description=__doc__)
    ap.add_argument("--pennmush-dir")
    ap.add_argument("--only", action="append", default=[], metavar="SCENARIO[/CASE]",
                    help="run only these scenario files (a /CASE suffix limits the report to that case)")
    ap.add_argument("--out", default=str(HERE / "reports" / "latest"), help="report directory")
    ap.add_argument("--work", default=str(HERE / ".work"), help="scratch directory for server data and logs")
    ap.add_argument("--dotnet", default=default_dotnet(), help="dotnet host that satisfies global.json (default: $DOTNET, $DOTNET_ROOT/dotnet, then PATH)")
    ap.add_argument("--no-build", action="store_true", help="skip `dotnet build` (already built)")
    ap.add_argument("--baseline", action="store_true",
                    help="treat steps listed in baseline.json as open gaps (CI ratchet: only new differences fail)")
    ap.add_argument("--write-baseline", action="store_true", help="rewrite baseline.json from this run")
    ap.add_argument("--allow-failures", action="store_true", help="always exit 0 (report only)")
    args = ap.parse_args(argv)

    pennmush = find_pennmush(args.pennmush_dir)
    scn_dir = HERE / "scenarios"
    wanted = {o.split("/")[0]: (o.split("/")[1] if "/" in o else None) for o in args.only}
    files = sorted(p for p in scn_dir.glob("*.scn") if not wanted or p.stem in wanted)
    if not files:
        raise SystemExit("no scenario files selected")
    scenarios = [scenario.load(p) for p in files]
    allowlist = cmp.load_allowlist(HERE / "known-differences.json")
    baseline_path = HERE / "baseline.json"
    baseline = cmp.load_baseline(baseline_path) if args.baseline else {}
    setup_cmds, anchors = world.load_setup(HERE / "world" / "setup.mush")

    old_runs = sorted(Path(args.work).glob("run-*")) if Path(args.work).exists() else []
    for old in old_runs[:-2]:  # keep the last two runs' logs, drop the rest
        shutil.rmtree(old, ignore_errors=True)
    work = Path(args.work) / time.strftime("run-%Y%m%d-%H%M%S")
    work.mkdir(parents=True)
    penn = servers.PennMush(pennmush, work)
    sharp = servers.SharpMush(REPO, work, HERE / ".cache", args.dotnet, build=not args.no_build)
    try:
        print(f"[parity] starting PennMUSH from {pennmush}")
        penn.start()
        ptarget = runner.Target("PennMUSH", "127.0.0.1", penn.port, "PSYNCp")
        print("[parity] building the world on PennMUSH from world/setup.mush")
        (work / "logs" / "setup-transcript.txt").write_text("\n".join(world.run_setup(ptarget, setup_cmds)))
        flatfile = work / "world.db"
        dump = penn.game / "data" / "outdb.gz"
        servers.wait_for(dump.exists, "PennMUSH to write outdb.gz", 30, [penn.proc])
        penn.dump_flatfile(flatfile)
        print("[parity] starting SharpMUSH and importing the flatfile")
        sharp.start(flatfile)
        starget = runner.Target("SharpMUSH", "127.0.0.1", sharp.port, "PSYNCs")

        p_anchor, p_free = world.resolve_anchors(ptarget, anchors)
        s_anchor, s_free = world.resolve_anchors(starget, anchors)
        anchor_table = {k: (p_anchor[k], s_anchor[k]) for k in p_anchor}
        print(f"[parity] anchors: {anchor_table}")

        all_p, all_s = [], []
        for scn in scenarios:
            print(f"[parity] scenario {scn.name}")
            pr, _ = runner.run_scenario(ptarget, scn)
            sr, _ = runner.run_scenario(starget, scn)
            all_p += pr
            all_s += sr
        pcanon = world.canonicalizer(p_anchor, p_anchor, p_free)
        scanon = world.canonicalizer(s_anchor, p_anchor, s_free, foreign_tag="S")
        def site(d: Path):
            return [(d / n).read_text(errors="replace") for n in ("connect.txt", "motd.txt", "wizmotd.txt") if (d / n).exists()]
        results, stale, fixed = cmp.compare(all_p, all_s, pcanon, scanon, allowlist,
                                     site(penn.game / "txt"), site(REPO / "SharpMUSH.Server"), baseline)
        orphans = cmp.orphaned(baseline, allowlist, results)
        if any(wanted.values()):
            results = [r for r in results if wanted.get(r.penn.scenario) in (None, r.penn.case)]
            stale, fixed, orphans = [], [], []

        if args.write_baseline:
            steps = [{"key": r.key, "command": r.penn.command}
                     for r in sorted(results, key=lambda r: r.key) if r.status in (cmp.DIFF, cmp.OPEN)]
            baseline_path.write_text(json.dumps({"steps": steps}, indent=1) + "\n")
            print(f"[parity] wrote {len(steps)} open gaps to {baseline_path}")
        cov = {}
        try:
            uc, uf = coverage.used_in_scenarios(files)
            pc, pf = coverage.penn_universe(ptarget)
            cov = {"commands": (uc, pc), "functions": (uf, pf)}
        except Exception as e:  # coverage is advisory; never mask the comparison
            print(f"[parity] coverage unavailable: {e}")
            cov = {"commands": (set(), set()), "functions": (set(), set())}

        ver = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=REPO, capture_output=True, text=True).stdout.strip()
        pver = subprocess.run(["git", "rev-parse", "--short", "HEAD"], cwd=pennmush, capture_output=True, text=True).stdout.strip()
        meta = {"SharpMUSH commit": ver, "PennMUSH commit": pver, "Scenarios": ", ".join(s.name for s in scenarios)}
        out = Path(args.out)
        if out.exists():
            shutil.rmtree(out)
        totals = report.write_reports(out, meta, results, stale, fixed, orphans, anchor_table, cov, "tools/parity/scenarios")
        print(f"[parity] report: {out / 'report.md'}")
        print(f"[parity] {totals}")
        bad = totals[cmp.DIFF] + totals[cmp.ERROR] + len(stale) + len(fixed) + len(orphans)
        return 0 if (bad == 0 or args.allow_failures) else 1
    finally:
        sharp.stop()
        penn.stop()


if __name__ == "__main__":
    sys.exit(main())
