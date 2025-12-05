# Stage 1: Build
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish SharpMUSH.ConnectionServer/SharpMUSH.ConnectionServer.csproj -c Release -o /app

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 4201 4202 4203
ENV ASPNETCORE_URLS=http://+:4202
ENTRYPOINT ["dotnet", "SharpMUSH.ConnectionServer.dll"]
