# Build from the repository root: docker build -f src/NotificationService/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore separately so dependency downloads can be cached.
COPY global.json Directory.Build.props ./
COPY src/Shared/Shared.csproj src/Shared/
COPY src/NotificationService/NotificationService.csproj src/NotificationService/
RUN dotnet restore src/NotificationService/NotificationService.csproj

COPY src/Shared/ src/Shared/
COPY src/NotificationService/ src/NotificationService/
RUN dotnet publish src/NotificationService/NotificationService.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Persist this directory with a named volume when running the service.
ENV ConnectionStrings__Notifications="Data Source=/app/data/notifications.db"
RUN mkdir -p /app/data && chown app:app /app/data

COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "NotificationService.dll"]
