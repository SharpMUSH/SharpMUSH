#!/usr/bin/env bash
# SharpMUSH one-click installer: turns a fresh Ubuntu or Debian VPS into a running game.
#
#   curl -fsSL https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/oneclick/install.sh | sudo bash
#
# Every answer is optional and comes from the environment (cloud-init, a StackScript, or typed in):
#   SHARPMUSH_DOMAIN          The portal's domain. Left empty, the game is served at <ip>.sslip.io.
#   CLOUDFLARE_API_TOKEN      With SHARPMUSH_DOMAIN: create the Cloudflare Tunnel, its hostname and a
#                             DNS-only telnet record. Needs Account > Cloudflare Tunnel > Edit,
#                             Zone > DNS > Edit and Zone > Zone > Read. Used once, never stored.
#   CLOUDFLARE_TUNNEL_TOKEN   Or: an existing tunnel's token. Point its public hostname at http://caddy:80.
#   SHARPMUSH_TELNET_HOST     The telnet record the API token creates (default telnet.<domain>).
#   SHARPMUSH_IMAGE_TAG       Image tag to run (default dev).
#   SHARPMUSH_GUARD           "off" serves the portal and telnet to everyone before /setup is done.
#   RESTIC_REPOSITORY, RESTIC_PASSWORD, B2_ACCOUNT_ID, B2_ACCOUNT_KEY, AWS_ACCESS_KEY_ID,
#   AWS_SECRET_ACCESS_KEY     Nightly encrypted backups (deploy/README.md, "Backups").
#   SHARPMUSH_SOURCE_REF      Git ref the stack's files are fetched from when they are not beside
#                             this script (default main).
#
# Commands:
#   install.sh            prepare, then configure (the default)
#   install.sh prepare    Docker, files, images and units; starts nothing (for building VM images)
#   install.sh configure  write .env from the answers, arm the guard, start the stack; safe to repeat
#   install.sh configure --ask   the same, asking for each answer at the terminal
set -euo pipefail

SHARPMUSH_DIR=${SHARPMUSH_DIR:-/opt/sharpmush}
SOURCE_REF=${SHARPMUSH_SOURCE_REF:-main}
SOURCE_URL="https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/${SOURCE_REF}"
# Where this script's own files are; nowhere when it was piped in from curl.
HERE=/nonexistent
if [ -f "${BASH_SOURCE[0]:-}" ]; then HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd); fi

