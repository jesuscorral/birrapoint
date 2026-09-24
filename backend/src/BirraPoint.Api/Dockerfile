# syntax=docker/dockerfile:1
# BirraPoint API — multi-stage image (FR-043, constitution Containerization constraint).
# Build context is `backend/` (needs Directory.*.props and BirraPoint.ServiceDefaults):
#   docker build -f backend/src/BirraPoint.Api/Dockerfile backend
# No secrets or environment-specific configuration are baked in: connection strings, Keycloak,
# SMTP and Frontend__BaseUrl all arrive as environment variables at container start (FR-046).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached across source-only changes.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/BirraPoint.ServiceDefaults/BirraPoint.ServiceDefaults.csproj src/BirraPoint.ServiceDefaults/
COPY src/BirraPoint.Api/BirraPoint.Api.csproj src/BirraPoint.Api/
RUN dotnet restore src/BirraPoint.Api/BirraPoint.Api.csproj

COPY src/BirraPoint.ServiceDefaults/ src/BirraPoint.ServiceDefaults/
COPY src/BirraPoint.Api/ src/BirraPoint.Api/
RUN dotnet publish src/BirraPoint.Api/BirraPoint.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
# Non-root user shipped by the official .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "BirraPoint.Api.dll"]
