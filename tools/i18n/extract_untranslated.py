#!/usr/bin/env python3
"""Emit the untranslated keys for a locale as batches ready to hand to an LLM.

Output is JSON so the reply can be validated and spliced back mechanically. Each
entry carries the key, the English value, the resx <comment> if there is one, and
the placeholder names the translation must preserve — because "keep {0}" stated
per-string is far more reliable than stated once in a system prompt.

    # what still needs doing
    python3 tools/i18n/extract_untranslated.py ru --stats

    # batches of 40 into a directory
    python3 tools/i18n/extract_untranslated.py ru --batch-size 40 --out /tmp/ru

    # a whole new locale (no resx yet) is the same command
    python3 tools/i18n/extract_untranslated.py pl --batch-size 40 --out /tmp/pl

Splice replies back with merge_translations.py.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

RES_DIR = os.path.join("SharpMUSH.Client", "Resources")
NEUTRAL = os.path.join(RES_DIR, "SharedResource.resx")
PLACEHOLDER = re.compile(r"\{(\w+)")

# Prefix -> which portal surface a key belongs to. Gives the model register
# context ("admin tooling" reads differently from "player-facing chrome") and
# lets you translate the surfaces players see first.
SURFACES = {
    "Auth": "player-facing: sign-in, registration, account",
    "Nav": "player-facing: navigation, settings, mail, play",
    "Rol": "player-facing: scenes and character profiles / staff: roles",
    "Wk": "mixed: wiki authoring and media library",
    "Wid": "player-facing: dashboard widgets",
    "Term": "player-facing: in-browser telnet terminal and softcode editor",
    "Res": "mixed: wiki, scenes, config leftovers",
    "WidgetZone": "staff: layout editor zone names",
    "Adm": "staff: admin pages and dashboard",
    "Lay": "staff: layout and application administration",
    "Pkg": "staff: softcode package manager",
    "Enum": "mixed: permission labels and enum display names",
}


def load(path):
    root = ET.parse(path).getroot()
    out = {}
    for d in root.findall("data"):
        v, c = d.find("value"), d.find("comment")
        out[d.get("name")] = (v.text or "" if v is not None else "",
                              c.text if c is not None else None)
    return out


def surface_of(key: str) -> str:
    for p in sorted(SURFACES, key=len, reverse=True):
        if key.startswith(p):
            return SURFACES[p]
    return "staff: MUSH server configuration"


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("locale", help="BCP-47 tag, e.g. ru, pl, pt-BR, zh-Hans")
    ap.add_argument("--batch-size", type=int, default=40)
    ap.add_argument("--out", help="directory for batch files (default: stdout)")
    ap.add_argument("--stats", action="store_true", help="counts only")
    ap.add_argument("--surface", help="only keys whose surface description contains this")
    args = ap.parse_args()

    if not os.path.isfile(NEUTRAL):
        print(f"error: {NEUTRAL} not found — run from the repository root", file=sys.stderr)
        return 2

    neutral = load(NEUTRAL)
    target = os.path.join(RES_DIR, f"SharedResource.{args.locale}.resx")
    existing = load(target) if os.path.isfile(target) else {}

    todo = [k for k in neutral if k not in existing]
    if args.surface:
        todo = [k for k in todo if args.surface.lower() in surface_of(k).lower()]
    todo.sort(key=lambda k: (surface_of(k), k))

    if args.stats:
        print(f"locale        {args.locale}")
        print(f"resx          {'exists' if existing else 'does not exist yet'}")
        print(f"english keys  {len(neutral)}")
        print(f"translated    {len(existing)}")
        print(f"untranslated  {len(todo)}")
        by = {}
        for k in todo:
            by[surface_of(k)] = by.get(surface_of(k), 0) + 1
        print("\nuntranslated by surface:")
        for s, n in sorted(by.items(), key=lambda kv: -kv[1]):
            print(f"  {n:5}  {s}")
        print(f"\n{-(-len(todo) // args.batch_size)} batches of {args.batch_size}")
        return 0

    batches = [todo[i:i + args.batch_size] for i in range(0, len(todo), args.batch_size)]
    for idx, batch in enumerate(batches, 1):
        payload = {
            "locale": args.locale,
            "batch": idx,
            "of": len(batches),
            "instructions_file": "docs/localization/ai-translation.md",
            "strings": [
                {
                    "key": k,
                    "english": neutral[k][0],
                    "context": neutral[k][1],
                    "surface": surface_of(k),
                    "placeholders": sorted(set(PLACEHOLDER.findall(neutral[k][0]))),
                }
                for k in batch
            ],
        }
        text = json.dumps(payload, ensure_ascii=False, indent=2)
        if args.out:
            os.makedirs(args.out, exist_ok=True)
            path = os.path.join(args.out, f"{args.locale}.{idx:03}.json")
            with open(path, "w", encoding="utf8") as fh:
                fh.write(text + "\n")
            print(path)
        else:
            print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
