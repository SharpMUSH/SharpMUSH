#!/usr/bin/env python3
"""Append new resource keys, with their translations, to the neutral resx and every satellite.

The locale gate (DeclaredLocaleCoverageTests) requires every player-facing key to exist in every
declared locale, so a new string is a 16-file edit. This takes one JSON file describing the keys
and writes each locale's value into its resx, appended before </root> in the file's own style.

    python3 tools/i18n/add_keys.py keys.json

Input:

    {"NavBannerMinimise": {"": "Minimise banner", "de": "Banner minimieren", ...},
     "NavBannerRestore":  {"": "Show banner", ...}}

The "" entry is the neutral (English) value and is required. A locale left out of a key is
skipped for that locale; the gate will then tell you if it was player-facing. Refuses to add a key
the neutral resx already has, and refuses a locale that has no resx.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape, quoteattr

RES_DIR = os.path.join("SharpMUSH.Client", "Resources")


def resx_path(res_dir: str, locale: str) -> str:
    name = "SharedResource.resx" if locale == "" else f"SharedResource.{locale}.resx"
    return os.path.join(res_dir, name)


def read(path: str) -> str:
    with open(path, encoding="utf-8") as f:
        return f.read()


def existing_keys(text: str) -> set[str]:
    return {name for d in ET.fromstring(text.encode("utf-8")).iter("data") if (name := d.get("name")) is not None}


def entry(key: str, value: str) -> str:
    return f'  <data name={quoteattr(key)} xml:space="preserve">\n    <value>{escape(value)}</value>\n  </data>\n'


def add_keys(keys: dict[str, dict[str, str]], res_dir: str = RES_DIR) -> int:
    have = existing_keys(read(resx_path(res_dir, "")))
    for key, values in keys.items():
        if "" not in values:
            print(f"{key}: no neutral value", file=sys.stderr)
            return 1
        if key in have:
            print(f"{key}: already in the neutral resx", file=sys.stderr)
            return 1

    locales = sorted({loc for values in keys.values() for loc in values})
    for loc in locales:
        path = resx_path(res_dir, loc)
        if not os.path.exists(path):
            print(f"{loc}: {path} does not exist", file=sys.stderr)
            return 1

    for loc in locales:
        path = resx_path(res_dir, loc)
        text = read(path)
        present = existing_keys(text)
        block = "".join(entry(key, values[loc]) for key, values in keys.items() if loc in values and key not in present)
        if not block:
            continue
        idx = text.rindex("</root>")
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(text[:idx] + block + text[idx:])
        print(f"{path}: +{block.count('<data ')}")
    return 0


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    with open(sys.argv[1], encoding="utf-8") as f:
        keys: dict[str, dict[str, str]] = json.load(f)
    if (code := add_keys(keys)) != 0:
        return code
    # The same gates CI runs, so a bad plural or placeholder is caught at authoring time.
    return subprocess.call([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "validate_resx.py")])


if __name__ == "__main__":
    sys.exit(main())
