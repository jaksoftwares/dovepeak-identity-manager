# Self-Hosting Guide

Deploy Dovepeak Identity on a single Linux server with Docker Compose (milestone M6.5). The result: hosted sign-in, the developer portal and the Management API on your own domains over HTTPS, two clustered Keycloak nodes, encrypted point-in-time backups, and monitoring with alerts.

Plan about an hour for a first deployment.

## What you get

```text
Internet ──443──▶ Caddy (TLS, automatic certificates)
                   ├── auth.example.com    ──▶ edge (rate limits, admin paths blocked) ──▶ Keycloak ×2 (cluster)
                   ├── console.example.com ──▶ developer portal
                   └── api.example.com     ──▶ Management API
                  PostgreSQL (+ pgBackRest), Valkey, workers, mail relay ──▶ your SMTP provider
                  OpenTelemetry Collector, Prometheus, Alertmanager, Tempo, Grafana (127.0.0.1 only)
```

Only ports 80 and 443 are open to the internet. Everything else stays on private Docker networks or binds to `127.0.0.1`.

## 1. Requirements

| Item | Minimum | Recommended |
| ---- | ------- | ----------- |
| Server | 4 vCPU, 8 GB RAM, 60 GB SSD | 8 vCPU, 16 GB RAM (sign-in capacity scales with CPU: password hashing) |
| OS | Ubuntu 24.04 LTS or Debian 12, 64-bit | Same, with full-disk encryption (LUKS) |
| Docker | Docker Engine 27+ with the Compose plugin (`docker compose version`) | |
| DNS | Three names pointing at the server (A/AAAA) | e.g. `auth.`, `console.`, `api.` under your domain |
| Email | An SMTP provider with submission on port 587 and STARTTLS | SPF, DKIM and DMARC set up for the sender domain |
| Backups | Space on the server for the backup volume | An S3-compatible bucket in another location |

Encrypt the disk when you provision the server: it holds password hashes, signing keys and personal data (threat model, open question 3).

## 2. Prepare the server

```bash
# Docker Engine and Compose plugin (https://docs.docker.com/engine/install/)
curl -fsSL https://get.docker.com | sh

# Firewall: SSH, HTTP (certificate challenges and redirects) and HTTPS only
sudo ufw allow OpenSSH && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp && sudo ufw allow 443/udp
sudo ufw enable

# Code
sudo mkdir -p /opt/dovepeak && sudo chown "$USER" /opt/dovepeak
git clone https://github.com/jaksoftwares/dovepeak-identity-manager.git /opt/dovepeak
cd /opt/dovepeak && git checkout <release tag, e.g. v1.0.0>
```

Docker publishes ports past `ufw`; that is fine here because only Caddy publishes public ports.

## 3. Configure

```bash
cd /opt/dovepeak/deploy/production
cp .env.example .env
chmod 600 .env
```

Edit `.env` and replace every `CHANGE_ME`:

| Setting | Notes |
| ------- | ----- |
| `IDENTITY_HOST`, `PORTAL_HOST`, `API_HOST` | The three DNS names. **`IDENTITY_HOST` becomes part of every token issuer**: choose it once; changing it later breaks every integration. |
| `ACME_EMAIL` | Receives certificate expiry warnings |
| Passwords and secrets | `openssl rand -base64 32` for each. `VALKEY_PASSWORD`: letters and digits only (it is embedded in a URL). `MAIL_RELAY_PASSWORD`: at least 16 characters. |
| `PLATFORM_API_KEY_DIGEST_KEY` | `openssl rand -base64 32`. Changing it later invalidates every Management API key. |
| `SMTP_*`, `MAIL_FROM` | Your provider's submission server and credentials; the sender address on a domain with SPF/DKIM/DMARC. |
| `BACKUP_ENCRYPTION_PASSPHRASE` | `openssl rand -base64 48`. **Store it outside the server** (password manager or vault): without it, no backup can be restored. |
| `BACKUP_REPO_TYPE` and `BACKUP_S3_*` | `s3` with a bucket elsewhere is strongly recommended. With `posix`, backups live on the same disk as the database and do not survive losing the server. |

