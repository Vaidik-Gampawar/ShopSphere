# Build from the repository root: docker build -f src/OrderService/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore separately so dependency downloads can be cached.
COPY global.json Directory.Build.props ./
COPY src/Shared/Shared.csproj src/Shared/
COPY src/OrderService/OrderService.csproj src/OrderService/
RUN dotnet restore src/OrderService/OrderService.csproj

COPY src/Shared/ src/Shared/
COPY src/OrderService/ src/OrderService/
RUN dotnet publish src/OrderService/OrderService.csproj \
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
ENTRYPOINT ["dotnet", "OrderService.dll"]
