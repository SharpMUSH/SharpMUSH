#!/usr/bin/env bash
# Fixture-backed semantic load: builds one issued fixture world from a dataset, then measures each rate in
# its own fresh process (a cold pass straight after process start, then a warm pass), against that world.
# usage: run-fixture.sh <dataset-dir> <label> <output-root> [phase-seconds] [rates...]
set -euo pipefail
dataset=$1; label=$2; root=$3; seconds=${4:-600}; shift $(( $# < 4 ? $# : 4 ))
rates=("$@"); [ ${#rates[@]} -eq 0 ] && rates=(0 5 20 50)
repo=$(cd "$(dirname "$0")/../.." && pwd)
lock=${SEMANTIC_BUILD_LOCK:-/temp/codex/sharpmush-build.lock}
out="$root/$label"; mkdir -p "$out"
run() { # spec-file log
  SHARPMUSH_SEMANTIC_LOAD=$1 flock --close "$lock" timeout -k 30 10800 dotnet run --no-build --project "$repo/SharpMUSH.Tests" -- \
    --treenode-filter '/*/*/SemanticLoadMeasurement/*' --no-ansi > "$2" 2>&1
}
spec() { # mode output rates-json final
  printf '{"Mode":"%s","Label":"%s","Dataset":"%s","Output":"%s","World":"%s","Rates":%s,"PhaseSeconds":%s,"CommandsPerSecond":10,"Rebuild":true,"Final":%s}\n' \
    "$1" "$label" "$dataset" "$2" "$out/build" "$3" "$seconds" "$4"
}
# A world already built for this label is measured again rather than rebuilt.
if [ ! -f "$out/build/build.json" ]; then
  spec build "$out/build" '[]' false > "$out/build.spec.json"
  run "$out/build.spec.json" "$out/build.log" || { echo "build failed: $out/build.log"; exit 1; }
fi
last=${rates[-1]}
for rate in "${rates[@]}"; do
  final=false; [ "$rate" = "$last" ] && final=true
  spec measure "$out/rate-$rate" "[$rate]" "$final" > "$out/rate-$rate.spec.json"
  run "$out/rate-$rate.spec.json" "$out/rate-$rate.log" || echo "rate $rate failed: $out/rate-$rate.log"
done
