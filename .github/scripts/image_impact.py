#!/usr/bin/env python3
"""Shared dev publishing/PR impact policy; reads project graphs without executing MSBuild."""
import argparse
import fnmatch
import html
import json
import os
from pathlib import PurePosixPath
import posixpath
import re
import shlex
import subprocess
import xml.etree.ElementTree as ET

IMAGES = {
    "server": "Dockerfile",
    "connectionserver": "SharpMUSH.ConnectionServer/Dockerfile",
    "socketserver": "SharpMUSH.SocketServer/Dockerfile",
}
POLICY = {".github/workflows/docker-dev.yml", ".github/scripts/image_impact.py"}


def git(*args):
    return subprocess.check_output(["git", *args]).decode("utf-8", errors="surrogateescape")


def source_patterns(read, dockerfile):
    """Include both compile dependencies and linked resources outside project directories."""
    try:
        docker = read(dockerfile)
    except (FileNotFoundError, KeyError):
        return set()  # Image did not exist at one side of a split/rename.
    projects = re.findall(r"dotnet\s+publish\s+[\"']?([^\s\"']+\.[cf]sproj)", docker)
    if not projects:
        raise ValueError(f"No explicit publish project in {dockerfile}; update image impact analysis")
    patterns = {dockerfile}
    for instruction in docker.replace("\\\n", " ").splitlines():
        match = re.match(r"\s*(?:COPY|ADD)\s+(.*)", instruction, re.IGNORECASE)
        if not match or "--from=" in match[1]:
            continue
        value = re.sub(r"^--[^ ]+\s+", "", match[1])
        tokens = json.loads(value) if value.startswith("[") else shlex.split(value)
        for source in tokens[:-1]:
            source = source.removeprefix("./").rstrip("/")
            # COPY . . supplies the build context; the project closure selects its relevant files.
            if source in {"", "."}:
                continue
            if "$" in source:
                raise ValueError(f"Cannot resolve Docker input {source} in {dockerfile}")
            patterns.update({source, source + "/**"})
    seen = set()
    while projects:
        project = projects.pop().replace("\\", "/")
        if project in seen:
            continue
        seen.add(project)
        directory = str(PurePosixPath(project).parent)
        patterns.add(directory + "/**")
        root = ET.fromstring(read(project))
        for element in root.iter():
            tag = element.tag.rsplit("}", 1)[-1]
            if tag == "PackageReference":
                continue
            for value in (element.get("Include", ""), element.get("Project", "")):
                for item in value.split(";"):
                    item = item.replace("\\", "/").replace("$(MSBuildThisFileDirectory)", "")
                    if not item or "$(" in item or "@(" in item:
                        if tag == "ProjectReference" and item:
                            raise ValueError(f"Cannot resolve ProjectReference {item} in {project}")
                        continue
                    path = posixpath.normpath(posixpath.join(directory, item))
                    if path.startswith("../") or path.startswith("/"):
                        raise ValueError(f"Input outside repository: {path}")
                    if tag == "ProjectReference":
                        projects.append(path)
                    elif tag == "Import":
                        # Imported build logic may reference arbitrary inputs. Fail safe.
                        patterns.add("**")
                    elif item.startswith("../"):
                        patterns.add(path)
    return patterns


def is_global(path):
    name = PurePosixPath(path).name.lower()
    return (path in POLICY or name in {".dockerignore", "global.json", "nuget.config"}
            or name.endswith((".props", ".targets", ".sln", ".slnx"))
            or path.startswith(".config/dotnet-tools"))


def analyze(changed, readers, removed=None):
    result = {}
    for image, dockerfile in IMAGES.items():
        try:
            readers[-1](dockerfile)  # Only images present in the head revision can publish.
        except (FileNotFoundError, KeyError):
            result[image] = []
            if removed is not None and any(source_patterns(read, dockerfile) for read in readers[:-1]):
                removed.append(image)
            continue
        patterns = set()
        for read in readers:
            patterns.update(source_patterns(read, dockerfile))
        result[image] = sorted(path for path in changed
                               if is_global(path) or any(fnmatch.fnmatchcase(path, p) for p in patterns))
    return result


def report(result, removed=()):
    drops = bool(result["socketserver"])
    lines = ["## Deployment impact", "",
             "**SocketServer restart / live connection drops: " + ("YES" if drops else "NO") + "**", "",
             ("On merge to main, changed image inputs are published after successful validation. "
             "Watchtower will replace the corresponding container when its image digest changes."), "",
             ("SocketServer replacement disconnects its TCP and WebSocket clients. "
             "Game-server and ConnectionServer rendering-worker updates retain client sockets in SocketServer."), ""]
    for image, paths in result.items():
        lines.extend([f"### {image}: {'removed (no publish)' if image in removed else 'publish' if paths else 'unchanged'}", ""])
        lines.extend(f"- <code>{html.escape(path)}</code>" for path in paths)
        lines.append("")
    lines.append("This is source-input impact, not a promise about image digests or deployment success. "
                 "Manual force publishing and independently updated base images require operator review.")
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--merge-base", action="store_true", help="Compare PR changes since the common ancestor")
    args = parser.parse_args()
    # Validate refs before passing them into revision/path expressions.
    head = git("rev-parse", "--verify", args.head + "^{commit}").strip()
    if set(args.base) == {"0"}:
        base = None  # First push: all tracked inputs are new.
        changed = git("ls-tree", "-r", "--name-only", "-z", head).split("\0")
    else:
        base = git("rev-parse", "--verify", args.base + "^{commit}").strip()
        if args.merge_base:
            base = git("merge-base", base, head).strip()
        changed = git("diff", "--name-only", "--no-renames", "-z", base, head).split("\0")
    def read_revision(ref, path):
        value = subprocess.run(["git", "show", f"{ref}:{path}"], capture_output=True)
        if value.returncode:
            raise FileNotFoundError(f"{ref}:{path}")
        return value.stdout.decode("utf-8")
    readers = [lambda path, ref=ref: read_revision(ref, path) for ref in (base, head) if ref]
    removed = []
    result = analyze(set(changed) - {""}, readers, removed)
    summary = report(result, removed)
    print(summary)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as output:
            output.write(summary)
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a") as output:
            for image, paths in result.items():
                output.write(f"{image}={str(bool(paths)).lower()}\n")
                output.write(f"{image}_removed={str(image in removed).lower()}\n")


if __name__ == "__main__":
    main()
