# Deploying SharpMUSH (hobby scale, single host)

This stack runs the whole game on one small VM (~€4–6/mo on a budget VPS such as
Hetzner CX22/CAX11, or $0 on Oracle Cloud Always Free ARM). It uses **LMDB embedded**
through Lightning.NET (one directory on disk — no separate database server), **NATS**
for messaging, and a **restic** sidecar for nightly encrypted backups.

Everything runs under Docker Compose. **Kubernetes is not needed** at this scale.

## What's here

Two entry points, pick one based on how you terminate TLS:

| File | Purpose |
|------|---------|
| `docker-compose.prod.yml` | The stack: nats, connectionserver, sharpmush-server, backup, and **Caddy** for TLS. Use this if the box faces the internet directly. |
| `docker-compose.cloudflare.yml` | Same stack but fronted by a **Cloudflare Tunnel** instead of Caddy (no open web ports, hidden origin IP). See the Cloudflare section below. |
| `Caddyfile` | Automatic-HTTPS reverse proxy config used by `docker-compose.prod.yml` |
| `sharpmush.service` | Optional systemd unit — brings the stack up from the compose file on boot |
| `.env.example` | Template for secrets/config — copy to `.env` and fill in |
| `.gitignore` | Keeps your real `.env` out of git |
| `../nats.conf` | NATS server config (repo root, shared with the dev stack). Sets `max_payload` to 6 MB — a server-level limit with no CLI flag, so it has to come from a file. Both compose files bind-mount it. |
| `README.md` | This file |

## First-time setup

```bash
cd deploy
cp .env.example .env
# Edit .env: set your domain.

# OPTIONAL — backups. Skip this whole block and the stack runs fine without them.
# Set the restic/B2 credentials in .env, uncomment COMPOSE_PROFILES=backup, then
# initialise the repository once (creates the encrypted repo in your bucket):
#   openssl rand -base64 32   # for RESTIC_PASSWORD  (SAVE THIS — losing it makes backups unrecoverable)
docker compose -f docker-compose.prod.yml run --rm backup restic init

# Build and start everything:
docker compose -f docker-compose.prod.yml up -d --build
```

The web portal comes up on `https://<your-domain>` (via Caddy) and telnet on port `4201`.
Then open `https://<your-domain>/setup` straight away: the first visitor claims the admin
account linked to `#1`.

### Socket worker identity and connection capacity

SocketServer and renderer run as UID/GID `1654:1654`. New `render-socket` volumes
inherit that ownership from the images; the Kubernetes example uses `fsGroup: 1654`.
When upgrading an existing root-owned socket volume, stop both services and change
its ownership before starting the new images (this maintenance disconnects clients):

```bash
docker compose -f docker-compose.prod.yml stop connectionserver renderer
docker compose -f docker-compose.prod.yml run --rm --no-deps --user 0:0 --entrypoint chown renderer -R 1654:1654 /run/sharpmush
docker compose -f docker-compose.prod.yml up -d connectionserver renderer
```

Use your selected Compose file for all three commands. SocketServer waits for the
renderer to answer an HTTP/2 health request on the shared Unix socket before startup.
Its WebSocket capacity defaults to 10,000 concurrent upgraded connections; set the
positive `ConnectionServer__MaxConcurrentUpgradedConnections` environment variable
to tune that limit. This limit does not apply to raw Telnet sockets.

### The database

The world is an LMDB environment at `/app/data/lightning` on the `app-data` volume: one
`data.mdb` file plus a `lock.mdb` reader table, written by the server process itself. Nothing
else to run, tune or connect to. Three settings on `sharpmush-server` matter:

| Variable | Set to | Why |
|---|---|---|
| `SHARPMUSH_DATABASE_PROVIDER` | `lightning` | selects the provider |
| `SHARPMUSH_LIGHTNING_PATH` | `data/lightning` | relative to `/app`, so it lands on the volume |
| `SHARPMUSH_LIGHTNING_SYNC` | `periodic` | sync to disk once a second rather than on every commit; a power loss costs at most that second and the file stays consistent. `full` syncs every commit at roughly 5 ms each. |

