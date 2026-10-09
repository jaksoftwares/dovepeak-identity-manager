# Build context: repository root.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY services/management-api/src/Dovepeak.Identity.Keycloak/Dovepeak.Identity.Keycloak.csproj services/management-api/src/Dovepeak.Identity.Keycloak/
COPY services/management-api/src/Dovepeak.Identity.Persistence/Dovepeak.Identity.Persistence.csproj services/management-api/src/Dovepeak.Identity.Persistence/
COPY services/management-api/src/Dovepeak.Identity.Platform/Dovepeak.Identity.Platform.csproj services/management-api/src/Dovepeak.Identity.Platform/
COPY services/workers/Dovepeak.Identity.Workers/Dovepeak.Identity.Workers.csproj services/workers/Dovepeak.Identity.Workers/
RUN dotnet restore services/workers/Dovepeak.Identity.Workers/Dovepeak.Identity.Workers.csproj

COPY services/management-api/src/Dovepeak.Identity.Keycloak/ services/management-api/src/Dovepeak.Identity.Keycloak/
COPY services/management-api/src/Dovepeak.Identity.Persistence/ services/management-api/src/Dovepeak.Identity.Persistence/
COPY services/management-api/src/Dovepeak.Identity.Platform/ services/management-api/src/Dovepeak.Identity.Platform/
COPY services/workers/Dovepeak.Identity.Workers/ services/workers/Dovepeak.Identity.Workers/
COPY identity/keycloak/realm-template/ identity/keycloak/realm-template/
RUN dotnet publish services/workers/Dovepeak.Identity.Workers/Dovepeak.Identity.Workers.csproj \
    --configuration Release --no-restore --output /app

# The aspnet image (not plain runtime): the shared Platform library uses ASP.NET Core Data Protection to unprotect
# webhook signing secrets, which ships only in the Microsoft.AspNetCore.App shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .

# Npgsql loads GSSAPI (Kerberos) support on startup; Alpine does not include it by default.
RUN apk add --no-cache krb5-libs

USER $APP_UID
ENTRYPOINT ["dotnet", "Dovepeak.Identity.Workers.dll"]
