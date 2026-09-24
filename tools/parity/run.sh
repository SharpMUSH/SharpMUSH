#!/usr/bin/env bash
# One-command differential run: PennMUSH vs SharpMUSH. See tools/parity/README.md.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
exec python3 -m parity "$@"
