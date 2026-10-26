#!/usr/bin/env bash
# Launches SharpMUSH on Fly.io from the published image, with no checkout needed:
#
#   fly auth login
#   curl -fsSL https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/fly/launch.sh \
#     | bash -s -- <app-name> [region] [--dedicated-ipv4]
#
# The app's settings live in <app-name>.fly.toml in the current directory, downloaded the first
# time. Safe to run again: it creates only what is missing, then deploys the newest image, which
# restarts the machine and drops connected players. The app gets IPv6 and a shared IPv4 address,
# both free. The shared IPv4 carries only the web portal, so telnet is reachable over IPv6 alone;
# --dedicated-ipv4 adds an IPv4 address of the app's own ($2/month at Fly) for telnet.
set -euo pipefail

# Everything runs from main, called on the last line, so a download cut short runs nothing.
main() {
	local usage="usage: launch.sh <app-name> [region] [--dedicated-ipv4]"
	local dedicated=false args=() arg
	for arg in "$@"; do
		case "$arg" in
			--dedicated-ipv4) dedicated=true ;;
			-*) echo "$usage" >&2; exit 1 ;;
			*) args+=("$arg") ;;
		esac
	done
	local app=${args[0]:?$usage}
	local region=${args[1]:-iad}
	local source_url="https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/${SHARPMUSH_SOURCE_REF:-main}"
	# A relative path, which a Windows flyctl run from WSL also understands.
	local config="$app.fly.toml"
	# Under WSL a flyctl installed on Windows is found only by its .exe name.
	local fly
	fly=$(command -v fly || command -v flyctl || command -v fly.exe || command -v flyctl.exe) ||
		{ echo "Install flyctl first: https://fly.io/docs/flyctl/install/" >&2; exit 1; }

	if [ ! -f "$config" ]; then
		curl -fsSL "$source_url/deploy/fly/fly.toml" -o "$config.tmp"
		mv "$config.tmp" "$config"
		echo "Wrote $config; edit it and run this again to change the app's settings."
	fi

	if ! "$fly" status --app "$app" >/dev/null 2>&1; then
		"$fly" apps create "$app"
	fi

	if ! "$fly" volumes list --app "$app" | grep -q sharpmush_data; then
		"$fly" volumes create sharpmush_data --app "$app" --region "$region" --size 10 --yes
	fi

	local ips
	ips=$("$fly" ips list --app "$app")
	if [ "$dedicated" = true ]; then
		grep 'v4' <<<"$ips" | grep -vq 'shared' || "$fly" ips allocate-v4 --app "$app" --yes
	else
		grep -q 'v4' <<<"$ips" || "$fly" ips allocate-v4 --shared --app "$app"
	fi
	grep -q 'v6' <<<"$ips" || "$fly" ips allocate-v6 --app "$app"

	# One machine: the world's volume can be attached to only one.
	"$fly" deploy --app "$app" --config "$config" --primary-region "$region" --ha=false

	echo
	if grep 'v4' <<<"$("$fly" ips list --app "$app")" | grep -vq 'shared'; then
		echo "Telnet: $app.fly.dev port 4201, over IPv4 or IPv6"
	else
		echo "Telnet: $app.fly.dev port 4201, over IPv6 only (run again with --dedicated-ipv4 for IPv4)"
	fi
	"$fly" ips list --app "$app"
	echo "The setup link is in the log (look for setup_code):"
	echo "  $fly logs --app $app --no-tail | grep setup_code"
}

main "$@"
