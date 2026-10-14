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
