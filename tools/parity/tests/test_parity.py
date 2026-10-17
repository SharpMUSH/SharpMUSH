"""Offline tests for the harness itself (no servers): python3 -m unittest discover -s tools/parity/tests"""
import json
import sys
import tempfile
import unittest
import urllib.error
import urllib.request
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from parity import compare, scenario
from parity.echo import EchoSite
from parity.normalize import DbrefCanonicalizer, normalize
from parity.runner import StepRecord, Target, _execute
from parity.scenario import Step
from parity.session import Session, split_telnet, strip_telnet
from parity.servers import StartupError, wait_for, finish_import_preview, verify_import_committed


class StartupDiagnosticsTests(unittest.TestCase):
    def failure(self, text):
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "server.out"
            log.write_text(text, encoding="utf-8")
            process = SimpleNamespace(log=log, returncode=134, poll=lambda: 134)
            with self.assertRaises(StartupError) as failure:
                wait_for(lambda: False, "import", 1, [process])
            return str(failure.exception)

    def test_lmdb_startup_failure_exposes_code_without_dumping_log_values(self):
        failure = self.failure("client_secret=must-never-print\nUnhandled exception. LightningDB -30791: MDB_DBS_FULL: Environment maxdbs limit reached\n")
        self.assertIn("MDB_DBS_FULL", failure)
        self.assertNotIn("must-never-print", failure)
        self.assertNotIn("Environment maxdbs", failure)

    def test_dotnet_startup_failure_exposes_type_without_exception_message(self):
        failure = self.failure("Unhandled exception. System.InvalidOperationException: invalid token must-never-print\n")
        self.assertIn("System.InvalidOperationException", failure)
        self.assertNotIn("must-never-print", failure)

    def test_missing_log_preserves_original_startup_failure(self):
        process = SimpleNamespace(log=Path("missing-startup-log"), returncode=134, poll=lambda: 134)
        with self.assertRaisesRegex(StartupError, "process exited with 134"):
            wait_for(lambda: False, "import", 1, [process])


class ImportRestartTests(unittest.TestCase):
    def fixture(self, directory, state=4, marker=True):
        world = Path(directory) / "world"
        root = Path(str(world) + ".imports")
        session_id = "a" * 32
        session = root / session_id
        (session / "world").mkdir(parents=True)
        (session / "keys").mkdir()
        (session / "session.json").write_text(json.dumps({"Id": session_id, "State": state}))
        if marker:
            (root / "pending.json").write_text(json.dumps(session_id))
        return world, root, session_id

    def test_clean_exit_requires_an_actual_durable_promotion_intent(self):
        with tempfile.TemporaryDirectory() as directory:
            world, _, session_id = self.fixture(directory)
            process = SimpleNamespace(returncode=0, poll=lambda: 0)
            self.assertEqual(finish_import_preview(process, world), session_id)

    def test_clean_exit_without_intent_cannot_fake_success(self):
        with tempfile.TemporaryDirectory() as directory:
            world, _, _ = self.fixture(directory, marker=False)
            process = SimpleNamespace(returncode=0, poll=lambda: 0)
            with self.assertRaisesRegex(StartupError, "without a valid durable"):
                finish_import_preview(process, world)

    def test_failure_exit_does_not_restart_even_with_an_intent_file(self):
        with tempfile.TemporaryDirectory() as directory:
            world, _, _ = self.fixture(directory)
            process = SimpleNamespace(returncode=17, poll=lambda: 17)
            with self.assertRaisesRegex(StartupError, "exited with 17"):
                finish_import_preview(process, world)

    def test_aborted_session_is_not_restart_authority(self):
        with tempfile.TemporaryDirectory() as directory:
            world, _, _ = self.fixture(directory, state=7)
            process = SimpleNamespace(returncode=0, poll=lambda: 0)
            with self.assertRaises(StartupError):
                finish_import_preview(process, world)

    def test_restart_must_finish_commit_without_another_pending_operation(self):
        with tempfile.TemporaryDirectory() as directory:
            world, root, session_id = self.fixture(directory, state=6)
            with self.assertRaises(StartupError):
                verify_import_committed(world, session_id)
            (root / "pending.json").unlink()
            verify_import_committed(world, session_id)

    def test_a_second_promotion_request_is_not_a_committed_engine(self):
        with tempfile.TemporaryDirectory() as directory:
            world, root, session_id = self.fixture(directory, state=4)
            (root / "pending.json").unlink()
            with self.assertRaises(StartupError):
                verify_import_committed(world, session_id)