Then copy the whole `.env` into your password manager or vault as well: rebuilding a lost server needs exactly these values.

### Alert notifications

Copy `deploy/observability/alertmanager.yml`, add a receiver (email, Slack, PagerDuty; see [observability.md](operations/observability.md#sending-alerts-somewhere)) and point `ALERTMANAGER_CONFIG` at your copy. Use a channel that does not depend on Dovepeak Identity for sign-in.

## 4. Start

```bash
docker compose up -d --build --wait
```

The first start builds the images and takes several minutes. When it returns, every service is healthy:

```bash
docker compose ps
```

Caddy obtains certificates on the first HTTPS request to each name; DNS must already point at the server.

## 5. Verify

```bash
# Issuer and keys of the platform realm, over HTTPS
curl -s https://auth.example.com/realms/dovepeak-platform/.well-known/openid-configuration | grep issuer
# Administrative paths are blocked on the public hostname (expect 403)
curl -s -o /dev/null -w "%{http_code}\n" https://auth.example.com/admin/
# Both Keycloak nodes in one cluster (expect "(2)")
docker compose logs keycloak-2 | grep "ISPN000094" | tail -1
# First full backup (taken automatically a few minutes after start)
docker compose exec pg-backup pgbackrest info
```

Then open `https://console.example.com`, create your developer account (the verification email must arrive through your SMTP provider), create an organization and a project, and follow the portal's integration guide for a first application.

## 6. Operate

| Task | How |
| ---- | --- |
| Dashboards and traces | `ssh -L 3001:127.0.0.1:3001 -L 9090:127.0.0.1:9090 -L 9093:127.0.0.1:9093 you@server`, then http://localhost:3001 (Grafana, `admin` / `GRAFANA_ADMIN_PASSWORD`) |
| Keycloak admin console (rarely needed; every change is reverted by reconciliation unless made through Dovepeak) | `ssh -L 8081:127.0.0.1:8081 you@server`, then http://localhost:8081/admin |
| Monthly restore drill | `scripts/ops/restore-drill.sh`, record the result in [restore-drills.md](operations/restore-drills.md) |
| Signing key rotation (every 90 days) | [signing-key-rotation.md](runbooks/signing-key-rotation.md) |
| Upgrade Dovepeak | `git fetch && git checkout <new tag>`, read the release notes, then `docker compose up -d --build --wait` (a full backup first: `docker compose exec pg-backup pgbackrest --type=full backup`) |
| Upgrade Keycloak | [keycloak-upgrade.md](runbooks/keycloak-upgrade.md) |
| Logs | `docker compose logs -f <service>` (JSON; no tokens, passwords or email addresses) |

Incidents: [database failure](runbooks/database-failure.md), [email provider outage](runbooks/email-provider-outage.md), [signing key compromise](runbooks/signing-key-compromise.md).

## 7. Limits of this deployment

* **One server.** Two Keycloak nodes protect against a crashed Keycloak process, not against losing the server. Recovery from server loss is a restore on a new server (RTO 1 hour) and needs an off-site backup repository.
* **Capacity.** Measure sign-in capacity on your hardware with `tests/load/run.sh` before launch ([load-testing.md](operations/load-testing.md)).
* Read [known limitations](security/known-limitations.md) and [supported flows](supported-flows.md) before onboarding tenants.

## Rehearsing without a public domain

To try the production stack on a workstation, set `IDENTITY_HOST=identity.dovepeak.test` (and similar), `CADDY_LOCAL_CERTS=local_certs` (Caddy's own certificate authority) and point `SMTP_HOST` at a local capture server. Browsers will not trust the certificates; tests in this repository load Caddy's root certificate instead (`tests/e2e/ha-failover.sh`, `tests/load/run.sh`).
