"""Real-image, disposable-volume recovery smoke. Never point this at a live stack.

The caller supplies an isolated Compose project, a trusted local HTTPS proxy, the
server's already-built SDK stage, and local Restic repository/password mounts.
No SDK, Docker, Restic, or live application behavior is mocked by this module.
"""

from __future__ import annotations

import base64
import hashlib
import json
from pathlib import Path
import re
import secrets
import shlex
import ssl
import subprocess
import time
import urllib.error
import urllib.request
import uuid
import zipfile


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def documented_restore(readme: Path, copy_name: str) -> str:
    """Use the operator's exact inner restore shell, selecting one completed copy."""
    require(bool(re.fullmatch(r"\d{8}-\d{6}-\d{3}(?:-\d+)?", copy_name)),
            "The selected LMDB copy is not a completed dated backup")
    section = readme.read_text(encoding="utf-8").split("# 3. Put it in place.", 1)[1]
    match = re.search(r"--entrypoint sh sharpmush-server -c '\n(.*?)'", section, re.S)
    require(match is not None, "The documented recovery shell could not be located")
    script = match.group(1)
    require("REPLACE_WITH_SELECTED_TIMESTAMP" in script,
            "The documented recovery shell has no timestamp placeholder")
    require("lightning.plugins" in script, "The recovery instructions omit installed plugins")
    return script.replace("REPLACE_WITH_SELECTED_TIMESTAMP", copy_name)


def package_fixture(output: Path, plugin_id: str, assembly: str, destination: Path) -> Path:
    """Package only the same fixture DLL/deps files used by the integration suite."""
    files = {}
    for suffix in (".dll", ".deps.json"):
        source = output / (assembly + suffix)
        require(source.is_file(), f"Fixture publish did not produce {source.name}")
        files[source.name] = source.read_bytes()
    files["plugin.json"] = json.dumps(
        {"id": plugin_id, "version": "1.0.0", "entry": assembly + ".dll"}
    ).encode()
    manifest = [f"package: {plugin_id}", 'version: "1.0.0"', "kind: plugin",
                "binaries:", '  min_server_version: ">=2.0"', "  files:"]
    for name, content in files.items():
        manifest.extend((f"    - file: {name}",
                         "      sha256: " + hashlib.sha256(content).hexdigest()))
    files["package.yaml"] = ("\n".join(manifest) + "\n").encode()
    with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for name, content in files.items():
            archive.writestr(name, content)
    return destination


class Api:
    def __init__(self, port: int, ca_file: Path):
        self.base = f"https://localhost:{port}"
        self.context = ssl.create_default_context(cafile=str(ca_file))
        self.token = ""

    def request(self, method: str, path: str, *, body=None, raw: bytes | None = None,
                content_type: str = "application/json", binary: bool = False, timeout: int = 90):
        require(path.startswith("/") and not path.startswith("//"), "Expected a local API path")
        headers = {"Content-Type": content_type}
        if self.token:
            headers["Authorization"] = "Bearer " + self.token
        data = json.dumps(body).encode() if body is not None else raw
        request = urllib.request.Request(self.base + path, data=data, headers=headers, method=method)
        try:
            with urllib.request.urlopen(request, context=self.context, timeout=timeout) as response:
                answer = response.read()
                return answer if binary else json.loads(answer) if answer else None
        except urllib.error.HTTPError as error:
            # Deliberately omit response bodies: auth replies can contain credentials.
            raise RuntimeError(f"{method} {path} returned HTTP {error.code}") from error

    def upload(self, path: str, name: str, data: bytes, content_type: str):
        boundary = "smoke-" + uuid.uuid4().hex
        payload = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; "
                   f"filename=\"{name}\"\r\nContent-Type: {content_type}\r\n\r\n").encode()
        payload += data + f"\r\n--{boundary}--\r\n".encode()
        return self.request("POST", path, raw=payload,
                            content_type="multipart/form-data; boundary=" + boundary)

    def ready(self, timeout: int = 180) -> None:
        deadline = time.monotonic() + timeout
        last = "not contacted"
        while time.monotonic() < deadline:
            try:
                if self.request("GET", "/api/health", timeout=5)["status"] == "ready":
                    return
            except (OSError, ValueError, RuntimeError) as error:
                last = type(error).__name__
            time.sleep(2)
        raise RuntimeError(f"The restored server did not become ready in {timeout}s ({last})")

    def login(self, username: str, password: str) -> str:
        self.token = ""
        result = self.request("POST", "/api/auth/account-login",
                              body={"usernameOrEmail": username, "password": password})
        self.token = result["accountSessionToken"]
        require(bool(self.token), "Account login returned no session token")
        return result["accountId"]

    def command(self, command: str, result: str | None = None):
        response = self.request("POST", "/api/commands", body={"command": command, "result": result})
        require(response["truncated"] is False, "The command response was truncated")
        return response

    def evaluate(self, expression: str) -> str:
        response = self.request("POST", "/api/commands/eval", body={"expression": expression})
        require(response["truncated"] is False, "The evaluation response was truncated")
        return response["result"]


