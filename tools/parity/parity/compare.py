"""Compares two transcripts step by step and applies the known-differences allowlist."""
from __future__ import annotations

import difflib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

from .normalize import DbrefCanonicalizer, normalize
from .runner import StepRecord

MATCH, KNOWN, DIFF, ERROR, OPEN = "match", "known-difference", "DIFFERENCE", "ERROR", "open-gap"


@dataclass
class Entry:
    id: str
    scenario: str
    case: str
    step: Optional[int]
    reason: str
    tracking: str
    command: Optional[str] = None  # required with `step`: pins the positional key to its command
    profile: Optional[str] = None  # the `## ` heading in the compatibility profile that explains it
    # When set, the entry accepts exactly this (normalized) SharpMUSH output against exactly `penn`
    # and nothing else: an entry for SharpMUSH-only additions to a list does not also hide a wrong
    # or missing item on either side.
    sharp: Optional[str] = None
    penn: Optional[str] = None

    def covers(self, rec: StepRecord) -> bool:
        return (self.scenario == rec.scenario and self.case == rec.case
                and (self.step is None or self.step == rec.index))


PROFILE = (Path(__file__).resolve().parents[3]
           / "SharpMUSH.Documentation/Helpfiles/SharpMUSH/pennmush-compatibility.md")


def profile_headings(path: Path = PROFILE) -> set[str]:
    """Declared section headings across the compatibility articles, or a legacy profile."""
    text = path.read_text(encoding="utf-8-sig")
    marker = "<!-- help-article"
    if not text.startswith(marker):
        return {line[3:].strip() for line in text.splitlines() if line.startswith("## ")}

    # The runtime corpus check validates each declared heading against its Markdown.
    # Consume that same declaration instead of treating filenames as article identity.
    headings = set()
    for source in path.parent.glob("*.md"):
        article = source.read_text(encoding="utf-8-sig")
        if not article.startswith(marker):
            continue
        metadata = json.loads(article[len(marker):article.index("-->")])
        if metadata.get("corpus") == "help" and metadata.get("id", "").startswith("compatibility-"):
            headings.update(section["heading"] for section in metadata["sections"])
    return headings


def load_allowlist(path: Path, profile: Path = PROFILE) -> list[Entry]:
    """Every entry is a deliberate difference, so it must name the profile entry that documents it."""
    data = json.loads(path.read_text(encoding="utf-8"))
    headings = profile_headings(profile)
    out = []
    for e in data["entries"]:
        for required in ("id", "scenario", "case", "reason", "tracking", "profile"):
            if not e.get(required):
                raise ValueError(f"{path}: entry {e.get('id', e)!r} is missing '{required}'")
        if e.get("step") is not None and not e.get("command"):
            raise ValueError(f"{path}: entry {e['id']!r} names a step, so it needs that step's 'command'")
        if (e.get("sharp") is None) != (e.get("penn") is None):
            raise ValueError(f"{path}: entry {e['id']!r} pins one side's output; 'sharp' and 'penn' go together")
        if e["profile"] not in headings:
            raise ValueError(f"{path}: entry {e['id']!r} names profile entry {e['profile']!r}, "
                             f"which is not a declared compatibility section in {profile.name}")
        out.append(Entry(e["id"], e["scenario"], e["case"], e.get("step"), e["reason"], e["tracking"],
                         e.get("command"), e["profile"], e.get("sharp"), e.get("penn")))
    return out


def load_baseline(path: Path) -> dict[str, str]:
    """baseline.json: open gaps as {key: command}. The command pins each positional key."""
    return {s["key"]: s["command"] for s in json.loads(path.read_text(encoding="utf-8"))["steps"]}


