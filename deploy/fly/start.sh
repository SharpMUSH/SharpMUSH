#!/bin/bash
# Starts SharpMUSH's processes in one container and stops the container when any of them dies, so
# the host restarts it. Everything the game keeps is under /data (the volume).
#
# SHARPMUSH_ROLE picks which of them this container runs:
#   all       everything, the renderer on a Unix socket (the default)
#   edge      Caddy, NATS and the connection server: every player's socket. Updating it drops them.
#   server    the game server and the world. Updating it drops nobody.
#   renderer  the rendering worker. Updating it drops nobody.
# Run apart, the parts find each other on the host's private network: the edge reaches the server at
# SHARPMUSH_SERVER_ADDRESS (host:port) and the renderer at Rendering__Address, and the server reaches
# NATS at SHARPMUSH_NATS_URL. Nothing but the edge may be public: the others ask for no credentials.
#
# Until the owner finishes /setup, the portal answers only a browser that opened the setup link
# printed in the log, and telnet listens on an inside port only. Set SHARPMUSH_GUARD=off to skip that.
set -euo pipefail

ROLE=${SHARPMUSH_ROLE:-all}
case "$ROLE" in
	all | edge | server | renderer) ;;
	*) echo "sharpmush-start: SHARPMUSH_ROLE must be all, edge, server or renderer, not $ROLE" >&2; exit 1 ;;
esac
runs() { [ "$ROLE" = all ] || [ "$ROLE" = "$1" ]; }

DATA=/data
STATE=$DATA/state
# Fly names the app; Railway names the domain it generated for the service.
DOMAIN=${SHARPMUSH_DOMAIN:-${FLY_APP_NAME:+$FLY_APP_NAME.fly.dev}}
DOMAIN=${DOMAIN:-${RAILWAY_PUBLIC_DOMAIN:-}}
DOMAIN=${DOMAIN:-localhost:8000}
GUARD_PORT=14201
export SHARPMUSH_SERVER_ADDRESS=${SHARPMUSH_SERVER_ADDRESS:-127.0.0.1:8080}

as_app() { setpriv --reuid=app --regid=app --init-groups "$@"; }
# In the background: exec, so the recorded pid is the process itself and a TERM reaches it.
as_app_bg() { exec setpriv --reuid=app --regid=app --init-groups "$@"; }

mkdir -p "$STATE/guard" /run/sharpmush
chown app:app "$DATA" "$STATE" "$STATE/guard" /run/sharpmush
for dir in nats caddy; do mkdir -p "$DATA/$dir" && chown app:app "$DATA/$dir"; done

# --- the first-boot guard, kept where Caddy runs ---------------------------------------------------

guard_on() { runs edge && [ "${SHARPMUSH_GUARD:-on}" != off ] && [ ! -f "$STATE/guard-lifted" ]; }

if guard_on; then
	[ -s "$STATE/setup-code" ] || head -c 12 /dev/urandom | od -An -tx1 | tr -d ' \n' > "$STATE/setup-code"
	code=$(cat "$STATE/setup-code")
	cat > "$STATE/guard/guard.caddy" <<EOF
route {
	@setup_link query setup_code=$code
	header @setup_link Set-Cookie "sharpmush_setup=$code; Path=/; Max-Age=604800; HttpOnly; Secure; SameSite=Lax"
	redir @setup_link /setup 302
	@locked {
		not header Cookie *sharpmush_setup=$code*
		not path /health
	}
	respond @locked "This SharpMUSH server is waiting for its owner to finish setup. The setup link is in the server's log." 403
}
EOF
	TELNET_PORT=$GUARD_PORT
else
	echo "# The guard is off." > "$STATE/guard/guard.caddy"
	TELNET_PORT=4201
fi
chown app:app "$STATE/guard/guard.caddy"

setup_needed() { # 0: setup is pending, 1: finished, 2: no answer. Asked of the server directly,
	# which trusts its proxy to say the request came over HTTPS.
	# /dev/tcp takes an IPv6 address without its brackets.
	local reply host=${SHARPMUSH_SERVER_ADDRESS%:*}
	host=${host#[}
	host=${host%]}
	reply=$(timeout 5 bash -c 'exec 3<>"/dev/tcp/$1/$2" &&
		printf "GET /api/setup/status HTTP/1.0\r\nHost: localhost\r\nX-Forwarded-Proto: https\r\n\r\n" >&3 &&
		cat <&3' _ "$host" "${SHARPMUSH_SERVER_ADDRESS##*:}" 2>/dev/null) || true
	case "$reply" in
		*'"needsSetup":false'*) return 1 ;;
		*'"needsSetup":true'*) return 0 ;;
		*) return 2 ;;
	esac
}

# --- processes -------------------------------------------------------------------------------------

declare -A pid

wait_for() { # wait_for <what> <test command...>
	local what=$1 _
	shift
	for _ in $(seq 1 120); do
		"$@" 2>/dev/null && return 0
		sleep 1
	done
	echo "sharpmush-start: $what did not come up" >&2
	exit 1
}

