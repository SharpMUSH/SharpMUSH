# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish SharpMUSH.SocketServer/SharpMUSH.SocketServer.csproj -c Release -o /app

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /run/sharpmush && chown 1654:1654 /run/sharpmush
USER 1654:1654
EXPOSE 4201 4202 4203
ENV ASPNETCORE_URLS=http://+:4202
ENTRYPOINT ["dotnet", "SharpMUSH.SocketServer.dll"]
