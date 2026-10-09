# Build context: repository root (needs Directory.Build.props and .editorconfig).

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props .editorconfig ./
COPY services/management-api/src/Dovepeak.Identity.ManagementApi/Dovepeak.Identity.ManagementApi.csproj services/management-api/src/Dovepeak.Identity.ManagementApi/
RUN dotnet restore services/management-api/src/Dovepeak.Identity.ManagementApi/Dovepeak.Identity.ManagementApi.csproj

COPY services/management-api/src/ services/management-api/src/
RUN dotnet publish services/management-api/src/Dovepeak.Identity.ManagementApi/Dovepeak.Identity.ManagementApi.csproj \
    --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
COPY --from=build /app .

# Run as the image's built-in non-root user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Dovepeak.Identity.ManagementApi.dll"]
