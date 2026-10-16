# Build from the repository root: docker build -f src/IdentityService/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore separately so dependency downloads can be cached.
COPY global.json Directory.Build.props ./
COPY src/Shared/Shared.csproj src/Shared/
COPY src/IdentityService/IdentityService.csproj src/IdentityService/
RUN dotnet restore src/IdentityService/IdentityService.csproj

COPY src/Shared/ src/Shared/
COPY src/IdentityService/ src/IdentityService/
RUN dotnet publish src/IdentityService/IdentityService.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "IdentityService.dll"]