def orphaned(baseline: dict[str, str], allowlist: list[Entry], results) -> list[str]:
    """Baseline/allowlist entries whose step no longer exists or now runs a different command.

    Keys are positional (`case#index`): inserting or removing a step shifts every later key, which
    would silently re-point an entry at another step. Each entry records its command, so a shift
    shows up here (and fails the run) instead. Only scenarios that ran are checked.
    """
    command = {r.key: r.penn.command for r in results}
    ran = {r.penn.scenario for r in results}
    out = []
    for key, cmd in sorted(baseline.items()):
        if key.split("/", 1)[0] in ran and command.get(key) != cmd:
            out.append(f"baseline `{key}` was `{cmd}`, now {_now(command.get(key))}")
    for e in allowlist:
        if e.step is None or e.scenario not in ran:
            continue
        key = f"{e.scenario}/{e.case}#{e.step}"
        if command.get(key) != e.command:
            out.append(f"allowlist {e.id} `{key}` was `{e.command}`, now {_now(command.get(key))}")
    return out


def _now(cmd: Optional[str]) -> str:
    return "missing" if cmd is None else f"`{cmd}`"


@dataclass
class Result:
    key: str
    status: str
    penn: StepRecord
    sharp: StepRecord
    penn_text: str
    sharp_text: str
    diff: str
    entry: Optional[Entry] = None


def _strip_site_text(text: str, site_texts: list[str]) -> str:
    """Rule `site-text`: the connect screen and MOTD files are per-site content, not behaviour."""
    for t in site_texts:
        t = normalize(t)
        if t:
            text = text.replace(t, "", 1)
    return text


def _render(rec: StepRecord, canon: DbrefCanonicalizer, site_texts: list[str]) -> str:
    text = normalize(rec.output)
    if rec.kind in ("login", "login-fail"):
        text = normalize(_strip_site_text(text, site_texts))
        text = normalize(text.lstrip("\n"))
    parts = [canon.apply(text)]
    for name in sorted(rec.async_output):
        parts.append(f"[{name}] " + canon.apply(normalize(rec.async_output[name])).replace("\n", f"\n[{name}] "))
    return "\n".join(p for p in parts if p != "")


def compare(penn: list[StepRecord], sharp: list[StepRecord], penn_canon, sharp_canon, allowlist,
            penn_site: list[str], sharp_site: list[str], baseline=frozenset()):
    results: list[Result] = []
    used: set[str] = set()
    by_key = {r.key(): r for r in sharp}
    scenario = None
    for p in penn:
        if p.scenario != scenario:  # #NEW<k> numbering is per scenario file
            scenario = p.scenario
            penn_canon.new_scope(scenario)
            sharp_canon.new_scope(scenario)
        s = by_key.get(p.key())
        if s is None:
            s = StepRecord(p.scenario, p.case, p.index, p.kind, p.session, p.command,
                           error="step not reached on SharpMUSH (an earlier step failed)")
        pt, st = _render(p, penn_canon, penn_site), _render(s, sharp_canon, sharp_site)
        entry = next((e for e in allowlist if e.covers(p)), None)
        accepts = entry is not None and (entry.sharp is None or (entry.sharp, entry.penn) == (st, pt))
        if p.error or s.error:
            status = ERROR
            diff = "\n".join(f"{n}: {r.error}" for n, r in (("PennMUSH", p), ("SharpMUSH", s)) if r.error)
        elif pt == st:
            status, diff = MATCH, ""
            # A matching step under an allowlist entry is only "stale" if EVERY step it covers
            # matches; decided after the loop.
        else:
            status = KNOWN if accepts else (OPEN if p.key() in baseline else DIFF)
            diff = "\n".join(difflib.unified_diff(pt.split("\n"), st.split("\n"), "PennMUSH", "SharpMUSH", lineterm="", n=2))
        if entry and status in (KNOWN, DIFF, OPEN):
            used.add(entry.id)
        results.append(Result(p.key(), status, p, s, pt, st, diff, entry if status == KNOWN else None))
    stale = [e for e in allowlist if e.id not in used and any(e.covers(r.penn) for r in results)]
    # A baseline entry whose step now matches, or differs only as an allowlist entry accepts, is
    # progress the baseline must record.
    fixed = sorted(k for k in baseline if any(r.key == k and r.status in (MATCH, KNOWN) for r in results))
    return results, stale, fixed
