from __future__ import annotations

import json
from collections import OrderedDict
from datetime import datetime, timezone
from pathlib import Path

from .compare import DIFF, ERROR, KNOWN, MATCH, OPEN
from .normalize import RULES


def _fence(text: str) -> str:
    return "```\n" + (text if text else "(no output)") + "\n```"


def write_reports(out: Path, meta: dict, results, stale, fixed, orphans, anchors, coverage, scenario_dir: str):
    out.mkdir(parents=True, exist_ok=True)
    by_scn: "OrderedDict[str, list]" = OrderedDict()
    for r in results:
        by_scn.setdefault(r.penn.scenario, []).append(r)
    STATUSES = (MATCH, KNOWN, OPEN, DIFF, ERROR)
    counts = {s: {k: 0 for k in STATUSES} for s in by_scn}
    for s, rs in by_scn.items():
        for r in rs:
            counts[s][r.status] += 1
    total = {k: sum(c[k] for c in counts.values()) for k in STATUSES}

    L = []
    L.append("# PennMUSH vs SharpMUSH parity report\n")
    L.append(f"- Generated: {datetime.now(timezone.utc):%Y-%m-%d %H:%M:%SZ}")
    for k, v in meta.items():
        L.append(f"- {k}: {v}")
    L.append(f"- **Steps: {sum(total.values())} — {total[MATCH]} match, {total[KNOWN]} known difference, "
             f"{total[OPEN]} open gap (baseline), {total[DIFF]} UNEXPECTED DIFFERENCE, {total[ERROR]} error; "
             f"{len(stale)} stale allowlist entries, {len(fixed)} baseline entries now fixed, "
             f"{len(orphans)} orphaned entries**\n")
    L.append("| Scenario | Steps | Match | Known | Open gap | Unexpected | Error |\n|---|---:|---:|---:|---:|---:|---:|")
    for s, c in counts.items():
        L.append(f"| {s} | {sum(c.values())} | {c[MATCH]} | {c[KNOWN]} | {c[OPEN]} | {c[DIFF]} | {c[ERROR]} |")
    L.append("")

    bad = [r for r in results if r.status in (DIFF, ERROR, OPEN)]
    L.append(f"## Differences ({len(bad)}; open gaps are tracked in baseline.json)\n")
    if not bad:
        L.append("None.\n")
    for r in bad:
        L.append(f"### `{r.key}` — {r.status}\n")
        L.append(f"- Repro: `tools/parity/run.sh --only {r.penn.scenario}/{r.penn.case}` — "
                 f"{scenario_dir}/{r.penn.scenario}.scn:{r.penn.lineno}, session `{r.penn.session}`, "
                 f"{r.penn.kind} `{r.penn.command}`\n")
        if r.diff:  # `-` lines are PennMUSH (the reference), `+` lines are SharpMUSH; full text is in report.json
            L.append("```diff\n" + r.diff + "\n```\n")

    known = [r for r in results if r.status == KNOWN]
    L.append(f"## Known differences observed ({len(known)})\n")
    for r in known:
        L.append(f"- `{r.key}` — {r.entry.id}: {r.entry.reason} (tracking: {r.entry.tracking})")
    L.append("")
    if fixed:
        L.append("## Fixed since the baseline (remove from baseline.json)\n")
        L.extend(f"- `{k}`" for k in fixed)
        L.append("")
    if stale:
        L.append("## Stale allowlist entries (the difference is gone — delete the entry)\n")
        for e in stale:
            L.append(f"- {e.id} `{e.scenario}/{e.case}`: {e.reason}")
        L.append("")

    if orphans:
        L.append("## Orphaned baseline/allowlist entries (a step was inserted or removed — re-key them)\n")
        L.extend(f"- {o}" for o in orphans)
        L.append("")

    L.append("## Import anchors (dbref on each server)\n")
    L.append("| Anchor | PennMUSH | SharpMUSH |\n|---|---:|---:|")
    for label, (p, s) in anchors.items():
        L.append(f"| {label} | #{p} | #{s} |")
    L.append("")

    L.append("## Coverage\n")
    for kind, (used, universe) in coverage.items():
        hit = sorted(used & universe)
        L.append(f"- {kind}: {len(hit)} of {len(universe)} PennMUSH {kind} exercised "
                 f"({100 * len(hit) // max(1, len(universe))}%). Full list in coverage.json.")
    L.append("")

    L.append("## Normalization rules applied to both sides\n")
    for rule in RULES:
        L.append(f"- `{rule.name}`: {rule.description}")
    L.append("- `site-text`: on login steps the connect screen, MOTD and wizard MOTD text of each server "
             "(connect.txt, motd.txt, wizmotd.txt) and SharpMUSH's `Connected!` line are removed: they are "
             "site content, not behaviour.")
    L.append("- `dbref`: dbrefs of world-fixture objects are mapped to PennMUSH's numbering; dbrefs of "
             "objects created during scenarios become `#NEW<k>` in order of first appearance; any other "
             "SharpMUSH dbref is one of its own system objects and becomes `#S<n>`, so it never matches "
             "the PennMUSH object that happens to share its number.")
    (out / "report.md").write_text("\n".join(L) + "\n", encoding="utf-8")

    def dump(r):
        return {"key": r.key, "status": r.status, "command": r.penn.command, "session": r.penn.session,
                "penn": r.penn_text, "sharp": r.sharp_text, "diff": r.diff,
                "known": r.entry.id if r.entry else None}
    (out / "report.json").write_text(json.dumps({
        "meta": meta, "totals": total, "stale": [e.id for e in stale], "fixed": fixed, "orphaned": orphans,
        "results": [dump(r) for r in results]}, indent=1), encoding="utf-8")
    (out / "coverage.json").write_text(json.dumps(
        {k: {"used": sorted(u), "penn_total": len(uni), "covered": sorted(u & uni)} for k, (u, uni) in coverage.items()}, indent=1), encoding="utf-8")
    return total