class NormalizeTests(unittest.TestCase):
    def test_ansi_eol_and_trailing_whitespace(self):
        self.assertEqual(normalize("\x1b[1;37mRoom\x1b[0m  \r\nnext\r\n\r\n"), "Room\nnext")

    def test_timestamp(self):
        self.assertEqual(normalize("at Thu Sep 24 16:59:11 2026."), "at <TIMESTAMP>.")

    def test_other_text_is_untouched(self):
        self.assertEqual(normalize("a  b\tc"), "a  b\tc")

    def test_a_sequence_split_across_reads_is_completed_by_the_next_read(self):
        text, rest = split_telnet(b"ab\xff\xfb")
        self.assertEqual((text, rest), (b"ab", b"\xff\xfb"))
        self.assertEqual(split_telnet(rest + b"\xc9cd"), (b"cd", b""))

    def test_an_escaped_iac_is_stripped_once(self):
        # Stripped text must not be stripped again: 0xFF 0xFF is one data byte, not an IAC.
        text, _ = split_telnet(b"x\xff\xff\xfby")
        self.assertEqual(text, b"x\xff\xfby")

    def test_telnet_negotiation_is_stripped(self):
        self.assertEqual(strip_telnet(b"\xff\xfb\xc9hi\xff\xfa\x18\x00x\xff\xf0!\xff\xff"), b"hi!\xff")


class DbrefTests(unittest.TestCase):
    def test_anchors_map_to_reference_and_new_objects_are_numbered_by_appearance(self):
        c = DbrefCanonicalizer({16: 3, 17: 4}, first_free=25)
        self.assertEqual(c.apply("Wiz(#16) Alice(#17) made #30 and #27 then #30 again"),
                         "Wiz(#3) Alice(#4) made #NEW1 and #NEW2 then #NEW1 again")

    def test_objid_keeps_its_form_but_not_its_ctime(self):
        c = DbrefCanonicalizer({19: 6}, first_free=30)
        self.assertEqual(c.apply("#19:1790269900000 #6"), "#6:<CTIME> #6")

    def test_system_dbrefs_and_errors_are_untouched(self):
        c = DbrefCanonicalizer({}, first_free=30)
        self.assertEqual(c.apply("#0 #1 #-1"), "#0 #1 #-1")

    def test_a_non_reference_sides_own_system_objects_are_tagged(self):
        # SharpMUSH's #3 is one of its system objects, not PennMUSH's #3 (Wiz, anchored at #16).
        c = DbrefCanonicalizer({16: 3, 0: 0}, first_free=30, foreign_tag="S")
        self.assertEqual(c.apply("#16 #3 #0 #31 #-1"), "#3 #S3 #0 #NEW1 #-1")

    def test_room_number_text(self):
        c = DbrefCanonicalizer({}, first_free=30)
        self.assertEqual(c.apply("Foo created with room number 31."), "Foo created with room number NEW1.")


class ScenarioTests(unittest.TestCase):
    def load(self, text):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "x.scn"
            p.write_text(text)
            return scenario.load(p)

    def test_parse(self):
        s = self.load("# c\n::case a.b desc here\n::login w Wiz pw\nthink hi\n::as w\n::settle\n")
        self.assertEqual([st.kind for st in s.cases[0].steps], ["login", "command", "settle"])
        self.assertEqual(s.cases[0].description, "desc here")
        self.assertEqual(s.cases[0].steps[1].session, "w")

    def test_duplicate_case_ids_are_rejected(self):
        # Step keys are scenario/case#index, so a repeated id would pair the wrong steps.
        with self.assertRaises(scenario.ScenarioError):
            self.load("::case a\n::login w Wiz x\n::case a\n::login w Wiz x\n")

    def test_command_before_login_is_an_error(self):
        with self.assertRaises(scenario.ScenarioError):
            self.load("::case a\nthink hi\n")

    def test_until_applies_to_the_next_command_only(self):
        s = self.load("::case a\n::login w Wiz pw\n::until DONE\n@http me/A={{echo}}\nthink hi\n")
        self.assertEqual([st.until for st in s.cases[0].steps], ["", "DONE", ""])

    def test_until_needs_a_command_after_it(self):
        with self.assertRaises(scenario.ScenarioError):
            self.load("::case a\n::login w Wiz pw\n::until DONE\n")


