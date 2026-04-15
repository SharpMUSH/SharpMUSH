#!/usr/bin/env python3
"""
Transforms Notify( calls that use ErrorMessages.Notifications.* constants into
NotifyLocalized( calls that look up the key by name.

Handles two patterns (only for 2-argument Notify calls):
  1. .Notify(target, ErrorMessages.Notifications.Key)
     → .NotifyLocalized(target, nameof(ErrorMessages.Notifications.Key))

  2. .Notify(target, string.Format(ErrorMessages.Notifications.Key, arg1, ...))
     → .NotifyLocalized(target, nameof(ErrorMessages.Notifications.Key), arg1, ...)

Skips any Notify call with more than 2 top-level arguments (e.g., calls that
include a sender or NotificationType parameter) to avoid silently dropping
those arguments. Skips: .NotifyLocalized(, .NotifyAndReturn(, .NotifyExcept(
"""

import re
import sys
from pathlib import Path


# ---------------------------------------------------------------------------
# Paren-balanced helpers
# ---------------------------------------------------------------------------


def find_matching_paren(text: str, start: int) -> int:
    """Return index of the ) matching the ( at text[start]. Returns -1 on failure."""
    assert text[start] == "(", f"Expected '(' at {start}, got {text[start]!r}"
    depth = 0
    i = start
    n = len(text)
    while i < n:
        c = text[i]
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return i
        elif c == '"':
            # Regular string literal: skip until closing unescaped "
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    break
                i += 1
        elif c == "@" and i + 1 < n and text[i + 1] == '"':
            # Verbatim string @"...": escape is "" inside
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    break
                i += 1
        i += 1
    return -1


def split_depth0_commas(text: str) -> list[str]:
    """Split *text* by commas that are at paren-depth 0. Returns list of trimmed chunks."""
    parts: list[str] = []
    buf: list[str] = []
    depth = 0
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == "(":
            depth += 1
            buf.append(c)
        elif c == ")":
            depth -= 1
            buf.append(c)
        elif c == "," and depth == 0:
            parts.append("".join(buf).strip())
            buf = []
        elif c == '"':
            buf.append(c)
            i += 1
            while i < n:
                if text[i] == "\\":
                    buf.append(text[i])
                    i += 1
                    if i < n:
                        buf.append(text[i])
                elif text[i] == '"':
                    buf.append(text[i])
                    break
                else:
                    buf.append(text[i])
                i += 1
        elif c == "@" and i + 1 < n and text[i + 1] == '"':
            buf.append(c)
            buf.append(text[i + 1])
            i += 2
            while i < n:
                buf.append(text[i])
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        buf.append(text[i + 1])
                        i += 2
                        continue
                    break
                i += 1
        else:
            buf.append(c)
        i += 1
    if buf:
        parts.append("".join(buf).strip())
    return parts


# ---------------------------------------------------------------------------
# Core transformation
# ---------------------------------------------------------------------------

# Matches the start of a .Notify( call that is NOT .NotifyLocalized/AndReturn/Except
NOTIFY_RE = re.compile(r"\.Notify(?!Localized|AndReturn|Except|And)\(")

# Matches a bare ErrorMessages.Notifications.Key (whole arg, trimmed)
SIMPLE_KEY_RE = re.compile(r"^ErrorMessages\.Notifications\.(\w+)$")

# Matches string.Format(ErrorMessages.Notifications.Key) or
#            string.Format(ErrorMessages.Notifications.Key, args...)
FORMAT_KEY_RE = re.compile(
    r"^string\.Format\(\s*ErrorMessages\.Notifications\.(\w+)\s*(?:,\s*(.+))?\)$",
    re.DOTALL,
)


def transform_content(content: str) -> tuple[str, int]:
    """Return (transformed_content, number_of_replacements_made)."""
    result: list[str] = []
    replacements = 0
    i = 0

    while True:
        m = NOTIFY_RE.search(content, i)
        if m is None:
            result.append(content[i:])
            break

        open_paren_pos = m.end() - 1  # position of '('
        close_paren_pos = find_matching_paren(content, open_paren_pos)
        if close_paren_pos == -1:
            # Malformed – skip
            result.append(content[i : m.end()])
            i = m.end()
            continue

        args_text = content[open_paren_pos + 1 : close_paren_pos]
        args = split_depth0_commas(args_text)

        if len(args) < 2:
            result.append(content[i : m.end()])
            i = m.end()
            continue

        target = args[0]
        msg_arg = args[1].strip()

        # --- Pattern: bare constant ---
        simple_m = SIMPLE_KEY_RE.match(msg_arg)
        if simple_m:
            key = simple_m.group(1)
            # Guard: only rewrite 2-arg calls — extra args are sender/type and must
            # not be silently dropped (NotifyLocalized has no sender/type params).
            if len(args) != 2:
                result.append(content[i : m.end()])
                i = m.end()
                continue
            new_call = (
                f".NotifyLocalized({target}, nameof(ErrorMessages.Notifications.{key}))"
            )
            result.append(content[i : m.start()])
            result.append(new_call)
            i = close_paren_pos + 1
            replacements += 1
            continue

        # --- Pattern: string.Format(constant, args...) ---
        fmt_m = FORMAT_KEY_RE.match(msg_arg)
        if fmt_m:
            key = fmt_m.group(1)
            fmt_args = fmt_m.group(2)  # None or the raw args string after the key
            # Guard: only rewrite when the full call has exactly 2 top-level args —
            # a 3rd+ arg would be sender/type which NotifyLocalized doesn't accept.
            if len(args) != 2:
                result.append(content[i : m.end()])
                i = m.end()
                continue
            if fmt_args and fmt_args.strip():
                new_call = f".NotifyLocalized({target}, nameof(ErrorMessages.Notifications.{key}), {fmt_args.strip()})"
            else:
                new_call = f".NotifyLocalized({target}, nameof(ErrorMessages.Notifications.{key}))"
            result.append(content[i : m.start()])
            result.append(new_call)
            i = close_paren_pos + 1
            replacements += 1
            continue

        # Not a pattern we handle — preserve and advance past the (
        result.append(content[i : m.end()])
        i = m.end()

    return "".join(result), replacements


# ---------------------------------------------------------------------------
# File-level runner
# ---------------------------------------------------------------------------


def process_file(path: Path, dry_run: bool = False) -> int:
    original = path.read_text(encoding="utf-8-sig")
    transformed, count = transform_content(original)
    if count > 0 and not dry_run:
        path.write_text(transformed, encoding="utf-8")
    return count


def main() -> None:
    import argparse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("roots", nargs="+", help="Directory or file paths to transform")
    parser.add_argument(
        "--dry-run", action="store_true", help="Show counts without writing"
    )
    args = parser.parse_args()

    total = 0
    for root_str in args.roots:
        root = Path(root_str)
        files = list(root.rglob("*.cs")) if root.is_dir() else [root]
        for f in sorted(files):
            count = process_file(f, dry_run=args.dry_run)
            if count:
                action = "would transform" if args.dry_run else "transformed"
                print(f"{f}: {action} {count} call(s)")
                total += count

    print(
        f"\nTotal: {total} replacement(s) {'(dry run)' if args.dry_run else 'applied'}"
    )


if __name__ == "__main__":
    main()
