# SharpMUSH on Railway

Railway runs a game as three services, one per image: the edge (`sharpmush/sharpmush-edge`, built
from `deploy/fly/Dockerfile`; Caddy, NATS and the connection server), the game server
(`sharpmush/sharpmush-server`) and the renderer (`sharpmush/sharpmush-renderer`). The edge holds
every player's socket, so updating the server or the renderer drops nobody. A Railway template
turns the settings below into a **Deploy on Railway** button; Railway templates are made in a
Railway account's template composer, not from a file in this repository.

## Template settings

Three services in one project, each from its Docker image's `dev` tag, with automatic image
updates off. They reach each other on the project's private network, under the names Railway
gives them (`<service>.railway.internal`); the `${{...}}` values below are Railway reference
variables and fill those names in.

`edge`, from `sharpmush/sharpmush-edge:dev`, the only public service:

| Setting | Value |
|---|---|
| Variable `PORT` | `8000` |
| Variable `SHARPMUSH_TRUSTED_PROXIES` | `0.0.0.0/0 ::/0` |
| Variable `SHARPMUSH_SERVER_ADDRESS` | `${{server.RAILWAY_PRIVATE_DOMAIN}}:8080` |
| Variable `Rendering__Address` | `http://${{renderer.RAILWAY_PRIVATE_DOMAIN}}:5100` |
| Volume | mounted at `/data` (NATS's store, the setup guard's state) |
| Public networking | a generated domain, port `8000` |
| TCP proxy | port `4201` |
| Healthcheck path | `/health` |

`server`, from `sharpmush/sharpmush-server:dev`, with the world:

| Setting | Value |
|---|---|
| Variable `NATS_URL` | `nats://${{edge.RAILWAY_PRIVATE_DOMAIN}}:4222` |
| Variable `ASPNETCORE_URLS` | `http://+:8080` |
| Variable `ASPNETCORE_Kestrel__Certificates__Default__Path` | empty |
| Variable `ASPNETCORE_Kestrel__Certificates__Default__Password` | empty |
| Variable `ForwardedHeaders__KnownNetworks__0` | `0.0.0.0/0` |
| Variable `ForwardedHeaders__KnownNetworks__1` | `::/0` |
| Variable `SHARPMUSH_LIGHTNING_PATH` | `/data/lightning` |
| Variable `SHARPMUSH_LIGHTNING_SYNC` | `periodic` |
| Variable `SHARPMUSH_BACKUP_PATH` | `/data/backup` |
| Variable `SHARPMUSH_BACKUP_INTERVAL` | `6h` |
| Variable `SHARPMUSH_BACKUP_KEEP` | `2` |
| Variable `Wiki__AssetRoot` | `/data/wiki-assets` |
| Volume | mounted at `/data` |
| Public networking | none |

`renderer`, from `sharpmush/sharpmush-renderer:dev`:

| Setting | Value |
|---|---|
| Variable `Rendering__Address` | `http://${{RAILWAY_PRIVATE_DOMAIN}}:5100` |
| Public networking | none |

`SHARPMUSH_TRUSTED_PROXIES` lets Caddy believe Railway's edge about the visitor's address and that
the visit came over HTTPS. Port 8000 is reachable only through that edge, so trusting every source
trusts only Railway. The server trusts forwarded headers from any address for the same reason: only
the edge reaches it, from a private address that changes with each deploy. The server's certificate
variables are empty because it serves plain HTTP to the edge. The server and the renderer get no
public networking: NATS, the server and the renderer ask for no credentials, and Railway's private
network reaches only this project.

The setup link in the edge's deploy log uses the generated domain (`RAILWAY_PUBLIC_DOMAIN`), and the
log names the TCP proxy's host and port (`RAILWAY_TCP_PROXY_DOMAIN`, `RAILWAY_TCP_PROXY_PORT`).
Players connect there. Railway picks both, for example `shortline.proxy.rlwy.net` port `31457`.

## Updates

Leave Railway's automatic image updates off. When a service's settings show an update for the
image, take an `@backup`, then apply it:

- `server` and `renderer`: apply whenever. Players stay connected through the restart.
- `edge`: every update drops everyone connected, so announce a time first.
