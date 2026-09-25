"""Starts and stops the two servers under test, each isolated in a run directory on free ports."""
from __future__ import annotations

import hashlib
import os
import platform
import shutil
import signal
import socket
import subprocess
import tarfile
import tempfile
import time
import urllib.request
from pathlib import Path

NATS_VERSION = "v2.11.8"


class StartupError(RuntimeError):
    pass


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def wait_for(predicate, what: str, timeout: float, procs=()):
    """Bounded readiness wait: returns as soon as `predicate()` holds, fails on timeout or when a
    watched process has already exited."""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        for p in procs:
            if p.poll() is not None:
                raise StartupError(f"{what}: process exited with {p.returncode} before becoming ready")
        time.sleep(0.05)
    raise StartupError(f"timed out after {timeout:.0f}s waiting for {what}")


def can_connect(port: int) -> bool:
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=1):
            return True
    except OSError:
        return False


def log_contains(path: Path, needle: str) -> bool:
    try:
        return needle in path.read_text(errors="replace")
    except OSError:
        return False


class Process:
    def __init__(self, name: str, argv, cwd: Path, log: Path, env=None):
        self.name = name
        self.log = log
        log.parent.mkdir(parents=True, exist_ok=True)
        self._fh = open(log, "wb")
        self.popen = subprocess.Popen(argv, cwd=cwd, env=env, stdout=self._fh, stderr=subprocess.STDOUT,
                                      start_new_session=True)

    def poll(self):
        return self.popen.poll()

    @property
    def returncode(self):
        return self.popen.returncode

    def stop(self):
        if self.popen.poll() is None:
            try:
                os.killpg(self.popen.pid, signal.SIGTERM)
                self.popen.wait(timeout=15)
            except (ProcessLookupError, subprocess.TimeoutExpired):
                try:
                    os.killpg(self.popen.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
        self._fh.close()


class PennMush:
    """A reference PennMUSH: a private copy of `<pennmush>/game` running the prebuilt netmud."""

    def __init__(self, pennmush_dir: Path, workdir: Path):
        self.src = pennmush_dir
        self.game = workdir / "penn-game"
        self.port = free_port()
        self.proc: Process | None = None
        self.logs = workdir / "logs"

    def start(self):
        netmud = self.src / "src" / "netmud"
        if not netmud.exists():
            raise StartupError(f"{netmud} not found. Build the reference first: "
                               f"cd {self.src} && ./configure && make")
        shutil.copytree(self.src / "game", self.game, symlinks=False,
                        ignore=shutil.ignore_patterns("data", "log", "netmush", "info_slave", "ssl_slave", "save"))
        (self.game / "data").mkdir()
        (self.game / "log").mkdir()
        for name, target in (("netmush", "netmud"), ("info_slave", "info_slave"), ("ssl_slave", "ssl_slave")):
            if (self.src / "src" / target).exists():
                (self.game / name).symlink_to(self.src / "src" / target)
        # Config always comes from the shipped defaults (*.dst). `make` only generates mush.cnf and
        # `make update-conf` the rest, so a checkout's *.cnf depend on which targets ran there: CI
        # had no alias.cnf and lost `function_alias modulo mod`, while a local build did.
        for cnf_name, dst_name in (("mush.cnf", "mushcnf.dst"), ("alias.cnf", "aliascnf.dst"),
                                   ("restrict.cnf", "restrictcnf.dst"), ("names.cnf", "namescnf.dst")):
            shutil.copyfile(self.src / "game" / dst_name, self.game / cnf_name)
        cnf = self.game / "mush.cnf"
        text = cnf.read_text()
        lines = []
        for line in text.splitlines():
            if line.startswith("port "):
                line = f"port {self.port}"
            elif line.startswith("ssl_port "):
                line = "ssl_port 0"
            lines.append(line)
        cnf.write_text("\n".join(lines) + "\n")
        # --disable-socket-quota: netmud allows a connection 100 commands then 1 per second (a
        # compile-time constant); sentinel traffic exhausts that and stalls a session mid-scenario.
        self.proc = Process("pennmush", ["./netmush", "--no-session", "--disable-socket-quota", "mush.cnf"], self.game, self.logs / "pennmush.out")
        # Readiness comes from PennMUSH's own log: a probe connection that closes at once wedges its
        # event loop until the next restart (observed), so nothing may connect early.
        wait_for(lambda: log_contains(self.game / "log" / "netmush.log", "RESTART FINISHED"),
                 "PennMUSH to finish starting", 30, [self.proc])

    def stop(self):
        if self.proc:
            self.proc.stop()

    def dump_flatfile(self, dest: Path):
        """Copy out the database written by @dump as an uncompressed flatfile."""
        import gzip
        src = self.game / "data" / "outdb.gz"
        with gzip.open(src, "rb") as f, open(dest, "wb") as out:
            shutil.copyfileobj(f, out)


def _which(name: str) -> str | None:
    return shutil.which(name)


def fetch_nats(cache: Path) -> Path:
    """nats-server from $NATS_SERVER, PATH, or a checksum-verified pinned release."""
    for cand in (os.environ.get("NATS_SERVER"), _which("nats-server")):
        if cand:
            return Path(cand)
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
        raise StartupError("nats-server not found: install it or set NATS_SERVER (auto-download is linux-amd64 only)")
    binary = cache / f"nats-server-{NATS_VERSION}" / "nats-server"
    if binary.exists():
        return binary
    cache.mkdir(parents=True, exist_ok=True)
    base = f"https://github.com/nats-io/nats-server/releases/download/{NATS_VERSION}"
    archive_name = f"nats-server-{NATS_VERSION}-linux-amd64.tar.gz"
    archive = cache / archive_name
    urllib.request.urlretrieve(f"{base}/{archive_name}", archive)
    sums = urllib.request.urlopen(f"{base}/SHA256SUMS", timeout=60).read().decode()
    expected = next((l.split()[0] for l in sums.splitlines() if l.strip().endswith(archive_name)), None)
    actual = hashlib.sha256(archive.read_bytes()).hexdigest()
    if not expected or expected != actual:
        archive.unlink()
        raise StartupError(f"nats-server checksum mismatch for {archive_name}: expected {expected}, got {actual}")
    with tarfile.open(archive) as t:
        t.extractall(cache / f"extract-{NATS_VERSION}")
    found = next((cache / f"extract-{NATS_VERSION}").rglob("nats-server"))
    binary.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(found), binary)
    binary.chmod(0o755)
    return binary


