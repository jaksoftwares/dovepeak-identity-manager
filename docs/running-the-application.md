# Running Dovepeak Identity

Every step and command needed to run Dovepeak Identity with all of its infrastructure: the local development stack, the example apps, the production stack (on a server or rehearsed on a workstation) and the test suites.

There are three ways to run it:

| Option | What it is | When to use it |
| ------ | ---------- | -------------- |
| [A. Local development stack](#a-local-development-stack) | Every service in Docker on your machine | Normal development and trying the product |
| [B. Example apps](#b-example-apps) | Next.js BFF, React SPA and a protected .NET API on top of A | Seeing a real sign-in end to end |
| [C. Production stack](#c-production-stack) | Caddy with TLS, a two-node Keycloak cluster, pgBackRest backups, full observability | Deploying to a server, or rehearsing that deployment locally |

Commands are run from the repository root unless a step says otherwise. Where PowerShell and bash differ, both are shown.

---

## 0. Prerequisites

| Tool | Version | Check |
| ---- | ------- | ----- |
| Docker Desktop (or Docker Engine on Linux) | 24+ with Compose v2.20+ | `docker compose version` |
| .NET SDK | 10.0 | `dotnet --version` |
| Node.js | 22+ | `node --version` |
| Git | any recent | `git --version` |

* Make sure Docker is running before you start.
* Give Docker Desktop enough memory (Settings → Resources). The stack runs about ten containers, including Keycloak.
* On Windows, the `.sh` scripts (load test, failover test, restore drill, log scan) need **Git Bash** or WSL, not PowerShell.

---

## A. Local development stack

### A1. Configure the environment

Create `.env` from the template:

```bash
cp .env.example .env              # bash
Copy-Item .env.example .env       # PowerShell
```

The values are for local development only. If a port is already in use on your machine, change the matching `*_HOST_PORT` value in `.env`.

### A2. Start everything

```bash
docker compose up -d --build --wait
```

The first run downloads images and builds four containers, so it can take several minutes. This one command starts:

* PostgreSQL (databases `dovepeak` and `keycloak`)
* Valkey (rate-limit counters, sessions, email quotas)
* Keycloak (the identity engine)
* The edge proxy (Traefik: rate limits, blocks `/admin`)
* The mail relay (email quotas and delivery metrics)
* Mailpit (captures all outgoing email)
* The Management API
* The workers (outbox, webhooks, reconciliation, audit)
* The developer portal

It also runs two one-time jobs:

* `db-migrate` applies the database migrations.
* `keycloak-bootstrap` creates the Management API's least-privilege Keycloak service account.

`--wait` returns only when every service is healthy.

### A3. Check that it is healthy

```bash
docker compose ps
curl http://localhost:5080/health/ready         # bash; expect "status":"Healthy"
curl.exe http://localhost:5080/health/ready     # PowerShell
```

| Service | URL | Notes |
| ------- | --- | ----- |
| **Developer portal** | http://localhost:3100 | Start here. Built-in docs at `/docs` |
| Public identity endpoint | http://localhost:8080 | Through the edge. `/admin` and the master realm return 403 by design |
| Keycloak admin console | http://localhost:8081/admin | Local only. Credentials: `KEYCLOAK_ADMIN_*` in `.env` |
| Management API | http://localhost:5080/health/ready | OpenAPI document at http://localhost:5080/openapi/v1.json |
| Keycloak health | http://localhost:9000/health/ready | |
| **Mailpit** (email UI) | http://localhost:8025 | Verification and password-reset emails land here |
| PostgreSQL | `localhost:5442` | Databases `dovepeak` and `keycloak` |
| Valkey | `localhost:6379` | Password: `VALKEY_PASSWORD` in `.env` |

All ports bind to `127.0.0.1` and are not reachable from your network.

### A4. Use it

1. Open http://localhost:3100 and choose **Create an account**.
2. Confirm your email in Mailpit at http://localhost:8025.
3. Create an organization, then a project, then an application.
4. Open the application's **Integration** tab. It shows the issuer, client ID and everything else an app needs.

### A5. Optional: observability

This adds the OpenTelemetry Collector, Prometheus, Alertmanager, Tempo, the blackbox exporter and Grafana.

1. In `.env`, set:

   ```dotenv
   OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
   OTEL_EXPORTER_OTLP_HTTP_ENDPOINT=http://otel-collector:4318
   KC_TRACING_ENABLED=true
   ```

2. Start the stack with the observability profile:

   ```bash
   docker compose --profile observability up -d --build --wait
   ```

3. Open the dashboards:

   | Tool | URL | Credentials |
   | ---- | --- | ----------- |
   | Grafana | http://localhost:3001 | `admin` / `GRAFANA_ADMIN_PASSWORD` in `.env` |
   | Prometheus | http://localhost:9090 | |
   | Alertmanager | http://localhost:9093 | |

See [observability.md](operations/observability.md) for metrics, traces, alerts and drills.

### A6. Stop or reset

```bash
docker compose down        # stop, keep data
docker compose down -v     # stop and delete all local data (full reset)
```

### A7. Optional: run the .NET services outside Docker

Useful while developing the Management API or workers. Start only the infrastructure in Docker:

```bash
docker compose up -d --wait postgres valkey keycloak keycloak-bootstrap edge mailpit db-migrate
```

Then, in separate terminals:

```bash
dotnet watch --project services/management-api/src/Dovepeak.Identity.ManagementApi   # http://localhost:5204
dotnet run --project services/workers/Dovepeak.Identity.Workers
```

Database migrations:

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project services/management-api/src/Dovepeak.Identity.Persistence
dotnet ef database update --project services/management-api/src/Dovepeak.Identity.Persistence
```

---

## B. Example apps

Stack A must be running first.

### B1. Create the demo tenant

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- demo-setup
```

This provisions the `dp-demo` realm, the BFF and SPA clients and a demo user. It writes each example's `.env.local` and prints the demo user's credentials.

### B2. Start the protected .NET API

In its own terminal. It listens on http://localhost:5301.

```bash
dotnet run --project examples/dotnet-protected-api/Dovepeak.Examples.ProtectedApi -- --Auth:Issuer=http://localhost:8080/realms/dp-demo --Auth:Audience=dovepeak-demo-api --Auth:RequireHttpsMetadata=false --Cors:AllowedOrigins:0=http://localhost:5173
```

The `--Cors:AllowedOrigins:0` argument is only needed for the React SPA.

### B3. Install the npm workspaces

From the repository root (the SDK and both examples are npm workspaces):

```bash
npm install
npm run build:sdk
```

### B4. Start an example app

In another terminal. Pick one or both.

**Next.js backend-for-frontend** (the recommended pattern for web apps), at http://localhost:3000:

```bash
cd examples/nextjs-bff
npm run dev
```

**React single-page app**, at http://localhost:5173:

```bash
npm run dev -w @dovepeak/example-react-spa
```

Sign in with the demo user printed by `demo-setup`, or register a new user and confirm the email in Mailpit, then call the protected API.

More detail: [Next.js BFF example](../examples/nextjs-bff/README.md), [React SPA example](../examples/react-spa/README.md).

---

## C. Production stack

The production compose file (`deploy/production/compose.yaml`) adds:

* Caddy with automatic TLS
* A two-node Keycloak cluster
* pgBackRest encrypted point-in-time backups
* The full observability stack

```text
Internet ──443──▶ Caddy (TLS, automatic certificates)
                   ├── auth.example.com    ──▶ edge (rate limits, admin paths blocked) ──▶ Keycloak ×2 (cluster)
                   ├── console.example.com ──▶ developer portal
                   └── api.example.com     ──▶ Management API
                  PostgreSQL (+ pgBackRest), Valkey, workers, mail relay ──▶ your SMTP provider
                  OpenTelemetry Collector, Prometheus, Alertmanager, Tempo, Grafana (127.0.0.1 only)
```

### C1. On a Linux server

The full guide is [self-hosting.md](self-hosting.md). Requirements in short:

* At least 4 vCPU, 8 GB RAM and 60 GB SSD.
* Ubuntu 24.04 or Debian 12.
* Docker Engine 27+.
* Three DNS names pointing at the server.
* An SMTP provider on port 587 with STARTTLS.
* Ideally, an S3-compatible bucket elsewhere for backups.

**1. Install Docker and open the firewall.**

```bash
curl -fsSL https://get.docker.com | sh
sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw allow 443/udp
sudo ufw enable
```

**2. Get the code.**

```bash
sudo mkdir -p /opt/dovepeak && sudo chown "$USER" /opt/dovepeak
git clone https://github.com/jaksoftwares/dovepeak-identity-manager.git /opt/dovepeak
cd /opt/dovepeak && git checkout <release tag, e.g. v1.0.0>
```

**3. Create the configuration.**

```bash
cd /opt/dovepeak/deploy/production
cp .env.example .env
chmod 600 .env
```

**4. Edit `.env` and replace every `CHANGE_ME`.**

| Setting | Notes |
| ------- | ----- |
| `IDENTITY_HOST`, `PORTAL_HOST`, `API_HOST` | The three DNS names. `IDENTITY_HOST` becomes part of every token issuer: choose it once. |
| `ACME_EMAIL` | Receives certificate expiry warnings |
| Passwords and secrets | `openssl rand -base64 32` for each. `VALKEY_PASSWORD`: letters and digits only. `MAIL_RELAY_PASSWORD`: at least 16 characters. |
| `PLATFORM_API_KEY_DIGEST_KEY` | `openssl rand -base64 32`. Changing it later invalidates every Management API key. |
| `SMTP_*`, `MAIL_FROM` | Your provider's submission server and credentials; a sender domain with SPF, DKIM and DMARC |
| `BACKUP_ENCRYPTION_PASSPHRASE` | `openssl rand -base64 48`. Store it outside the server: without it no backup can be restored. |
| `BACKUP_REPO_TYPE`, `BACKUP_S3_*` | `s3` with a bucket elsewhere is strongly recommended |

Keep a copy of the whole `.env` in a password manager or vault. Optionally add an alert receiver: copy `deploy/observability/alertmanager.yml` and point `ALERTMANAGER_CONFIG` at the copy.

**5. Start.**

```bash
docker compose up -d --build --wait
docker compose ps
```

**6. Verify.**

```bash
curl -s https://auth.example.com/realms/dovepeak-platform/.well-known/openid-configuration | grep issuer
curl -s -o /dev/null -w "%{http_code}\n" https://auth.example.com/admin/      # expect 403
docker compose logs keycloak-2 | grep "ISPN000094" | tail -1                  # expect a cluster of (2)
docker compose exec pg-backup pgbackrest info                                 # first full backup
```

Then open `https://console.example.com`, create your developer account, and follow the portal's integration guide.

**7. Operate.**

| Task | How |
| ---- | --- |
| Dashboards | `ssh -L 3001:127.0.0.1:3001 -L 9090:127.0.0.1:9090 -L 9093:127.0.0.1:9093 you@server`, then http://localhost:3001 |
| Keycloak admin console | `ssh -L 8081:127.0.0.1:8081 you@server`, then http://localhost:8081/admin |
| Upgrade | `docker compose exec pg-backup pgbackrest --type=full backup`, then `git fetch && git checkout <new tag>` and `docker compose up -d --build --wait` |
| Logs | `docker compose logs -f <service>` |
| Runbooks | [Signing key rotation](runbooks/signing-key-rotation.md), [key compromise](runbooks/signing-key-compromise.md), [database failure](runbooks/database-failure.md), [email outage](runbooks/email-provider-outage.md), [Keycloak upgrade](runbooks/keycloak-upgrade.md) |

### C2. Rehearsing production on a workstation

You can run the production stack locally without a public domain. To do that, `deploy/production/.env` needs these settings:

```dotenv
IDENTITY_HOST=identity.dovepeak.test
PORTAL_HOST=console.dovepeak.test
API_HOST=api.dovepeak.test
CADDY_LOCAL_CERTS=local_certs
SMTP_HOST=host.docker.internal
SMTP_PORT=1025
SMTP_STARTTLS=false
BACKUP_REPO_TYPE=posix
```

With these settings, Caddy uses its own local certificate authority, and email goes to the local stack's Mailpit.

The production stack also binds `127.0.0.1` ports 8081, 3001, 9090 and 9093, which clash with stack A. Stop stack A first, but keep Mailpit running:

```bash
docker compose stop                     # from the repository root
docker compose up -d mailpit            # keep Mailpit for email capture
cd deploy/production
docker compose up -d --build --wait
```

Next, map the hostnames to your machine in the hosts file:

* Linux/macOS: `/etc/hosts`
* Windows: `C:\Windows\System32\drivers\etc\hosts`, edited as Administrator

```text
127.0.0.1 identity.dovepeak.test console.dovepeak.test api.dovepeak.test
```

Open https://console.dovepeak.test. Your browser will warn about the certificate because it comes from Caddy's local CA. The repository's tests load that CA's root certificate instead.

To stop, run from `deploy/production`:

```bash
docker compose down        # add -v to delete data and backups
```

---

## D. Tests

| Suite | Command | Needs |
| ----- | ------- | ----- |
| Unit tests | `dotnet test Dovepeak.Identity.slnx --filter "Category!=Integration"` | Nothing |
| Integration tests | `dotnet test tests/Dovepeak.Identity.IntegrationTests` | Stack A |
| API fuzzing (Schemathesis) | `dotnet test tests/Dovepeak.Identity.IntegrationTests --filter Category=Fuzz` | Stack A |
| TypeScript SDK unit tests | `npm ci && npm run build:sdk && npm run test:sdk` | Nothing |
| TypeScript SDK contract tests | `npm run test:integration -w @dovepeak/identity` | Stack A |
| BFF end to end | `node tests/e2e/bff-smoke.mjs` | Stack A, plus the protected API and the BFF (section B) |
| Portal end to end (Playwright) | `cd portal && npx playwright install chromium && npx playwright test` | Stack A |
| React SPA end to end (Playwright) | `npm run build -w @dovepeak/example-react-spa`, `npm run preview -w @dovepeak/example-react-spa`, then `cd examples/react-spa && npx playwright test` | Stack A, plus the protected API |
| Log secret scan | `scripts/ci/scan-logs-for-secrets.sh` | After running the tests |
| Load test (k6) | `tests/load/run.sh` (e.g. `VUS=20 DURATION=5m tests/load/run.sh`) | Stack C |
| Keycloak failover | `tests/e2e/ha-failover.sh` | Stack C |
| Restore drill | `scripts/ops/restore-drill.sh` | Stack C |

On Windows, run the `.sh` scripts from Git Bash, or prefix them with `bash` in PowerShell.

---

## E. Developer CLI

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- <command>
```

| Command | Purpose |
| ------- | ------- |
| `demo-setup` | Provision the demo tenant, clients and user; write the examples' `.env.local` |
| `scale-test --realms <n>` | Measure provisioning and runtime behaviour as tenant count grows |
| `rotate-keys`, `retire-keys`, `emergency-rotate-keys [--all]` | Signing key operations |
| `drill-drift --realm <r> --client <c>` | Incident drill: tamper with a client in Keycloak and expect a revert |

---

## F. Troubleshooting

| Symptom | Fix |
| ------- | --- |
| `Copy .env.example to .env` error | A required variable is missing; create or complete `.env` |
| `port is already allocated` | Change the matching `*_HOST_PORT` in `.env`. For stack C, stop stack A first |
| A service stays unhealthy | `docker compose logs -f <service>` (e.g. `keycloak`, `management-api`). Keycloak needs 30–60 seconds on first start |
| No verification email | Check Mailpit (http://localhost:8025) and `docker compose logs mail-relay`. The relay allows 5 emails per recipient per hour by default |
| Need a clean slate | `docker compose down -v`, then start again |
