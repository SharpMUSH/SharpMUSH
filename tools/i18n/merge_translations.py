#!/usr/bin/env python3
"""Splice translated batches back into a locale's resx.

Takes the JSON replies produced from extract_untranslated.py batches and writes
them into SharedResource.<locale>.resx, creating the file if it does not exist.

Refuses to write anything if any string in the input fails a pre-flight check —
partial writes across 28 batches are how a resx ends up half-broken with no clear
way back. Run validate_resx.py afterwards for the full gate set.

    python3 tools/i18n/merge_translations.py ru /tmp/ru/*.done.json

Reply format (the batch file with a "translated" field added per string):

    {"locale": "ru", "strings": [
       {"key": "RolPoseCount", "translated": "{count, plural, ...}",
        "comment": "optional translator note"}
    ]}
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import xml.etree.ElementTree as ET

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from validate_resx import placeholders  # noqa: E402  the gate and the pre-flight must agree

RES_DIR = os.path.join("SharpMUSH.Client", "Resources")
NEUTRAL = os.path.join(RES_DIR, "SharedResource.resx")

EMPTY_RESX = """<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype">
    <value>text/microsoft-resx</value>
  </resheader>
  <resheader name="version">
    <value>2.0</value>
  </resheader>
  <resheader name="reader">
    <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
  <resheader name="writer">
    <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
  </resheader>
</root>
"""


def esc(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def load(path):
    root = ET.parse(path).getroot()
    out = {}
    for d in root.findall("data"):
        v = d.find("value")
        out[d.get("name")] = v.text or "" if v is not None else ""
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("locale")
    ap.add_argument("replies", nargs="+", help="translated batch JSON files")
    ap.add_argument("--force", action="store_true",
                    help="overwrite keys that already have a translation")
    args = ap.parse_args()

    if not os.path.isfile(NEUTRAL):
        print(f"error: {NEUTRAL} not found — run from the repository root", file=sys.stderr)
        return 2

    neutral = load(NEUTRAL)
    target = os.path.join(RES_DIR, f"SharedResource.{args.locale}.resx")
    existing = load(target) if os.path.isfile(target) else {}

    incoming: dict[str, tuple[str, str | None]] = {}
    problems: list[str] = []

    for path in args.replies:
        try:
            payload = json.load(open(path, encoding="utf8"))
        except json.JSONDecodeError as e:
            problems.append(f"{path}: not valid JSON ({e})")
            continue
        if payload.get("locale") not in (None, args.locale):
            problems.append(f"{path}: declares locale {payload['locale']!r}, "
                            f"expected {args.locale!r}")
        for s in payload.get("strings", []):
            key, value = s.get("key"), s.get("translated")
            if not key:
                problems.append(f"{path}: entry with no key")
                continue
            if value is None or value == "":
                problems.append(f"{path}: {key}: no 'translated' value")
                continue
            if key not in neutral:
                problems.append(f"{path}: {key}: not a key in SharedResource.resx")
                continue
            if key in existing and not args.force:
                problems.append(f"{path}: {key}: already translated (pass --force to replace)")
                continue
            if key in incoming:
                problems.append(f"{path}: {key}: appears in more than one reply")
                continue
            # Argument names only. A naive \{(\w+) also matches the prose inside an ICU category
            # body — `one {Edited # time}` yields a bogus "Edited" — so a correct translation that
            # worded that branch differently was rejected as a placeholder mismatch. The guard that
            # was meant to exempt plurals never fired, because "plural" is not preceded by a brace
            # and so was never captured either. Share validate_resx.py's parser instead: the
            # pre-flight and the gate disagreeing about what a placeholder is helps nobody.
            want = placeholders(neutral[key])
            got = placeholders(value)
            if want != got:
                problems.append(f"{path}: {key}: placeholders {sorted(got)} "
                                f"!= English {sorted(want)}")
                continue
            incoming[key] = (value, s.get("comment"))

    if problems:
        print(f"{len(problems)} problem(s) — nothing written:\n", file=sys.stderr)
        for p in problems:
            print(f"  {p}", file=sys.stderr)
        return 1

    if not incoming:
        print("nothing to merge")
        return 0

    if not os.path.isfile(target):
        with open(target, "w", encoding="utf8") as fh:
            fh.write(EMPTY_RESX)
        print(f"created {target}")

    src = open(target, encoding="utf8").read()
    blocks = []
    for key in sorted(incoming):
        value, comment = incoming[key]
        block = f'  <data name="{key}" xml:space="preserve">\n    <value>{esc(value)}</value>\n'
        if comment:
            block += f"    <comment>{esc(comment)}</comment>\n"
        block += "  </data>"
        blocks.append(block)
    src = src.replace("</root>", "\n".join(blocks) + "\n</root>")
    open(target, "w", encoding="utf8").write(src)

    ET.parse(target)  # fail loudly rather than leave malformed XML on disk
    print(f"merged {len(incoming)} strings into {target}")
    print(f"now run: python3 tools/i18n/validate_resx.py --locale {args.locale}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
