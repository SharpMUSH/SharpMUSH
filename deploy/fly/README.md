# SharpMUSH on Fly.io

Fly runs one container per machine, and a volume attaches to one machine. `Dockerfile` copies the
published server, connection server and renderer images, NATS and Caddy into one image, and
`start.sh` starts the processes its role names and stops the container if any of them stops. CI
publishes it as `sharpmush/sharpmush-fly:dev`, rebuilt whenever one of the images it copies is, so
launching needs no checkout. Railway runs the same image (`deploy/railway/`).

## Roles

`SHARPMUSH_ROLE` picks what one container of this image runs:

| Role | Runs | Needs | Volume at `/data` |
|---|---|---|---|
| `all` (default) | everything, the renderer on a Unix socket | | the world, NATS, the setup guard |
| `edge` | Caddy, NATS, the connection server: every player's socket | `SHARPMUSH_SERVER_ADDRESS` (host:port), `Rendering__Address` | NATS, the setup guard |
| `server` | the game server and the world | `SHARPMUSH_NATS_URL` | the world and its backups |
| `renderer` | the rendering worker | `Rendering__Address` | none |

Run as three parts, updating the server or the renderer drops nobody, as under the compose stack.
The edge and the server each need a volume of their own; without one, an update loses what they
wrote.

Only the edge may be public: NATS, the server and the renderer ask for no credentials, so only the
game's own parts may reach them. On Fly that means a private network made for the game
(`fly apps create --network`), since the organization's default network reaches every app in it.
The server trusts forwarded headers from `SHARPMUSH_TRUSTED_PROXIES` (default: any address, since
only the edge can reach it).

## Launch

```bash
fly auth login
curl -fsSL https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/fly/launch.sh | bash -s -- my-game iad
fly logs --app my-game --no-tail | grep setup_code
```

On Windows, `launch.ps1` does the same in PowerShell:

```powershell
fly auth login
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/fly/launch.ps1))) my-game iad
```

The game runs as three apps on a private network of their own (`fly apps create --network
my-game`), so no other app in the Fly organization can reach the parts that ask for no credentials:

| App | Role | Settings | Machine | Volume | Public |
|---|---|---|---|---|---|
| `my-game` | edge | `my-game.edge.fly.toml` | shared-cpu-1x, 1 GB | `sharpmush_edge`, 1 GB | portal, telnet |
| `my-game-server` | server | `my-game.server.fly.toml` | shared-cpu-2x, 2 GB | `sharpmush_data`, 10 GB | no |
| `my-game-renderer` | renderer | `my-game.renderer.fly.toml` | shared-cpu-1x, 512 MB | none | no |

`launch.sh` downloads `edge.fly.toml`, `server.fly.toml` and `renderer.fly.toml` into the current
directory under those names, with the game's name filled in, then creates the apps, volumes and
addresses and deploys the edge, the server and the renderer, in that order. Keep the `.fly.toml`
files: they hold each part's settings, and the script reuses them when run again.

`--single` (`-Single`) runs the game as one app, `my-game`, with the `all` role and its settings in
`my-game.fly.toml` (shared-cpu-2x, 2 GB, the world on `sharpmush_data`). It costs less, but every
update drops everyone connected. The script finds a game's layout from the files beside it, so a
game launched either way stays that way.

The edge gets an IPv6 address and a shared IPv4 address. Fly's shared IPv4 address carries only
HTTP, so the portal and the web terminal reach everyone but telnet on port 4201 answers over IPv6
alone. Players whose network has no IPv6 can't reach it. Add `--dedicated-ipv4`
(`-DedicatedIPv4`) to give the edge an IPv4 address of its own for telnet; running the script
again with it adds one to an existing game.

Open the `setup_code` link from the log to finish `/setup`. Until then the portal answers only that
link, and telnet listens on an inside port. Once setup is finished both open, and players connect
to `my-game.fly.dev` on port 4201. `SHARPMUSH_GUARD=off` in the edge's `[env]` skips the guard.

## Updates

Nothing updates on its own. Take an `@backup` first: going back to an older image needs it,
because an older server refuses a world a newer one has migrated.

Running the launch script again deploys the newest image to the server and the renderer. Players
stay connected through both restarts. The edge is left running; `--update-edge` (`-UpdateEdge`)
deploys it too, which drops everyone connected, so announce a time first. One part at a time:

```bash
fly deploy --app my-game-server --config my-game.server.fly.toml
fly deploy --app my-game-renderer --config my-game.renderer.fly.toml
fly deploy --app my-game --config my-game.edge.fly.toml
```

A game launched with `--single` restarts as a whole on every update and drops everyone connected:

```bash
fly deploy --app my-game --config my-game.fly.toml
```

Moving a single-app game to the split layout means launching a new game and restoring an
`@backup` copy into its server's volume.

## Cost

At Fly's list prices: the machines in the table above, $2/month for a dedicated IPv4 address if
you add one, and $0.15 per GB per month for the volumes. Split, that is roughly $20/month; a
`--single` game roughly $11. Fly bills by the second; see fly.io/pricing.

## Your own domain

```bash
fly certs add mush.example.com --app my-game
```

Point the domain at the app as `fly certs` says, set `SHARPMUSH_DOMAIN` in `my-game.edge.fly.toml`
(`my-game.fly.toml` for a `--single` game), and deploy that app again.

## Backups

`@backup` copies are written to `/data/backup` on the world's volume, and Fly snapshots volumes
daily. There is no restic sidecar here.
