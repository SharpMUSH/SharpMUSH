# Native-client TLS (opt-in)

The SocketServer can accept TLS from native MU* clients on TCP **4203**. Use
`docker-compose.telnet-tls.yml` after either production base file. The base alone needs no
certificate and opens no TLS listener (`ConnectionServer:TelnetSslPort` defaults to `0`).
This is TLS from the start of the connection, not STARTTLS or HTTPS.

**Plaintext 4201 stays enabled and published.** Passwords sent there are unencrypted. Tell
players to choose 4203, enable their client's TLS/SSL option, and keep certificate validation
enabled. If you do not want legacy plaintext access, separately remove/restrict the 4201
publication and verify it is unreachable externally; this overlay does not make that change.

Docker-published ports can bypass ordinary host firewall rules such as ufw. Use
[Docker-aware filtering](https://docs.docker.com/engine/network/packet-filtering-firewalls/)
or remove the unwanted mapping, and verify reachability from outside the host.

TLS terminates in `connectionserver`, not a TCP proxy, so the game retains the socket's client
address. Caddy/Cloudflare continue to handle only web HTTPS and the existing `/ws` route.
The game's `ssl_port` setting advertises the MSSP port; it does not enable the listener.

## 1. Prepare certificates on the Docker host

Obtain a certificate from a CA your players' clients trust, with a DNS subject alternative
name for the hostname they use (for example `telnet.mush.example.com`). Supply:

- `fullchain.pem`: PEM leaf certificate followed by the issuer intermediate certificate(s).
- `privkey.pem`: its matching, unencrypted PEM private key. Keep it confidential.

Use a dedicated directory outside the checkout, for example `/etc/sharpmush/telnet-tls`.
The overlay mounts only that directory, read-only, at `/run/sharpmush-tls`. Use real files
there: copying only ACME `live/` symlinks can leave their `archive/` targets outside the mount.
Do not mount the CA client's account credentials or all of `/etc/letsencrypt`.

SocketServer runs as UID/GID **1654:1654**. It needs directory traversal and file-read access,
including after each renewal. A root-owned directory with group `1654`, mode `0750`, and
files owned by root/group `1654`, mode `0640`, is one option on a normal Linux Docker host:

```bash
# Replace source paths with your issued files. install copies symlink targets as real files.
sudo install -d -o root -g 1654 -m 0750 /etc/sharpmush/telnet-tls
sudo install -o root -g 1654 -m 0640 /path/to/fullchain.pem /etc/sharpmush/telnet-tls/fullchain.pem
sudo install -o root -g 1654 -m 0640 /path/to/privkey.pem /etc/sharpmush/telnet-tls/privkey.pem
```

Rootless Docker, user-namespace remapping and SELinux can need host-specific ownership or
labels. Verify access as the container user below; do not make the private key world-readable.
The overlay configures Kestrel's [default PEM certificate and key](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints#configure-https-in-appsettingsjson)
using `ASPNETCORE_Kestrel__Certificates__Default__Path` and `...__KeyPath`.

Certificate acquisition and renewal are operator-managed. This does not provide ACME,
read Caddy's certificate storage, or use a [Cloudflare Origin CA certificate](https://developers.cloudflare.com/ssl/origin-configuration/origin-ca/)
(ordinary native clients do not trust Origin CA). Cloudflare's normal web proxy/tunnel does not provide TLS
for native clients; use a DNS-only hostname pointing at the host, with the certificate for
that hostname, and allow inbound TCP 4203. This exposes the host IP.

### Worked ACME recipe: Debian/Ubuntu with the Cloudflare tunnel

For the tunnel-based stack, public HTTP port 80 is normally unused on the host.
The following uses [Certbot standalone](https://eff-certbot.readthedocs.io/en/stable/using.html#standalone)
with a DNS-only Telnet hostname. Replace the example hostname and email first:

```bash
sudo apt-get update
sudo apt-get install --no-install-recommends certbot
sudo certbot certonly --standalone --non-interactive --agree-tos \
  --email operator@example.com --cert-name telnet.mush.example.com \
  -d telnet.mush.example.com
```

Public TCP **80** must reach this host during issuance and every renewal; check both
host and hosting-provider firewalls, and every published A/AAAA address. Certbot binds
port 80 only while validating. On the Caddy-based stack, Caddy already owns that port:
use an automated DNS challenge or an appropriate webroot instead of stopping the web
front end for this recipe. See Certbot's [DNS plugins](https://eff-certbot.readthedocs.io/en/stable/using.html#dns-plugins).

Install the resulting real files using the permissions above, taking them from
`/etc/letsencrypt/live/telnet.mush.example.com/`. Then select and persist the overlay
below **before** publishing the secure address in a directory or MSSP. A certificate
alone does not enable SharpMUSH's listener.

## 2. Select the base and overlay

From `deploy/`, copy `.env.example` to `.env` if this is a new install and configure the base
stack's domain/tunnel settings. Set this in `.env`:

```dotenv
SHARPMUSH_TELNET_TLS_CERT_DIR=/etc/sharpmush/telnet-tls
```

It is an absolute path on the Docker **host**, not a path inside a container. The overlay
requires a nonempty setting. Its long bind-mount syntax has `create_host_path: false` to
require an existing directory instead of creating one. Check the directory on the Docker host
before invoking Compose too: [some Docker Desktop/WSL versions have not enforced this flag](https://github.com/docker/compose/issues/13602).
A missing, unreadable,
malformed or mismatched certificate/key prevents SocketServer startup; it does not fall back
to plaintext on 4203. Clients must additionally reject expired, untrusted or wrong-hostname
certificates during the handshake checks below. `docker compose config` checks the model,
not filesystem permissions or certificate validity, so also run the preflight and handshake checks.

Choose **one** pair (the base must come first):

```bash
# Caddy/web HTTPS:
export COMPOSE_FILE=docker-compose.prod.yml:docker-compose.telnet-tls.yml

# OR Cloudflare/web HTTPS:
export COMPOSE_FILE=docker-compose.cloudflare.yml:docker-compose.telnet-tls.yml
```

The equivalent explicit commands for configuration validation are:

```bash
docker compose -f docker-compose.prod.yml -f docker-compose.telnet-tls.yml config --quiet
docker compose -f docker-compose.cloudflare.yml -f docker-compose.telnet-tls.yml config --quiet
```

Run the command matching your configured base. Keep both files selected for **every** later
`up`, `pull`, `run`, `restart` and `down`, including backups/restores and image updates. An
explicit `-f` overrides `COMPOSE_FILE`: add `-f docker-compose.telnet-tls.yml` to base-only
examples elsewhere in the guide. Starting with the base alone recreates SocketServer without
the TLS configuration. For persistence, set the chosen `COMPOSE_FILE` in `deploy/.env`; shell
exports apply only to that shell. If using `sharpmush.service`, add the overlay `-f` argument
after the base in **all three** `ExecStartPre`, `ExecStart` and `ExecStop` commands too.
For the shipped Cloudflare unit, preserve the checkout's unit with this drop-in:

```bash
sudo install -d /etc/systemd/system/sharpmush.service.d
sudo tee /etc/systemd/system/sharpmush.service.d/telnet-tls.conf >/dev/null <<'UNIT'
[Service]
ExecStartPre=
ExecStartPre=-/usr/bin/docker compose -f docker-compose.cloudflare.yml -f docker-compose.telnet-tls.yml pull --quiet
ExecStart=
ExecStart=/usr/bin/docker compose -f docker-compose.cloudflare.yml -f docker-compose.telnet-tls.yml up -d --remove-orphans
ExecStop=
ExecStop=/usr/bin/docker compose -f docker-compose.cloudflare.yml -f docker-compose.telnet-tls.yml down
UNIT
sudo systemctl daemon-reload
```

For Caddy, replace `docker-compose.cloudflare.yml` with `docker-compose.prod.yml`
in each command. Reloading the unit does not restart the game.

```bash
# Announce a maintenance window first if players are already connected.
(
  set -eu
  # Run on the Docker host. Match your .env path; this export also gives Compose the checked path.
  export SHARPMUSH_TELNET_TLS_CERT_DIR=/etc/sharpmush/telnet-tls
  test -d "$SHARPMUSH_TELNET_TLS_CERT_DIR"
  docker compose config --quiet
  # Uses the normal image user, 1654:1654, and opens no listener:
  docker compose run --rm --no-deps --entrypoint sh connectionserver -c \
    'test -r /run/sharpmush-tls/fullchain.pem && test -r /run/sharpmush-tls/privkey.pem'
  docker compose up -d
  docker compose logs --tail=100 connectionserver
)
```

Adding the overlay recreates the socket owner and **disconnects all its native and WebSocket
clients**. It adds the certificate mount and 4203 mapping; it retains the renderer socket,
NATS settings, trusted proxies, 4201 mapping and internal 4202 `/ws` endpoint.

## 3. Verify before giving players the address

From a separate machine, verify the certificate chain **and hostname**, not just an open port:

```bash
openssl s_client -connect telnet.mush.example.com:4203 \
  -servername telnet.mush.example.com -verify_hostname telnet.mush.example.com \
  -verify_return_error -brief
```

Require successful verification. For an isolated test CA, add `-CAfile /path/to/test-ca.pem`;
never turn verification off to make production work. A wrong hostname or untrusted issuer
must fail. Use a TLS-capable MU* client to check the normal welcome screen, login and command
output. A TCP connection alone is not a successful TLS/telnet test.

With just one logged-in test connection for the player at a time, `think ssl(me)` should
return `1` over TLS and `0` over plaintext, and `think terminfo(me)` should include `ssl`
only over TLS. Also check the web
portal's `wss://<portal-host>/ws` still works. If retaining 4201, verify its normal plaintext
behavior independently. Do not send real credentials on plaintext just to test it.

If startup fails, inspect the socket owner's logs and verify the exact paths, permissions,
matching key, certificate chain and validity dates. Fix the cause before retrying. Do not
remove the TLS overlay as a workaround while advertising 4203 as secure.

Set the game's MSSP `ssl_port` to the published TLS port only after this succeeds.
Game-directory secure Telnet entries must agree with the actual listener. Grapevine's
web client prefers a configured secure Telnet entry; an unavailable 4203 can prevent
play even when plaintext 4201 works. `ssl_port` advertises a port and cannot open it.

## 4. Renew and restart deliberately

Arrange renewal before expiry with your CA tooling. Copy the renewed full chain and matching
key into the same dedicated directory, preserving the permissions above. Finish installing
both files before restarting, and avoid overlapping a restart with half of a new pair.

**Restart `connectionserver` to load the replacement.** Changing PEM files alone is not the
rotation procedure for this code-configured listener, and this guide promises no hot reload
or zero-disconnection renewal:

```bash
# With the same base + overlay selected; announce the disconnection first.
docker compose restart connectionserver
docker compose logs --tail=100 connectionserver
```

This drops every native and WebSocket connection owned by that service, even if only the
TLS certificate changed. Engine/renderer-only restarts do not load a new socket-owner
certificate. Repeat the verified remote handshake and compare the served certificate's
serial/expiry with the replacement; then reconnect a native client and the web terminal.
If renewal changes the directory or Compose settings, use `docker compose up -d connectionserver`
to recreate it with those settings, rather than `restart`.

### Certbot renewal hook for the worked recipe

The distribution's `certbot.timer` renews certificates, but SharpMUSH also needs the
renewed pair copied and its socket owner restarted. Install this root-owned hook,
replacing the hostname, checkout path and base file for your game. It only handles
this certificate lineage, validates the new pair before replacing either live file,
and serializes concurrent copies. Its restart disconnects native and WebSocket clients;
choose a renewal maintenance policy suitable for your game.

```bash
sudo install -d -m 0755 /etc/letsencrypt/renewal-hooks/deploy
sudo tee /etc/letsencrypt/renewal-hooks/deploy/sharpmush-telnet >/dev/null <<'HOOK'
#!/bin/sh
set -eu
[ "${RENEWED_LINEAGE:-}" = /etc/letsencrypt/live/telnet.mush.example.com ] || exit 0
exec 9>/run/lock/sharpmush-telnet-tls.lock
flock 9
certdir=/etc/sharpmush/telnet-tls
install -o root -g 1654 -m 0640 "$RENEWED_LINEAGE/fullchain.pem" "$certdir/fullchain.pem.new"
install -o root -g 1654 -m 0640 "$RENEWED_LINEAGE/privkey.pem" "$certdir/privkey.pem.new"
certkey=$(openssl x509 -in "$certdir/fullchain.pem.new" -pubkey -noout)
privatekey=$(openssl pkey -in "$certdir/privkey.pem.new" -pubout)
[ "$certkey" = "$privatekey" ]
openssl x509 -in "$certdir/fullchain.pem.new" -noout -checkhost telnet.mush.example.com -checkend 86400
mv "$certdir/fullchain.pem.new" "$certdir/fullchain.pem"
mv "$certdir/privkey.pem.new" "$certdir/privkey.pem"
cd /opt/SharpMUSH/deploy
docker compose -f docker-compose.cloudflare.yml -f docker-compose.telnet-tls.yml restart connectionserver
HOOK
sudo chmod 0700 /etc/letsencrypt/renewal-hooks/deploy/sharpmush-telnet
sudo sh -n /etc/letsencrypt/renewal-hooks/deploy/sharpmush-telnet
sudo systemctl enable --now certbot.timer
sudo certbot renew --cert-name telnet.mush.example.com --dry-run
```

A normal dry run tests ACME renewal without running deploy hooks. To validate the copy
and restart path too, run the following during the announced maintenance window, then
repeat the external TLS and web-terminal checks above. This uses the currently issued
certificate, not the staging certificate:

```bash
sudo env RENEWED_LINEAGE=/etc/letsencrypt/live/telnet.mush.example.com \
  /etc/letsencrypt/renewal-hooks/deploy/sharpmush-telnet
sudo systemctl list-timers certbot.timer
```

Keep public port 80 available for future standalone challenges. Monitor ACME renewal
failures **and deploy-hook errors separately**: [Certbot does not directly return a failure
status for a failed hook](https://eff-certbot.readthedocs.io/en/stable/using.html#renewing-certificates).
A healthy timer or a newly renewed certificate on disk does not prove that SocketServer
loaded it, and a failed deploy hook is not automatically retried once renewal is no longer due.

Configure an external TLS monitor for the public hostname and port, including chain/hostname
validation and an expiry alert (for example, 14 days remaining). After each renewal, compare
the certificate actually served on 4203 with the installed one:

```bash
# Run in bash from a separate machine; pipefail also catches a failed handshake.
set -o pipefail
openssl s_client -connect telnet.mush.example.com:4203 \
  -servername telnet.mush.example.com -verify_hostname telnet.mush.example.com \
  -verify_return_error </dev/null | openssl x509 -noout -serial -dates -checkend 1209600
# On the game host, compare serial/expiry against the externally served certificate:
sudo openssl x509 -in /etc/sharpmush/telnet-tls/fullchain.pem -noout -serial -dates
sudo journalctl -u certbot.service --since '7 days ago'
```

Alert on hook failures in Certbot's logs independently of the service's exit status.
If copying or restarting failed, fix that cause and explicitly rerun the hook with
`RENEWED_LINEAGE` as above; verify the served serial and expiry again. Waiting for the next
scheduled renewal does not retry deployment of an already renewed certificate.

## Regression checks

From the repository root:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_telnet_tls_deployment.py' -v
dotnet run --project SharpMUSH.Tests -- --treenode-filter '/*/*/NativeTelnetTlsTests/*'
```

The Python checks validate documentation/configuration invariants and, when Docker Compose
is installed, render both bases with and without the overlay using dummy settings. They
do not start a production stack or prove Docker's bind mount or certificate permissions.
The TUnit checks exercise the actual SocketServer listener with temporary test certificates.
Both layers complement the isolated deployment/remote client checks above; record skipped
or unrun checks explicitly instead of counting them as passes.
