"""Replays scenarios against one target (PennMUSH or SharpMUSH) and records transcripts."""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional

from .scenario import Scenario
from .session import Session, SessionError


@dataclass
class StepRecord:
    scenario: str
    case: str
    index: int              # step number within the case, from 0
    kind: str
    session: str
    command: str
    output: str = ""        # what the acting session received
    async_output: dict = field(default_factory=dict)  # other sessions' unsolicited output
    error: Optional[str] = None
    lineno: int = 0

    def key(self) -> str:
        return f"{self.scenario}/{self.case}#{self.index}"


class Target:
    def __init__(self, name: str, host: str, port: int, token_prefix: str):
        self.name, self.host, self.port, self.prefix = name, host, port, token_prefix


def run_scenario(target: Target, scenario: Scenario) -> tuple[list[StepRecord], dict]:
    sessions: dict[str, Session] = {}
    banners: dict[str, str] = {}
    records: list[StepRecord] = []
    try:
        for case in scenario.cases:
            for index, step in enumerate(case.steps):
                rec = StepRecord(scenario.name, case.id, index, step.kind, step.session, step.text,
                                 lineno=step.lineno)
                records.append(rec)
                try:
                    _execute(target, sessions, banners, step, rec)
                except SessionError as e:
                    rec.error = str(e)
                    # The stream position is unknown after a timeout; nothing later in this
                    # scenario can be trusted, so stop instead of comparing shifted output.
                    return records, banners
    finally:
        for s in sessions.values():
            s.close()
    return records, banners


def _execute(target, sessions, banners, step, rec):
    if step.kind in ("login", "login-fail"):
        s = Session(step.session, target.host, target.port, target.prefix)
        sessions[step.session] = s
        banners[step.session] = s.read_banner()
        s.send("connect " + step.text)
        if step.kind == "login":
            token = s.sync_start()
            rec.output = s.sync_finish(token)
            _sync_others(sessions, step.session, rec)
        else:
            rec.output = s.sync_prelogin()
        return
    session = sessions.get(step.session)
    if session is None:
        raise SessionError(f"no open session {step.session!r}")
    if step.kind == "logout":
        session.close()
        del sessions[step.session]
        _sync_others(sessions, step.session, rec)
        return
    if step.kind == "settle":
        rec.output = session.settle()
        _sync_others(sessions, step.session, rec)
        return
    session.send(step.text)
    # Barrier 1: the acting session's own sentinel returns only after its command has run.
    # PennMUSH executes one command per player per cycle, so sentinels sent to other sessions
    # at the same time could run *before* the command; they are therefore sent afterwards.
    rec.output = session.sync_finish(session.sync_start())
    _sync_others(sessions, step.session, rec)


def _sync_others(sessions, actor, rec):
    # Barrier 2: whatever the step wrote to other sessions is already on their streams and is
    # followed by their sentinel.
    others = {n: s for n, s in sessions.items() if n != actor}
    tokens = {n: s.sync_start() for n, s in others.items()}
    for n, s in others.items():
        text = s.sync_finish(tokens[n])
        if text:
            rec.async_output[n] = text
