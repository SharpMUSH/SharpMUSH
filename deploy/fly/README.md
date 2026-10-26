# SharpMUSH on Fly.io

Fly runs one container per machine, and a volume attaches to one machine. The compose stack's
services share a Unix socket and a volume, so here they run in one container instead:
`Dockerfile` copies the published server, connection server and renderer images, NATS and Caddy
into one image, and `start.sh` starts them in order and stops the container if any of them stops.

The cost of fitting in one container: every update restarts the whole machine and drops connected
players, where the compose stack keeps their sockets open across engine updates.

## Launch

```bash
fly auth login
git clone https://github.com/SharpMUSH/SharpMUSH && cd SharpMUSH
deploy/fly/launch.sh my-game iad
fly logs --app my-game --no-tail | grep setup_code
```

On Windows, run the script with `bash deploy/fly/launch.sh my-game iad` from PowerShell (WSL); it
uses the Windows `fly.exe`.

`launch.sh` creates the app, a 10 GB volume (`sharpmush_data`, mounted at `/data`), a dedicated IPv4
address for telnet, and an IPv6 address, then deploys one `shared-cpu-2x` machine with 2 GB.
Running it again deploys the current checkout.

Open the `setup_code` link from the log to finish `/setup`. Until then the portal answers only that
link, and telnet listens on an inside port. Once setup is finished both open, and players connect
to the dedicated IPv4 address on port 4201. `SHARPMUSH_GUARD=off` in `fly.toml`'s `[env]` skips the
guard.

## Cost

At Fly's list prices: the machine (shared-cpu-2x, 2 GB), $2/month for the dedicated IPv4 address,
and $0.15 per GB per month for the volume. Fly bills by the second; see fly.io/pricing.

## Your own domain

```bash
fly certs add mush.example.com --app my-game
```

Point the domain at the app as `fly certs` says, set `SHARPMUSH_DOMAIN` in `fly.toml`, and run
`launch.sh` again.

## Backups

`@backup` copies are written to `/data/backup` on the volume, and Fly snapshots volumes daily.
There is no restic sidecar here.
