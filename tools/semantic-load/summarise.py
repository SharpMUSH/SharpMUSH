#!/usr/bin/env python3
"""Turns the runs' summary.json files into the Markdown tables the measurements report quotes."""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path


def ms(value) -> str:
    return "–" if value in (None, "NaN") or math.isnan(value) else f"{value:.1f}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runs", type=Path, required=True)
    parser.add_argument("--preview", type=Path)
    args = parser.parse_args()
    out = []
    if args.preview and (args.preview / "summary.json").exists():
        p = json.loads((args.preview / "summary.json").read_text())
        out += ["### Disposable server, wiki reads off", "",
                f"Engine ready {p['engine_ready_seconds']:.0f} s after start; {p['rate_per_second']} commands/s over {p['sessions']} sessions.", "",
                "| Pass | Commands | p50 ms | p95 ms | p99 ms | max ms | look p99 | say p99 | pose p99 | @set p99 | Peak RSS MiB | Minor faults | Major faults | GC pause s | Allocated MiB | Writer hold p95 ≤ ms |",
                "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|"]
        for name, phase in p["phases"].items():
            c, k = phase["commands"], phase["by_command"]
            out.append(f"| {name} | {c['count']} | {ms(c['p50'])} | {ms(c['p95'])} | {ms(c['p99'])} | {ms(c['max'])} | "
                       + " | ".join(ms(k.get(n, {}).get("p99")) for n in ("look", "say", "pose", "@set"))
                       + f" | {phase['peak_rss_bytes'] / 2**20:.0f} | {phase['minor_faults']} | {phase['major_faults']} | {phase['gc_pause_seconds']:.2f}"
                       + f" | {phase['allocated_bytes'] / 2**20:.0f} | {ms(phase['writer_hold']['p95_le_ms'])} |")
        out.append("")
    for label in sorted(d.name for d in args.runs.iterdir() if d.is_dir() and d.name.startswith("pf2e-")):
        root = args.runs / label
        build = json.loads((root / "build" / "build.json").read_text()) if (root / "build" / "build.json").exists() else None
        out += [f"### {label}", ""]
        if build:
            t = build["Timings"]
            out += [f"Build: {build['Pages']} pages ({build['Refused']} refused), {build['ImportedPages']} pages with {build['Facts']} facts; "
                    f"activate {t.get('activate', 0):.1f} s, create {t.get('create', 0):.0f} s, import {t.get('import', 0):.0f} s. "
                    f"File {build['Storage']['FileBytes'] / 2**20:.0f} MiB, live {build['Storage']['LiveBytes'] / 2**20:.0f} MiB."
                    + (f" Activation failed: {build['ActivationFailure'].splitlines()[0]}" if build.get("ActivationFailure") else ""), ""]
        out += ["| R/s | Pass | Queries | q p50 | q p95 | q p99 | Outcomes | Cmd p50 | Cmd p95 | Cmd p99 | Writer wait p99 | Writer hold p99 | Read txn p99 | Peak reads | Peak WS MiB | Faults min/maj | GC pause ms | Alloc MiB | File MiB | Rebuild |",
                "|---:|---|---:|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---|"]
        for summary in sorted(root.glob("rate-*/rate-*/summary.json"), key=lambda p: (int(p.parent.name.split("-")[1]), p.parent.name.endswith("warm"))):
            s = json.loads(summary.read_text())
            q, c = s["Queries"], s["Commands"]
            outcomes = ", ".join(f"{k} {v}" for k, v in sorted(s["QueryOutcomes"].items())) or "–"
            rebuild = s.get("Rebuild")
            rb = f"{rebuild['Subjects']} subj in {rebuild['Seconds']:.1f} s ({rebuild['SubjectsPerSecond']:.0f}/s), {rebuild['Outcome']}" if rebuild else "–"
            out.append(f"| {s['Rate']} | {s['Pass']} | {q['Count']} | {ms(q['P50'])} | {ms(q['P95'])} | {ms(q['P99'])} | {outcomes} | "
                       f"{ms(c['P50'])} | {ms(c['P95'])} | {ms(c['P99'])} | {ms(s['WriterWaitMs']['P99'])} | {ms(s['WriterHoldMs']['P99'])} | "
                       f"{ms(s['ReadTransactionMs']['P99'])} | {s['PeakReadsActive']} | {s['PeakWorkingSet'] / 2**20:.0f} | {s['MinorFaults']}/{s['MajorFaults']} | "
                       f"{s['GcPauseMs']:.0f} | {s['AllocatedBytes'] / 2**20:.0f} | {s['Storage']['FileBytes'] / 2**20:.0f} | {rb} |")
        by_kind = {}
        for summary in root.glob("rate-*/rate-*-warm/summary.json"):
            s = json.loads(summary.read_text())
            for kind, v in s["QueriesByKind"].items():
                by_kind.setdefault(kind, {})[s["Rate"]] = v
        if by_kind:
            rates = sorted({r for v in by_kind.values() for r in v})
            out += ["", "Warm query p99 ms by shape:", "", "| Shape | " + " | ".join(f"R={r}" for r in rates) + " |", "|---|" + "---:|" * len(rates)]
            for kind in sorted(by_kind):
                out.append(f"| {kind} | " + " | ".join(ms(by_kind[kind].get(r, {}).get("P99")) for r in rates) + " |")
        out.append("")
    print("\n".join(out))


if __name__ == "__main__":
    main()
