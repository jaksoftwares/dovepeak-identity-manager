# Dovepeak Identity

**Open-source, self-hostable Authentication as a Service.**

Dovepeak Identity gives applications a shared, secure identity platform — registration, login, sessions, tokens, roles and a developer portal — so teams stop rebuilding authentication for every product.

> **Status:** Pre-alpha. Phase 0 (project foundations) is in progress. Nothing here is production-ready yet. See the [implementation plan](implementation-plan.md).

---

## Documentation

| Document                                                 | Purpose                                     |
| -------------------------------------------------------- | ------------------------------------------- |
| [Problem statement](problem-statement.md)                | Product and architecture specification      |
| [Implementation phases](implementation-phases.md)        | Phase-by-phase build plan and decisions     |
| [Implementation plan](implementation-plan.md)            | Ordered milestones and release gates        |
| [Architecture decisions](docs/adr/README.md)             | ADRs                                        |
| [Threat model](docs/security/threat-model.md)            | STRIDE threat model                         |

## Repository Layout

```text
/docs                       ADRs, architecture, threat model, runbooks
/services/management-api    ASP.NET Core Management API
/services/workers           Background workers
/identity/keycloak          Realm templates, login themes, extensions
/portal                     Next.js developer portal
/sdks/typescript            @dovepeak/identity
/sdks/dotnet                Dovepeak.Identity
/examples                   Sample integrations
/deploy                     Local and production deployment configuration
/tests                      End-to-end, tenant isolation and load tests
/branding                   Brand assets and guidelines
```

---

## Getting Started

### Prerequisites

| Tool            | Version | Check                     |
| --------------- | ------- | ------------------------- |
| Docker Desktop  | 24+ with Compose v2.20+ | `docker compose version` |
| .NET SDK        | 10.0    | `dotnet --version`        |
| Git             | any     | `git --version`           |

### 1. Configure the environment

```bash
cp .env.example .env
```

The defaults are for local development only. If a port is already in use on your machine, change the matching `*_HOST_PORT` value in `.env`.

### 2. Start the stack

```bash
docker compose up -d --build --wait
```

The first run downloads images and can take several minutes.

### 3. Verify

| Service             | URL                                        | Notes                                           |
| ------------------- | ------------------------------------------ | ----------------------------------------------- |
| Management API      | http://localhost:5080/health/ready         | Should return `"status":"Healthy"`              |
| Keycloak admin      | http://localhost:8080/admin                | Credentials from `.env` (`KEYCLOAK_ADMIN_*`)    |
| Keycloak health     | http://localhost:9000/health/ready         |                                                 |
| Mailpit (email UI)  | http://localhost:8025                      | Captures all outgoing email                     |
| PostgreSQL          | `localhost:5442`                           | Databases: `dovepeak`, `keycloak`               |
| Valkey              | `localhost:6379`                           | Redis-compatible; password in `.env`            |

All ports are bound to `127.0.0.1` and are not reachable from your network.

### Running the Management API outside Docker

Start the dependencies only, then run the API with hot reload:

```bash
docker compose up -d --wait postgres valkey keycloak mailpit
dotnet watch --project services/management-api/src/Dovepeak.Identity.ManagementApi
```

The API listens on http://localhost:5204 and reads connection settings from `appsettings.Development.json`.

### Running tests

```bash
dotnet test Dovepeak.Identity.slnx
```

### Stopping and resetting

```bash
docker compose down          # stop, keep data
docker compose down -v       # stop and delete all local data
```

---

## Health Endpoints

| Endpoint         | Purpose                                                                 |
| ---------------- | ----------------------------------------------------------------------- |
| `/health/live`   | Process is running. Never checks dependencies.                          |
| `/health/ready`  | PostgreSQL, Valkey and Keycloak are reachable. Returns 503 otherwise.   |

Responses report only a status per dependency. Error details are never included.

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). All participants must follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Security

Do not report vulnerabilities in public issues. See [SECURITY.md](SECURITY.md).

## Licence

[Apache License 2.0](LICENSE).
