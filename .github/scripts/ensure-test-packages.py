"""Restore only when a cold/partial NuGet cache cannot satisfy the build artifacts."""

import json
from pathlib import Path
import shutil
import subprocess

# The BUILD job supplies all project.assets.json files. Validate actual package
# presence instead of assuming that a cache-key hit means its contents are complete.
projects = subprocess.check_output(["git", "ls-files", "-z", "*.csproj"]).decode().split("\0")
missing = set()
checked = set()
incomplete_directories = set()
for project in filter(None, projects):
    assets_path = Path(project).parent / "obj" / "project.assets.json"
    if not assets_path.exists():
        # Some templates/examples are intentionally outside the solution build.
        continue
    assets = json.loads(assets_path.read_text())
    package_roots = [Path(root) for root in assets["packageFolders"]]
    for name, library in assets["libraries"].items():
        if library["type"] != "package":
            continue
        if name in checked:
            continue
        checked.add(name)
        if not any(
            all((root / library["path"] / file).is_file() for file in library["files"])
            for root in package_roots
        ):
            missing.add(name)
            incomplete_directories.update(root / library["path"] for root in package_roots)

if missing:
    # NuGet considers an extracted package with a hash marker complete, even if a
    # cached DLL disappeared. Evict incomplete entries so restore extracts them again.
    for directory in incomplete_directories:
        if directory.exists():
            shutil.rmtree(directory)
    print(f"Restoring dependencies: {len(missing)} packages absent from the NuGet cache.", flush=True)
    subprocess.run(["dotnet", "restore"], check=True)
