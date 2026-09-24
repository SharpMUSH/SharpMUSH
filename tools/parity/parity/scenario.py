"""Scenario files: one command per line, `::directive` lines for the harness.

    # comment
    ::case wiz.think   Wizard can think
    ::login wiz Wiz wizpass       open session 'wiz' by connecting as Wiz (must succeed)
    ::login-fail bad Wiz nope     open a session whose login is expected to be refused
    think add(1,2)                sent verbatim on the current session
    ::as alice                    switch the current session
    ::settle                      wait for queued work (@wait/@trigger) to finish
    ::logout wiz

Directives use `::` because every MUSH command starts with a word, `@`, `+` or `&` and none can
start with `::` on either server.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Step:
    kind: str            # login | login-fail | command | settle | logout
    text: str = ""       # command text, or "name character password" for login
    session: str = ""
    lineno: int = 0


@dataclass
class Case:
    id: str
    description: str
    steps: list[Step] = field(default_factory=list)


@dataclass
class Scenario:
    name: str
    path: Path
    cases: list[Case]


class ScenarioError(ValueError):
    pass


def load(path: Path) -> Scenario:
    cases: list[Case] = []
    current_session = ""
    for lineno, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.rstrip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("::"):
            parts = line[2:].split(None, 1)
            directive, rest = parts[0], (parts[1] if len(parts) > 1 else "")
            if directive == "case":
                cid, _, desc = rest.partition(" ")
                if not cid:
                    raise ScenarioError(f"{path}:{lineno}: ::case needs an id")
                cases.append(Case(cid, desc.strip()))
                continue
            if not cases:
                raise ScenarioError(f"{path}:{lineno}: ::{directive} before any ::case")
            steps = cases[-1].steps
            if directive == "login":
                bits = rest.split()
                if len(bits) < 2:
                    raise ScenarioError(f"{path}:{lineno}: ::login <session> <character> [password]")
                current_session = bits[0]
                steps.append(Step("login", " ".join(bits[1:]), bits[0], lineno))
            elif directive == "login-fail":
                bits = rest.split()
                if len(bits) < 2:
                    raise ScenarioError(f"{path}:{lineno}: ::login-fail <session> <character> [password]")
                current_session = bits[0]
                steps.append(Step("login-fail", " ".join(bits[1:]), bits[0], lineno))
            elif directive == "as":
                current_session = rest.strip()
            elif directive == "settle":
                steps.append(Step("settle", "", current_session, lineno))
            elif directive == "logout":
                steps.append(Step("logout", "", rest.strip() or current_session, lineno))
            else:
                raise ScenarioError(f"{path}:{lineno}: unknown directive ::{directive}")
            continue
        if not cases:
            raise ScenarioError(f"{path}:{lineno}: command before any ::case")
        if not current_session:
            raise ScenarioError(f"{path}:{lineno}: command with no session; use ::login first")
        cases[-1].steps.append(Step("command", line, current_session, lineno))
    return Scenario(path.stem, path, cases)
