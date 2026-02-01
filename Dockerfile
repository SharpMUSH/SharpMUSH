# Stage 1: Build
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish SharpMUSH.Server/SharpMUSH.Server.csproj -c Release -o /app

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080 4201 4202 4203 9092
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "SharpMUSH.Server.dll"]

