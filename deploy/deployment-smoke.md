# Disposable deployment smoke

The `Disposable deployment smoke` Actions workflow exercises the production Dockerfiles
and `docker-compose.prod.yml` plus the opt-in native TLS overlay. It runs on an ordinary
GitHub-hosted Ubuntu runner with a 45-minute job deadline (40 minutes for build/test).
It checks the exact PR head, rather than a moving published image tag.

The harness requires at least 12 GiB free before building. It does not push images, use
repository secrets, contact a production service, or create a cloud backup repository.
It has `contents: read` permission only. Container ports bind to runner loopback, and
all volume/network names belong to this job's unique project. Backing networks are
internal; only Caddy and SocketServer also join a disposable ingress bridge, because
Docker can omit host port mappings for internal-only networks. Those two containers
have outbound capability, but the fixture supplies no external service or credentials;
Caddy uses manual test TLS instead of ACME. No host firewall settings are changed.
Generated certificates and passwords stay in the runner's temporary directory and are
never uploaded as artifacts. The workflow's `always()` cleanup removes its Compose
resources; the hosted runner is discarded even if the job is terminated.

## What it exercises

- Build the actual Server, SocketServer, and RenderingWorker images from the checked-out
  commit. Start the production service model with isolated CI overrides.
- Use a generated CA/intermediate/leaf certificate with real container-mounted PEMs.
  Check native TLS and plaintext output, `/ws` through the production Caddy route,
  container UID/key access failures, missing bind mount rejection, and explicit
  certificate renewal followed by a SocketServer restart.
- Claim a fresh administrative account through the public setup API, then log in.
  Install the repository's SamplePlugin and CommandOnlyPlugin fixtures through the
  package APIs, execute their functions, and leave CommandOnlyPlugin disabled.
- Write a world marker and wiki asset, leave an uploaded plugin package pending, then
  request a real `@backup`. Restic snapshots the configured backup sources into a local
  repository. It never copies the live LMDB file.
- Mutate the throwaway world and replace plugin/asset/key files, restore the Restic
  snapshot into a fresh scratch volume, and run the exact inner restore shell from
  `deploy/README.md` with the selected completed timestamp.
- Restart, log in again, and verify world identity and marker, asset bytes, active plugin
  execution, disabled-plugin state, pending-upload usability, and stale-plugin removal.

The script intentionally refuses an ordinary local invocation. Use its PR-triggered
Actions run, or the workflow's manual dispatch once the workflow is on the default
branch. Lightweight harness and documentation checks run without Docker:

```bash
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

## Limits

An Actions pass proves this small generated fixture on the hosted Linux runner. It does
not replace the operator's restore drill against a representative backup, certificate
trust/permissions checks on the deployment host, or capacity planning for a large world.
The Cloudflare stack remains covered by Compose-model checks; this runtime job does not
create a tunnel, test Cloudflare services, request ACME certificates, or test Docker
Desktop/WSL bind behavior. Certificate refresh remains operator-managed and restarting
the SocketServer disconnects its native and WebSocket clients.
