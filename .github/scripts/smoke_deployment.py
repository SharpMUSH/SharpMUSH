"""Bounded, destructive-to-fixtures-only deployment smoke for GitHub-hosted CI.

Builds the repository Dockerfiles at the checked-out commit. The production Compose
model and TLS overlay are retained, except for explicit CI isolation: local image
tags, unique resources, loopback ports, internal backing networks, a manual test certificate,
and a local Restic repository. Never use a deployment .env or a production volume.
"""

import argparse
import copy
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import time
import urllib.error
import urllib.request
import ssl


ROOT = Path(__file__).resolve().parents[2]
IMAGES = {
    "sharpmush-server": "Dockerfile",
    "connectionserver": "SharpMUSH.SocketServer/Dockerfile",
    "renderer": "SharpMUSH.RenderingWorker/Dockerfile",
}
SERVICES = ("nats", "renderer", "connectionserver", "sharpmush-server", "caddy", "backup")


def run(command, *, capture=False, timeout=120, check=True):
    return subprocess.run(command, check=check, text=True, capture_output=capture,
                          timeout=timeout, env=clean_environment())


def clean_environment():
    # No inherited cloud credentials, alternate Docker daemon/context, Compose file,
    # deployment variables, or implicit .env may affect this fixture.
    names = ("PATH", "HOME", "USER", "LANG", "LC_ALL", "TMPDIR", "TERM")
    return {key: os.environ[key] for key in names if key in os.environ}


def project_name():
    run_id = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    if os.environ.get("GITHUB_ACTIONS") != "true" or not run_id.isdigit() or not attempt.isdigit():
        raise RuntimeError("This destructive fixture harness only runs in GitHub Actions")
    return f"smush-smoke-{run_id}-{attempt}"


def fixture_directory():
    temporary = Path(os.environ["RUNNER_TEMP"]).resolve()
    return temporary / project_name()


def compose_prefix(project, workdir):
    # `config` otherwise omits the opt-in backup service. Explicit service names
    # on `up` below still keep its cron container from starting automatically.
    return ["docker", "compose", "--project-name", project, "--env-file",
            str(workdir / "empty.env"), "--profile", "backup"]


def port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def isolated_model(source, *, project, workdir, image_tags, ports):
    """Make the permitted isolation changes explicit and unit-testable."""
    model = copy.deepcopy(source)
    model["name"] = project
    if set(model["services"]) != set(SERVICES):
        raise AssertionError("Review isolation before adding/removing production services")
    for name, service in model["services"].items():
        service.pop("container_name", None)
        service.pop("ports", None)
        service.pop("labels", None)
        service["restart"] = "no"
        service["stop_grace_period"] = "10s"
        if name in image_tags:
            service["image"] = image_tags[name]
            service["pull_policy"] = "never"
            service.pop("build", None)
    for name, volume in model.get("volumes", {}).items():
        if volume.get("external"):
            raise AssertionError("External volumes are forbidden")
        volume["name"] = project + "_" + name
    for name, network in model.get("networks", {}).items():
        if network.get("external"):
            raise AssertionError("External networks are forbidden")
        network["name"] = project + "_" + name
        network["internal"] = True
    # Docker can silently omit published ports on an internal-only bridge. A
    # separate, job-scoped ingress bridge gives only the two socket frontends a
    # publishable endpoint; every host mapping still binds loopback. The engine,
    # broker, renderer and backup remain on internal networks only.
    model["networks"]["smoke-ingress"] = {"name": project + "_smoke-ingress", "driver": "bridge"}
    for name in ("caddy", "connectionserver"):
        model["services"][name].setdefault("networks", {})["smoke-ingress"] = None
    model["volumes"]["smoke-repository"] = {"name": project + "_smoke-repository"}
    model["volumes"]["smoke-restore"] = {"name": project + "_smoke-restore"}
    connection = model["services"]["connectionserver"]
    certificate_mounts = [v for v in connection.get("volumes", []) if v["target"] == "/run/sharpmush-tls"]
    if len(certificate_mounts) != 1 or certificate_mounts[0].get("type") != "bind" or not certificate_mounts[0].get("read_only"):
        raise AssertionError("TLS overlay must mount exactly one read-only certificate directory")
    # Some Compose versions omit false values while rendering JSON. Preserve the
    # source overlay's explicit guard when this model is read by Compose again.
    certificate_mounts[0].setdefault("bind", {})["create_host_path"] = False
    connection["ports"] = [
        {"target": target, "published": str(ports[label]), "host_ip": "127.0.0.1", "protocol": "tcp"}
        for target, label in ((4201, "plaintext"), (4203, "tls"))
    ]
    server = model["services"]["sharpmush-server"]
    # Use actual on-demand @backup, never wait for or race the production schedule.
    server["environment"]["SHARPMUSH_BACKUP_INTERVAL"] = ""
    server["environment"]["SHARPMUSH_LIGHTNING_MAPSIZE"] = "268435456"
    caddy = model["services"]["caddy"]
    caddy["ports"] = [{"target": 443, "published": str(ports["https"]),
                       "host_ip": "127.0.0.1", "protocol": "tcp"}]
    caddy["volumes"] = [v for v in caddy["volumes"] if v["target"] != "/etc/caddy/Caddyfile"]
    caddy["volumes"] += [
        {"type": "bind", "source": str(workdir / "Caddyfile"), "target": "/etc/caddy/Caddyfile", "read_only": True},
        {"type": "bind", "source": str(workdir / "certificates"), "target": "/run/smoke-tls", "read_only": True},
    ]
    backup = model["services"]["backup"]
    for key in ("RESTIC_PASSWORD", "B2_ACCOUNT_ID", "B2_ACCOUNT_KEY"):
        backup["environment"].pop(key, None)
    backup["environment"].update({"RESTIC_REPOSITORY": "/repo",
                                  "RESTIC_PASSWORD_FILE": "/run/smoke-restic-password"})
    backup["volumes"] += [
        {"type": "volume", "source": "smoke-repository", "target": "/repo"},
        {"type": "bind", "source": str(workdir / "restic-password"),
         "target": "/run/smoke-restic-password", "read_only": True},
    ]
    return model