`SHARPMUSH_LIGHTNING_MAPSIZE` (bytes, default 64 GiB) is the ceiling on the file's size, not
memory — the file is sparse and only grows as the world does. Raise it before a world reaches it;
the server refuses writes with `MDB_MAP_FULL` rather than corrupting anything.

Nothing outside the server process should read `data.mdb` while the game runs. To get a copy
that is safe to read, have the server make one: see [Backups](#backups-restic).

#### Switching a box that ran SurrealDB

There is no data migration: the world starts fresh and the first visitor to `/setup` claims the
admin again. Wiki asset uploads live in the same volume and are kept.

```bash
cd deploy
export COMPOSE_FILE=docker-compose.cloudflare.yml   # or docker-compose.prod.yml
git pull                                           # brings in the compose change
docker compose stop sharpmush-server
docker run --rm -v deploy_app-data:/data alpine rm -rf /data/surreal   # the old RocksDB store
docker compose up -d                               # recreates the server on lightning
```

`deploy_app-data` is the volume's name when the stack is started from this directory; check
with `docker volume ls` if you started it from elsewhere.

### Pointing the in-browser terminal at the right place

The portal's web terminal connects to a **WebSocket** endpoint, `/ws`, which is served by the
**connection server** (`:4202`), *not* the main server. The `Caddyfile` already routes
`https://<your-domain>/ws` to the connection server for you, so in the portal set the terminal's
**Server URI** to:

```
wss://<your-domain>/ws
```

(Same origin, so no mixed-content or CORS issues.) The default `ws://localhost:4202/ws` is for
local development only — over HTTPS a browser will refuse a plaintext `ws://` connection.

## Ports

| Port | Service | Exposed to internet? |
|------|---------|----------------------|
| 80 / 443 | Caddy (web portal, SignalR, and `/ws` → connection server) | yes |
| 4201 | Telnet — **plaintext** | yes |
| 4203 | Telnet over TLS — *configured but not yet implemented* | not yet |
| 8080 | ASP.NET server (HTTP) | no — internal, behind Caddy |
| 4202 | Connection server HTTP / `/ws` WebSocket | no — internal, reached via Caddy's `/ws` route |
| 4222 / 8222 | NATS client / monitoring | no — internal only |

> **Telnet is unencrypted today.** Everything a player types on `4201`, including their password
> on login, crosses the network in the clear. The web portal is HTTPS via Caddy, but MU\* clients
> connect straight to `4201` and bypass it entirely.
>
> `ssl_port` (4203) exists in the configuration and **nothing listens on it** —
> [#743](https://github.com/SharpMUSH/SharpMUSH/issues/743) is the issue to implement it, and
> records the intended deployment shape: the Caddy stack can reuse the Let's Encrypt certificate
> Caddy already renews into `caddy-data`, and the Cloudflare stack (which has no Caddy, and so no
> local certificate) needs an ACME sidecar doing a DNS-01 challenge.
>
> Until then, if you need encrypted player connections, terminate TLS in front of `4201` yourself
> (stunnel, or a TCP-mode proxy). Be aware of the cost: the game takes a connection's IP from the
> socket's peer address and has no PROXY-protocol support, so every player will appear to connect
> from the proxy — site-locks, bans and connection logs all lose the real client address.

If you **don't** use Caddy (e.g. you terminate TLS at a load balancer), remember the browser
terminal's `/ws` endpoint lives on the connection server (`:4202`), not the main server — you must
publish `4202` or add an equivalent `/ws` proxy route, in addition to publishing `8080`.

If you want to front the app with **Cloudflare** instead of Caddy, don't edit this file —
use `docker-compose.cloudflare.yml` and follow the section below.

## Fronting with Cloudflare (`docker-compose.cloudflare.yml`)

This variant replaces Caddy with a **Cloudflare Tunnel**. A small `cloudflared` container
dials *outbound* to Cloudflare and Cloudflare routes public traffic back down that
connection, which means:

- you open **no inbound web ports** (no 80/443 on the host firewall),
- you manage **no TLS certificate** (Cloudflare terminates HTTPS at its edge),
- your server's **origin IP stays hidden** behind Cloudflare.

### The one thing to understand first: there are TWO independent front doors

SharpMUSH is reached two different ways, and Cloudflare only handles one of them:

| Traffic | Protocol | Path | Goes through Cloudflare? |
|---------|----------|------|--------------------------|
| Web portal, REST, SignalR, WebSocket terminal | HTTP/HTTPS + WS | Cloudflare Tunnel → `sharpmush-server:8080` | **Yes** |
| Telnet (MU\* clients) | raw TCP | Client → host IP `:4201` directly | **No** |

Cloudflare's normal proxy (the orange cloud) only carries HTTP/HTTPS and WebSockets.
**Raw telnet is plain TCP and cannot go through it** on free/standard plans — that would
require the paid Spectrum product. So the Zero Trust / Tunnel steps below apply **only to
the web side**. Telnet is configured entirely separately, with a plain DNS record, and is
served straight off the host. Keep the two in separate mental buckets.

### Part A — the web side (Cloudflare Zero Trust Tunnel)

This is the part the Zero Trust dashboard is for. It does **not** touch telnet.

1. **Create the tunnel.** In the Cloudflare dashboard go to
   **Zero Trust → Networks → Tunnels → Create a tunnel**, choose the **Cloudflared**
   connector type, and give it a name (e.g. `sharpmush`).
2. **Copy the token.** After creating it, Cloudflare shows an install command containing a
   long token (the value after `--token`). Copy just that token into your `.env`:
   ```
   CLOUDFLARE_TUNNEL_TOKEN=eyJh...   # the token string, nothing else
   ```
   You do **not** need to run the install command Cloudflare shows — the `cloudflared`
   service in the compose file runs the tunnel for you using this token.
3. **Map your public hostname(s).** Still on the tunnel's config page, open the
   **Public Hostname** tab and add a route:
   - **Subdomain/domain:** `mush.example.com` (your portal address)
   - **Service type:** `HTTP`
   - **URL:** `sharpmush-server:8080`

   Cloudflare automatically creates the proxied (orange-cloud) DNS record for that
   hostname for you — you don't add it by hand. `HTTP` here is correct: the hop from the
   tunnel to the container is on the private docker network; the public side is still HTTPS.
4. **Add the WebSocket route — required for the in-browser terminal.** The terminal's `/ws`
   endpoint is served by the **connection server** (`:4202`), not the main server, so it needs
   its own Public Hostname entry. Add a route and make sure it sits **above** the catch-all from
   step 3 (Cloudflare matches top-to-bottom):
   - **Subdomain/domain:** `mush.example.com`
   - **Path:** `ws`  *(matches `/ws`)*
   - **Service type:** `HTTP`
   - **URL:** `connectionserver:4202`

   Then set the portal's terminal **Server URI** to `wss://mush.example.com/ws` (same origin —
   no mixed-content). WebSockets traverse the tunnel with no extra config.

   > Alternative: instead of a path route, use a dedicated hostname — add
   > `ws.mush.example.com` → `HTTP` → `connectionserver:4202` and point the Server URI at
   > `wss://ws.mush.example.com/ws`. Either works; the path route keeps everything same-origin.

### Part B — the telnet side (plain DNS, no Zero Trust involved)

Telnet does not use the tunnel at all. You expose it directly and give players a hostname
that resolves straight to your server:

1. In the normal Cloudflare **DNS** app (not Zero Trust), add an `A` (and/or `AAAA`)
   record, e.g. `telnet.mush.example.com` → your host's public IP.
2. Set that record to **DNS only (grey cloud)**, *not* proxied. A proxied record would try
   to send telnet through Cloudflare's HTTP proxy, which does not work.
3. Make sure the host firewall allows inbound **TCP 4201** (the compose file already
   publishes it). Players then connect their MU\* client to `telnet.mush.example.com 4201`.

> Trade-off to be aware of: because this record is DNS-only, that hostname reveals your
> server's real IP. The web portal stays hidden behind Cloudflare; only the telnet
> hostname is exposed. If hiding the telnet IP matters to you, that requires Cloudflare
> Spectrum (paid) or a different TCP proxy.

### Part C — bring it up

```bash
cd deploy
cp .env.example .env      # fill in CLOUDFLARE_TUNNEL_TOKEN and your domain
docker compose -f docker-compose.cloudflare.yml run --rm backup restic init   # optional, once — backups only
docker compose -f docker-compose.cloudflare.yml up -d --build
```

Check the tunnel is healthy with `docker compose -f docker-compose.cloudflare.yml logs -f cloudflared`
(you want to see it register a connection to Cloudflare), then load `https://mush.example.com`.

### Ports with Cloudflare

| Port | Service | Open on host firewall? |
|------|---------|------------------------|
| 4201 | Telnet | **yes** (Part B) |
| 80 / 443 | — | **no** — the tunnel is outbound-only |
| 8080 | ASP.NET server (HTTP) | no — internal, reached via the tunnel |
| 4222 / 8222 | NATS client / monitoring | no — internal only |

## Starting on boot

`restart: unless-stopped` already brings the containers back after a reboot, and for most
boxes that is enough. But it replays the **last state**, not the **declared** one: it
restarts whatever happened to be running when the box went down. A `docker compose down`,
a hand-stopped container, or an edited compose file therefore survives the reboot, and the
box silently drifts from what `deploy/` says it should be.

`sharpmush.service` closes that gap by running `docker compose up -d` on boot, so the box
reconciles against the compose file every time it starts:

```bash
# as root, on the host
ln -s /opt/SharpMUSH/deploy/sharpmush.service /etc/systemd/system/sharpmush.service
systemctl daemon-reload
systemctl enable --now sharpmush.service
```

It pulls before starting (so a boot also picks up an image the box missed while it was
down) and tolerates a registry outage rather than keeping the game offline. Edit the
`WorkingDirectory` and the compose filename in the unit if your checkout is not at
`/opt/SharpMUSH` or you deploy the Caddy stack.

Note this does not replace watchtower, which handles updates while the box is *up*.

## Backups (restic)

**Backups are opt-in and off by default.** The `backup` service lives behind a compose
profile, so it is not created at all until you turn it on — an unconfigured box gets no
backup container rather than one crash-looping against a repository that does not exist.
To enable it, fill in the restic settings in `.env` and uncomment:

```bash
COMPOSE_PROFILES=backup
```

Once enabled, the `backup` service snapshots `/data/backup` and `/data/wiki-assets` to your
bucket every night at 03:30, keeping 7 daily and 4 weekly snapshots. The volume is mounted
**read-only**, so a backup run can never corrupt live data.

**What it snapshots is a copy, not the live world.** restic reads `data.mdb` front to back
while commits keep landing, and LMDB does not promise such a copy opens. So the server takes
its own copies instead, with the routine LMDB supplies for exactly this (`mdb_env_copy`): each
one is a complete environment, written into `/app/data/backup/<timestamp>` while the game keeps
running, with `latest` pointing at the newest. `SHARPMUSH_BACKUP_INTERVAL=6h` on
`sharpmush-server` takes one every six hours, so the 03:30 run always finds a recent one. The
live world at `/data/lightning` is deliberately **not** in `RESTIC_BACKUP_SOURCES`.

| Setting on `sharpmush-server` | Default | What it does |
|---|---|---|
| `SHARPMUSH_BACKUP_INTERVAL` | unset — no scheduled copy | How often a copy is taken. `6h`, `90m`, `1h30m` or a count of seconds. |
| `SHARPMUSH_BACKUP_KEEP` | `2` | How many copies stay on disk. Each is a whole world, so this is a disk-space decision. |
| `SHARPMUSH_BACKUP_PATH` | `<world>.backups` | Where the copies go. Both stacks set it to `data/backup`. Set it explicitly for an in-memory SurrealDB endpoint so backups land on the mounted volume. |
| `SHARPMUSH_LIGHTNING_BACKUP_COMPACT` | on | Omit free pages: smaller copies, slower to produce. `false` turns it off. |

A wizard can take one at any time in-game with `@backup`, and list what is on disk with
`@backup/list`.

Every database that can copy its own world does so into the same directory layout, so the restic
configuration above is the same whichever one you run:

| Database | What a backup is | Consistency |
|---|---|---|
| `lightning` (what these stacks run) | LMDB's own `mdb_env_copy` of the environment | a point-in-time snapshot by construction |
| `surrealdb` | `world.surql`, the engine's own export, restored with `surreal import` | logical, taken from a running game — not documented as an instant |

The `docker compose run --rm backup …` commands below work whether or not the profile is
enabled — `run` activates a service's profile automatically.

> The commands below (and under **Updating**) omit `-f` by exporting `COMPOSE_FILE`, so
> they work for either stack. Set it once per shell to whichever stack you deployed:
> ```bash
> export COMPOSE_FILE=docker-compose.prod.yml        # or docker-compose.cloudflare.yml
> ```

```bash
# List snapshots:
docker compose run --rm backup restic snapshots

# Run a backup right now:
docker compose run --rm backup backup   # (entrypoint verb)

# Restore the latest snapshot into a scratch dir to inspect it:
docker compose run --rm -v restore:/restore backup \
  restic restore latest --target /restore
```

**To restore for real** (these stacks run `lightning`): stop the stack, then put the snapshot's
contents back into the `app-data` volume — one of the `backup/<timestamp>` directories becomes
`lightning`, and `wiki-assets` goes back as it is. The game reads whatever is in the volume on boot.

> Restoring SurrealDB is not a file copy. With the game **stopped**, load
> `backup/<timestamp>/world.surql` into an empty database with
> `surreal import --ns sharpmush --db world <file>`. The import replaces the database; it is not a merge.

```bash
docker compose stop sharpmush-server connectionserver

# 1. Restore into a scratch volume. The -v is what makes the files outlive the container;
#    without it restic writes into the container's own filesystem and they are gone.
docker compose run --rm -v restore:/restore backup \
  restic restore latest --target /restore

# 2. See which copies the snapshot carried and pick one.
docker compose run --rm --no-deps -v restore:/restore --entrypoint sh sharpmush-server \
  -c 'ls /restore/data/backup'

# 3. Put it in place. The app image mounts app-data read-write, unlike the backup service.
docker compose run --rm --no-deps -v restore:/restore --entrypoint sh sharpmush-server -c '
  rm -rf /app/data/lightning &&
  cp -a /restore/data/backup/<timestamp> /app/data/lightning &&
  cp -a /restore/data/wiki-assets/. /app/data/wiki-assets/'

docker compose start connectionserver sharpmush-server
docker volume rm restore    # once the game is up and you are satisfied
```

A restored copy carries no `lock.mdb` — LMDB writes a fresh one on open — and no
`lightning.previous`, which is a superseded world from a staging promotion and never part of a
copy.

The server does **not** need to stop for the nightly run: what restic reads is a finished copy,
and a copy still being written is named `.incoming-*` and excluded until it is moved into place
complete.

## Updating

**The Cloudflare stack updates itself.** Every merge to `main` runs the full test suite and then
publishes changed images to Docker Hub (`.github/workflows/docker-dev.yml`):
`sharpmush/sharpmush-server:dev` for the engine, `sharpmush/sharpmush-connectionserver:dev`
for the renderer, and `sharpmush/sharpmush-socketserver:dev` for the socket owner. The
`watchtower` service polls every 5 minutes and recreates each of these three labeled services
when its image tag changes, pruning superseded images. Socket owner replacement drops live sockets.
Nothing on the box ever builds, and no inbound access is required. There is no separate client
image — the Blazor WASM portal is baked into the server image at build time.

To update the *other* services (nats, cloudflared, backup — deliberately outside watchtower's
label scope) or to force an immediate app update instead of waiting for the poll:

```bash
git pull
docker compose pull
docker compose up -d
```

The `app-data` volume persists across updates, so the world is untouched. Database migrations
are recorded in the database and re-applied only when new, so unattended restarts are safe.

## Updates and live connections

See [Connections during deployment](connection-updates.md) for lifecycle notices, PR deployment-impact checks, stop grace, session recovery limits and rollout options.