log() { printf '\033[1;36m==>\033[0m %s\n' "$*"; }
die() { printf '\033[1;31mxx\033[0m %s\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "Run this as root (sudo)."

# --- prepare ---------------------------------------------------------------------------------------

fetch() { # fetch <path beside this script> <path in the repo> <destination>
	if [ -f "$HERE/$1" ]; then
		cp "$HERE/$1" "$3"
	else
		curl -fsSL "$SOURCE_URL/$2" -o "$3"
	fi
}

install_packages() {
	export DEBIAN_FRONTEND=noninteractive
	apt-get update -q
	apt-get install -yq curl ca-certificates jq ufw openssl
	if ! command -v docker >/dev/null || ! docker compose version >/dev/null 2>&1; then
		log "Installing Docker"
		curl -fsSL https://get.docker.com | sh
	fi
	systemctl enable --now docker
}

write_files() {
	log "Writing the stack to $SHARPMUSH_DIR"
	mkdir -p "$SHARPMUSH_DIR"/{guard,state,bin}
	fetch compose.yml deploy/oneclick/compose.yml "$SHARPMUSH_DIR/compose.yml"
	fetch Caddyfile deploy/oneclick/Caddyfile "$SHARPMUSH_DIR/Caddyfile"
	fetch sharpmush-guard deploy/oneclick/sharpmush-guard "$SHARPMUSH_DIR/bin/sharpmush-guard"
	if [ -f "$HERE/nats.conf" ]; then cp "$HERE/nats.conf" "$SHARPMUSH_DIR/nats.conf"
	elif [ -f "$HERE/../../nats.conf" ]; then cp "$HERE/../../nats.conf" "$SHARPMUSH_DIR/nats.conf"
	else curl -fsSL "$SOURCE_URL/nats.conf" -o "$SHARPMUSH_DIR/nats.conf"; fi
	# This script itself, so `sharpmush-configure` can be run again later.
	if [ -f "$HERE/install.sh" ]; then cp "$HERE/install.sh" "$SHARPMUSH_DIR/bin/install.sh"
	else curl -fsSL "$SOURCE_URL/deploy/oneclick/install.sh" -o "$SHARPMUSH_DIR/bin/install.sh"; fi
	chmod 755 "$SHARPMUSH_DIR/bin/"*
	[ -f "$SHARPMUSH_DIR/guard/guard.caddy" ] || echo "# No guard yet." > "$SHARPMUSH_DIR/guard/guard.caddy"
	ln -sf "$SHARPMUSH_DIR/bin/install.sh" /usr/local/bin/sharpmush-install
	cat > /usr/local/bin/sharpmush-configure <<EOF
#!/bin/sh
exec "$SHARPMUSH_DIR/bin/install.sh" configure --ask "\$@"
EOF
	chmod 755 /usr/local/bin/sharpmush-configure
}

write_units() {
	cat > /etc/systemd/system/sharpmush.service <<EOF
[Unit]
Description=SharpMUSH stack (docker compose)
Requires=docker.service
After=docker.service network-online.target
Wants=network-online.target
ConditionPathExists=$SHARPMUSH_DIR/.env

[Service]
Type=oneshot
RemainAfterExit=yes
WorkingDirectory=$SHARPMUSH_DIR
ExecStartPre=-/usr/bin/docker compose pull --quiet
ExecStart=/usr/bin/docker compose up -d --remove-orphans
ExecStop=/usr/bin/docker compose down
TimeoutStartSec=0

[Install]
WantedBy=multi-user.target
EOF
	cat > /etc/systemd/system/sharpmush-guard.service <<EOF
[Unit]
Description=Open SharpMUSH to everyone once its owner has finished /setup
After=sharpmush.service

[Service]
Type=oneshot
ExecStart=$SHARPMUSH_DIR/bin/sharpmush-guard
EOF
	cat > /etc/systemd/system/sharpmush-guard.timer <<'EOF'
[Unit]
Description=Check every 15 seconds whether SharpMUSH's /setup is finished

[Timer]
OnBootSec=30s
OnUnitActiveSec=15s
AccuracySec=1s

[Install]
WantedBy=timers.target
EOF
	systemctl daemon-reload
	systemctl enable sharpmush.service
}

write_motd() {
	cat > /etc/update-motd.d/99-sharpmush <<EOF
#!/bin/sh
[ -f $SHARPMUSH_DIR/state/summary.txt ] && cat $SHARPMUSH_DIR/state/summary.txt
exit 0
EOF
	chmod 755 /etc/update-motd.d/99-sharpmush
}

open_firewall() {
	# Docker publishes its ports ahead of ufw, so this is not what keeps telnet closed during the guard
	# (the 127.0.0.1 binding is). It keeps everything else on the box closed.
	ufw allow OpenSSH >/dev/null
	ufw allow 80/tcp >/dev/null
	ufw allow 443/tcp >/dev/null
	ufw allow 443/udp >/dev/null
	ufw allow 4201/tcp >/dev/null
	ufw --force enable >/dev/null
}

pull_images() {
	log "Pulling images (tag ${SHARPMUSH_IMAGE_TAG:-dev})"
	(cd "$SHARPMUSH_DIR" && SHARPMUSH_SITE=:80 SHARPMUSH_IMAGE_TAG=${SHARPMUSH_IMAGE_TAG:-dev} \
		docker compose --profile cloudflare --profile backup pull --quiet)
}

prepare() {
	install_packages
	write_files
	write_units
	write_motd
	open_firewall
	pull_images
}

# --- configure -------------------------------------------------------------------------------------

# The answers from an earlier run stay unless this run gives new ones.
load_previous() {
	[ -f "$SHARPMUSH_DIR/.env" ] || return 0
	local key value
	while IFS='=' read -r key value; do
		case "$key" in ''|\#*) continue ;; esac
		[ -n "${!key:-}" ] || export "$key=$value"
	done < "$SHARPMUSH_DIR/.env"
}

ask() { # ask <variable> <question> [secret]
	local current=${!1:-} shown reply
	shown=$current
	[ -n "${3:-}" ] && [ -n "$current" ] && shown="set"
	read -r -p "$2${shown:+ [$shown]}: " reply < /dev/tty || true
	case "$reply" in
		'') ;;
		none) export "$1=" ;;
		*) export "$1=$reply" ;;
	esac
	return 0
}