class CompareTests(unittest.TestCase):
    def rec(self, out, idx=0):
        return StepRecord("s", "c", idx, "command", "a", "think x", output=out)

    def canon(self):
        return DbrefCanonicalizer({}, 1000)

    def run_compare(self, p, s, allow=(), baseline=()):
        return compare.compare([p], [s], self.canon(), self.canon(), list(allow), [], [], frozenset(baseline))

    def test_new_dbref_numbering_restarts_per_scenario(self):
        # Scenario "a" shows a created object only on PennMUSH; scenario "b" must still match.
        def rec(scn, out):
            return StepRecord(scn, "c", 0, "command", "a", "think x", output=out)
        p = [rec("a", "#1001"), rec("b", "#1002 #1003")]
        s = [rec("a", ""), rec("b", "#1502 #1503")]
        r, _, _ = compare.compare(p, s, self.canon(), self.canon(), [], [], [], frozenset())
        self.assertEqual([x.status for x in r], [compare.DIFF, compare.MATCH])
        self.assertEqual(r[1].sharp_text, "#NEW1 #NEW2")

    def test_objects_from_an_earlier_scenario_keep_their_identity(self):
        # Both sides create two objects in "a"; in "b" SharpMUSH names the wrong one of them.
        def rec(scn, out):
            return StepRecord(scn, "c", 0, "command", "a", "think x", output=out)
        p = [rec("a", "#1001 #1002"), rec("b", "#1001 #1003")]
        s = [rec("a", "#1501 #1502"), rec("b", "#1502 #1503")]
        r, _, _ = compare.compare(p, s, self.canon(), self.canon(), [], [], [], frozenset())
        self.assertEqual([x.status for x in r], [compare.MATCH, compare.DIFF])
        self.assertEqual((r[1].penn_text, r[1].sharp_text), ("#NEW1@a #NEW1", "#NEW2@a #NEW1"))

    def test_match_ignores_colour(self):
        r, stale, _ = self.run_compare(self.rec("Room\r\n"), self.rec("\x1b[1mRoom\x1b[0m\r\n"))
        self.assertEqual(r[0].status, compare.MATCH)

    def test_difference_is_unexpected_by_default(self):
        r, _, _ = self.run_compare(self.rec("1"), self.rec("2"))
        self.assertEqual(r[0].status, compare.DIFF)
        self.assertIn("-1", r[0].diff)

    def test_allowlisted_difference_is_known_and_a_fixed_one_is_stale(self):
        entry = compare.Entry("KD-1", "s", "c", None, "why", "#1134", profile="p")
        r, stale, _ = self.run_compare(self.rec("1"), self.rec("2"), [entry])
        self.assertEqual((r[0].status, stale), (compare.KNOWN, []))
        r, stale, _ = self.run_compare(self.rec("1"), self.rec("1"), [entry])
        self.assertEqual((r[0].status, [e.id for e in stale]), (compare.MATCH, ["KD-1"]))

    def test_an_entry_with_sharp_accepts_that_output_and_nothing_else(self):
        entry = compare.Entry("KD-1", "s", "c", None, "why", "#1134", profile="p", sharp="1, 2", penn="1")
        r, stale, _ = self.run_compare(self.rec("1"), self.rec("1, 2"), [entry])
        self.assertEqual((r[0].status, stale), (compare.KNOWN, []))
        # Any other difference is still one, and the entry is not stale while it waits.
        r, stale, _ = self.run_compare(self.rec("1"), self.rec("1, 3"), [entry])
        self.assertEqual((r[0].status, stale), (compare.DIFF, []))
        r, stale, fixed = self.run_compare(self.rec("1"), self.rec("1, 3"), [entry], baseline=["s/c#0"])
        self.assertEqual((r[0].status, stale, fixed), (compare.OPEN, [], []))
        # A PennMUSH that answers differently is not the pinned difference either.
        r, _, _ = self.run_compare(self.rec("0"), self.rec("1, 2"), [entry])
        self.assertEqual(r[0].status, compare.DIFF)
        # Once only the accepted difference is left, the baseline entry has to go.
        r, _, fixed = self.run_compare(self.rec("1"), self.rec("1, 2"), [entry], baseline=["s/c#0"])
        self.assertEqual((r[0].status, fixed), (compare.KNOWN, ["s/c#0"]))

    def test_sharp_and_penn_are_pinned_together(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "k.json"
            p.write_text(json.dumps({"entries": [{"id": "KD-1", "scenario": "s", "case": "c", "reason": "r",
                                                  "tracking": "#1134", "profile": "p", "sharp": "x"}]}))
            prof = Path(d) / "p.md"
            prof.write_text("## p\n")
            with self.assertRaises(ValueError):
                compare.load_allowlist(p, prof)

    def test_baseline_marks_open_gaps_and_reports_fixed_ones(self):
        r, _, fixed = self.run_compare(self.rec("1"), self.rec("2"), baseline=["s/c#0"])
        self.assertEqual((r[0].status, fixed), (compare.OPEN, []))
        r, _, fixed = self.run_compare(self.rec("1"), self.rec("1"), baseline=["s/c#0"])
        self.assertEqual((r[0].status, fixed), (compare.MATCH, ["s/c#0"]))

    def test_entries_whose_step_moved_are_orphaned(self):
        results, _, _ = self.run_compare(self.rec("1"), self.rec("2"))
        self.assertEqual(compare.orphaned({"s/c#0": "think x"}, [], results), [])
        # A step inserted before it: the key now points at another command.
        self.assertEqual(compare.orphaned({"s/c#0": "think y"}, [], results),
                         ["baseline `s/c#0` was `think y`, now `think x`"])
        self.assertEqual(compare.orphaned({"s/c#1": "think x"}, [], results),
                         ["baseline `s/c#1` was `think x`, now missing"])
        entry = compare.Entry("KD-1", "s", "c", 0, "why", "#1134", "think y")
        self.assertEqual(len(compare.orphaned({}, [entry], results)), 1)
        # Scenarios that did not run are not judged.
        self.assertEqual(compare.orphaned({"other/c#0": "think y"}, [], results), [])

    def test_step_specific_allowlist_entries_need_their_command(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "k.json"
            p.write_text(json.dumps({"entries": [{"id": "KD-1", "scenario": "s", "case": "c", "step": 0,
                                                  "reason": "r", "tracking": "#1134"}]}))
            with self.assertRaises(ValueError):
                compare.load_allowlist(p)

    def test_error_is_never_a_match(self):
        bad = self.rec("")
        bad.error = "timed out"
        r, _, _ = self.run_compare(self.rec("x"), bad)
        self.assertEqual(r[0].status, compare.ERROR)

    def test_allowlist_entries_name_a_profile_entry_that_exists(self):
        with tempfile.TemporaryDirectory() as d:
            profile, p = Path(d) / "profile.md", Path(d) / "k.json"
            profile.write_text("# SECTION\n## A choice\ntext\n")
            entry = {"id": "KD-1", "scenario": "s", "case": "c", "reason": "r", "tracking": "#1134"}
            for missing in ({}, {"profile": "Not a heading"}, {"profile": "SECTION"}):
                p.write_text(json.dumps({"entries": [{**entry, **missing}]}))
                with self.assertRaises(ValueError, msg=missing):
                    compare.load_allowlist(p, profile)
            p.write_text(json.dumps({"entries": [{**entry, "profile": "A choice"}]}))
            self.assertEqual(compare.load_allowlist(p, profile)[0].profile, "A choice")

    def test_the_shipped_allowlist_names_real_profile_entries(self):
        allowlist = compare.load_allowlist(Path(__file__).resolve().parent.parent / "known-differences.json")
        self.assertTrue(allowlist)
        self.assertIn("`render()` markup compatibility", compare.profile_headings())

    def test_declared_profile_uses_section_metadata_across_articles(self):
        with tempfile.TemporaryDirectory() as d:
            def article(name, identity, headings):
                path = Path(d) / name
                metadata = {"corpus": "help", "id": identity,
                            "sections": [{"heading": heading} for heading in headings]}
                path.write_text("<!-- help-article\n" + json.dumps(metadata) + "\n-->\n# Title\n"
                                "```sharp\n## Not an entry\n```\n")
                return path
            profile = article("overview.md", "pennmush-compatibility", [])
            article("arbitrary-name.md", "compatibility-economy", ["Money policy"])
            article("unrelated.md", "other-guide", ["Not a compatibility choice"])
            self.assertEqual(compare.profile_headings(profile), {"Money policy"})

    def test_allowlist_entries_need_a_reason_and_tracking(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "k.json"
            p.write_text(json.dumps({"entries": [{"id": "KD-1", "scenario": "s", "case": "c"}]}))
            with self.assertRaises(ValueError):
                compare.load_allowlist(p)


class RunnerTests(unittest.TestCase):
    class FakeSession:
        def __init__(self, logged_in):
            self.logged_in, self.calls = logged_in, []

        def settle(self):
            self.calls.append("settle")
            return "settled"

        def sync_prelogin(self):
            self.calls.append("prelogin")
            return "prelogin"

        def send(self, line):
            self.calls.append(f"send {line}")

        def read_line_ending(self, text):
            self.calls.append(f"until {text}")
            return f"late {text}\n"

        def sync_start(self):
            self.calls.append("sync")
            return "t"

        def sync_finish(self, token):
            return "after\n"

    def test_settle_before_login_uses_the_prelogin_sentinel(self):
        bad = self.FakeSession(logged_in=False)
        rec = StepRecord("s", "c", 0, "settle", "bad", "")
        _execute(None, {"bad": bad}, {}, Step(kind="settle", session="bad"), rec)
        self.assertEqual(bad.calls, ["prelogin"])
        self.assertEqual(rec.output, "prelogin")

    def test_settle_after_login_waits_on_the_queue(self):
        wiz = self.FakeSession(logged_in=True)
        rec = StepRecord("s", "c", 0, "settle", "wiz", "")
        _execute(None, {"wiz": wiz}, {}, Step(kind="settle", session="wiz"), rec)
        self.assertEqual(wiz.calls, ["settle"])

    def test_until_reads_late_output_before_the_sentinel_and_echo_is_filled_in(self):
        wiz = self.FakeSession(logged_in=True)
        rec = StepRecord("s", "c", 0, "command", "wiz", "@http me/A={{echo}}/get")
        target = Target("t", "127.0.0.1", 1, "P", "http://127.0.0.1:9")
        _execute(target, {"wiz": wiz}, {}, Step("command", "@http me/A={{echo}}/get", "wiz", until="DONE"), rec)
        self.assertEqual(wiz.calls, ["send @http me/A=http://127.0.0.1:9/get", "until DONE", "sync"])
        self.assertEqual(rec.output, "late DONE\nafter\n")


class TriggerCompletionTests(unittest.TestCase):
    class ControlledDelivery:
        """Release queued say output after the first Wiz receive, independently of peer commands.

        Without an explicit witness that receive includes Wiz's foreground token. With a witness
        it contains only the trigger acknowledgement. Both schedules then deliver the same say to
        both clients; only socket I/O is replaced, leaving Session parsing and runner sealing real.
        """
        def __init__(self):
            self.buffers = {"wiz": bytearray(), "alice": bytearray()}
            self.pending_say = False
            self.connections = iter((self.Socket(self, "wiz"), self.Socket(self, "alice")))

        def connect(self, address, timeout):
            return next(self.connections)

        class Socket:
            def __init__(self, delivery, name):
                self.delivery, self.name = delivery, name

            def setblocking(self, value):
                pass

            def settimeout(self, value):
                pass

            def sendall(self, data):
                command = data.decode().removesuffix("\r\n")
                if self.name == "wiz" and command == "@trigger *Alice/TRIG=1":
                    self.delivery.buffers["wiz"].extend(b"Alice - Triggered.\r\n")
                    self.delivery.pending_say = True
                elif command.startswith("think "):
                    self.delivery.buffers[self.name].extend((command[6:] + "\r\n").encode())
                elif command.startswith("@wait 0=think "):
                    self.delivery.buffers[self.name].extend((command[14:] + "\r\n").encode())
                else:
                    raise AssertionError(f"unexpected controlled command: {command!r}")

            def recv(self, count):
                buffer = self.delivery.buffers[self.name]
                if not buffer:
                    raise AssertionError(f"unexpected read without controlled output: {self.name}")
                chunk = bytes(buffer[:count])
                del buffer[:count]
                if self.name == "wiz" and self.delivery.pending_say:
                    self.delivery.pending_say = False
                    self.delivery.buffers["wiz"].extend(b'Alice says, "triggered 1"\r\n')
                    self.delivery.buffers["alice"].extend(b'You say, "triggered 1"\r\n')
                return chunk

    def replay_trigger(self, step):
        delivery = self.ControlledDelivery()
        with patch("parity.session.socket.create_connection", side_effect=delivery.connect):
            sessions = {name: Session(name, "controlled", 1, "TEST") for name in ("wiz", "alice")}
        for session in sessions.values():
            session.logged_in = True
        record = StepRecord("20-admin-commands", "admin.force", 2, "command", "wiz", step.text)
        _execute(Target("controlled", "controlled", 1, "TEST"), sessions, {}, step, record)
        following = StepRecord("20-admin-commands", "admin.force", 3, "settle", "wiz", "")
        _execute(None, sessions, {}, Step("settle", "", "wiz"), following)
        return record, following

    def test_foreground_token_alone_can_split_a_queued_say_across_adjacent_steps(self):
        record, following = self.replay_trigger(Step("command", "@trigger *Alice/TRIG=1", "wiz"))
        self.assertEqual(record.output, "Alice - Triggered.\r\n")
        self.assertEqual(record.async_output, {"alice": 'You say, "triggered 1"\r\n'})
        self.assertEqual(following.output, 'Alice says, "triggered 1"\r\n')
        self.assertEqual(following.async_output, {})

    def test_admin_trigger_completion_keeps_both_clients_output_in_the_trigger_step(self):
        path = Path(__file__).resolve().parent.parent / "scenarios" / "20-admin-commands.scn"
        loaded = scenario.load(path)
        case = next(case for case in loaded.cases if case.id == "admin.force")
        record, following = self.replay_trigger(case.steps[2])
        self.assertEqual(record.output, 'Alice - Triggered.\r\nAlice says, "triggered 1"\r\n')
        self.assertEqual(record.async_output, {"alice": 'You say, "triggered 1"\r\n'})
        self.assertEqual(following.output, "")
        self.assertEqual(following.async_output, {})


class EchoSiteTests(unittest.TestCase):
    def test_describes_what_the_request_chose(self):
        site = EchoSite().start()
        try:
            req = urllib.request.Request(site.url + "/post", data=b"a=1", method="POST",
                                         headers={"Content-Type": "text/csv", "X-B": "2", "X-A": "1"})
            with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(req) as reply:
                self.assertEqual(reply.read().decode(), "POST type=text/csv auth=- x-a=1 x-b=2 body=a=1")
        finally:
            site.stop()

    def test_auth_asks_for_a_login(self):
        site = EchoSite().start()
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            with self.assertRaises(urllib.error.HTTPError) as refused:
                opener.open(site.url + "/auth")
            self.assertEqual(refused.exception.code, 401)
            self.assertEqual(refused.exception.headers["WWW-Authenticate"], 'Basic realm="parity"')
        finally:
            site.stop()


if __name__ == "__main__":
    unittest.main()
