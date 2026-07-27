#!/usr/bin/env python3
"""Validate the portal's resource files.

Machine-checkable invariants only. These are the gates that make an LLM-drafted
translation trustworthy without a native reviewer reading all 1097 strings:
a dropped placeholder, a stray plural category or an untranslated block are all
mechanically detectable, and they are also the failures an LLM actually makes.

    python3 tools/i18n/validate_resx.py                     # all locales
    python3 tools/i18n/validate_resx.py --locale ru pl      # some locales
    python3 tools/i18n/validate_resx.py --list-count-bearing
    python3 tools/i18n/validate_resx.py --max-length-ratio 2.5

Exit status is non-zero if any gate fails, so this drops straight into CI.
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

RES_DIR = os.path.join("SharpMUSH.Client", "Resources")
NEUTRAL = os.path.join(RES_DIR, "SharedResource.resx")

# CLDR plural categories per target locale. A locale may use FEWER categories
# than listed (a translator may collapse), never more.
CLDR_CATEGORIES = {
    "en": {"one", "other"},
    "de": {"one", "other"},
    "es": {"one", "many", "other"},
    "fr": {"one", "many", "other"},
    "nl": {"one", "other"},
    "sv": {"one", "other"},
    "da": {"one", "other"},
    "nb": {"one", "other"},
    "bg": {"one", "other"},
    "hu": {"one", "other"},
    "pt-BR": {"one", "many", "other"},
    "ro": {"one", "few", "other"},
    "hr": {"one", "few", "other"},
    "ru": {"one", "few", "many", "other"},
    "pl": {"one", "few", "many", "other"},
    "zh-Hans": {"other"},
}

# Values that are identical across languages on purpose: brand, protocol and
# MUSH terms of art. Keeps the "looks untranslated" gate from crying wolf.
DO_NOT_TRANSLATE_EXACT = {
    "AppTitle",
}

PLACEHOLDER = re.compile(r"\{(\w+)")
ICU_PLURAL = re.compile(r"\{\s*(\w+)\s*,\s*plural\s*,(.*)\}\s*$", re.S)
ICU_CATEGORY = re.compile(r"(?:^|\s)(zero|one|two|few|many|other|=\d+)\s*\{")
LEGACY_PLURAL = re.compile(r"\(s\)|\(es\)")


def load(path: str) -> dict[str, tuple[str, str | None]]:
    """name -> (value, comment). Empty file or missing <value> is a hard error."""
    root = ET.parse(path).getroot()
    out: dict[str, tuple[str, str | None]] = {}
    for d in root.findall("data"):
        name = d.get("name")
        if name is None:
            raise SystemExit(f"{path}: <data> with no name attribute")
        value_el = d.find("value")
        comment_el = d.find("comment")
        out[name] = (
            value_el.text or "" if value_el is not None else "",
            comment_el.text if comment_el is not None else None,
        )
    return out


def locale_of(path: str) -> str:
    """SharedResource.pt-BR.resx -> pt-BR"""
    stem = os.path.basename(path)[len("SharedResource.") : -len(".resx")]
    return stem or "en"


def categories(value: str) -> set[str] | None:
    """Plural categories used by an ICU MessageFormat value, or None if not one."""
    m = ICU_PLURAL.search(value.strip())
    if not m:
        return None
    return {c for c in ICU_CATEGORY.findall(m.group(2)) if not c.startswith("=")}


def check_locale(neutral: dict, path: str, max_ratio: float) -> tuple[list[str], list[str]]:
    """Returns (hard failures, advisories).

    Hard failures are invariants: a violation is always a defect. Advisories are
    heuristics that produce real false positives — "identical to English" fires on
    every French/Spanish cognate, of which there are many — so they are reported
    but do not fail CI unless --strict is passed.
    """
    loc = locale_of(path)
    tr = load(path)
    allowed = CLDR_CATEGORIES.get(loc)
    fails: list[str] = []
    notes: list[str] = []

    def fail(msg: str) -> None:
        fails.append(f"{loc}: {msg}")

    def note(msg: str) -> None:
        notes.append(f"{loc}: {msg}")

    # 1. No key that the neutral file doesn't have. A stale key is dead weight
    #    that no test will ever exercise.
    for k in sorted(set(tr) - set(neutral)):
        fail(f"{k}: present in translation but not in SharedResource.resx")

    # 2. Placeholder parity. The single highest-value gate: an LLM dropping {0}
    #    produces a string that renders, looks fine in review, and loses data.
    for k, (value, _) in sorted(tr.items()):
        if k not in neutral:
            continue
        want = set(PLACEHOLDER.findall(neutral[k][0]))
        got = set(PLACEHOLDER.findall(value))
        # ICU plural values name their arg and use # inside categories; the
        # category names themselves are not placeholders.
        if categories(value) is not None or categories(neutral[k][0]) is not None:
            want -= {"plural"}
            got -= {"plural"} | (allowed or set())
        if want != got:
            missing, extra = sorted(want - got), sorted(got - want)
            detail = []
            if missing:
                detail.append(f"missing {missing}")
            if extra:
                detail.append(f"unexpected {extra}")
            fail(f"{k}: placeholder mismatch ({'; '.join(detail)})")

    # 3. Plural categories must be legal for the locale.
    if allowed is not None:
        for k, (value, _) in sorted(tr.items()):
            used = categories(value)
            if used is None:
                continue
            illegal = sorted(used - allowed)
            if illegal:
                fail(f"{k}: plural categories {illegal} are not valid for {loc} "
                     f"(allowed: {sorted(allowed)})")

    # 4. A key the neutral file renders as a plural must stay a plural.
    for k, (value, _) in sorted(tr.items()):
        if k in neutral and categories(neutral[k][0]) is not None and categories(value) is None:
            fail(f"{k}: neutral value is an ICU plural but this locale's is not")

    # 5. ADVISORY — nothing left in English. A heuristic, not an invariant:
    #    cognates make this fire constantly for fr/es/ro/it ("Configuration",
    #    "Important", "Actions", "Page" are all genuinely identical in French).
    #    Useful as a review worklist, useless as a gate.
    for k, (value, _) in sorted(tr.items()):
        if k in DO_NOT_TRANSLATE_EXACT or k not in neutral:
            continue
        en_value = neutral[k][0]
        if value == en_value and re.search(r"[A-Za-z]{4}", en_value):
            note(f"{k}: identical to English — cognate, term of art, or untranslated?")

    # 6. ADVISORY — length budget. German compounds and Russian run long, and
    #    the nav chips and stat tiles are fixed-width in places. Short strings
    #    only, which is where overflow actually bites.
    for k, (value, _) in sorted(tr.items()):
        if k not in neutral:
            continue
        en_len = len(neutral[k][0])
        if 0 < en_len <= 30 and len(value) > en_len * max_ratio:
            note(f"{k}: {len(value)} chars vs {en_len} English "
                 f"(>{max_ratio}x on a short string — check for UI overflow)")

    return fails, notes


def check_neutral(neutral: dict) -> list[str]:
    fails = []
    for k, (value, _) in sorted(neutral.items()):
        if LEGACY_PLURAL.search(value):
            fails.append(f"en: {k}: uses '(s)' — convert to ICU plural "
                         f"(see docs/localization/plural-forms.md)")
    pairs = [k for k in neutral if k.endswith("One") and k[:-3] + "Many" in neutral]
    for k in sorted(pairs):
        fails.append(f"en: {k}/{k[:-3]}Many: one+many key pair — collapse into a "
                     f"single ICU plural (see docs/localization/plural-forms.md)")
    return fails


def list_count_bearing(neutral: dict) -> None:
    hits = []
    for k, (value, _) in sorted(neutral.items()):
        if LEGACY_PLURAL.search(value) or categories(value) is not None:
            hits.append((k, value))
        elif re.search(r"\{\d+\}", value) and re.search(
            r"\b(pose|change|page|word|setting|error|warning|revision|asset|object|"
            r"attribute|conflict|field|occurrence|byte|record|time)s?\b", value, re.I
        ):
            hits.append((k, value))
    for k, v in hits:
        print(f"{k:32} {v!r}")
    print(f"\n{len(hits)} count-bearing values", file=sys.stderr)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--locale", nargs="*", help="limit to these locales")
    ap.add_argument("--list-count-bearing", action="store_true")
    ap.add_argument("--max-length-ratio", type=float, default=2.5)
    ap.add_argument("--strict", action="store_true",
                    help="treat advisories as failures too")
    ap.add_argument("--quiet-advisories", action="store_true",
                    help="print the advisory count but not each line")
    args = ap.parse_args()

    if not os.path.isfile(NEUTRAL):
        print(f"error: {NEUTRAL} not found — run from the repository root", file=sys.stderr)
        return 2

    neutral = load(NEUTRAL)

    if args.list_count_bearing:
        list_count_bearing(neutral)
        return 0

    paths = sorted(p for p in glob.glob(os.path.join(RES_DIR, "SharedResource.*.resx")))
    if args.locale:
        wanted = set(args.locale)
        paths = [p for p in paths if locale_of(p) in wanted]
        unknown = wanted - {locale_of(p) for p in paths}
        for u in sorted(unknown):
            print(f"warning: no resx for locale {u}", file=sys.stderr)

    fails = check_neutral(neutral)
    notes: list[str] = []
    print(f"{'en':11} {len(neutral):5} keys  (neutral)")
    for p in paths:
        tr = load(p)
        loc = locale_of(p)
        pct = 100 * len(tr) // len(neutral) if neutral else 0
        unknown_locale = "" if loc in CLDR_CATEGORIES else "  [no CLDR entry — add to CLDR_CATEGORIES]"
        print(f"{loc:11} {len(tr):5} keys  {pct:3}%{unknown_locale}")
        f, n = check_locale(neutral, p, args.max_length_ratio)
        fails += f
        notes += n

    if notes:
        print(f"\n{len(notes)} advisory/advisories (heuristic — review, do not "
              f"blindly fix):", file=sys.stderr)
        if not args.quiet_advisories:
            for n in notes:
                print(f"  {n}", file=sys.stderr)

    if fails:
        print(f"\n{len(fails)} failure(s):\n", file=sys.stderr)
        for f in fails:
            print(f"  {f}", file=sys.stderr)

    if fails or (args.strict and notes):
        return 1

    print("\nall gates passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
