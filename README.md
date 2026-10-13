# Dovepeak Identity

**Open-source, self-hostable Authentication as a Service.**

Dovepeak Identity gives applications a shared, secure identity platform — registration, login, sessions, tokens, roles and a developer portal — so teams stop rebuilding authentication for every product.

> **Status:** Release candidate for 1.0. Phases 0–5 are complete; Phase 6 (production readiness) is implemented and rehearsed: observability and SLOs, encrypted point-in-time backups, a two-node Keycloak cluster, runbooks, load testing and API fuzzing, and a production deployment with automatic TLS. Version 1.0 is not tagged until the independent penetration test is done and its critical and high findings are fixed. See the [implementation plan](implementation-plan.md).

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
| [Running the application](docs/running-the-application.md)     | Every command to run the full stack, examples and tests |
| [Integration guide](docs/integration-guide.md)                 | Add sign-in to your app: where user data lives, which credential goes where, every stack, APIs, branding, API keys, webhooks |
| [Self-hosting guide](docs/self-hosting.md)                     | Production deployment on a Linux server         |
| [Supported flows](docs/supported-flows.md)                     | Flows, defaults and what is deliberately rejected |
| [Service-level objectives](docs/operations/service-level-objectives.md) | Availability, latency, RPO and RTO targets |
| [Observability](docs/operations/observability.md)              | Metrics, traces, alerts and drills              |
| [Runbooks](docs/runbooks)                                      | Database failure, email outage, key rotation and compromise, Keycloak upgrade, tenant sharding |
| [Release policy](docs/release-policy.md)                       | Versioning, supported versions, security patches |
| [GitHub repository setup](docs/github-repository-setup.md)     | Branch protection and security settings         |

## Architecture (local development stack)

```text
                                       Developers ──▶ Portal (:3100) ──┐
             public :8080                         local-only :8081   ▼
 Browsers ──▶ edge (Traefik) ──▶ Keycloak ◀── admin API ── Management API (:5080)
 Apps         rate limits,        realm per                 workers (outbox, webhooks,
              blocks /admin       tenant environment        reconciliation, audit)
                    │                  │                          │
                    │                  │                          ▼
                    └──── Valkey ◀─────┘                     PostgreSQL
                    (rate-limit counters,             (dovepeak + keycloak databases)
                     sessions, email quotas)

 Keycloak ──SMTP──▶ mail relay (quotas, delivery metrics) ──▶ provider (Mailpit locally)
 Optional: docker compose --profile observability up  (collector, Prometheus, Alertmanager, Tempo, Grafana)
```

The production deployment (`deploy/production`) adds Caddy for TLS, a two-node Keycloak cluster and pgBackRest backups; see the [self-hosting guide](docs/self-hosting.md).

## How It Works: Every Container Explained

`docker compose up` starts the whole platform as a set of containers. Each one has a single job. This section explains what every container does, why it is there, the order the containers start in, and how a request travels through them.

### The big picture in one paragraph

**Keycloak** is the identity engine: it stores users, checks passwords, shows the hosted login pages and issues tokens. Dovepeak never lets the internet talk to Keycloak directly. Public traffic goes through **the edge** (Traefik), which applies rate limits and blocks administrative paths. Developers never configure Keycloak by hand either. They use the **developer portal**, which calls the **Management API**, which stores the desired configuration in **PostgreSQL** and applies it to Keycloak. **Workers** run in the background: they finish slow provisioning work, deliver webhooks, collect audit events and undo any change made to Keycloak outside Dovepeak. **Valkey** holds fast, short-lived data: rate-limit counters, portal sessions and email quotas. All email leaves through the **mail relay**, which enforces sending quotas before handing the message to an email provider (**Mailpit** locally).

### Core containers (always started)

