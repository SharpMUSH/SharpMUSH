# SharpMUSH operator handbook

This handbook is the task map for the supported single-host Compose deployment. Exact commands and settings live in [`deploy/README.md`](../../deploy/README.md); connection-preservation boundaries live in [`deploy/connection-updates.md`](../../deploy/connection-updates.md). Run every command from `deploy/` and use the same Compose file that created the stack.

## First claim and deployment

Choose one ingress model:

- `docker-compose.prod.yml` includes Caddy. Open web ports 80/443 and the intended telnet port; keep application, NATS, metrics, and connection-server HTTP ports private.
- `docker-compose.cloudflare.yml` uses a Cloudflare Tunnel for web ingress and hides the web origin. Telnet still needs an intentional public path.

Create `.env` from `.env.example`, set unique secrets, restrict its permissions, and keep it out of source control and diagnostic bundles. Initialize Restic only if the backup profile is enabled, then bring up the selected stack. Protect `/setup` at the network or proxy until the intended administrator claims the pre-generated account linked to `#1`.

Immediately after claim:

1. verify administrator login in a fresh browser and ordinary player login over telnet and the portal terminal;
2. confirm `/ws` routes to the connection server and health/readiness checks are green;
3. inspect container logs for migration, NATS, renderer, authentication, and proxy errors without publishing tokens or environment values;
4. scrape the private Prometheus metrics endpoints and set alerts for availability, storage headroom, queue pressure, and repeated authentication failures;
5. run `@storage` as a wizard and record map limit, live data, allocated disk, backup peak requirement, free space, and leftover worlds.

## Know what must be backed up

These stores are not interchangeable:

| State | Owner | Protection |
| --- | --- | --- |
| Lightning/LMDB world | `sharpmush-server` in the app-data volume | Use `@backup` to create a point-in-time world copy. Never have Restic or another tool read live `data.mdb`. |
| Wiki assets | app-data volume, outside the LMDB world | Include directly in Restic snapshots. A world copy alone does not contain uploads. |
| NATS JetStream | NATS data volume | Persist it for replay/resume continuity and back it up if that recovery window matters. Loss does not become world restoration. |
| Plugin assemblies/manifests and operator config | deployment filesystem or mounted plugin path | Version or snapshot them with `.env` values kept in a separate secrets system. |
| Restic repository | off-host object storage | Protect its password separately, monitor scheduled results, and test restores. It contains safe LMDB copies plus other selected files, not a magic live-database snapshot. |

Retention is not verification. Review scheduled backup output and `@backup/list`, but also perform a restore drill on a calendar.

## Restore drill

1. Choose a dated Restic snapshot and restore it to an isolated directory or host. Never overwrite the live volume during a drill.
2. Confirm the restored set contains a completed point-in-time LMDB copy, wiki assets, required plugins, and the intended deployment configuration. Do not restore `lock.mdb` as authoritative data.
3. Start a non-public stack against the restored paths with new test-only secrets and no production DNS, mail, bots, or outbound automation.
4. Validate world identity, known dbrefs, administrator and ordinary-player login, representative softcode, mail/channels, wiki pages and assets, plugins, telnet, and portal/WebSocket access.
5. Record snapshot id, date, operator, elapsed time, recovered scope, checks performed, failures, and corrective actions. A drill is complete only when the record names a usable recovery point.

The exact Restic restore procedure and volume paths are in the [deployment backup section](../../deploy/README.md#backups-restic).

## Routine checks

- Review readiness, container restart counts, recent warnings/errors, metrics alerts, disk free space, and `@storage`.
- Confirm backups completed and off-host retention is healthy.
- Inspect leftover `.previous`, `.precompact`, `.staging-*`, and incomplete backup directories; remove them only after their replacement is verified.
- Check TLS expiry, proxy routes, telnet reachability, and a real portal login rather than relying on a static asset response.
- Review account, ban, sitelock, and administrator changes. Rotate exposed secrets and revoke affected sessions promptly.

## Upgrade and rollback

Keep the repository's existing image-tag and automatic-update policy. Before a deliberate upgrade, read release notes and [`deploy/connection-updates.md`](../../deploy/connection-updates.md), confirm storage headroom, take and verify a fresh point-in-time world copy, and record current image digests and Compose configuration. Pull images, recreate only the intended services, then repeat readiness, logs, metrics, storage, login, command, portal, and integration checks.

An engine or rendering-worker replacement can preserve client sockets through the stable SocketServer. Replacing SocketServer closes kernel sockets; authenticated WebSocket clients may resume within the configured recovery window, while ordinary telnet reconnects. Host, disk, or NATS loss has wider boundaries. Schedule accordingly.

Deployment rollback means restoring the prior image/Compose definition when data is compatible. Data restore means stopping writes and replacing persistent state from a verified recovery point. Do not use one as a substitute for the other, and do not boot an older binary against newly migrated state unless that downgrade is explicitly supported.

## Incident entry points

| Symptom | First checks | Protect first |
| --- | --- | --- |
| `MDB_MAP_FULL`, low disk, or rising world size | `@storage`, filesystem free space, leftover worlds, backup peak | Stop nonessential writes; do not delete the only recovery copy. Raise the configured map ceiling before retrying. |
| NATS unavailable or replay/resume failing | NATS health/logs, JetStream volume, retention and payload limits | Preserve its volume; expect engine/gateway coordination and recovery to degrade. |
| Output delayed but sockets remain | renderer health and Unix-socket permissions | Keep SocketServer running; replace the rendering worker only. |
| All telnet/WebSocket sockets drop | SocketServer lifecycle, host/network, proxy and deploy history | Stop repeated recreation; establish whether clients can resume or must log in. |
| Portal login or setup fails | server readiness, proxy host/HTTPS headers, session store, sitelocks, bans | Keep `/setup` protected and avoid disabling auth controls as a diagnostic. |
| Suspected compromise | account/audit evidence, exposed secrets, sessions, images and host access | Isolate ingress as needed, preserve logs, rotate secrets, revoke sessions, and restore only from trusted state. |

When collecting diagnostics, include timestamps, image digests, service state, relevant health/metrics values, and bounded log excerpts. Redact passwords, Restic credentials, cookies, bearer tokens, resume tokens, connection strings, and the contents of `.env`.
