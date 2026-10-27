# Stage 1: Build
# Built on the runner's own platform: the output is portable .NET, with every architecture's
# native libraries, so an arm64 image needs no emulated compile.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
# Restore once for the whole solution, then publish with --no-restore so neither publish re-restores.
RUN dotnet restore
# The release tag's version (docker-publish.yml); left empty, Directory.Build.props supplies it.
ARG SHARPMUSH_VERSION=
RUN dotnet publish SharpMUSH.Server/SharpMUSH.Server.csproj -c Release -o /app --no-restore \
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
ENV ASPNETCORE_URLS=http://+:8080;https://+:8081
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/sharpmush-dev.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=DevPassword123!
ENTRYPOINT ["dotnet", "SharpMUSH.Server.dll"]
