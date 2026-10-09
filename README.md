# Dovepeak Identity

**Open-source, self-hostable Authentication as a Service.**

Dovepeak Identity gives applications a shared, secure identity platform — registration, login, sessions, tokens, roles and a developer portal — so teams stop rebuilding authentication for every product.

> **Status:** Pre-alpha. Phases 0–2 (foundations, architecture validation, core identity platform) are complete. The Management API, developer portal and SDKs follow in Phases 3–5. Not production-ready. See the [implementation plan](implementation-plan.md).

---

## Documentation

| Document                                                       | Purpose                                         |
| -------------------------------------------------------------- | ----------------------------------------------- |
| [Problem statement](problem-statement.md)                      | Product and architecture specification          |
| [Implementation phases](implementation-phases.md)              | Phase-by-phase build plan and decisions         |
| [Implementation plan](implementation-plan.md)                  | Ordered milestones and release gates            |
| [Architecture decisions](docs/adr/README.md)                   | ADRs                                            |
| [Phase 1 validation report](docs/validation/phase-1-validation-report.md) | Measured results and the identity engine go/no-go |
| [Threat model](docs/security/threat-model.md)                  | STRIDE threat model and mitigation status       |
| [Known limitations](docs/security/known-limitations.md)        | Security limitations, stated openly             |
| [Runbooks](docs/runbooks)                                      | Operational procedures (signing key rotation)   |
| [GitHub repository setup](docs/github-repository-setup.md)     | Branch protection and security settings         |

## Architecture (current)

```text
             public :8080                         local-only :8081
 Browsers ──▶ edge (Traefik) ──▶ Keycloak ◀── admin API ── Management API (:5080)
 Apps         rate limits,        realm per                 workers (audit collection)
              blocks /admin       tenant environment              │
                    │                  │                          ▼
                    └──── Valkey ◀─────┘                     PostgreSQL
                    (rate-limit counters,             (dovepeak + keycloak databases)
                     BFF sessions)
```

## Repository Layout

```text
/docs                       ADRs, threat model, runbooks, validation reports
/services/management-api    Management API, Keycloak integration library, persistence (EF Core)
/services/workers           Background workers (audit event collection, retention)
/identity/keycloak          Realm template, user profile, bootstrap script, login theme
/examples                   Next.js BFF and .NET protected API examples
/tools                      dovepeak-dev CLI (demo setup, scale test, key rotation)
/deploy                     Local edge and database configuration
/tests                      Integration tests (.NET) and end-to-end tests (Node)
/scripts                    CI scripts (log secret scanning)
/portal, /sdks              Developer portal and SDKs (Phases 4–5)
/branding                   Brand assets and guidelines
```

---

## Getting Started

### Prerequisites

| Tool            | Version                 | Check                     |
| --------------- | ----------------------- | ------------------------- |
| Docker Desktop  | 24+ with Compose v2.20+ | `docker compose version`  |
| .NET SDK        | 10.0                    | `dotnet --version`        |
| Node.js         | 22+ (for the BFF example) | `node --version`        |

### 1. Configure the environment

```bash
cp .env.example .env
```

The defaults are for local development only. If a port is already in use on your machine, change the matching `*_HOST_PORT` value in `.env`.

### 2. Start the stack

```bash
docker compose up -d --build --wait
```

The first run downloads images and builds three containers; it can take several minutes. One-shot jobs run automatically: `db-migrate` applies database migrations and `keycloak-bootstrap` creates the Management API's least-privilege Keycloak account.

### 3. Verify

| Service                   | URL                                  | Notes                                                      |
| ------------------------- | ------------------------------------ | ---------------------------------------------------------- |
| Public identity endpoint  | http://localhost:8080                | Through the edge. `/admin` and the master realm return 403 |
| Keycloak admin console    | http://localhost:8081/admin          | Direct, local only. Credentials: `KEYCLOAK_ADMIN_*` in `.env` |
| Management API            | http://localhost:5080/health/ready   | Should return `"status":"Healthy"`                         |
| Keycloak health           | http://localhost:9000/health/ready   |                                                            |
| Mailpit (email UI)        | http://localhost:8025                | Captures all outgoing email                                |
| PostgreSQL                | `localhost:5442`                     | Databases: `dovepeak`, `keycloak`                          |
| Valkey                    | `localhost:6379`                     | Redis-compatible; password in `.env`                       |

All ports are bound to `127.0.0.1` and are not reachable from your network.

### 4. Try a real sign-in

The [Next.js BFF example](examples/nextjs-bff/README.md) walks through registration, email verification and calling a protected API:

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- demo-setup
```

---

## Testing

| Suite | Command | Needs the stack |
| ----- | ------- | --------------- |
| Unit tests | `dotnet test Dovepeak.Identity.slnx --filter "Category!=Integration"` | No |
| Integration tests (~85 tests: provisioning, flows, tokens, sessions, recovery, lockout, keys, audit, edge) | `dotnet test tests/Dovepeak.Identity.IntegrationTests` | Yes |
| BFF end-to-end | `node tests/e2e/bff-smoke.mjs` (see the example README) | Yes, plus the example apps |
| Log secret scan | `scripts/ci/scan-logs-for-secrets.sh` | Yes, after running the tests |

Integration tests provision their own throwaway tenant realms through the production provisioning code and delete them afterwards. Every suite runs in CI on each pull request.

## Developer CLI

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- <command>
```

| Command | Purpose |
| ------- | ------- |
| `demo-setup` | Provision the demo tenant, BFF client and user; write the example's `.env.local` |
| `scale-test --realms <n>` | Measure provisioning and runtime behaviour as tenant count grows |
| `rotate-keys`, `retire-keys`, `emergency-rotate-keys` | Signing key operations ([runbook](docs/runbooks/signing-key-rotation.md)) |

## Running services outside Docker

```bash
docker compose up -d --wait postgres valkey keycloak keycloak-bootstrap edge mailpit db-migrate
dotnet watch --project services/management-api/src/Dovepeak.Identity.ManagementApi   # http://localhost:5204
dotnet run --project services/workers/Dovepeak.Identity.Workers
```

### Database migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project services/management-api/src/Dovepeak.Identity.Persistence
dotnet ef database update --project services/management-api/src/Dovepeak.Identity.Persistence
```

### Stopping and resetting

```bash
docker compose down          # stop, keep data
docker compose down -v       # stop and delete all local data
```

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). All participants must follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Security

Do not report vulnerabilities in public issues. See [SECURITY.md](SECURITY.md).

## Licence

[Apache License 2.0](LICENSE).
