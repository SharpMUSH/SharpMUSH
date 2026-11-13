# Stage 1: Build
# Built on the runner's own platform and published for the image's: an arm64 image needs no
# emulated compile, and it carries only its own platform's native libraries rather than every
# platform's.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish SharpMUSH.SocketServer/SharpMUSH.SocketServer.csproj -c Release -o /app \
    -r linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64) --self-contained false

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /run/sharpmush && chown 1654:1654 /run/sharpmush
USER 1654:1654
EXPOSE 4201 4202 4203
ENV ASPNETCORE_URLS=http://+:4202
ENTRYPOINT ["dotnet", "SharpMUSH.SocketServer.dll"]
