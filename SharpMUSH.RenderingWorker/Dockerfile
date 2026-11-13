# Built on the runner's own platform and published for the image's: an arm64 image needs no
# emulated compile, and it carries only its own platform's native libraries rather than every
# platform's.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN case "$TARGETARCH" in amd64) RID=linux-x64 ;; arm64) RID=linux-arm64 ;; \
      *) echo "Unsupported TARGETARCH '$TARGETARCH': build with BuildKit (docker buildx)" >&2; exit 1 ;; esac \
    && dotnet publish SharpMUSH.RenderingWorker/SharpMUSH.RenderingWorker.csproj -c Release -o /app \
    -r $RID --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /run/sharpmush && chown 1654:1654 /run/sharpmush
USER 1654:1654
ENV Rendering__SocketPath=/run/sharpmush/render.sock
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=3 CMD ["dotnet", "SharpMUSH.RenderingWorker.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "SharpMUSH.RenderingWorker.dll"]
