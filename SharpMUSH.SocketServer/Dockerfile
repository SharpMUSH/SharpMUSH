# Stage 1: Build
# Built on the runner's own platform and published for the image's: an arm64 image needs no
# emulated compile, and it carries only its own platform's native libraries rather than every
# platform's.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN case "$TARGETARCH" in amd64) RID=linux-x64 ;; arm64) RID=linux-arm64 ;; \
      *) echo "Unsupported TARGETARCH '$TARGETARCH': build with BuildKit (docker buildx)" >&2; exit 1 ;; esac \
    && dotnet publish SharpMUSH.SocketServer/SharpMUSH.SocketServer.csproj -c Release -o /app \
    -r $RID --self-contained false

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /run/sharpmush && chown 1654:1654 /run/sharpmush
USER 1654:1654
EXPOSE 4201 4202 4203
ENV ASPNETCORE_URLS=http://+:4202
ENTRYPOINT ["dotnet", "SharpMUSH.SocketServer.dll"]
