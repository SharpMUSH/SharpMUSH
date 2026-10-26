# SharpMUSH on Railway

Railway runs the same all-in-one image as Fly (`sharpmush/sharpmush-fly`, built from
`deploy/fly/Dockerfile`): one service with the world on a volume. A Railway template turns the
settings below into a **Deploy on Railway** button; Railway templates are made in a Railway
account's template composer, not from a file in this repository.

## Template settings

One service, from a Docker image:

| Setting | Value |
|---|---|
| Source image | `sharpmush/sharpmush-fly:dev` |
| Volume | mounted at `/data` |
| Variable `PORT` | `8000` |
| Variable `SHARPMUSH_TRUSTED_PROXIES` | `0.0.0.0/0 ::/0` |
| Public networking | a generated domain, port `8000` |
| TCP proxy | port `4201` |
| Healthcheck path | `/health` |
| Automatic image updates | off |

`SHARPMUSH_TRUSTED_PROXIES` lets Caddy believe Railway's edge about the visitor's address and that
the visit came over HTTPS. Port 8000 is reachable only through that edge, so trusting every source
trusts only Railway.

The setup link in the deploy log uses the generated domain (`RAILWAY_PUBLIC_DOMAIN`), and the log
names the TCP proxy's host and port (`RAILWAY_TCP_PROXY_DOMAIN`, `RAILWAY_TCP_PROXY_PORT`). Players
connect there. Railway picks both, for example `shortline.proxy.rlwy.net` port `31457`.

## Updates

Leave Railway's automatic image updates off: every update restarts the service and drops everyone
connected. When the service's settings show an update for the image, announce a time, take an
`@backup`, then apply it.