class SharpMush:
    """SharpMUSH as deployed: nats-server + Server (engine) + ConnectionServer (renderer) +
    SocketServer (telnet), all on private ports, with the world imported from a PennMUSH flatfile."""

    def __init__(self, repo: Path, workdir: Path, cache: Path, dotnet: str = "dotnet", build: bool = True):
        self.repo, self.work, self.cache, self.dotnet, self.build = repo, workdir, cache, dotnet, build
        self.port = free_port()
        self.procs: list[Process] = []
        self.logs = workdir / "logs"

    def _run(self, name, argv, cwd, env_extra):
        env = dict(os.environ)
        env.update(env_extra)
        p = Process(name, argv, cwd, self.logs / f"{name}.out", env)
        self.procs.append(p)
        return p

    def start(self, flatfile: Path):
        if self.build:
            self.logs.mkdir(parents=True, exist_ok=True)
            for project in ("SharpMUSH.Server", "SharpMUSH.SocketServer", "SharpMUSH.ConnectionServer"):
                r = subprocess.run([self.dotnet, "build", "-v", "q", "-nologo", project],
                                   cwd=self.repo, capture_output=True, text=True)
                (self.logs / f"dotnet-build-{project}.out").write_text(r.stdout + r.stderr)
                if r.returncode != 0:
                    raise StartupError(f"dotnet build {project} failed; see {self.logs / f'dotnet-build-{project}.out'}")
        nats_port, http_port, server_port = free_port(), free_port(), free_port()
        nats_dir = self.work / "nats"
        nats_dir.mkdir(parents=True)
        conf = self.work / "nats.conf"
        conf.write_text(f'max_payload: 6291456\njetstream: true\nstore_dir: "{nats_dir}"\nport: {nats_port}\nhost: 127.0.0.1\n')
        nats = self._run("nats", [str(fetch_nats(self.cache)), "-c", str(conf)], self.work, {})
        wait_for(lambda: can_connect(nats_port), "nats-server", 30, [nats])
        nats_url = f"nats://127.0.0.1:{nats_port}"
        # Unix socket paths are limited to 108 bytes, so it cannot live in the (deep) run directory.
        self._sock_dir = Path(tempfile.mkdtemp(prefix="parity-"))
        render_sock = self._sock_dir / "render.sock"
        render = self._run("renderer", [self.dotnet, "run", "--no-build", "--no-launch-profile"], self.repo / "SharpMUSH.ConnectionServer",
                           {"Rendering__SocketPath": str(render_sock)})
        wait_for(render_sock.exists, "the renderer socket", 90, [render])
        world = self.work / "world"
        world.mkdir()
        server = self._run("server", [self.dotnet, "run", "--no-build", "--no-launch-profile"], self.repo / "SharpMUSH.Server",
                           {"NATS_URL": nats_url, "SHARPMUSH_LIGHTNING_PATH": str(world),
                            "ASPNETCORE_URLS": f"http://127.0.0.1:{server_port}",
                            "PENNMUSH_DATABASE_PATH": str(flatfile),
                            "PENNMUSH_CONVERSION_STOP_ON_FAILURE": "true"})
        wait_for(lambda: log_contains(server.log, "PennMUSH database conversion completed successfully"),
                 "the PennMUSH import to complete (see logs/server.out)", 180, [server])
        sock = self._run("socketserver", [self.dotnet, "run", "--no-build", "--no-launch-profile"], self.repo / "SharpMUSH.SocketServer",
                         {"NATS_URL": nats_url, "Rendering__SocketPath": str(render_sock),
                          "ConnectionServer__TelnetPort": str(self.port),
                          "ConnectionServer__HttpPort": str(http_port)})
        wait_for(lambda: can_connect(self.port), "SharpMUSH telnet port", 90, [sock])

    def stop(self):
        for p in reversed(self.procs):
            p.stop()
        if getattr(self, "_sock_dir", None):
            shutil.rmtree(self._sock_dir, ignore_errors=True)