| Container | Image | Host port | What it does |
| --------- | ----- | --------- | ------------ |
| `postgres` | `postgres:17.11-alpine` | `5442` | **The database.** Holds two separate databases with their own least-privilege users, created on first start by `deploy/local/postgres/init/01-create-databases.sh`. `dovepeak` stores the platform's own data: organizations, members, projects, environments, applications, API keys, webhooks, the outbox and audit events. Tenant-owned tables are protected by forced PostgreSQL Row-Level Security, so one organization can never read another's rows. `keycloak` stores Keycloak's data: realms, users, password hashes, sessions and signing keys. |
| `valkey` | `valkey/valkey:8.1-alpine` | `6379` | **The fast in-memory store** (an open-source, Redis-compatible fork). Holds data that is short-lived or needs very fast updates. It stores three kinds of data: the edge's per-IP rate-limit counters, the developer portal's server-side sessions (database 2), and the mail relay's per-recipient and per-tenant email counters. It requires a password and saves to disk (`appendonly`), so it survives restarts. |
| `keycloak` | `quay.io/keycloak/keycloak:26.7.5` | `8081` (admin, local only), `9000` (health and metrics) | **The identity engine.** Every tenant environment (development, staging, production of each project) is its own isolated Keycloak *realm*, with its own users, keys and settings. Keycloak serves the hosted login, registration, password-reset and email-verification pages (with the Dovepeak theme from `identity/keycloak/themes`). It runs the OpenID Connect and OAuth 2.0 endpoints that apps use to get tokens, and it signs those tokens. Its public address is `http://localhost:8080` (through the edge). The admin console on `8081` is bound to your machine only and is never exposed publicly. |
| `keycloak-bootstrap` | `quay.io/keycloak/keycloak:26.7.5` | none | **One-shot setup job; runs once and exits.** Waits for Keycloak to be healthy, then runs `identity/keycloak/bootstrap/bootstrap.sh`. The script creates the `dovepeak-management` service account, which the Management API and workers use to talk to Keycloak. That account gets only the permissions it needs, not full admin rights. The Management API and workers wait until this job has finished successfully. A status of `Exited (0)` is expected. |
| `edge` | `traefik:v3.5.3` | `8080` | **The public front door to Keycloak.** Everything a browser or app sends to the identity endpoint passes through here (routes in `deploy/local/traefik/dynamic.yml`). It returns **403** for `/admin`, the `master` realm, `/metrics` and `/health`, so attackers cannot reach administrative surfaces. It applies three tiers of **per-IP rate limits**, counted in Valkey: very strict on endpoints that send email (registration, password reset), strict on endpoints that check credentials (login, token, logout), and generous for everything else. It adds security headers. Its access logs drop the URL path, because email links carry one-time tokens in the query string. |
| `mailpit` | `axllent/mailpit:v1.27` | `8025` (web UI), `1025` (SMTP) | **A fake email inbox for local development.** It catches every email the platform sends (verification links, password resets, security alerts, invitations) and shows them at http://localhost:8025. Nothing reaches real mailboxes. In production it is replaced by a real SMTP provider. |
| `mail-relay` | built from `services/mail-relay` | none | **The gatekeeper for outgoing email.** Every Keycloak realm sends its email here instead of directly to the provider. Each realm signs in with its realm name, so the relay knows which tenant is sending. The relay enforces **sending quotas**: by default 5 emails per recipient per hour, 20 per day, and 500 per tenant per hour. This prevents anyone from using "forgot password" to flood someone's inbox or burn the email budget. It counts in Valkey, forwards allowed messages to the upstream provider (Mailpit locally) and records delivery metrics. |
| `db-migrate` | built from `services/management-api/.../Dockerfile.migrations` | none | **One-shot setup job; runs once and exits.** Applies the Entity Framework Core database migrations to the `dovepeak` database, so the schema always matches the code. The Management API and workers start only after it finishes successfully. A status of `Exited (0)` is expected. |
| `management-api` | built from `services/management-api` | `5080` | **The brain of the platform** (ASP.NET Core, `/v1` REST API, OpenAPI at `/openapi/v1.json`). Everything a developer can configure goes through it: organizations, members and invitations, API keys, projects and their three environments, applications (SPA, native, web, machine), roles, scopes, branding, token lifetimes, webhooks, end users and sessions. It authenticates developers with tokens from the `dovepeak-platform` realm or with organization API keys. It checks organization roles (viewer ⊂ developer ⊂ admin ⊂ owner), saves the desired state in PostgreSQL and applies it to Keycloak with the bootstrap service account. Work that takes time, such as creating a realm, is written to the *outbox* and finished by the workers. |
| `workers` | built from `services/workers` | none | **The background jobs.** Four jobs run on intervals. The **outbox job** completes queued work such as provisioning a new environment's realm, retrying safely if interrupted. The **webhook job** delivers signed (HMAC-SHA256) event notifications to customers' HTTPS endpoints, with retries and exponential backoff. The **reconciliation job** compares every environment in Keycloak with its stored configuration and reverts changes made directly in Keycloak (*drift*), emitting a `drift.corrected` event. The **audit collection job** pulls login and admin events from Keycloak into the platform's audit log and deletes events older than the retention period. |
| `portal` | built from `portal` (Next.js) | `3100` | **The developer portal: the website developers use.** Sign up, create organizations, projects and applications, copy integration details, manage API keys, webhooks, branding and team members, and read the built-in docs at `/docs`. It is a *backend-for-frontend*: you sign in through Keycloak's `dovepeak-platform` realm, but the tokens stay on the portal server and in Valkey. The browser only holds an `HttpOnly` session cookie. Every action the portal takes is a call to the Management API. |

