# Stage 1: Build
# Built on the runner's own platform and published for the image's: an arm64 image needs no
# emulated compile, and it carries only its own platform's native libraries rather than every
# platform's (Windows alone was ~320 MB of the portable publish).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
# Restore once for the whole solution; the client publishes with --no-restore. The server restores
# again for its runtime identifier, from the warm cache.
RUN dotnet restore
# The release tag's version (docker-publish.yml); left empty, Directory.Build.props supplies it.
ARG SHARPMUSH_VERSION=
# ReadyToRun: the engine's assemblies are compiled ahead of time instead of by the JIT at run time.
# Measured on an idle fresh world: the host starts in 2.0 s instead of 3.2 s, and docker stats shows
# ~25 MB less (less JIT-compiled code and less startup garbage).
RUN dotnet publish SharpMUSH.Server/SharpMUSH.Server.csproj -c Release -o /app \
    -r linux-$([ "$TARGETARCH" = arm64 ] && echo arm64 || echo x64) --self-contained false \
    -p:PublishReadyToRun=true \
    ${SHARPMUSH_VERSION:+-p:SharpMUSHVersion=$SHARPMUSH_VERSION}
# Publish the Blazor WASM portal and bundle its static assets into the server's web root, so a
# single image serves the API + SignalR + the portal at one origin. Without this the server has no
# wwwroot/index.html and "/" returns 404. The endpoints manifest goes into the content root beside
# the server: it is what makes the server answer with MapStaticAssets — immutable caching for the
# fingerprinted files, precompressed br/gzip for every asset (see PortalStaticFiles.cs).
RUN dotnet publish SharpMUSH.Client/SharpMUSH.Client.csproj -c Release -o /client --no-restore
RUN mkdir -p /app/wwwroot && cp -a /client/wwwroot/. /app/wwwroot/ \
    && cp /client/SharpMUSH.Client.staticwebassets.endpoints.json /app/
# Copy the dev certificate if it exists (optional for build, can be mounted at runtime)
RUN if [ -f SharpMUSH.Server/sharpmush-dev.pfx ]; then \
      cp SharpMUSH.Server/sharpmush-dev.pfx /app/sharpmush-dev.pfx; \
    fi

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080 8081 4201 4202 4203 9092
# Plain HTTP: the image is always run behind a proxy that ends TLS. A dev certificate copied above
# serves HTTPS only when ASPNETCORE_URLS and ASPNETCORE_Kestrel__Certificates__Default__Path name it.
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "SharpMUSH.Server.dll"]
