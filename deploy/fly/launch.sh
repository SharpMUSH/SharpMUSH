#!/usr/bin/env bash
# Launches SharpMUSH on Fly.io from a checkout of this repository:
#
#   fly auth login
#   deploy/fly/launch.sh <app-name> [region]
#
# Safe to run again: it creates only what is missing, then deploys. A dedicated IPv4 address is
# allocated for telnet (Fly charges $2/month for it); IPv6 is free.
set -euo pipefail

app=${1:?usage: deploy/fly/launch.sh <app-name> [region]}
region=${2:-iad}
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
fly=$(command -v fly || command -v flyctl) || { echo "Install flyctl first: https://fly.io/docs/flyctl/install/" >&2; exit 1; }

if ! "$fly" status --app "$app" >/dev/null 2>&1; then
	"$fly" apps create "$app"
fi

if ! "$fly" volumes list --app "$app" | grep -q sharpmush_data; then
	"$fly" volumes create sharpmush_data --app "$app" --region "$region" --size 10 --yes
fi

ips=$("$fly" ips list --app "$app")
if ! grep -q 'v4' <<<"$ips" || ! grep 'v4' <<<"$ips" | grep -vq 'shared'; then
	"$fly" ips allocate-v4 --app "$app" --yes
fi
grep -q 'v6' <<<"$ips" || "$fly" ips allocate-v6 --app "$app"

# One machine: the world's volume can be attached to only one.
"$fly" deploy "$root" --app "$app" --primary-region "$region" --ha=false \
	--config "$root/deploy/fly/fly.toml" --dockerfile "$root/deploy/fly/Dockerfile" \
	--ignorefile "$root/deploy/fly/fly.dockerignore"

echo
echo "Telnet: port 4201 on the dedicated v4 address below"
"$fly" ips list --app "$app"
echo "The setup link is in the log (look for setup_code):"
echo "  $fly logs --app $app --no-tail | grep setup_code"
