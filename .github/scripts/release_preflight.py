"""Read-only release metadata check: release_preflight.py [intended-version].

Without an argument, check the engine version in Directory.Build.props (normal PR CI).
Before publishing, supply the intended tag's version without its leading v.
This checks the working tree only; it does not create a tag, release or image.
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
NUMBER = r'(?:0|[1-9][0-9]*)'
IDENTIFIER = rf'(?:{NUMBER}|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
VERSION = re.compile(rf'{NUMBER}\.{NUMBER}\.{NUMBER}(?:-{IDENTIFIER}(?:\.{IDENTIFIER})*)?')


def check(root, intended=None):
    """Return the validated version, or fail before any publication side effects."""
    values = ET.parse(root / 'Directory.Build.props').findall('.//SharpMUSHVersion')
    if len(values) != 1 or not values[0].text:
        raise ValueError('Directory.Build.props must name exactly one SharpMUSHVersion.')
    version = values[0].text.strip()
    intended = version if intended is None else intended
    if not VERSION.fullmatch(intended):
        raise ValueError(f'{intended!r} is not a version like 1.2.3 or 1.2.3-rc.1 (without v).')
    if version != intended:
        raise ValueError(f'SharpMUSHVersion is {version}, but the intended release is {intended}.')
    changelog = (root / 'CHANGELOG.md').read_text(encoding='utf-8')
    heading = rf'^## \[{re.escape(intended)}\](?: - [0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}})?[ \t]*$'
    matches = list(re.finditer(heading, changelog, re.MULTILINE))
    if len(matches) != 1:
        raise ValueError(f'CHANGELOG.md must have exactly one \'## [{intended}]\' section.')
    body = re.split(r'^## ', changelog[matches[0].end():], maxsplit=1, flags=re.MULTILINE)[0]
    if not any(line.strip() and not line.startswith(('#', '[')) for line in body.splitlines()):
        raise ValueError(f'CHANGELOG.md section [{intended}] has no release notes.')
    return version


def main(argv):
    if len(argv) > 1:
        print(__doc__, file=sys.stderr)
        return 2
    try:
        version = check(ROOT, argv[0] if argv else None)
    except (ValueError, OSError, ET.ParseError) as error:
        print(f'::error::{error}', file=sys.stderr)
        return 1
    print(f'Release metadata agrees on {version}. Only this checkout was checked; nothing was published.')
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