ask_all() {
	echo
	echo "SharpMUSH setup. Press Enter to keep the value in brackets, or type none to clear it."
	echo "Leave the domain empty to start on this server's address at sslip.io."
	ask SHARPMUSH_DOMAIN "Domain for the web portal (e.g. mush.example.com)"
	if [ -n "${SHARPMUSH_DOMAIN:-}" ] && [[ "$SHARPMUSH_DOMAIN" != *.sslip.io ]]; then
		echo "Optional: a Cloudflare API token sets up the tunnel and DNS for that domain."
		ask CLOUDFLARE_API_TOKEN "Cloudflare API token (Enter to skip)" secret
		if [ -z "${CLOUDFLARE_API_TOKEN:-}" ]; then
			ask CLOUDFLARE_TUNNEL_TOKEN "Existing Cloudflare tunnel token (Enter to skip and use Let's Encrypt)" secret
		fi
	fi
}

public_ip() {
	local ip
	ip=$(ip -4 route get 1.1.1.1 2>/dev/null | awk '{for (i = 1; i < NF; i++) if ($i == "src") print $(i + 1)}')
	case "$ip" in ''|10.*|192.168.*|172.1[6-9].*|172.2[0-9].*|172.3[01].*|100.6[4-9].*|100.[7-9][0-9].*|100.1[01][0-9].*|100.12[0-7].*)
		ip=$(curl -4 -fsS --max-time 10 https://api.ipify.org || true) ;;
	esac
	[ -n "$ip" ] || die "Could not find this server's public IPv4 address."
	echo "$ip"
}

cf_api() { # cf_api <method> <path> [json]
	local response
	response=$(curl -sS -X "$1" "https://api.cloudflare.com/client/v4$2" \
		-H "Authorization: Bearer $CLOUDFLARE_API_TOKEN" -H "Content-Type: application/json" \
		${3:+--data "$3"})
	if [ "$(jq -r '.success' <<<"$response")" != "true" ]; then
		die "Cloudflare refused $1 $2: $(jq -c '.errors' <<<"$response" 2>/dev/null || echo "$response")"
	fi
	echo "$response"
}

cf_dns_upsert() { # cf_dns_upsert <zone id> <type> <name> <content> <proxied>
	local body existing
	body=$(jq -nc --arg t "$2" --arg n "$3" --arg c "$4" --argjson p "$5" \
		'{type: $t, name: $n, content: $c, proxied: $p, ttl: 1, comment: "SharpMUSH"}')
	existing=$(cf_api GET "/zones/$1/dns_records?name=$3" | jq -r '.result[0].id // empty')
	if [ -n "$existing" ]; then
		cf_api PUT "/zones/$1/dns_records/$existing" "$body" >/dev/null
	else
		cf_api POST "/zones/$1/dns_records" "$body" >/dev/null
	fi
}

