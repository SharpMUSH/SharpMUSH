"""Offline tests for the harness itself (no servers): python3 -m unittest discover -s tools/parity/tests"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from parity import compare, scenario
from parity.normalize import DbrefCanonicalizer, normalize
from parity.runner import StepRecord, _execute
from parity.scenario import Step
from parity.session import split_telnet, strip_telnet


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


class CompareTests(unittest.TestCase):
    def rec(self, out, idx=0):
        return StepRecord("s", "c", idx, "command", "a", "think x", output=out)

    def canon(self):
        return DbrefCanonicalizer({}, 1000)

    def run_compare(self, p, s, allow=(), baseline=()):
        return compare.compare([p], [s], self.canon(), self.canon(), list(allow), [], [], frozenset(baseline))

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


if __name__ == "__main__":
    unittest.main()
