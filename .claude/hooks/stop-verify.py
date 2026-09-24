#!/usr/bin/env python3
"""Claude Code Stop hook: refuse to end a turn while the work is unverified.

A turn may not end while any of these is true, checked in this order:

  1. A build/test job the agent started in the background is still running. Ending the turn
     kills it (headless runs exit), and whatever it would have reported is lost.
  2. Changed C# files do not match .editorconfig. This is the CI `format` job, run on the
     changed files only.
  3. A test project that depends on the changed files fails, or the client JS tests fail when
     the client scripts changed.

"Changed" means the working tree (staged, unstaged, untracked) plus commits not yet on the
upstream branch (or origin/main when there is no upstream). With nothing changed the hook
exits after a few git calls. A passing result is remembered against a fingerprint of the
changed files' contents, so the tests are not re-run until those files change again.

Blocking exits 2 with the reason on stderr; Claude Code shows it to the agent and keeps the
turn going. A tool that is missing or crashes never blocks: CI stays the authority.

Escape hatches (set in the environment Claude Code is launched from):
  SHARPMUSH_STOP_HOOK=off         skip the hook entirely
  SHARPMUSH_STOP_HOOK_TESTS=off   keep the format and background-job checks, skip tests
  SHARPMUSH_STOP_HOOK_MAX_BLOCKS  consecutive blocks before the hook gives up (default 8)
It is also off under CI (CI or GITHUB_ACTIONS set), where the real gates run.

See .claude/hooks/README.md.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import signal
import subprocess
import sys
import time
from pathlib import Path

MAX_BLOCKS = int(os.environ.get("SHARPMUSH_STOP_HOOK_MAX_BLOCKS", "8"))
# Keep well inside the hook timeout in .claude/settings.json (3600s): a hook that is killed
# for running too long does not block, so the turn would end with the work unverified.
TEST_DEADLINE_SECONDS = int(os.environ.get("SHARPMUSH_STOP_HOOK_TEST_TIMEOUT", "2400"))
LOG_TAIL_LINES = 60

FORMAT_EXCLUDES = ["--exclude", "**/bin/**", "--exclude", "**/obj/**", "--exclude", "**/TestResults/**"]

# Files outside any project that still change what every project builds or runs.
GLOBAL_INPUTS = {
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "nuget.config",
    "NuGet.config",
    ".editorconfig",
}

# The client scripts are exercised by node, not by any .NET test project.
CLIENT_TEST_INPUTS = ("SharpMUSH.Client/wwwroot/", "tools/client-tests/")

# Test projects that start Testcontainers. Without a Docker daemon they cannot pass, which says
# nothing about the change, so they are left to CI.
NEEDS_DOCKER = {"SharpMUSH.Tests.Integration"}

# Background commands that produce a verdict the agent must read. Servers and watchers are
# deliberately not listed: an agent may leave those running on purpose. Anchored to the
# executable, so the shell the Bash tool wraps a command in (whose argv holds the whole script
# text) never matches; the command itself, its child, does.
BACKGROUND_JOB = re.compile(
    r"^(\S*/)?("
    r"dotnet(\.exe)?\s+(test|build|format|run\s+.*--project\s+\S*Tests)\b"
    r"|node\s+--test\b"
    r"|npm\s+(test|run\s+test)\b"
    r")"
)
# Long-lived helpers `dotnet build` leaves behind; they are not the agent's jobs.
BUILD_SERVER = re.compile(r"MSBuild\.dll.*(/nodemode|-nodemode)|VBCSCompiler|rzc\.dll|/nodeReuse")


def git(root: Path, *args: str, check: bool = True) -> str:
    result = subprocess.run(
        ["git", "-C", str(root), *args],
        capture_output=True,
        text=True,
        check=False,
    )
    if check and result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr.strip()}")
    return result.stdout


def block(reason: str, state_dir: Path | None, session: str) -> None:
    if state_dir is not None:
        counter = state_dir / f"blocks-{session}"
        count = int(counter.read_text() or "0") if counter.exists() else 0
        counter.write_text(str(count + 1))
    print(reason, file=sys.stderr)
    sys.exit(2)


def allow(message: str | None = None) -> None:
    if message:
        print(json.dumps({"systemMessage": message}))
    sys.exit(0)


# --- background jobs --------------------------------------------------------------------------


def process_table() -> dict[int, tuple[int, str]]:
    result = subprocess.run(["ps", "-Ao", "pid=,ppid=,args="], capture_output=True, text=True, check=False)
    table: dict[int, tuple[int, str]] = {}
    for line in result.stdout.splitlines():
        parts = line.strip().split(None, 2)
        if len(parts) >= 2 and parts[0].isdigit() and parts[1].isdigit():
            table[int(parts[0])] = (int(parts[1]), parts[2] if len(parts) > 2 else "")
    return table


def running_background_jobs() -> list[str]:
    """Build/test commands running under this Claude Code process, other than this hook."""
    try:
        table = process_table()
    except OSError:
        return []  # no `ps` (Windows): the check cannot run, so it does not block

    ancestors = []
    pid = os.getpid()
    while pid in table and pid > 1:
        ancestors.append(pid)
        pid = table[pid][0]
    claude = next(
        (p for p in ancestors if re.search(r"(^|/)claude(\s|$)|claude-code|@anthropic-ai", table[p][1])),
        None,
    )
    if claude is None:
        return []

    children: dict[int, list[int]] = {}
    for pid, (ppid, _) in table.items():
        children.setdefault(ppid, []).append(pid)

    own = set(ancestors)
    jobs = []
    stack = [p for p in children.get(claude, []) if p not in own]
    while stack:
        pid = stack.pop()
        args = table[pid][1]
        if BACKGROUND_JOB.search(args) and not BUILD_SERVER.search(args):
            jobs.append(f"  pid {pid}: {args[:200]}")
        stack.extend(children.get(pid, []))
    return sorted(jobs)


# --- what changed ----------------------------------------------------------------------------


def comparison_base(root: Path) -> str | None:
    for ref in ("@{upstream}", "origin/main"):
        base = git(root, "merge-base", "HEAD", ref, check=False).strip()
        if base:
            return base
    return None


def changed_files(root: Path, base: str | None) -> list[str]:
    paths: set[str] = set()
    status = git(root, "status", "--porcelain=v1", "-z", "--untracked-files=all")
    entries = status.split("\0")
    i = 0
    while i < len(entries):
        entry = entries[i]
        i += 1
        if len(entry) < 4:
            continue
        paths.add(entry[3:])
        if entry[0] in "RC":  # rename/copy: the next entry is the source path
            paths.add(entries[i])
            i += 1
    if base:
        paths.update(p for p in git(root, "diff", "--name-only", "-z", f"{base}..HEAD").split("\0") if p)
    return sorted(paths)


def fingerprint(root: Path, base: str | None, files: list[str], checks: list[str]) -> str:
    digest = hashlib.sha256()
    digest.update(f"{base}\n{checks}\n".encode())
    present = [f for f in files if (root / f).is_file()]
    hashes: list[str] = []
    if present:
        hashes = subprocess.run(
            ["git", "-C", str(root), "hash-object", "--stdin-paths"],
            input="\n".join(present),
            capture_output=True,
            text=True,
            check=False,
        ).stdout.split()
    blob = dict(zip(present, hashes))
    for f in files:
        digest.update(f"{f}\0{blob.get(f, 'deleted')}\n".encode())
    return digest.hexdigest()


# --- which tests -----------------------------------------------------------------------------


def project_graph(root: Path, files: list[str]) -> tuple[dict[str, str], dict[str, set[str]], dict[str, list[str]]]:
    """Project directory -> csproj; csproj -> the csprojs it references; csproj -> linked inputs.

    Linked inputs are items a project pulls in from outside its own directory (help files,
    oracle fixtures, embedded package YAML, ...). Each is kept as the path up to its first
    wildcard, so a changed file under it selects the project.
    """
    csprojs = [p for p in git(root, "ls-files", "-z", "*.csproj").split("\0") if p]
    csprojs += [f for f in files if f.endswith(".csproj") and f not in csprojs and (root / f).is_file()]
    by_dir = {str(Path(p).parent): p for p in csprojs}
    refs: dict[str, set[str]] = {}
    linked: dict[str, list[str]] = {}
    for proj in csprojs:
        folder = str(Path(proj).parent)
        refs[proj] = set()
        linked[proj] = []
        if not (root / proj).is_file():
            continue  # deleted in the working tree: still "touched", so its dependents are tested
        text = (root / proj).read_text(encoding="utf-8", errors="replace")
        for kind, include in re.findall(r'<(\w+)\s+(?:Include|Update|Project)="([^"]+)"', text):
            include = include.replace("$(MSBuildThisFileDirectory)", "").replace("$(MSBuildProjectDirectory)", "")
            target = os.path.normpath(os.path.join(folder, include.replace("\\", "/")))
            if kind == "ProjectReference":
                refs[proj].add(target)
            elif ".." in include and "$(" not in include:
                linked[proj].append(re.split(r"[*?]", target, maxsplit=1)[0])
    return by_dir, refs, linked


def owning_project(path: str, by_dir: dict[str, str]) -> str | None:
    parent = Path(path).parent
    while True:
        key = str(parent)
        if key in by_dir:
            return by_dir[key]
        if key in ("", "."):
            return None
        parent = parent.parent


def is_test_project(proj: str) -> bool:
    name = Path(proj).stem
    return name.startswith("SharpMUSH.Tests") and name != "SharpMUSH.Tests.Infrastructure"


def affected_test_projects(root: Path, files: list[str]) -> list[str]:
    by_dir, refs, linked = project_graph(root, files)
    tests = [p for p in refs if is_test_project(p) and (root / p).is_file()]

    if any(Path(f).name in GLOBAL_INPUTS for f in files if "/" not in f):
        return sorted(tests)

    touched = {proj for f in files if (proj := owning_project(f, by_dir))}
    touched |= {proj for proj, prefixes in linked.items() for f in files for prefix in prefixes if f.startswith(prefix)}

    def depends_on_touched(proj: str, seen: set[str]) -> bool:
        if proj in touched:
            return True
        seen.add(proj)
        return any(depends_on_touched(r, seen) for r in refs.get(proj, ()) if r not in seen)

    return sorted(t for t in tests if depends_on_touched(t, set()))


# --- checks ----------------------------------------------------------------------------------


def run_logged(root: Path, cmd: list[str], log: Path, timeout: int) -> tuple[int | None, str]:
    """Run cmd in its own process group; on timeout or when the hook is killed, end the group.

    `dotnet run` starts the test executable as a child, so killing only `dotnet` would orphan
    the suite outside Claude Code's process tree, where the background-job check cannot see it.
    """
    with log.open("w") as out:
        proc = subprocess.Popen(cmd, cwd=root, stdout=out, stderr=subprocess.STDOUT, start_new_session=True)
        previous = signal.signal(signal.SIGTERM, lambda *_: (kill_group(proc), sys.exit(1)))
        try:
            code = proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            kill_group(proc)
            code = None
        finally:
            signal.signal(signal.SIGTERM, previous)
    lines = log.read_text(errors="replace").splitlines()
    return code, "\n".join(lines[-LOG_TAIL_LINES:])


def kill_group(proc: subprocess.Popen) -> None:
    try:
        os.killpg(proc.pid, signal.SIGKILL)
    except (ProcessLookupError, PermissionError, AttributeError):
        proc.kill()
    proc.wait()


def check_format(root: Path, cs_files: list[str], whole_repo: bool, log: Path) -> str | None:
    """A blocking message if formatting differs; None if clean. Raises FormatUnavailable if the
    tool itself failed, which is neither a pass nor a failure of the change."""
    cmd = ["dotnet", "format", "whitespace", "--folder", ".", *FORMAT_EXCLUDES]
    if not whole_repo:
        cmd += ["--include", *cs_files]
    code, tail = run_logged(root, [*cmd, "--verify-no-changes"], log, 300)
    if code == 2:
        fix = " ".join(c if "*" not in c else f"'{c}'" for c in cmd)
        return (
            "Formatting does not match .editorconfig, so CI's `format` job will fail.\n"
            f"Fix it with:\n  {fix}\n"
            "Run it until it reports no changes: the formatter needs two passes to converge.\n\n"
            f"{tail}"
        )
    if code != 0:
        raise FormatUnavailable(f"`dotnet format` could not run (exit {code}); see {log}")
    return None


class FormatUnavailable(Exception):
    pass


def check_tests(root: Path, projects: list[str], client: bool, state_dir: Path) -> str | None:
    deadline = time.monotonic() + TEST_DEADLINE_SECONDS
    runs = [(Path(p).stem, ["dotnet", "run", "--project", str(Path(p).parent), "--", "--maximum-parallel-tests", "8"]) for p in projects]
    if client:
        tests = sorted(str(p.relative_to(root)) for p in (root / "tools" / "client-tests").glob("*.test.mjs"))
        if tests:
            runs.append(("client-tests", ["node", "--test", *tests]))

    for name, cmd in runs:
        remaining = int(deadline - time.monotonic())
        log = state_dir / f"{name}.log"
        code, tail = run_logged(root, cmd, log, max(remaining, 1))
        if code is None:
            return (
                f"`{' '.join(cmd)}` did not finish within the hook's {TEST_DEADLINE_SECONDS}s test budget.\n"
                f"Run it yourself (full log: {log}), fix what hangs, and confirm it passes before ending the turn."
            )
        if code != 0:
            return (
                f"Tests failed: `{' '.join(cmd)}` exited {code}.\n"
                f"Fix them before ending the turn. Full log: {log}\n\n{tail}"
            )
    return None


def find_dotnet(root: Path) -> str | None:
    """A dotnet that satisfies global.json.

    The one on PATH is often a distro package that lags the pinned SDK, while dotnet-install.sh
    puts the right one in ~/.dotnet. Agents may run with HOME pointed elsewhere, so the account's
    real home directory is tried too. The chosen SDK's directory becomes DOTNET_ROOT for every
    command the hook runs.
    """
    candidates = [shutil.which("dotnet")]
    for home in (os.environ.get("DOTNET_ROOT"), str(Path.home() / ".dotnet"), real_home() + "/.dotnet"):
        if home:
            candidates.append(str(Path(home) / "dotnet"))
    for candidate in dict.fromkeys(c for c in candidates if c and os.access(c, os.X_OK)):
        env = {**os.environ, "DOTNET_ROOT": str(Path(os.path.realpath(candidate)).parent)}
        probe = subprocess.run([candidate, "--version"], cwd=root, capture_output=True, env=env, check=False)
        if probe.returncode == 0:
            os.environ["DOTNET_ROOT"] = env["DOTNET_ROOT"]
            os.environ["PATH"] = env["DOTNET_ROOT"] + os.pathsep + os.environ.get("PATH", "")
            return candidate
    return None


def docker_usable() -> bool:
    if shutil.which("docker") is None:
        return False
    try:
        return subprocess.run(["docker", "info"], capture_output=True, check=False, timeout=20).returncode == 0
    except subprocess.TimeoutExpired:
        return False


def real_home() -> str:
    try:
        import pwd

        return pwd.getpwuid(os.getuid()).pw_dir
    except (ImportError, KeyError):
        return ""


# --- main ------------------------------------------------------------------------------------


def main() -> None:
    if os.environ.get("SHARPMUSH_STOP_HOOK", "").lower() in ("off", "0", "false"):
        allow()
    if os.environ.get("CI") or os.environ.get("GITHUB_ACTIONS"):
        allow()

    try:
        payload = json.load(sys.stdin)
    except ValueError:
        payload = {}
    session = re.sub(r"[^A-Za-z0-9_-]", "", str(payload.get("session_id", "default"))) or "default"
    start = Path(payload.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR") or os.getcwd())

    top = subprocess.run(["git", "-C", str(start), "rev-parse", "--show-toplevel", "--absolute-git-dir"],
                         capture_output=True, text=True, check=False)
    if top.returncode != 0:
        allow()  # not in a git checkout: nothing to verify
    root_str, git_dir = top.stdout.splitlines()[:2]
    root = Path(root_str)
    state_dir = Path(git_dir) / "claude-stop-hook"
    state_dir.mkdir(exist_ok=True)

    counter = state_dir / f"blocks-{session}"
    if not payload.get("stop_hook_active"):
        counter.unlink(missing_ok=True)  # a fresh attempt to stop, not a retry after a block
    elif counter.exists() and int(counter.read_text() or "0") >= MAX_BLOCKS:
        counter.unlink(missing_ok=True)
        allow(f"Stop hook gave up after {MAX_BLOCKS} consecutive blocks; this turn ended UNVERIFIED.")

    jobs = running_background_jobs()
    if jobs:
        block(
            "Background jobs you started are still running, and ending the turn kills them:\n"
            + "\n".join(jobs)
            + "\nWait for them to finish (Monitor or the task notification), read their results, "
            "then end the turn. Kill any you no longer need.",
            state_dir,
            session,
        )

    base = comparison_base(root)
    files = changed_files(root, base)
    if not files:
        allow()

    cs_files = [f for f in files if f.endswith(".cs") and (root / f).is_file()]
    format_whole_repo = ".editorconfig" in files
    run_tests = os.environ.get("SHARPMUSH_STOP_HOOK_TESTS", "").lower() not in ("off", "0", "false")
    projects = affected_test_projects(root, files) if run_tests else []
    client = run_tests and any(f.startswith(CLIENT_TEST_INPUTS) for f in files)

    skipped = [p for p in projects if Path(p).stem in NEEDS_DOCKER and not docker_usable()]
    projects = [p for p in projects if p not in skipped]

    checks = (["format"] if cs_files or format_whole_repo else []) + projects + (["client"] if client else [])
    if not checks:
        allow()

    stamp = state_dir / "verified"
    current = fingerprint(root, base, files, checks)
    if stamp.exists() and stamp.read_text().strip() == current:
        allow()

    needs_dotnet = "format" in checks or bool(projects)
    if needs_dotnet and find_dotnet(root) is None:
        # A missing SDK is an environment problem, not unverified work; blocking on it would
        # trap the agent. Say so loudly and let CI be the gate.
        allow("Stop hook could not verify: no usable .NET SDK for global.json (`dotnet --version` failed).")

    unverified = []
    if "format" in checks:
        try:
            failure = check_format(root, cs_files, format_whole_repo, state_dir / "format.log")
        except FormatUnavailable as error:
            # A broken tool must not block (CI's format job is authoritative), but it must not
            # be remembered as a pass either.
            failure = None
            unverified.append(str(error))
        if failure:
            block(failure, state_dir, session)

    if projects or client:
        failure = check_tests(root, projects, client, state_dir)
        if failure:
            block(failure, state_dir, session)

    note = f" Not run (no Docker; CI runs them): {', '.join(Path(p).stem for p in skipped)}." if skipped else ""
    if unverified:
        passed = [c for c in checks if c != "format"]
        allow(f"Stop hook could not verify formatting: {'; '.join(unverified)}. Passed: {', '.join(Path(c).stem for c in passed) or 'nothing else'}.{note}")
    stamp.write_text(current)
    allow(f"Stop hook verified: {', '.join(Path(c).stem for c in checks)}.{note}")


if __name__ == "__main__":
    main()