cloudflare_setup() {
	local domain=$SHARPMUSH_DOMAIN candidate=$SHARPMUSH_DOMAIN zone='' zone_id account tunnel name
	log "Setting up Cloudflare for $domain"
	while [[ "$candidate" == *.* ]]; do
		zone=$(cf_api GET "/zones?name=$candidate" | jq -c '.result[0] // empty')
		[ -n "$zone" ] && break
		candidate=${candidate#*.}
	done
	[ -n "$zone" ] || die "None of $domain's parent domains is a zone this Cloudflare token can read."
	zone_id=$(jq -r '.id' <<<"$zone")
	account=$(jq -r '.account.id' <<<"$zone")

	name="sharpmush-$domain"
	tunnel=$(cf_api GET "/accounts/$account/cfd_tunnel?name=$name&is_deleted=false" | jq -r '.result[0].id // empty')
	if [ -z "$tunnel" ]; then
		tunnel=$(cf_api POST "/accounts/$account/cfd_tunnel" \
			"$(jq -nc --arg n "$name" '{name: $n, config_src: "cloudflare"}')" | jq -r '.result.id')
	fi
	cf_api PUT "/accounts/$account/cfd_tunnel/$tunnel/configurations" \
		"$(jq -nc --arg h "$domain" '{config: {ingress: [{hostname: $h, service: "http://caddy:80"}, {service: "http_status:404"}]}}')" >/dev/null
	CLOUDFLARE_TUNNEL_TOKEN=$(cf_api GET "/accounts/$account/cfd_tunnel/$tunnel/token" | jq -r '.result')

	cf_dns_upsert "$zone_id" CNAME "$domain" "$tunnel.cfargotunnel.com" true
	SHARPMUSH_TELNET_HOST=${SHARPMUSH_TELNET_HOST:-telnet.$domain}
	cf_dns_upsert "$zone_id" A "$SHARPMUSH_TELNET_HOST" "$(public_ip)" false
}

arm_guard() {
	if [ "${SHARPMUSH_GUARD:-on}" = off ] || [ -f "$SHARPMUSH_DIR/state/guard-lifted" ]; then
		echo "# The guard is off." > "$SHARPMUSH_DIR/guard/guard.caddy"
		SHARPMUSH_TELNET_BIND=0.0.0.0
		systemctl disable --now sharpmush-guard.timer 2>/dev/null || true
		return
	fi
	local code
	[ -s "$SHARPMUSH_DIR/state/setup-code" ] || openssl rand -hex 12 > "$SHARPMUSH_DIR/state/setup-code"
	chmod 600 "$SHARPMUSH_DIR/state/setup-code"
	code=$(cat "$SHARPMUSH_DIR/state/setup-code")
	cat > "$SHARPMUSH_DIR/guard/guard.caddy" <<EOF
# Written by install.sh. Until the owner finishes /setup, only a browser that opened the setup link
# (which sets this cookie) reaches the game. sharpmush-guard replaces this file when /setup is done.
route {
	@setup_link query setup_code=$code
	header @setup_link Set-Cookie "sharpmush_setup=$code; Path=/; Max-Age=604800; HttpOnly; Secure; SameSite=Lax"
	redir @setup_link /setup 302
	@locked {
		not header Cookie *sharpmush_setup=$code*
		not path /health
	}
	respond @locked "This SharpMUSH server is waiting for its owner to finish setup. The setup link is shown when you log in to the server over SSH." 403
}
EOF
	SHARPMUSH_TELNET_BIND=127.0.0.1
	systemctl enable --now sharpmush-guard.timer
}

write_env() {
	local env=$SHARPMUSH_DIR/.env profiles=''
	[ -n "${CLOUDFLARE_TUNNEL_TOKEN:-}" ] && profiles=cloudflare
	[ -n "${RESTIC_REPOSITORY:-}" ] && profiles=${profiles:+$profiles,}backup
	umask 077
	cat > "$env" <<EOF
# Written by $SHARPMUSH_DIR/bin/install.sh. Run sharpmush-configure to change the answers.
SHARPMUSH_DOMAIN=${SHARPMUSH_DOMAIN}
SHARPMUSH_SITE=${SHARPMUSH_SITE}
SHARPMUSH_HTTP_BIND=${SHARPMUSH_HTTP_BIND}
SHARPMUSH_TELNET_BIND=${SHARPMUSH_TELNET_BIND}
SHARPMUSH_TELNET_HOST=${SHARPMUSH_TELNET_HOST:-}
SHARPMUSH_IMAGE_TAG=${SHARPMUSH_IMAGE_TAG:-dev}
SHARPMUSH_GUARD=${SHARPMUSH_GUARD:-on}
CLOUDFLARE_TUNNEL_TOKEN=${CLOUDFLARE_TUNNEL_TOKEN:-}
COMPOSE_PROFILES=${profiles}
RESTIC_REPOSITORY=${RESTIC_REPOSITORY:-}
RESTIC_PASSWORD=${RESTIC_PASSWORD:-}
B2_ACCOUNT_ID=${B2_ACCOUNT_ID:-}
B2_ACCOUNT_KEY=${B2_ACCOUNT_KEY:-}
AWS_ACCESS_KEY_ID=${AWS_ACCESS_KEY_ID:-}
AWS_SECRET_ACCESS_KEY=${AWS_SECRET_ACCESS_KEY:-}
EOF
}

write_summary() {
	local telnet=${SHARPMUSH_TELNET_HOST:-} summary=$SHARPMUSH_DIR/state/summary.txt
	[ -n "$telnet" ] || telnet=$(public_ip)
	{
		echo
		echo "  SharpMUSH  https://$SHARPMUSH_DOMAIN"
		echo "  Telnet     $telnet 4201"
		if [ -f "$SHARPMUSH_DIR/state/setup-code" ] && [ ! -f "$SHARPMUSH_DIR/state/guard-lifted" ] \
			&& [ "${SHARPMUSH_GUARD:-on}" != off ]; then
			echo
			echo "  Finish setup in your browser (only this link gets in until you do):"
			echo "    https://$SHARPMUSH_DOMAIN/?setup_code=$(cat "$SHARPMUSH_DIR/state/setup-code")"
			echo "  Telnet opens to everyone once setup is finished."
		fi
		echo "  Change the domain or Cloudflare settings: sharpmush-configure"
		echo
	} > "$summary"
	chmod 600 "$summary"
}

configure() {
	load_previous
	[ "${1:-}" = --ask ] && ask_all

	if [ -n "${CLOUDFLARE_API_TOKEN:-}" ]; then
		[ -n "${SHARPMUSH_DOMAIN:-}" ] || die "CLOUDFLARE_API_TOKEN needs SHARPMUSH_DOMAIN."
		cloudflare_setup
	fi
	if [ -z "${SHARPMUSH_DOMAIN:-}" ]; then
		[ -z "${CLOUDFLARE_TUNNEL_TOKEN:-}" ] || die "CLOUDFLARE_TUNNEL_TOKEN needs SHARPMUSH_DOMAIN, the hostname the tunnel serves."
		SHARPMUSH_DOMAIN="$(public_ip | tr . -).sslip.io"
		log "No domain given: using $SHARPMUSH_DOMAIN"
	fi
	if [ -n "${CLOUDFLARE_TUNNEL_TOKEN:-}" ]; then
		SHARPMUSH_SITE=:80
		SHARPMUSH_HTTP_BIND=127.0.0.1
	else
		SHARPMUSH_SITE=$SHARPMUSH_DOMAIN
		SHARPMUSH_HTTP_BIND=0.0.0.0
		# Left over from an earlier run in tunnel mode; its profile is off now, so compose no longer sees it.
		docker rm -f sharpmush-cloudflared >/dev/null 2>&1 || true
	fi

	arm_guard
	write_env
	write_summary
	log "Starting SharpMUSH"
	(cd "$SHARPMUSH_DIR" && docker compose up -d --remove-orphans)
	# A changed guard file or site needs Caddy to read its configuration again.
	docker exec sharpmush-caddy caddy reload --config /etc/caddy/Caddyfile >/dev/null 2>&1 || true
	systemctl start sharpmush.service
	cat "$SHARPMUSH_DIR/state/summary.txt"
}

case "${1:-install}" in
	install) prepare; configure "${2:-}" ;;
	prepare) prepare ;;
	configure) shift; configure "${1:-}" ;;
	*) die "Unknown command $1 (install, prepare or configure)." ;;
esac
