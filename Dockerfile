# Stage 1: Build
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore && dotnet publish SharpMUSH.Server/SharpMUSH.Server.csproj -c Release -o /app
# Conditionally copy the dev certificate if it exists
RUN if [ -f SharpMUSH.Server/sharpmush-dev.pfx ]; then \
      cp SharpMUSH.Server/sharpmush-dev.pfx /app/sharpmush-dev.pfx; \
    fi

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/nightly/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The certificate was already copied in the build stage if it existed
EXPOSE 8080 8081 4201 4202 4203 9092
ENV ASPNETCORE_URLS=http://+:8080;https://+:8081
ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/sharpmush-dev.pfx
ENV ASPNETCORE_Kestrel__Certificates__Default__Password=DevPassword123!
ENTRYPOINT ["dotnet", "SharpMUSH.Server.dll"]




