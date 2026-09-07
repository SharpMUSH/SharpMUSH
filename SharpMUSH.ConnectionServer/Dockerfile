FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish SharpMUSH.ConnectionServer/SharpMUSH.ConnectionServer.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV Rendering__SocketPath=/run/sharpmush/render.sock
ENTRYPOINT ["dotnet", "SharpMUSH.ConnectionServer.dll"]
