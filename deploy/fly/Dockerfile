# SharpMUSH in one container, for hosts that run one container per app (Fly.io). The compose stack
# keeps the socket owner apart so an engine update never drops players; here every update restarts
# the whole machine, which is the price of fitting on these hosts.
#
# Build from the repository root (it needs nats.conf):
#   docker build -f deploy/fly/Dockerfile -t sharpmush-allinone .
ARG SHARPMUSH_IMAGE_TAG=dev
FROM sharpmush/sharpmush-server:${SHARPMUSH_IMAGE_TAG} AS server
FROM sharpmush/sharpmush-socketserver:${SHARPMUSH_IMAGE_TAG} AS connection
FROM sharpmush/sharpmush-renderer:${SHARPMUSH_IMAGE_TAG} AS renderer
FROM nats:2.10-alpine AS nats
FROM caddy:2-alpine AS caddy

FROM mcr.microsoft.com/dotnet/aspnet:11.0
COPY --from=server /app /app/server
COPY --from=connection /app /app/connection
COPY --from=renderer /app /app/renderer
COPY --from=nats /usr/local/bin/nats-server /usr/local/bin/nats-server
COPY --from=caddy /usr/bin/caddy /usr/local/bin/caddy
COPY nats.conf /etc/sharpmush/nats.conf
COPY deploy/fly/Caddyfile /etc/sharpmush/Caddyfile
COPY deploy/fly/start.sh /usr/local/bin/sharpmush-start
RUN chmod 755 /usr/local/bin/sharpmush-start
# Portal (through Caddy) and telnet.
EXPOSE 8000 4201
VOLUME /data
ENTRYPOINT ["/usr/local/bin/sharpmush-start"]