# Together, the renderer is on a Unix socket; apart, Rendering__Address in the environment names it.
if [ "$ROLE" = all ]; then
	export Rendering__SocketPath=/run/sharpmush/render.sock
	unset Rendering__Address
fi

start_connection() {
	(cd /app/connection && exec env \
		NATS_URL=nats://127.0.0.1:4222 \
		ConnectionServer__TelnetPort="$TELNET_PORT" \
		ForwardedHeaders__KnownProxies__0=127.0.0.1 \
		setpriv --reuid=app --regid=app --init-groups dotnet SharpMUSH.SocketServer.dll) &
	pid[connection]=$!
}

if runs edge; then
	# Every interface, so a server run apart reaches it over the private network.
	as_app_bg nats-server -a :: -js -sd "$DATA/nats" -m 8222 -c /etc/sharpmush/nats.conf &
	pid[nats]=$!
	wait_for nats bash -c 'exec 3<>/dev/tcp/127.0.0.1/4222'
fi

if runs renderer; then
	(cd /app/renderer && exec setpriv --reuid=app --regid=app --init-groups dotnet SharpMUSH.RenderingWorker.dll) &
	pid[renderer]=$!
	if [ "$ROLE" = all ]; then wait_for renderer test -S /run/sharpmush/render.sock; fi
fi

if runs edge; then start_connection; fi

if runs server; then
	if [ "$ROLE" = all ]; then
		server_env=(NATS_URL=nats://127.0.0.1:4222 ASPNETCORE_URLS=http://127.0.0.1:8080
			ForwardedHeaders__KnownProxies__0=127.0.0.1)
	else
		# Apart, the server has no public address: the edge is the only proxy that reaches it, from a
		# private address that changes whenever its machine is replaced.
		server_env=(NATS_URL="${SHARPMUSH_NATS_URL:?the server role needs SHARPMUSH_NATS_URL}"
			ASPNETCORE_URLS=http://+:8080)
		read -ra networks <<<"${SHARPMUSH_TRUSTED_PROXIES:-0.0.0.0/0 ::/0}"
		for i in "${!networks[@]}"; do server_env+=("ForwardedHeaders__KnownNetworks__$i=${networks[$i]}"); done
	fi
	(cd /app/server && exec env \
		SHARPMUSH_LIGHTNING_PATH="$DATA/lightning" \
		SHARPMUSH_LIGHTNING_SYNC=periodic \
		SHARPMUSH_BACKUP_INTERVAL=6h \
		SHARPMUSH_BACKUP_KEEP=2 \
		SHARPMUSH_BACKUP_PATH="$DATA/backup" \
		Wiki__AssetRoot="$DATA/wiki-assets" \
		ASPNETCORE_Kestrel__Certificates__Default__Path= \
		ASPNETCORE_Kestrel__Certificates__Default__Password= \
		"${server_env[@]}" \
		setpriv --reuid=app --regid=app --init-groups dotnet SharpMUSH.Server.dll) &
	pid[server]=$!
fi

if runs edge; then
	XDG_DATA_HOME=$DATA/caddy XDG_CONFIG_HOME=$DATA/caddy as_app_bg caddy run --config /etc/sharpmush/Caddyfile &
	pid[caddy]=$!
fi

stop_all() {
	kill -TERM "${pid[@]}" 2>/dev/null || true
	wait || true
}
trap 'stop_all; exit 0' TERM INT

if guard_on; then
	echo
	echo "  Finish setting up SharpMUSH in your browser (only this link gets in until you do):"
	echo "    https://$DOMAIN/?setup_code=$code"
	if [ -n "${RAILWAY_TCP_PROXY_DOMAIN:-}" ]; then
		echo "  Telnet opens at $RAILWAY_TCP_PROXY_DOMAIN port ${RAILWAY_TCP_PROXY_PORT:-} once setup is finished."
	else
		echo "  Telnet opens on port 4201 once setup is finished."
	fi
	echo
fi

while sleep 5; do
	for name in "${!pid[@]}"; do
		if ! kill -0 "${pid[$name]}" 2>/dev/null; then
			echo "sharpmush-start: $name stopped; stopping the container so it restarts" >&2
			stop_all
			exit 1
		fi
	done
	if guard_on; then
		set +e; setup_needed; needed=$?; set -e
		if [ "$needed" = 1 ]; then
			echo "# The owner finished /setup; the guard is off." > "$STATE/guard/guard.caddy"
			as_app caddy reload --config /etc/sharpmush/Caddyfile --address localhost:2019
			touch "$STATE/guard-lifted"
			# Telnet moves to the public port; nobody is connected yet, so the restart costs nothing.
			TELNET_PORT=4201
			kill -TERM "${pid[connection]}"
			wait "${pid[connection]}" 2>/dev/null || true
			start_connection
			echo "sharpmush-start: setup finished; the portal and telnet are open"
		fi
	fi
done