def run_recovery_checks(compose: list[str], *, https_port: int, ca_file: Path,
                        workdir: Path, root: Path, builder_image: str) -> dict:
    """Claim a NEW world, install fixtures, snapshot, damage, restore, and verify.

    Required Compose volumes: app-data, smoke-repository, smoke-restore. The
    caller owns cleanup and preserves service logs on failure. All destructive
    operations below target the validated smoke project's app-data only.
    """
    from smoke_deployment import clean_environment

    def run(arguments: list[str], timeout: int = 180) -> str:
        # Match the orchestrator: inherited cloud credentials and alternate Docker
        # contexts/hosts must never redirect a supposedly disposable operation.
        try:
            completed = subprocess.run(arguments, cwd=root, check=True, capture_output=True,
                                       text=True, timeout=timeout, env=clean_environment())
        except subprocess.CalledProcessError as error:
            with (workdir / "recovery-subprocess-error.log").open("a", encoding="utf-8") as log:
                log.write((error.stdout or "") + "\n" + (error.stderr or "") + "\n")
            raise
        return completed.stdout.strip()

    def dc(*arguments: str, timeout: int = 180) -> str:
        return run([*compose, *arguments], timeout=timeout)

    config = json.loads(dc("config", "--format", "json"))
    project = config["name"]
    require("smoke" in project, "Refusing recovery outside a smoke Compose project")
    for key in ("app-data", "smoke-repository", "smoke-restore"):
        volume = config["volumes"][key]
        require(not volume.get("external") and volume["name"].startswith(project + "_"),
                f"Refusing non-isolated {key} volume")
    backup_env = config["services"]["backup"]["environment"]
    require(backup_env.get("RESTIC_REPOSITORY") == "/repo", "Restic must use the local smoke repository")
    require(backup_env.get("RESTIC_PASSWORD_FILE") == "/run/smoke-restic-password",
            "Restic must use the disposable smoke password file")
    app_mount = next(m for m in config["services"]["sharpmush-server"]["volumes"]
                     if m["target"] == "/app/data")
    require(app_mount["type"] == "volume" and app_mount["source"] == "app-data",
            "Refusing recovery on a non-smoke application data mount")
    restore_volume = config["volumes"]["smoke-restore"]["name"]

    def app_shell(script: str, *, scratch: bool = False) -> str:
        mounts = ["-v", restore_volume + ":/restore"] if scratch else []
        return dc("run", "--rm", "--no-deps", *mounts, "--entrypoint", "sh",
                  "sharpmush-server", "-ec", script)

    def restic(*arguments: str, scratch: bool = False) -> str:
        mounts = ["-v", restore_volume + ":/restore"] if scratch else []
        return dc("run", "--rm", "--no-deps", *mounts, "--entrypoint", "restic",
                  "backup", *arguments, timeout=300)

    def inventory() -> str:
        return app_shell("cd /app/data; "
                         "find lightning.plugins lightning.dataprotection-keys wiki-assets "
                         "-printf '%p %m %y %l\\n'; "
                         "find lightning.plugins lightning.dataprotection-keys wiki-assets "
                         "-type f -exec sha256sum {} \\;")

    fixtures = workdir / "recovery-fixtures"
    fixtures.mkdir(parents=True, exist_ok=True)
    build_commands = []
    for assembly, plugin_id in (("SamplePlugin", "sample"), ("CommandOnlyPlugin", "command-only")):
        project_path = f"SharpMUSH.Tests/Fixtures/{assembly}/{assembly}.csproj"
        build_commands.append("dotnet publish " + shlex.quote(project_path)
                              + " -c Release --no-restore -o /fixture/" + plugin_id)
    build_commands.append("chmod -R a+rwX /fixture")
    run(["docker", "run", "--rm", "--entrypoint", "sh", "-v", str(fixtures.resolve()) + ":/fixture",
         builder_image, "-ec", " && ".join(build_commands)], timeout=600)
    print("PASS: both real plugin fixtures published from the server SDK build stage", flush=True)
    packages = {plugin_id: package_fixture(fixtures / plugin_id, plugin_id, assembly,
                                           workdir / (plugin_id + ".zip"))
                for assembly, plugin_id in (("SamplePlugin", "sample"), ("CommandOnlyPlugin", "command-only"))}

    api = Api(https_port, ca_file)
    api.ready()
    require(api.request("GET", "/api/setup/status")["needsSetup"] is True,
            "Recovery smoke requires an unclaimed throwaway world")
    username, password = "smoke_" + uuid.uuid4().hex[:12], secrets.token_urlsafe(24)
    claimed = api.request("POST", "/api/setup/complete", body={"username": username, "password": password})
    account_id = claimed["accountId"]
    require(api.login(username, password) == account_id, "Initial account login changed identity")
    api.request("POST", "/api/setup/wizard/finish")
    identity = api.evaluate("objid(me)")
    require(identity.startswith("#1:"), "The smoke account is not acting as the seeded administrator")
    marker = "recovered_" + uuid.uuid4().hex
    require(api.command("&SMOKE_RECOVERY me=" + marker, "get(me/SMOKE_RECOVERY)")["result"] == marker,
            "The live LMDB recovery marker was not written")

    for plugin_id, package in packages.items():
        staged = api.upload("/api/packages/plugins/upload", package.name, package.read_bytes(), "application/zip")
        selection = {"remote": staged["remote"], "path": staged["path"], "version": None,
                     "configureAnswers": {}}
        plan = api.request("POST", "/api/packages/plan", body=selection)
        require(plan["packageId"] == plugin_id, "The staged fixture has an unexpected package identity")
        api.request("POST", "/api/packages/apply", body={**selection, "decisions": [], "allowPluginCode": True})

    dc("restart", "sharpmush-server")
    api.ready()
    api.login(username, password)
    require(api.evaluate("pluginadd(2,3)") == "5", "The installed sample plugin did not execute")
    require(api.evaluate("unloadme()") == "42", "The installed command-only plugin did not execute")
    api.request("POST", "/api/packages/plugins/command-only/disable")
    dc("restart", "sharpmush-server")
    api.ready()
    api.login(username, password)

    def verify_plugins() -> dict:
        response = api.request("GET", "/api/packages/plugins")
        plugins = {p["id"]: p for p in response["plugins"]}
        active, disabled = plugins["sample"], plugins["command-only"]
        require(active["status"] in (0, "Loaded", "loaded") and active["enabled"] is True,
                "The installed sample plugin is not loaded and enabled")
        require(disabled["status"] in (1, "Disabled", "disabled") and disabled["enabled"] is False,
                "The command-only plugin did not remain disabled")
        require(active["origin"] in (1, "Installed", "installed"), "The sample plugin is not installed")
        require(api.evaluate("pluginadd(2,3)") == "5", "The loaded plugin's function did not execute")
        require(api.evaluate("unloadme()") != "42", "The disabled plugin's function still executes")
        return {"active": active, "disabled": disabled}

    verify_plugins()
    print("PASS: installed plugin executes and disabled plugin remains off after restart", flush=True)
    png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j1ioAAAAASUVORK5CYII=")
    asset = api.upload("/api/wiki-assets", "recovery.png", png, "image/png")
    require(api.request("GET", asset["url"], binary=True) == png, "The seeded wiki asset did not round-trip")
    waiting = api.upload("/api/packages/plugins/upload", packages["command-only"].name,
                         packages["command-only"].read_bytes(), "application/zip")
    backup_answer = api.command("@backup")
    written = re.search(r"Backup (\d{8}-\d{6}-\d{3}(?:-\d+)?) written", "\n".join(backup_answer["output"]))
    require(written is not None, "@backup did not report a completed LMDB copy")
    copy_name = written.group(1)
    restore_script = documented_restore(root / "deploy" / "README.md", copy_name)
    print("PASS: supported @backup produced a completed real LMDB copy", flush=True)

    # Quiesce filesystem state; LMDB itself was copied by the supported @backup command.
    dc("stop", "sharpmush-server", "connectionserver")
    before_files = "\n".join(sorted(inventory().splitlines()))
    require(".uploads/" in before_files and "plugins.state.json" in before_files,
            "The fixture does not contain hidden upload and disabled-plugin state")
    require("lightning.dataprotection-keys/" in before_files,
            "The running server did not create a persisted data-protection key ring")
    restic("init")
    sources = shlex.split(backup_env["RESTIC_BACKUP_SOURCES"])
    require(set(sources) == {"/data/backup", "/data/wiki-assets", "/data/lightning.dataprotection-keys",
                             "/data/lightning.plugins"}, "The backup service's configured sources changed")
    restic("backup", *sources, "--exclude", "/data/backup/.incoming-*", "--tag", "recovery-smoke")
    snapshots = json.loads(restic("snapshots", "--json"))
    require(len(snapshots) == 1, "The smoke repository is not fresh or snapshot creation was ambiguous")
    snapshot_id = snapshots[0]["id"]

    dc("start", "connectionserver", "sharpmush-server")
    api.ready()
    api.login(username, password)
    require(api.command("&SMOKE_RECOVERY me=after_snapshot", "get(me/SMOKE_RECOVERY)")["result"] == "after_snapshot",
            "The newer world mutation did not persist before recovery")
    dc("stop", "sharpmush-server", "connectionserver")
    app_shell("rm -rf /app/data/lightning.plugins /app/data/wiki-assets /app/data/lightning.dataprotection-keys; "
              "mkdir -p /app/data/lightning.plugins/stale-after-snapshot; "
              "printf stale > /app/data/lightning.plugins/stale-after-snapshot/Stale.dll")
    require(not app_shell("find /restore -mindepth 1 -print -quit", scratch=True),
            "Restore scratch volume must be fresh and empty")
    restic("restore", snapshot_id, "--target", "/restore", scratch=True)
    app_shell(restore_script, scratch=True)
    require("\n".join(sorted(inventory().splitlines())) == before_files,
            "Restored content/modes differ: plugin/upload/key/asset data or stale code remains")
    print("PASS: local Restic snapshot restored by the documented shell, including hidden files and modes", flush=True)
    dc("start", "connectionserver", "sharpmush-server")
    api.ready()
    require(api.login(username, password) == account_id, "The restored account could not log in with its original identity")
    require(api.evaluate("objid(me)") == identity, "The restored world changed the administrator's object identity")
    require(api.evaluate("get(me/SMOKE_RECOVERY)") == marker, "The restored LMDB world did not recover its saved marker")
    require(api.request("GET", asset["url"], binary=True) == png, "The restored wiki asset is not served correctly")
    plugin_result = verify_plugins()
    pending_plan = api.request("POST", "/api/packages/plan", body={"remote": waiting["remote"],
                               "path": waiting["path"], "version": None, "configureAnswers": {}})
    require(pending_plan["packageId"] == "command-only", "The restored hidden upload cannot be planned")
    print("PASS: restored login, world marker, wiki asset, loaded/disabled plugins and pending upload", flush=True)
    result = {"world_copy": copy_name, "snapshot_id": snapshot_id, "world_identity": identity,
              "login_restored": True, "world_marker_restored": True, "wiki_asset_restored": True,
              "plugin_files_and_keys_restored": True, "stale_plugin_removed": True,
              "hidden_upload_usable": True, "plugins": plugin_result}
    (workdir / "recovery-result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result
