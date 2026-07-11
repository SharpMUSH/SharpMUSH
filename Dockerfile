# Stage 1: Build
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src
COPY . .
# Restore once for the whole solution, then publish with --no-restore so neither publish re-restores.
RUN dotnet restore
RUN dotnet publish SharpMUSH.Server/SharpMUSH.Server.csproj -c Release -o /app --no-restore
# Publish the Blazor WASM portal and bundle its static assets into the server's web root, so a
# single image serves the API + SignalR + the portal at one origin (see UseBlazorFrameworkFiles in
# Program.cs). Without this the server has no wwwroot/index.html and "/" returns 404.
RUN dotnet publish SharpMUSH.Client/SharpMUSH.Client.csproj -c Release -o /client --no-restore
RUN mkdir -p /app/wwwroot && cp -a /client/wwwroot/. /app/wwwroot/
# Copy the dev certificate if it exists (optional for build, can be mounted at runtime)
RUN if [ -f SharpMUSH.Server/sharpmush-dev.pfx ]; then \
      cp SharpMUSH.Server/sharpmush-dev.pfx /app/sharpmush-dev.pfx; \
    fi

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080 8081 4201 4202 4203 9092
ENV ASPNETCORE_URLS=http://+:8080;https://+:8081
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/sharpmush-dev.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=DevPassword123!
ENTRYPOINT ["dotnet", "SharpMUSH.Server.dll"]
