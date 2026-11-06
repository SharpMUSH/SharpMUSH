# Two images for hosts that run one container per app (Fly.io, Railway): the all-in-one, where every
# update restarts the whole game, and the edge, which holds the players' sockets while the
# sharpmush-server and sharpmush-renderer images run beside it.
#
# Build from the repository root (it needs nats.conf):
#   docker build -f deploy/fly/Dockerfile -t sharpmush-allinone .
#   docker build -f deploy/fly/Dockerfile --target edge -t sharpmush-edge .
ARG SHARPMUSH_IMAGE_TAG=dev
FROM sharpmush/sharpmush-server:${SHARPMUSH_IMAGE_TAG} AS server
FROM sharpmush/sharpmush-socketserver:${SHARPMUSH_IMAGE_TAG} AS connection
FROM sharpmush/sharpmush-renderer:${SHARPMUSH_IMAGE_TAG} AS renderer
FROM nats:2.10-alpine AS nats
FROM caddy:2-alpine AS caddy

# The edge: Caddy, NATS and the connection server, which hold every player's socket. A split game
# (Fly, Railway) runs this beside the server and renderer images. Build with --target edge.
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS edge
COPY --from=connection /app /app/connection
COPY --from=nats /usr/local/bin/nats-server /usr/local/bin/nats-server
COPY --from=caddy /usr/bin/caddy /usr/local/bin/caddy
COPY nats.conf /etc/sharpmush/nats.conf
COPY deploy/fly/Caddyfile /etc/sharpmush/Caddyfile
COPY deploy/fly/start.sh /usr/local/bin/sharpmush-start
RUN chmod 755 /usr/local/bin/sharpmush-start
ENV SHARPMUSH_ROLE=edge
# Portal (through Caddy) and telnet.
EXPOSE 8000 4201
VOLUME /data
ENTRYPOINT ["/usr/local/bin/sharpmush-start"]

# Everything in one container: the edge plus the server and the renderer.
FROM edge AS allinone
COPY --from=server /app /app/server
COPY --from=renderer /app /app/renderer
ENV SHARPMUSH_ROLE=all
