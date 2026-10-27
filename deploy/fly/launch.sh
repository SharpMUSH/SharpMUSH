#!/usr/bin/env bash
# Launches SharpMUSH on Fly.io from the published image, with no checkout needed:
#
#   fly auth login
#   curl -fsSL https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/fly/launch.sh \
#     | bash -s -- <game> [region] [--dedicated-ipv4] [--single] [--update-edge]
#
# The game runs as three apps on a private network of their own: <game>, the edge that holds every
# player's socket and the only one with a public address; <game>-server, with the world on its
# volume; and <game>-renderer. Their settings live in <game>.edge.fly.toml, <game>.server.fly.toml
# and <game>.renderer.fly.toml in the current directory, downloaded the first time.
#
# Safe to run again: it creates only what is missing, then deploys the newest image to the server
# and the renderer, which drops nobody. The edge is deployed the first time, and again only with
# --update-edge, which drops everyone connected.
#
# --single runs the game as one app, <game>, with its settings in <game>.fly.toml: cheaper, but
# every update drops everyone connected. A game launched that way stays that way when run again.
#
# The edge gets IPv6 and a shared IPv4 address, both free. The shared IPv4 carries only the web
# portal, so telnet is reachable over IPv6 alone; --dedicated-ipv4 adds an IPv4 address of the
# app's own ($2/month at Fly) for telnet.
set -euo pipefail

# Everything runs from main, called on the last line, so a download cut short runs nothing.
main() {
	local usage="usage: launch.sh <game> [region] [--dedicated-ipv4] [--single] [--update-edge]"
	local dedicated=false single=false update_edge=false args=() arg
	for arg in "$@"; do
		case "$arg" in
			--dedicated-ipv4) dedicated=true ;;
			--single) single=true ;;
			--update-edge) update_edge=true ;;
			-*) echo "$usage" >&2; exit 1 ;;
			*) args+=("$arg") ;;
		esac
	done
	app=${args[0]:?$usage}
	region=${args[1]:-iad}
	[[ $app =~ ^[a-z0-9][a-z0-9-]*$ ]] || { echo "The game's name may hold only a-z, 0-9 and -." >&2; exit 1; }
	source_url="https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/${SHARPMUSH_SOURCE_REF:-main}"
	# Under WSL a flyctl installed on Windows is found only by its .exe name.
	fly=$(command -v fly || command -v flyctl || command -v fly.exe || command -v flyctl.exe) ||
		{ echo "Install flyctl first: https://fly.io/docs/flyctl/install/" >&2; exit 1; }

	# A game already launched keeps its layout.
	if [ -f "$app.fly.toml" ] && [ -f "$app.edge.fly.toml" ]; then
		echo "Both $app.fly.toml and $app.edge.fly.toml are here; keep the one for the game's layout." >&2
		exit 1
	elif [ -f "$app.edge.fly.toml" ] && [ "$single" = true ]; then
		echo "$app runs split ($app.edge.fly.toml); moving it to one app means restoring an @backup there." >&2
		exit 1
	elif [ -f "$app.fly.toml" ]; then
		single=true
	fi

	if [ "$single" = true ]; then
		launch_single
	else
		launch_split "$update_edge"
	fi

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

# download <file in deploy/fly> <local file>: once, with {game} replaced by the game's name.
download() {
	[ -f "$2" ] && return 0
	curl -fsSL "$source_url/deploy/fly/$1" -o "$2.tmp"
	sed -i.bak "s/{game}/$app/g" "$2.tmp" && rm -f "$2.tmp.bak"
	mv "$2.tmp" "$2"
	echo "Wrote $2; edit it and run this again to change that part's settings."
}

# create_app <name> [network]
create_app() {
	if ! "$fly" status --app "$1" >/dev/null 2>&1; then
		"$fly" apps create "$1" ${2:+--network "$2"}
	fi
}

# create_volume <app> <volume> <size in GB>
create_volume() {
	if ! "$fly" volumes list --app "$1" | grep -q "$2"; then
		"$fly" volumes create "$2" --app "$1" --region "$region" --size "$3" --yes
	fi
}

public_addresses() {
	local ips
	ips=$("$fly" ips list --app "$app")
	if [ "$dedicated" = true ]; then
		grep 'v4' <<<"$ips" | grep -vq 'shared' || "$fly" ips allocate-v4 --app "$app" --yes
	else
		grep -q 'v4' <<<"$ips" || "$fly" ips allocate-v4 --shared --app "$app"
	fi
	grep -q 'v6' <<<"$ips" || "$fly" ips allocate-v6 --app "$app"
}

# deploy <app> <config> [flag...]: one machine, since a volume can be attached to only one.
deploy() {
	"$fly" deploy --app "$1" --config "$2" --primary-region "$region" --ha=false "${@:3}"
}

launch_single() {
	# A relative path, which a Windows flyctl run from WSL also understands.
	download fly.toml "$app.fly.toml"
	create_app "$app"
	create_volume "$app" sharpmush_data 10
	public_addresses
	deploy "$app" "$app.fly.toml"
}

launch_split() {
	local update_edge=$1 part
	for part in edge server renderer; do download "$part.fly.toml" "$app.$part.fly.toml"; done
	# Their own network: NATS, the server and the renderer ask for no credentials, so no other app in
	# the organization may reach them.
	create_app "$app" "$app"
	create_app "$app-server" "$app"
	create_app "$app-renderer" "$app"
	create_volume "$app" sharpmush_edge 1
	create_volume "$app-server" sharpmush_data 10
	public_addresses

	# A new game's edge comes first: the server waits for its NATS before it answers. The edge's own
	# health check asks the server, so it is not waited for.
	local fresh=false
	if [ -z "$("$fly" machines list --app "$app" --quiet)" ]; then
		fresh=true
		deploy "$app" "$app.edge.fly.toml" --detach
	fi
	deploy "$app-server" "$app.server.fly.toml"
	deploy "$app-renderer" "$app.renderer.fly.toml"
	if [ "$fresh" = false ] && [ "$update_edge" = true ]; then
		deploy "$app" "$app.edge.fly.toml"
	elif [ "$fresh" = false ]; then
		echo "The edge was left running, so nobody was dropped. --update-edge updates it too, dropping everyone."
	fi
}

main "$@"