### One-shot jobs versus long-running services

`keycloak-bootstrap` and `db-migrate` are meant to stop. After a successful start, `docker compose ps -a` shows them as `Exited (0)`, and every other container as `running (healthy)`. If either job exits with a non-zero code, the services that depend on it do not start; check it with `docker compose logs db-migrate` or `docker compose logs keycloak-bootstrap`.

### Start-up order

Containers start in dependency order, and each step waits for the previous one to be *healthy* (or, for one-shot jobs, *completed successfully*):

```text
1. postgres, valkey, mailpit                     (no dependencies)
2. keycloak            ← postgres healthy
   db-migrate          ← postgres healthy          (runs, then exits)
   mail-relay          ← valkey healthy
3. keycloak-bootstrap  ← keycloak healthy          (runs, then exits)
   edge                ← keycloak + valkey healthy
4. management-api      ← postgres, valkey, mail-relay healthy; db-migrate and keycloak-bootstrap completed
   workers             ← db-migrate and keycloak-bootstrap completed
5. portal              ← management-api + valkey healthy
```

`docker compose up -d --wait` returns only when the whole chain is healthy.

### Observability containers (optional: `--profile observability`)

| Container | Image | Host port | What it does |
| --------- | ----- | --------- | ------------ |
| `otel-collector` | `otel/opentelemetry-collector-contrib` | none | **The telemetry hub.** The Management API, workers, mail relay, portal and Keycloak send traces and metrics here over OpenTelemetry (gRPC `4317`, HTTP `4318`). It removes sensitive attributes and query strings, then forwards traces to Tempo and exposes metrics to Prometheus. |
| `tempo` | `grafana/tempo` | none | **Trace storage.** Keeps distributed traces, so you can follow one request from the portal through the Management API into Keycloak and the database. |
| `prometheus` | `prom/prometheus` | `9090` | **Metrics storage and alert rules.** Scrapes Keycloak, the edge, the collector and the blackbox exporter, and evaluates the SLO and alert rules in `deploy/observability/prometheus/rules`. Keeps 30 days. |
| `blackbox` | `prom/blackbox-exporter` | none | **External health prober.** Checks the readiness endpoints from the outside, the way a user would, so an outage is detected even if a service cannot report on itself. |
| `alertmanager` | `prom/alertmanager` | `9093` | **Alert routing.** Receives firing alerts from Prometheus, groups them and sends notifications to the receivers configured in `deploy/observability/alertmanager.yml` (email, Slack, PagerDuty). |
| `grafana` | `grafana/grafana` | `3001` | **Dashboards.** Comes with the pre-provisioned *Dovepeak Identity — Overview* dashboard (`deploy/observability/grafana`). It shows SLO status (availability, sign-in latency, email hand-off), sign-ins and failed sign-ins per realm, edge responses and rate-limited requests, Management API latency, email outcomes and throttling, outbox, webhooks and drift corrections, and links to traces. Prometheus and Tempo are its data sources. |

