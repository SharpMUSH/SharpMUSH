"""Builds the shared world: run world/setup.mush on PennMUSH, dump it, and resolve anchors."""
from __future__ import annotations

import re
from pathlib import Path

from .normalize import DbrefCanonicalizer
from .runner import Target
from .session import Session

GOD = ("One", "godpass")


def load_setup(path: Path):
    commands, anchors = [], []
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip() or line.startswith("#"):
            continue
        if line.startswith("::anchor "):
            _, label, expr = line.split(None, 2)
            anchors.append((label, expr))
        else:
            commands.append(line)
    return commands, anchors


def run_setup(target: Target, commands: list[str]) -> list[str]:
    """Runs the setup commands on Penn; returns the transcript lines for the log."""
    s = Session("setup", target.host, target.port, target.prefix)
    s.read_banner()
    s.send("connect One")  # a fresh PennMUSH database has no password on #1 yet
    log = [s.sync_finish(s.sync_start())]
    for c in commands:
        s.send(c)
        log.append(f"> {c}\n" + s.sync_finish(s.sync_start()))
    s.send("@dump")
    log.append("> @dump\n" + s.sync_finish(s.sync_start()))
    s.close()
    return log


_DBREF = re.compile(r"#(\d+)(?::\d+)?")  # anchors need the number only; scenarios compare the full form


def resolve_anchors(target: Target, anchors) -> tuple[dict[str, int], int]:
    """Where each anchor lives on this server, plus the first free dbref (see DbrefCanonicalizer)."""
    s = Session("anchors", target.host, target.port, target.prefix)
    s.read_banner()
    s.send("connect %s %s" % GOD)
    s.sync_finish(s.sync_start())
    found: dict[str, int] = {}
    for label, expr in anchors:
        s.send(f"think {expr}")
        out = s.sync_finish(s.sync_start()).strip()
        m = _DBREF.fullmatch(out)
        if not m:
            raise RuntimeError(f"anchor {label!r} ({expr}) on {target.name} returned {out!r}, not a dbref")
        found[label] = int(m.group(1))
    # A probe object marks where scenario-created objects begin; it is deliberately kept.
    s.send("@create ParityProbe")
    s.sync_finish(s.sync_start())
    s.send("think [num(ParityProbe)]")
    out = s.sync_finish(s.sync_start()).strip()
    m = _DBREF.fullmatch(out)
    if not m:
        raise RuntimeError(f"could not find ParityProbe on {target.name}: {out!r}")
    first_free = int(m.group(1))
    s.close()
    return found, first_free


def canonicalizer(side: dict[str, int], reference: dict[str, int], first_free: int,
                  foreign_tag: str = "") -> DbrefCanonicalizer:
    return DbrefCanonicalizer({side[k]: reference[k] for k in side}, first_free, foreign_tag)
