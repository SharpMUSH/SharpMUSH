#!/usr/bin/env bash
# Fails when the plugin contract surface (the SharpMUSH.Library package) breaks binary compatibility
# with the pinned baseline while PluginContractVersion keeps the same major. See tools/plugin-contract/README.md.
#
# Usage: tools/plugin-contract/check.sh [--keep]
#   --keep  leave the scratch directory (baseline worktree and packages) in place for inspection.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"
here="tools/plugin-contract"
keep=false
[[ "${1:-}" == "--keep" ]] && keep=true

contract_file="SharpMUSH.Library/Plugins/PluginContractVersion.cs"
library_project="SharpMUSH.Library/SharpMUSH.Library.csproj"
generator_project="SharpMUSH.Implementation.Generated/SharpMUSH.Implementation.Generated.csproj"

fail() {
	echo "::error::$1"
	exit 1
}

# "new(3, 0, 0)" in PluginContractVersion.Current -> "3.0.0"
contract_version() {
	sed -nE 's/.*Current = new\(([0-9]+), ([0-9]+), ([0-9]+)\);.*/\1.\2.\3/p' | head -n 1
}

project_version() {
	sed -nE 's|.*<Version>([^<]+)</Version>.*|\1|p' "$1" | head -n 1
}

baseline_commit="$(tr -d '[:space:]' < "$here/baseline-commit")"
[[ "$baseline_commit" =~ ^[0-9a-f]{40}$ ]] || fail "$here/baseline-commit must hold one full commit SHA."

current="$(contract_version < "$contract_file")"
[[ -n "$current" ]] || fail "Could not read PluginContractVersion.Current from $contract_file."
current_major="${current%%.*}"

# The packages carry the contract's major: a plugin author picks the package by it.
for project in "$library_project" "$generator_project"; do
	version="$(project_version "$project")"
	[[ "${version%%.*}" == "$current_major" ]] \
		|| fail "$project has <Version>$version</Version>, but PluginContractVersion.Current is $current. Their majors must match."
done

if ! git cat-file -e "$baseline_commit^{commit}" 2>/dev/null; then
	git fetch --quiet --no-tags --depth=1 origin "$baseline_commit"
fi

baseline="$(git show "$baseline_commit:$contract_file" | contract_version)"
[[ -n "$baseline" ]] || fail "Could not read PluginContractVersion.Current at baseline $baseline_commit."
baseline_major="${baseline%%.*}"

echo "Plugin contract: $current (baseline $baseline at ${baseline_commit:0:12})"

if (( current_major < baseline_major )); then
	fail "PluginContractVersion.Current ($current) is older than the baseline ($baseline)."
fi
if (( current_major > baseline_major )); then
	echo "::notice::Plugin contract major bumped ($baseline -> $current); compatibility with the $baseline baseline is not required. Pin $here/baseline-commit to the release that ships $current."
	exit 0
fi

scratch="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/plugin-contract.XXXXXX")"
cleanup() {
	git worktree remove --force "$scratch/baseline" >/dev/null 2>&1 || true
	$keep || rm -rf "$scratch"
}
trap cleanup EXIT
$keep && echo "Scratch directory: $scratch"

pack_args=(-c Release -p:SkipFormatVerification=true -nologo -v:quiet)

echo "Packing the baseline contract package..."
git worktree add --quiet --detach --no-checkout "$scratch/baseline" "$baseline_commit"
# A sparse checkout (CI's) leaves out the onboarding artwork; so does the baseline, which never reads it.
if [[ "$(git config --get core.sparseCheckout || true)" == "true" ]]; then
	git -C "$scratch/baseline" sparse-checkout set --no-cone '/*' '!/SharpMUSH.Client/wwwroot/images/sharpie/'
fi
git -C "$scratch/baseline" checkout --quiet --detach "$baseline_commit"
dotnet pack "$scratch/baseline/$library_project" "${pack_args[@]}" -o "$scratch/baseline-pkg" >/dev/null \
	|| fail "The baseline commit $baseline_commit did not pack."
baseline_package="$(ls "$scratch"/baseline-pkg/SharpMUSH.Library.*.nupkg)"

echo "Packing the current contract package against it..."
status=0
# The SDK skips validation when its semaphore in obj/ is newer than the package inputs, and the baseline is
# not one of those inputs. A semaphore in the scratch directory makes every run validate.
dotnet pack "$library_project" "${pack_args[@]}" -o "$scratch/current-pkg" \
	-p:EnablePackageValidation=true \
	-p:_ApiCompatValidatePackageSemaphoreFile="$scratch/validate.semaphore" \
	-p:PackageValidationBaselinePath="$baseline_package" \
	> "$scratch/validation.log" 2>&1 || status=$?

breaks="$(grep -oE "error (CP|PKV)[0-9]+: .*" "$scratch/validation.log" | sed -E 's/ \[[^]]*\]$//' | sort -u || true)"
if [[ -n "$breaks" ]]; then
	echo "$breaks"
	count="$(wc -l <<< "$breaks")"
	fail "$count breaking change(s) to the plugin contract $current. Keep the old member or signature beside the new one, or bump PluginContractVersion.Current to $((current_major + 1)).0.0 (and the two package versions with it). See $here/README.md."
fi
if (( status != 0 )); then
	cat "$scratch/validation.log"
	fail "The current contract package did not pack."
fi

echo "No breaking changes to the plugin contract $current."
