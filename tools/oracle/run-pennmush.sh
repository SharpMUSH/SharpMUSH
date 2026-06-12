#!/usr/bin/env bash
# Runs a local PennMUSH as a behavior oracle for SharpMUSH's HTTP handler (help sharphttp).
# PennMUSH is NOT part of this repository (gitignored); see tools/oracle/README.md.
#
# Usage:
#   tools/oracle/run-pennmush.sh [path-to-pennmush]   # start (default ../SharpMUSH/pennmush)
#   tools/oracle/run-pennmush.sh stop                  # stop a previously started oracle
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PID_FILE="/tmp/sharpmush-oracle-pennmush.pid"

if [[ "${1:-}" == "stop" ]]; then
	if [[ -f "${PID_FILE}" ]]; then
		kill "$(cat "${PID_FILE}")" 2>/dev/null && echo "Oracle stopped." || echo "Oracle was not running."
		rm -f "${PID_FILE}"
	else
		echo "No oracle pid file found."
	fi
	exit 0
fi

PENNMUSH_DIR="${1:-$(cd "${REPO_ROOT}/.." && pwd)/SharpMUSH/pennmush}"
NETMUD="${PENNMUSH_DIR}/src/netmud"
GAME_DIR="${PENNMUSH_DIR}/game"
CNF="${GAME_DIR}/mush.cnf"

if [[ ! -x "${NETMUD}" ]]; then
	echo "error: netmud binary not found/executable at ${NETMUD}" >&2
	echo "Build PennMUSH first (./configure && make) or pass its path as the first argument." >&2
	exit 1
fi

# Raise the HTTP rate limit for interactive curl testing. http_handler itself is set
# in-game (@config/set http_handler=...) since it needs a player dbref; see README.md.
if grep -qE '^http_per_second ' "${CNF}"; then
	sed -i 's/^http_per_second .*/http_per_second 100/' "${CNF}"
fi

PORT="$(grep -E '^port ' "${CNF}" | awk '{print $2}')"
echo "Starting PennMUSH oracle from ${GAME_DIR} on port ${PORT:-4201}..."
cd "${GAME_DIR}"
"${NETMUD}" --no-session mush.cnf &
echo $! > "${PID_FILE}"

echo
echo "PennMUSH oracle running (pid $(cat "${PID_FILE}"))."
echo "  telnet localhost ${PORT:-4201}     # connect god, then see README.md for handler setup"
echo "  curl -isS http://localhost:${PORT:-4201}/foo?bar=baz"
echo "  $0 stop"