### What production adds (`deploy/production`)

| Container | What it does |
| --------- | ------------ |
| `caddy` | **The only container open to the internet** (ports 80 and 443). It obtains and renews HTTPS certificates automatically (Let's Encrypt) and routes the three public hostnames: the identity host goes to the edge, the console host to the portal, and the API host to the Management API. |
| `keycloak-1`, `keycloak-2` | **Two Keycloak nodes in one cluster** (replacing the single `keycloak`). They share sessions, so if one node crashes, users stay signed in and tokens keep working. They are built from a production Keycloak image (`deploy/production/keycloak`). |
| `pg-backup` | **Encrypted backups with pgBackRest.** PostgreSQL continuously archives its write-ahead log; this container schedules full backups (weekly by default) and differential backups (daily). That allows point-in-time restore to any moment, to local disk or an S3-compatible bucket. |
| (no `mailpit`) | The mail relay forwards to your real SMTP provider (`SMTP_*` settings). |

The observability containers always run in production. In production, only Caddy publishes public ports; Grafana, Prometheus, Alertmanager and the Keycloak admin console bind to `127.0.0.1` and are reached through an SSH tunnel.

### How a request travels through the system

**An end user signs in to your app:**

```text
Browser ─▶ edge (rate limit, block /admin) ─▶ Keycloak (realm of that environment)
        ◀─ hosted login page; user enters password; Keycloak checks it against the hash in PostgreSQL
        ◀─ redirect back to the app with a one-time code
App server ─▶ edge ─▶ Keycloak: exchanges the code (+ PKCE verifier) for signed tokens
App API checks the token signature against the realm's public keys (JWKS) — no call to Dovepeak needed
```

**A developer creates a project in the portal:**

```text
Browser ─▶ portal (session cookie; token kept server-side in Valkey)
portal ─▶ Management API: POST /v1/organizations/{id}/projects
Management API: checks the developer's role ─▶ saves project + 3 environments in PostgreSQL ─▶ writes outbox items
workers (outbox job) ─▶ Keycloak: creates one realm per environment from the realm template
workers (reconciliation job, continuously) ─▶ keeps every realm matching what PostgreSQL says
```

**A user asks for a password reset:**

```text
Browser ─▶ edge (strictest "email" rate limit) ─▶ Keycloak ─SMTP─▶ mail-relay
mail-relay: under quota for this recipient and tenant? ─▶ Mailpit (local) or SMTP provider (production)
```

### Where data lives (Docker volumes)

| Volume | Contents | Lost on `docker compose down -v` |
| ------ | -------- | -------------------------------- |
| `postgres-data` | Both databases: all platform and Keycloak data | Yes |
| `valkey-data` | Sessions, rate-limit and quota counters | Yes |
| `prometheus-data`, `tempo-data`, `grafana-data` | Metrics, traces, Grafana state (observability profile) | Yes |

`docker compose down` keeps all volumes. `docker compose down -v` deletes them and gives you a completely fresh platform.

## Repository Layout

```text
/docs                       ADRs, threat model, runbooks, validation reports
/services/management-api    Management API, platform services, Keycloak integration library, persistence (EF Core)
/services/workers           Background workers (outbox, webhooks, reconciliation, audit collection)
/services/mail-relay        SMTP relay between Keycloak and the email provider: per-recipient and per-tenant quotas
/identity/keycloak          Realm template, user profile, bootstrap script, login theme
/examples                   Next.js BFF, React SPA and .NET protected API examples, built on the SDKs
/tools                      dovepeak-dev CLI (demo setup, scale test, key rotation, incident drills)
/deploy/local               Local edge and database configuration
/deploy/production          Production compose: Caddy, Keycloak cluster, pgBackRest, full observability
/deploy/observability       OpenTelemetry Collector, Prometheus rules, Alertmanager, Tempo, Grafana dashboards
/tests                      Integration tests (.NET), end-to-end and failover tests (Node), load tests (k6)
/scripts                    CI scripts (log secret scanning) and operations scripts (restore drill)
/portal                     Developer portal (Next.js backend-for-frontend) with built-in documentation
/sdks/typescript            @dovepeak/identity: OIDC client, Next.js BFF, React hooks, Node.js token verification
/sdks/dotnet                Dovepeak.Identity: ASP.NET Core authentication, role/scope policies, Management API client
/branding                   Brand assets and guidelines
```

---

## Getting Started

This is the quick path. For every command — the example apps, the production stack, rehearsing production locally, all test suites and troubleshooting — see [Running the application](docs/running-the-application.md).

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
| Management API OpenAPI    | http://localhost:5080/openapi/v1.json | OpenAPI 3.1 document for `/v1`                            |
| Developer portal          | http://localhost:3100                | Create an account, then an organization and a project. Docs at `/docs` |
| Keycloak health           | http://localhost:9000/health/ready   |                                                            |
| Mailpit (email UI)        | http://localhost:8025                | Captures all outgoing email                                |
| PostgreSQL                | `localhost:5442`                     | Databases: `dovepeak`, `keycloak`                          |
| Valkey                    | `localhost:6379`                     | Redis-compatible; password in `.env`                       |

All ports are bound to `127.0.0.1` and are not reachable from your network.

### 4. Open the developer portal

Go to http://localhost:3100, choose **Create an account**, and confirm the email in Mailpit (http://localhost:8025). Then create an organization, a project and an application; the **Integration** tab shows everything your app needs.

To connect your own application — which credentials go where, where your users' data lives, and code for Next.js, single-page apps, mobile, machine-to-machine and APIs — follow the [integration guide](docs/integration-guide.md).

### 5. Try a real sign-in in an example app

The [Next.js BFF example](examples/nextjs-bff/README.md) walks through registration, email verification and calling a protected API:

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- demo-setup
```

## Management API

The Management API (`/v1`) is how developers and automation configure Dovepeak Identity. The developer portal (Phase 4) is built on it.

| Concept | Notes |
| ------- | ----- |
| Resource model | Organization → members, invitations, API keys, webhooks, audit events · Project → development, staging and production environments (one isolated realm each) · Environment → applications (SPA, native, web, machine), roles, end users and sessions |
| Authentication | Developer access tokens from the `dovepeak-platform` realm (portal client, audience `dovepeak-management-api`), or organization API keys: `Authorization: Bearer dpk_live_…` |
| Authorization | Organization roles nest: viewer ⊂ developer ⊂ admin ⊂ owner. API keys carry explicit scopes no broader than their creator's; managing the organization, members and API keys is reserved for people |
| Tenant isolation | Central authorization, EF Core query filters and forced PostgreSQL Row-Level Security; another organization's resources always return `404` ([ADR-0008](docs/adr/0008-tenant-isolation-in-the-management-plane.md)) |
| Token policy | Per application: access token lifetime (5–60 min) and session idle and maximum timeouts; omitted values inherit the environment baseline (10 min, 30 min, 12 h). The `/config` endpoint returns the effective values |
| Branding | Per project, in every environment: logo, brand colour (readable button text chosen automatically) and the subject and introduction of verification and password-reset emails (`PUT …/projects/{id}/branding`). Users also receive branded security alerts when their password or sign-in methods change |
| Roles and scopes | Application roles arrive in a flat `roles` claim. OAuth scopes are defined per environment (`…/scopes`), granted per application, and appear in the standard `scope` claim only when requested |
| Secrets | Client secrets, API keys and webhook signing secrets are shown once at creation and never returned again. Rotating a client secret keeps the previous one valid for 24 hours (`previousSecretExpiresAt`) so you can redeploy without downtime; `?revokePrevious=true` or `DELETE …/secret/previous` ends that at once |
| Errors | RFC 9457 Problem Details with a stable `code` field (e.g. `quota_exceeded`, `idempotency_key_reused`) |
| Idempotency | Send `Idempotency-Key` on create requests; a retry with the same body replays the original response (`Idempotent-Replayed: true`) without one-time secrets (`Dovepeak-Secret-Omitted: true`) |
| Webhooks | HTTPS endpoints receive signed events (`Dovepeak-Signature: t=…,v1=…`, HMAC-SHA256) with retries and exponential backoff; private and link-local addresses are refused |
| Drift | A worker reconciles every environment (realm settings, client policies, scopes, applications) with its stored configuration and reverts changes made directly in Keycloak, emitting a `drift.corrected` event |

```bash
curl -H "Authorization: Bearer $DOVEPEAK_API_KEY"   http://localhost:5080/v1/organizations/$ORG_ID/projects
```

---

## Testing

| Suite | Command | Needs the stack |
| ----- | ------- | --------------- |
| Unit tests | `dotnet test Dovepeak.Identity.slnx --filter "Category!=Integration"` | No |
| Integration tests (provisioning, flows, tokens, sessions, recovery, lockout, keys, audit, edge, Management API, tenant isolation) | `dotnet test tests/Dovepeak.Identity.IntegrationTests` | Yes |
| BFF end-to-end | `node tests/e2e/bff-smoke.mjs` (see the example README) | Yes, plus the example apps |
| Portal end-to-end (Playwright: the whole developer journey in a real browser) | `cd portal && npx playwright install chromium && npx playwright test` | Yes |
| TypeScript SDK unit tests | `npm ci && npm run build:sdk && npm run test:sdk` | No |
| TypeScript SDK contract tests (live Keycloak) | `npm run test:integration -w @dovepeak/identity` | Yes |
| React SPA end-to-end (Playwright) | `npm run build -w @dovepeak/example-react-spa`, `npm run preview -w …`, then `cd examples/react-spa && npx playwright test` | Yes, plus the example API |
| Log secret scan | `scripts/ci/scan-logs-for-secrets.sh` | Yes, after running the tests |
| API fuzzing (Schemathesis against the OpenAPI document) | `dotnet test tests/Dovepeak.Identity.IntegrationTests --filter Category=Fuzz` | Yes (runs Schemathesis in Docker) |
| Load test (k6: sign-in and refresh through the public edge) | `tests/load/run.sh` | The production stack (`deploy/production`) |
| Keycloak failover (stop a node; sessions and tokens keep working) | `tests/e2e/ha-failover.sh` | The production stack |
| Restore drill (point-in-time restore into a scratch copy) | `scripts/ops/restore-drill.sh` | The production stack |

Integration tests provision their own throwaway tenant realms through the production provisioning code and delete them afterwards. The Management API tests host the real API in-process, sign developers in through the real platform realm, and remove every organization they create. `DeployedStackTests` exercises the containerized API and workers with no in-process shortcuts. Every suite runs in CI on each pull request.

## SDKs

| Package | Install | Highlights |
| ------- | ------- | ---------- |
| [`@dovepeak/identity`](sdks/typescript/README.md) | `npm install @dovepeak/identity` | Authorization Code + PKCE client; `/next` backend-for-frontend with server-side sessions; `/react` hooks; `/node` token verification with optional introspection; typed errors |
| [`Dovepeak.Identity`](sdks/dotnet/src/Dovepeak.Identity/README.md) | `dotnet add package Dovepeak.Identity` | One-line JWT bearer setup, `RequireScope` / `RequireDovepeakRole`, introspection, typed Management API client with `IHttpClientFactory` |

The [browser threat model](docs/sdk/browser-threat-model.md) explains how tokens are stored and why a backend-for-frontend is recommended. The JavaScript packages are npm workspaces (`npm ci` at the repository root). Releases: [`.changeset/README.md`](.changeset/README.md).

## Developer CLI

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- <command>
```

| Command | Purpose |
| ------- | ------- |
| `demo-setup` | Provision the demo tenant, BFF client and user; write the example's `.env.local` |
| `scale-test --realms <n>` | Measure provisioning and runtime behaviour as tenant count grows |
| `rotate-keys`, `retire-keys`, `emergency-rotate-keys [--all]` | Signing key operations ([rotation](docs/runbooks/signing-key-rotation.md), [compromise](docs/runbooks/signing-key-compromise.md)) |
| `drill-drift --realm <r> --client <c>` | Incident drill: tamper with a client in Keycloak, expect a revert and the `ConfigurationDriftCorrected` alert |

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
