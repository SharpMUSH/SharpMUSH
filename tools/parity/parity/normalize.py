"""Output normalization. Every rule here is documented in tools/parity/README.md.

A rule exists only for output that is genuinely non-deterministic or that is a rendering choice
(colour) rather than behaviour. Behavioural differences are never normalized: they are either
fixed or listed in known-differences.json.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Callable

_ANSI = re.compile(r"\x1b\[[0-?]*[ -/]*[@-~]")
# `Thu Sep 24 16:59:11 2026` (both servers' default time format)
_TIMESTAMP = re.compile(r"\b(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun) (?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) +\d{1,2} \d\d:\d\d:\d\d \d{4}\b")
_TOKEN = re.compile(r"PSYNC[0-9a-f]*[A-Za-z0-9_]*x\d+")


@dataclass(frozen=True)
class Rule:
    name: str
    description: str
    apply: Callable[[str], str]


def _eol(text: str) -> str:
    return text.replace("\r\n", "\n").replace("\r", "\n")


RULES: list[Rule] = [
    Rule("eol", "CRLF/CR line endings become LF (telnet framing, not content).", _eol),
    Rule("ansi", "ANSI colour/style escape sequences are removed. SharpMUSH colours object names "
                 "and headers on connections where PennMUSH sends plain text; colour negotiation is "
                 "not what this harness compares.",
         lambda t: _ANSI.sub("", t)),
    Rule("timestamp", "Absolute timestamps like 'Thu Sep 24 16:59:11 2026' become <TIMESTAMP>.",
         lambda t: _TIMESTAMP.sub("<TIMESTAMP>", t)),
    Rule("sync-token", "The harness's own sync sentinels never appear in transcripts; if one leaks "
                       "into a line it becomes <SYNC>.",
         lambda t: _TOKEN.sub("<SYNC>", t)),
    Rule("trailing-ws", "Trailing spaces/tabs on each line are removed; a trailing blank line is dropped.",
         lambda t: "\n".join(line.rstrip(" \t") for line in t.split("\n")).rstrip("\n")),
]


def normalize(text: str) -> str:
    for rule in RULES:
        text = rule.apply(text)
    return text


# `#12:1790269900000` is an objid: dbref plus creation time, which differs on every run.
_DBREF = re.compile(r"#(-?\d+)(:\d{9,})?")
_ROOM_NUMBER = re.compile(r"(?i)\b(room number )(\d+)")


class DbrefCanonicalizer:
    """Maps one server's dbrefs onto a comparable numbering (rule `dbref`).

    * Anchors: objects the world fixture creates. SharpMUSH's importer may number them differently
      (before #1107 PennMUSH's #3 became SharpMUSH's #16). Each side reports where
      every anchor lives; anchor dbrefs are rewritten to the PennMUSH dbref, the reference.
    * New objects: anything at or above `first_free` was created by a scenario. It becomes
      #NEW<k>, numbered by first appearance on that side, so creation order is compared but
      allocation policy is not.
    * Objids (`#12:1790269900000`) keep their form but the creation time becomes <CTIME>.
    * Any other dbref on a non-reference side (`foreign_tag`, e.g. "S" for SharpMUSH) is that
      server's own system object, not PennMUSH's object with the same number: SharpMUSH's #3 is
      not PennMUSH's #3 (Wiz). It becomes #S3, so it can never match by accident.
    The import offset itself is reported by the `import.anchors` check, not hidden.
    """

    def __init__(self, anchors: dict[int, int], first_free: int, foreign_tag: str = ""):
        self._anchors = anchors
        self._first_free = first_free
        self._foreign_tag = foreign_tag
        self._new: dict[int, int] = {}

    def _canon(self, n: int) -> str:
        if n in self._anchors:
            return str(self._anchors[n])
        if n >= self._first_free:
            return "NEW" + str(self._new.setdefault(n, len(self._new) + 1))
        return self._foreign_tag + str(n)

    def apply(self, text: str) -> str:
        text = _ROOM_NUMBER.sub(lambda m: m.group(1) + self._canon(int(m.group(2))), text)
        def one(m):
            if int(m.group(1)) < 0:
                return m.group(0)
            return "#" + self._canon(int(m.group(1))) + (":<CTIME>" if m.group(2) else "")
        return _DBREF.sub(one, text)
