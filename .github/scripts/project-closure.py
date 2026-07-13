#!/usr/bin/env python3
"""Print the source-directory closure of one or more .csproj files.

Walks <ProjectReference> includes recursively and emits the unique set of
project directories (repo-relative, POSIX separators), one per line. Used by
docker-dev.yml to compute a content stamp per image so an image whose inputs
did not change is not re-pushed (and watchtower does not restart it).
"""
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
REFERENCE = re.compile(r'<ProjectReference\s+Include="([^"]+)"', re.IGNORECASE)


def closure(roots: list[Path]) -> set[Path]:
    seen: set[Path] = set()
    stack = [p.resolve() for p in roots]
    while stack:
        csproj = stack.pop()
        if csproj in seen:
            continue
        seen.add(csproj)
        text = csproj.read_text(encoding="utf-8", errors="replace")
        for include in REFERENCE.findall(text):
            ref = (csproj.parent / include.replace("\\", "/")).resolve()
            if ref.exists():
                stack.append(ref)
    return seen


def main() -> None:
    roots = [Path(arg) for arg in sys.argv[1:]]
    dirs = sorted(
        str(p.parent.relative_to(REPO_ROOT)).replace("\\", "/")
        for p in closure(roots)
    )
    print("\n".join(dirs))


if __name__ == "__main__":
    main()
