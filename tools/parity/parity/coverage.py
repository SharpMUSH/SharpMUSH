"""Coverage map: which commands and functions the scenarios exercise, against PennMUSH's own lists."""
from __future__ import annotations

import re
from pathlib import Path

from .runner import Target
from .session import Session
from .world import GOD

_FUNC = re.compile(r"(?<![A-Za-z0-9_])([A-Za-z_][A-Za-z0-9_]*)\(")
_SWITCH = re.compile(r"^\s*([@+]?\w+)(?:/[\w/]+)?")


def used_in_scenarios(paths: list[Path]) -> tuple[set[str], set[str]]:
    commands, functions = set(), set()
    for path in paths:
        for line in path.read_text(encoding="utf-8").splitlines():
            if not line.strip() or line.startswith("#") or line.startswith("::"):
                continue
            m = _SWITCH.match(line)
            if m:
                commands.add(m.group(1).lower())
            functions.update(f.lower() for f in _FUNC.findall(line))
    return commands, functions


def penn_universe(target: Target) -> tuple[set[str], set[str]]:
    s = Session("coverage", target.host, target.port, target.prefix)
    s.read_banner()
    s.send("connect %s %s" % GOD)
    s.sync_finish(s.sync_start())
    out = {}
    for what in ("commands", "functions"):
        s.send(f"@list {what}")
        text = s.sync_finish(s.sync_start())
        out[what] = {w.lower() for w in re.findall(r"[@+A-Za-z_][A-Za-z0-9_@+.\-]*", text)}
    s.close()
    return out["commands"], out["functions"]
