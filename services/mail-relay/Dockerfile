# Build context: repository root.

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY services/management-api/src/Dovepeak.Identity.Keycloak/Dovepeak.Identity.Keycloak.csproj services/management-api/src/Dovepeak.Identity.Keycloak/
COPY services/management-api/src/Dovepeak.Identity.Persistence/Dovepeak.Identity.Persistence.csproj services/management-api/src/Dovepeak.Identity.Persistence/
COPY services/management-api/src/Dovepeak.Identity.Platform/Dovepeak.Identity.Platform.csproj services/management-api/src/Dovepeak.Identity.Platform/
COPY services/mail-relay/Dovepeak.Identity.MailRelay/Dovepeak.Identity.MailRelay.csproj services/mail-relay/Dovepeak.Identity.MailRelay/
RUN dotnet restore services/mail-relay/Dovepeak.Identity.MailRelay/Dovepeak.Identity.MailRelay.csproj

COPY services/management-api/src/Dovepeak.Identity.Keycloak/ services/management-api/src/Dovepeak.Identity.Keycloak/
COPY services/management-api/src/Dovepeak.Identity.Persistence/ services/management-api/src/Dovepeak.Identity.Persistence/
COPY services/management-api/src/Dovepeak.Identity.Platform/ services/management-api/src/Dovepeak.Identity.Platform/
COPY services/mail-relay/Dovepeak.Identity.MailRelay/ services/mail-relay/Dovepeak.Identity.MailRelay/
COPY identity/keycloak/realm-template/ identity/keycloak/realm-template/
RUN dotnet publish services/mail-relay/Dovepeak.Identity.MailRelay/Dovepeak.Identity.MailRelay.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .

USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 2525 8080
ENTRYPOINT ["dotnet", "Dovepeak.Identity.MailRelay.dll"]
