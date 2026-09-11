FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish SharpMUSH.ConnectionServer/SharpMUSH.ConnectionServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /run/sharpmush && chown 1654:1654 /run/sharpmush
USER 1654:1654
ENV Rendering__SocketPath=/run/sharpmush/render.sock
HEALTHCHECK --interval=10s --timeout=5s --start-period=10s --retries=3 CMD ["dotnet", "SharpMUSH.ConnectionServer.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "SharpMUSH.ConnectionServer.dll"]
