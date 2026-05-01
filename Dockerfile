# Stage 1: Build
FROM mcr.microsoft.com/dotnet/nightly/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish SharpMUSH.Server/SharpMUSH.Server.csproj -c Release -o /app
# Copy the dev certificate if it exists (optional for build, can be mounted at runtime)
RUN if [ -f SharpMUSH.Server/sharpmush-dev.pfx ]; then \
      cp SharpMUSH.Server/sharpmush-dev.pfx /app/sharpmush-dev.pfx; \
    fi

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/nightly/aspnet:11.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080 8081 4201 4202 4203 9092
ENV ASPNETCORE_URLS=http://+:8080;https://+:8081
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/sharpmush-dev.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=DevPassword123!
ENTRYPOINT ["dotnet", "SharpMUSH.Server.dll"]
