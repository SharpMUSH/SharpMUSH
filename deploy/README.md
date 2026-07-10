# Deploying SharpMUSH (hobby scale, single host)

This stack runs the whole game on one small VM (~€4–6/mo on a budget VPS such as
Hetzner CX22/CAX11, or $0 on Oracle Cloud Always Free ARM). It uses **SurrealDB
embedded** (a RocksDB directory on disk — no separate database server), **NATS**
for messaging, and a **restic** sidecar for nightly encrypted backups.

Everything runs under Docker Compose. **Kubernetes is not needed** at this scale.

## What's here

Two entry points, pick one based on how you terminate TLS:

| File | Purpose |
|------|---------|
| `docker-compose.prod.yml` | The stack: nats, connectionserver, sharpmush-server, backup, and **Caddy** for TLS. Use this if the box faces the internet directly. |
| `docker-compose.cloudflare.yml` | Same stack but fronted by a **Cloudflare Tunnel** instead of Caddy (no open web ports, hidden origin IP). See the Cloudflare section below. |
| `Caddyfile` | Automatic-HTTPS reverse proxy config used by `docker-compose.prod.yml` |
| `.env.example` | Template for secrets/config — copy to `.env` and fill in |
| `.gitignore` | Keeps your real `.env` out of git |
| `README.md` | This file |

## First-time setup

```bash
cd deploy
cp .env.example .env
# Edit .env: set a JWT key, admin password, your domain, and restic/B2 credentials.
#   openssl rand -base64 48   # for JWT_SIGNING_KEY
#   openssl rand -base64 32   # for RESTIC_PASSWORD  (SAVE THIS — losing it makes backups unrecoverable)

# Initialise the restic repository once (creates the encrypted repo in your bucket):
docker compose -f docker-compose.prod.yml run --rm backup restic init

# Build and start everything:
docker compose -f docker-compose.prod.yml up -d --build
```

The web portal comes up on `https://<your-domain>` (via Caddy) and telnet on port `4201`.
The God/admin character named in `.env` is created on first boot.

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
| 4201 | Telnet | yes |
| 8080 | ASP.NET server (HTTP) | no — internal, behind Caddy |
| 4202 | Connection server HTTP / `/ws` WebSocket | no — internal, reached via Caddy's `/ws` route |
| 4222 / 8222 | NATS client / monitoring | no — internal only |

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
cp .env.example .env      # fill in CLOUDFLARE_TUNNEL_TOKEN plus the usual JWT/admin/restic values
docker compose -f docker-compose.cloudflare.yml run --rm backup restic init   # once
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

## Backups (restic)

The `backup` service snapshots the `app-data` volume (the SurrealDB RocksDB store +
wiki assets — i.e. the entire game) to your bucket every night at 03:30, keeping 7
daily and 4 weekly snapshots. The volume is mounted **read-only**, so a backup run can
never corrupt live data.

```bash
# List snapshots:
docker compose -f docker-compose.prod.yml run --rm backup restic snapshots

# Run a backup right now:
docker compose -f docker-compose.prod.yml run --rm backup backup   # (entrypoint verb)

# Restore the latest snapshot into a scratch dir to inspect it:
docker compose -f docker-compose.prod.yml run --rm -v restore:/restore backup \
  restic restore latest --target /restore
```

**To restore for real:** stop the stack, restore the snapshot's `/data` contents back
into the `app-data` volume, then start again. The game reads whatever is in the volume
on boot.

> Note: restic copies the live RocksDB files while the server runs. RocksDB is
> crash-consistent and recovers from its own write-ahead log, so this is fine for hobby
> use. For a guaranteed-quiet snapshot, `docker compose stop sharpmush-server` before the
> backup and start it after.

## Updating

```bash
git pull
docker compose -f docker-compose.prod.yml up -d --build
```

The `app-data` volume persists across rebuilds, so the world is untouched.