def test_caddyfile(original):
    marker = "{$SHARPMUSH_DOMAIN} {"
    if original.count(marker) != 1:
        raise AssertionError("Review the changed production Caddyfile before adapting it")
    # Keep the checked-in /ws and portal routing. This is manual fixture TLS: no
    # ACME, DNS, Caddy local-CA installation, or outside network is involved.
    return original.replace(marker, "https://localhost:443 {\n\ttls /run/smoke-tls/fullchain.pem /run/smoke-tls/privkey.pem", 1)


def wait_ready(https_port, ca_file, timeout=120):
    context = ssl.create_default_context(cafile=str(ca_file))
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    deadline = time.monotonic() + timeout
    last = "not contacted"
    while time.monotonic() < deadline:
        try:
            with urllib.request.urlopen(f"https://localhost:{https_port}/ready", context=context, timeout=5) as response:
                if response.status == 200 and response.read() == b"ready":
                    return
                last = f"HTTP {response.status}, unexpected readiness body"
        except urllib.error.HTTPError as error:
            last = f"HTTP {error.code}"
            time.sleep(2)
        except (OSError, urllib.error.URLError) as error:
            last = type(error).__name__ + ": " + str(error)
            time.sleep(2)
    raise RuntimeError("Disposable stack did not become ready within its deadline: " + last)


def verify_published_ports(compose, ports):
    for service, target, label in (("connectionserver", 4201, "plaintext"),
                                   ("connectionserver", 4203, "tls"), ("caddy", 443, "https")):
        mapped = run(compose + ["port", service, str(target)], capture=True).stdout.strip()
        if mapped != f"127.0.0.1:{ports[label]}":
            raise RuntimeError(f"Docker did not publish {service}:{target} on its expected loopback port")
    print("PASS: Docker published all three endpoints on runner loopback only", flush=True)


def cleanup():
    from smoke_native_tls import cleanup_certificates

    workdir = fixture_directory()
    model = workdir / "compose.json"
    try:
        if model.is_file():
            result = run(compose_prefix(project_name(), workdir) +
                         ["-f", str(model), "down", "--volumes",
                          "--remove-orphans", "--timeout", "10"], check=False, timeout=90)
            if result.returncode:
                raise RuntimeError("Disposable Compose cleanup failed")
    finally:
        # Keys/passwords stay runner-local and never become artifacts. Remove even
        # after a partial startup. The workflow always-step repeats cleanup.
        for relative in ("restic-password", "certificates/privkey.pem"):
            (workdir / relative).unlink(missing_ok=True)
        cleanup_certificates(workdir / "certificates")


