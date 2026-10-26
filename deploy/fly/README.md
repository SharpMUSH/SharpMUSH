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

On Windows, `launch.ps1` does the same in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File deploy\fly\launch.ps1 my-game iad
```

`bash deploy/fly/launch.sh` from PowerShell runs under WSL and also works; it uses the Windows
`fly.exe`.

`launch.sh` creates the app, a 10 GB volume (`sharpmush_data`, mounted at `/data`), an IPv6 address
and a shared IPv4 address, then deploys one `shared-cpu-2x` machine with 2 GB. Running it again
deploys the current checkout.

Fly's shared IPv4 address carries only HTTP, so the portal and the web terminal reach everyone but
telnet on port 4201 answers over IPv6 alone. Players whose network has no IPv6 can't reach it. Add
`--dedicated-ipv4` (`-DedicatedIPv4` for `launch.ps1`) to give the app an IPv4 address of its own
for telnet; running the script again with it adds one to an existing app.

Open the `setup_code` link from the log to finish `/setup`. Until then the portal answers only that
link, and telnet listens on an inside port. Once setup is finished both open, and players connect
to `<app>.fly.dev` on port 4201. `SHARPMUSH_GUARD=off` in `fly.toml`'s `[env]` skips the
guard.

## Cost

At Fly's list prices: the machine (shared-cpu-2x, 2 GB), $2/month for a dedicated IPv4 address if
you add one, and $0.15 per GB per month for the volume. Fly bills by the second; see fly.io/pricing.

## Your own domain

```bash
fly certs add mush.example.com --app my-game
```

Point the domain at the app as `fly certs` says, set `SHARPMUSH_DOMAIN` in `fly.toml`, and run
`launch.sh` again.

## Backups

`@backup` copies are written to `/data/backup` on the volume, and Fly snapshots volumes daily.
There is no restic sidecar here.
