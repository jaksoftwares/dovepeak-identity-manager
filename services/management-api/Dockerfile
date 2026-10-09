# Build context: repository root (needs Directory.Build.props and .editorconfig).

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY services/management-api/src/Dovepeak.Identity.ManagementApi/Dovepeak.Identity.ManagementApi.csproj services/management-api/src/Dovepeak.Identity.ManagementApi/
COPY services/management-api/src/Dovepeak.Identity.Keycloak/Dovepeak.Identity.Keycloak.csproj services/management-api/src/Dovepeak.Identity.Keycloak/
COPY services/management-api/src/Dovepeak.Identity.Persistence/Dovepeak.Identity.Persistence.csproj services/management-api/src/Dovepeak.Identity.Persistence/
COPY services/management-api/src/Dovepeak.Identity.Platform/Dovepeak.Identity.Platform.csproj services/management-api/src/Dovepeak.Identity.Platform/
RUN dotnet restore services/management-api/src/Dovepeak.Identity.ManagementApi/Dovepeak.Identity.ManagementApi.csproj

COPY services/management-api/src/ services/management-api/src/
COPY identity/keycloak/realm-template/ identity/keycloak/realm-template/
RUN dotnet publish services/management-api/src/Dovepeak.Identity.ManagementApi/Dovepeak.Identity.ManagementApi.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .

# Npgsql loads GSSAPI (Kerberos) support on startup; Alpine does not include it by default.
RUN apk add --no-cache krb5-libs

# Run as the image's built-in non-root user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Dovepeak.Identity.ManagementApi.dll"]
