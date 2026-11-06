#!/usr/bin/env python3
"""Fail a change to a package under examples/packages/ that does not raise its version.

Bootstrap upgrades an installed bundled package only when the build ships a newer version, so a change
without a bump never reaches a game that already has the package.
"""
import argparse
import re
import subprocess
import sys

ROOT = "examples/packages/"
VERSION = re.compile(r"^version:\s*[\"']?([^\"'\s#]+)", re.M)
SEMVER = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")


def version_key(text):
    """A sort key for a semantic version, or None when it is not one. A prerelease sorts before its release."""
    match = SEMVER.match(text)
    if not match:
        return None
    pre = match.group(4)
    identifiers = () if pre is None else tuple(
        (0, int(part), "") if part.isdigit() else (1, 0, part) for part in pre.split("."))
    return (int(match.group(1)), int(match.group(2)), int(match.group(3)), pre is None, identifiers)


def manifest_version(text):
    match = VERSION.search(text or "")
    return match.group(1) if match else None


def changed_packages(paths):
    """The package directories the changed paths fall in. Files at the root of examples/packages belong to none."""
    packages = set()
    for path in paths:
        if path.startswith(ROOT):
            parts = path[len(ROOT):].split("/")
            if len(parts) > 1:
                packages.add(parts[0])
    return sorted(packages)


def check(paths, read_base, read_head, read_target=None):
    """One line for each changed package whose version did not go up. A package added or removed needs no bump.

    read_base reads the merge-base, where the branch started; read_target reads the base branch as it is now. The
    head's version has to be newer than both, so two branches that each raise a package to the same version cannot
    both ship it with different contents.
    """
    problems = []
    for package in changed_packages(paths):
        manifest = f"{ROOT}{package}/package.yaml"
        before, after = read_base(manifest), read_head(manifest)
        if before is None or after is None:
            continue
        new = manifest_version(after)
        new_key = version_key(new or "")
        if new_key is None:
            problems.append(f"{package}: version {new!r} is not a semantic version.")
            continue
        olds = [manifest_version(before)]
        if read_target is not None and (current := read_target(manifest)) is not None:
            olds.append(manifest_version(current))
        for old in olds:
            old_key = version_key(old or "")
            if old_key is not None and new_key <= old_key:
                problems.append(f"{package}: changed without a version bump (still {new}; the base has {old}).")
                break
    return problems


def git_show(revision, path):
    result = subprocess.run(["git", "show", f"{revision}:{path}"], capture_output=True, text=True)
    return result.stdout if result.returncode == 0 else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    args = parser.parse_args()
    base = subprocess.run(["git", "merge-base", args.base, args.head], capture_output=True, text=True,
                          check=True).stdout.strip()
    paths = [p for p in subprocess.run(["git", "diff", "-z", "--name-only", base, args.head, "--", ROOT],
                                       capture_output=True, text=True, check=True).stdout.split("\0") if p]
    problems = check(paths, lambda p: git_show(base, p), lambda p: git_show(args.head, p),
                     lambda p: git_show(args.base, p))
    for problem in problems:
        print(f"::error file={ROOT}{problem.split(':')[0]}/package.yaml::{problem}")
    if problems:
        print("Raise the version in each package.yaml above (and its line in examples/packages/index.yaml).")
        return 1
    print(f"{len(changed_packages(paths))} changed package(s), each with a new version.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
