#!/usr/bin/env bash
set -euo pipefail

# Run at the repository root after the solution build. Tar preserves apphost/native
# permissions and the repository-relative layout used by plugin and content tests.
archive_dir="${1:?Pass an archive output directory}"
mkdir -p "$archive_dir"

for project in SharpMUSH.Tests SharpMUSH.Tests.BUnit SharpMUSH.Tests.ScenePlugin SharpMUSH.Tests.Integration; do
  test -d "$project/bin/Debug"
  tar -I 'gzip -1' -cf "$archive_dir/$project.tar.gz" "$project/bin/Debug"
done

# dotnet run --no-build still evaluates the project, so retain restored MSBuild
# metadata. ASP.NET manifests also point at generated client bin/obj assets.
# NuGet packages are restored by setup-dotnet's cache in each consuming job.
mapfile -d '' projects < <(git ls-files -z '*.csproj')
support_paths=(SharpMUSH.Client/bin/Debug)
for project in "${projects[@]}"; do
  intermediate="${project%/*}/obj"
  if [[ -d "$intermediate" ]]; then
    support_paths+=("$intermediate")
  fi
done
tar -I 'gzip -1' -cf "$archive_dir/test-build-support.tar.gz" "${support_paths[@]}"
