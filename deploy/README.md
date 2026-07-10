# Deploying SharpMUSH (hobby scale, single host)

This stack runs the whole game on one small VM (~€4–6/mo on a budget VPS such as
Hetzner CX22/CAX11, or $0 on Oracle Cloud Always Free ARM). It uses **SurrealDB
embedded** (a RocksDB directory on disk — no separate database server), **NATS**
for messaging, and a **restic** sidecar for nightly encrypted backups.

Everything runs under Docker Compose. **Kubernetes is not needed** at this scale.

## What's here

| File | Purpose |
|------|---------|
| `docker-compose.prod.yml` | The stack: nats, connectionserver, sharpmush-server, backup, (optional) caddy |
| `Caddyfile` | Automatic-HTTPS reverse proxy for the web portal (optional) |
| `.env.example` | Template for secrets/config — copy to `.env` and fill in |

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

## Ports

| Port | Service | Exposed to internet? |
|------|---------|----------------------|
| 80 / 443 | Caddy (web portal, SignalR, WebSocket) | yes |
| 4201 | Telnet | yes |
| 8080 | ASP.NET server (HTTP) | no — internal, behind Caddy |
| 4222 / 8222 | NATS client / monitoring | no — internal only |

If you don't use Caddy (e.g. you front the app with Cloudflare), delete the `caddy`
service and publish `8080` on `sharpmush-server` instead.

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