def failure_diagnostics(compose):
    """Print status and allowlisted failure categories, never raw application logs."""
    result = run(compose + ["ps", "--all", "--format", "json"], capture=True, check=False)
    # Compose versions return either a JSON list or one JSON object per line.
    try:
        statuses = json.loads(result.stdout)
        if isinstance(statuses, dict):
            statuses = [statuses]
    except ValueError:
        statuses = [json.loads(line) for line in result.stdout.splitlines() if line.strip()]
    for status in statuses:
        print("Container state:", status.get("Service"), status.get("State"), status.get("ExitCode"), flush=True)
    logs = run(compose + ["logs", "--no-color", "--tail", "100"], capture=True, check=False).stdout
    for category in ("OutOfMemoryException", "Permission denied", "UnauthorizedAccessException",
                     "FileNotFoundException", "DirectoryNotFoundException", "CryptographicException",
                     "Address already in use", "Connection refused", "MDB_MAP_FULL",
                     "A compatible .NET SDK was not found", "Unhandled exception"):
        if category.lower() in logs.lower():
            print("Container log failure category:", category, flush=True)


def main():
    from smoke_native_tls import generate_certificates, run_tls_checks
    from smoke_recovery import run_recovery_checks

    project = project_name()
    workdir = fixture_directory()
    workdir.mkdir(mode=0o755, parents=True, exist_ok=False)
    if shutil.disk_usage(workdir).free < 12 * 1024 ** 3:
        raise RuntimeError("Need at least 12 GiB free for three disposable image builds")
    sha = run(["git", "-C", str(ROOT), "rev-parse", "HEAD"], capture=True).stdout.strip()
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or sha != os.environ.get("SMOKE_EXPECTED_SHA"):
        raise RuntimeError("Checkout must match the exact requested commit")
    if run(["docker", "context", "show"], capture=True).stdout.strip() != "default":
        raise RuntimeError("Only the hosted runner's default Docker context is allowed")
    run(["docker", "compose", "version"])
    print(f"Building and testing exact commit {sha}", flush=True)
    images = {name: f"smush-smoke-{name}:{sha}" for name in IMAGES}
    for name, dockerfile in IMAGES.items():
        run(["docker", "build", "--label", f"org.opencontainers.image.revision={sha}",
             "-f", str(ROOT / dockerfile), "-t", images[name], str(ROOT)], timeout=1500)
    builder_image = f"smush-smoke-build:{sha}"
    run(["docker", "build", "--target", "build", "-f", str(ROOT / "Dockerfile"),
         "-t", builder_image, str(ROOT)], timeout=300)
    print("PASS: all production images built from the exact checkout", flush=True)
    ca_file = generate_certificates(workdir / "certificates")
    (workdir / "empty.env").write_text("")
    password = workdir / "restic-password"
    password.write_text(secrets.token_urlsafe(48))
    password.chmod(0o600)
    (workdir / "Caddyfile").write_text(test_caddyfile((ROOT / "deploy/Caddyfile").read_text()))
    ports = {key: port() for key in ("plaintext", "tls", "https")}
    if len(set(ports.values())) != len(ports):
        raise RuntimeError("Ephemeral port collision; no stack was started")
    render = compose_prefix(project, workdir) + ["-f", str(ROOT / "deploy/docker-compose.prod.yml"),
              "-f", str(ROOT / "deploy/docker-compose.telnet-tls.yml"), "config", "--format", "json"]
    environment = clean_environment()
    environment.update({"SHARPMUSH_DOMAIN": "localhost", "SHARPMUSH_TELNET_TLS_CERT_DIR": str(workdir / "certificates")})
    source = json.loads(subprocess.run(render, check=True, capture_output=True, text=True,
                                       env=environment, timeout=30).stdout)
    model = isolated_model(source, project=project, workdir=workdir, image_tags=images, ports=ports)
    (workdir / "compose.json").write_text(json.dumps(model, indent=2))
    compose = compose_prefix(project, workdir) + ["-f", str(workdir / "compose.json")]
    try:
        run(compose + ["up", "-d", "--no-build", "--wait", "--wait-timeout", "120",
                       "nats", "renderer", "connectionserver", "sharpmush-server", "caddy"], timeout=180)
        verify_published_ports(compose, ports)
        wait_ready(ports["https"], ca_file)
        print("PASS: real Compose stack ready through verified HTTPS proxy", flush=True)
        run_tls_checks(compose, tls_port=ports["tls"], plaintext_port=ports["plaintext"],
                       websocket_port=ports["https"], cert_dir=workdir / "certificates")
        wait_ready(ports["https"], ca_file)
        run_recovery_checks(compose, https_port=ports["https"], ca_file=ca_file, workdir=workdir,
                            root=ROOT, builder_image=builder_image)
        print("PASS: disposable deployment smoke completed", flush=True)
    except Exception:
        try:
            failure_diagnostics(compose)
        except Exception as diagnostics_error:
            print("Failure diagnostics unavailable:", type(diagnostics_error).__name__, flush=True)
        raise
    finally:
        cleanup()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cleanup", action="store_true")
    arguments = parser.parse_args()
    if arguments.cleanup:
        cleanup()
    else:
        main()
