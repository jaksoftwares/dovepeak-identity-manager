# Observability

How Dovepeak Identity is monitored: what each component emits, where it goes, which alerts exist and how to investigate them (milestone M6.1). Objectives and error budgets are in [service-level-objectives.md](service-level-objectives.md).

## Architecture

```text
 portal ──────────┐  OTLP/HTTP (traces)
 management-api ──┤
 workers ─────────┤  OTLP/gRPC (traces, metrics)      ┌─▶ Tempo (traces, 7 days)
 mail-relay ──────┤                                   │
 keycloak ────────┴──▶ OpenTelemetry Collector ───────┤
                        (redacts query strings,       └─▶ :8889 ◀── Prometheus (metrics, 30 days)
                         auth headers, cookies)                        │   ▲ scrapes Keycloak :9000/metrics,
                                                                      │   │ edge :8083, readiness probes (blackbox)
                                                                      ▼
                                                   Alertmanager ◀── alert rules      Grafana (dashboards, Explore)
```

* **Traces** follow one request across the portal, the Management API, PostgreSQL and Keycloak (W3C trace context). Background work has its own root spans: `reconcile environment`, `outbox <type>` and `relay email`.
* **Metrics**: ASP.NET Core, HttpClient, .NET runtime and Npgsql metrics from the platform services; Keycloak's own metrics including per-realm user events; edge request counts and latency per router; readiness probes for every component; and the platform's own counters:

| Metric | Labels | Meaning |
| ------ | ------ | ------- |
| `dovepeak_email_messages_total` | `outcome` (`sent`, `failed`, `throttled`), `reason` and `realm` when throttled | Emails handled by the mail relay |
| `dovepeak_reconciliation_drift_corrections_total` | `kind` | Direct changes to the identity engine that reconciliation reverted |
| `dovepeak_reconciliation_runs_total` | `outcome` | Reconciliation passes per environment |
| `dovepeak_outbox_messages_total` | `type`, `outcome` (`succeeded`, `retried`, `failed`) | Provisioning work processed by the workers |
| `dovepeak_webhook_deliveries_total` | `outcome` (`delivered`, `retried`, `failed`) | Webhook delivery attempts |
| `dovepeak_audit_events_collected_total` | `source` | Identity-engine events copied into the audit store |

### What is never recorded

Telemetry must not become a second place where secrets leak (threat model I-03):

* HttpClient spans record URLs without query strings (Keycloak action tokens and authorization codes travel there); the collector strips query strings, `Authorization` and `Cookie` attributes again as a second line of defence.
* The mail relay never records email addresses: quotas and logs use a truncated SHA-256 of realm and address.
* Health and readiness probes are not traced.

This is verified when the stack changes: the drill on 2026-10-10 scanned 40 Keycloak sign-in and token traces and found no `code`, `session_code`, `key`, `token`, `state` or `nonce` parameter.

## Running it

Locally the stack is an opt-in compose profile, because it adds about 600 MB of memory:

```bash
# .env: OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
#       OTEL_EXPORTER_OTLP_HTTP_ENDPOINT=http://otel-collector:4318
#       KC_TRACING_ENABLED=true
docker compose --profile observability up -d
```

| UI | URL | |
| -- | --- | - |
| Grafana | http://localhost:3001 | `admin` / `GRAFANA_ADMIN_PASSWORD`. Dashboard *Dovepeak Identity — Overview*; traces under Explore → Tempo |
| Prometheus | http://localhost:9090 | Alerts, rules and targets |
| Alertmanager | http://localhost:9093 | Active and silenced alerts |

The production compose file (`deploy/production`) always runs the stack; see the [self-hosting guide](../self-hosting.md). Configuration lives in [`deploy/observability`](../../deploy/observability).

### Sending alerts somewhere

`deploy/observability/alertmanager.yml` ships with a placeholder receiver, so alerts are only visible in the UI. Before going live, add a real receiver, for example:

```yaml
receivers:
  - name: default
    email_configs:
      - to: oncall@example.com
        from: alerts@example.com
        smarthost: smtp.example.com:587
        auth_username: alerts@example.com
        auth_password_file: /etc/alertmanager/smtp-password
```

Use a notification path that does not depend on Dovepeak Identity itself (for example, not a chat tool whose sign-in goes through it).

## Alerts

Rules are in [`deploy/observability/prometheus/rules`](../../deploy/observability/prometheus/rules).

| Alert | Severity | What it means | First steps |
| ----- | -------- | ------------- | ----------- |
| `AvailabilityFastBurn` / `AvailabilitySlowBurn` | critical / warning | Public requests are failing with 5xx | [Investigating](#investigating); check `ServiceDown`; [database failure](../runbooks/database-failure.md) |
| `SignInLatencyFastBurn` / `SignInLatencySlowBurn` | critical / warning | Sign-in is slower than 300 ms for too many requests | Keycloak CPU and database latency; slowest traces in Tempo |
| `SignInFailureSpike` | warning | > 30 failed sign-ins per minute in a realm and more than half of attempts fail | [Authentication anomalies](#authentication-anomalies) |
| `AccountLockoutSpike` | warning | > 20 sign-ins hit locked accounts in 10 minutes | [Authentication anomalies](#authentication-anomalies) |
| `EdgeRateLimitingSpike` | warning | > 60 requests per minute rejected with 429 | Confirm it is abuse, not a legitimate integration behind one IP |
| `RegistrationSpike` | warning | > 500 registrations in a realm in an hour | Possible automated sign-ups; contact the tenant |
| `EmailDeliveryFailing` | critical | > 5% of emails failed to reach the provider | [Email provider outage](../runbooks/email-provider-outage.md) |
| `EmailQuotaThrottling` | warning | > 10 emails dropped by quota in 15 minutes | Inbox flooding attempt, or a tenant that needs a higher quota |
| `ConfigurationDriftCorrected` | warning | Someone changed Keycloak directly and reconciliation reverted it | Audit log `drift.corrected` and Keycloak admin events: who had admin access? |
| `ReconciliationFailing` | warning | Reconciliation cannot reach Keycloak | Keycloak health; workers logs |
| `OutboxMessagesFailed` | warning | Provisioning gave up after all retries | Failed outbox messages; fix the cause, then retry |
| `WebhookDeliveriesFailing` | info | Many webhook deliveries failed permanently | Usually a customer endpoint; visible to the tenant |
| `ServiceDown` | critical | A component's readiness probe failed for 2 minutes | `docker compose ps`, the component's logs |
| `KeycloakNodeDown` | critical | A Keycloak node stopped answering | The cluster has lost redundancy; restart or replace the node |
| `ManagementApiErrors` | warning | > 5% of Management API requests fail | API logs and traces with errors |

Counters appear in Prometheus only after their first increment. Alerts on rare events (drift, failed outbox messages) therefore also fire when a series first appears, and the mail relay publishes its `sent` and `failed` counters at zero on start-up so the first failure is never missed.

## Investigating

1. **Dashboard**: *Dovepeak Identity — Overview* shows readiness of every component, the three SLOs, sign-in activity, email, drift and background jobs.
2. **Traces**: Grafana → Explore → Tempo. Useful TraceQL queries:
   * Slow sign-ins: `{ resource.service.name = "keycloak" && span.http.route =~ ".*login-actions.*" && duration > 300ms }`
   * Failing Management API calls: `{ resource.service.name = "dovepeak-management-api" && status = error }`
   * A reconciliation pass: `{ name = "reconcile environment" && span.dovepeak.realm = "<realm>" }`
   * Email handling: `{ name = "relay email" && status = error }`
3. **Logs**: every service logs JSON to stdout (`docker compose logs <service>`). Logs never contain tokens, passwords or email addresses.
4. **Audit log**: the Management API's audit endpoints and the portal's audit page show who did what in a tenant, including `drift.corrected`.

### Authentication anomalies

`SignInFailureSpike` and `AccountLockoutSpike` usually mean credential stuffing against one realm. The controls already in place are per-IP rate limits at the edge and temporary lockout after repeated failures (Phase 2). To respond:

1. Identify the realm and tenant from the alert's `realm` label (Management API: environment by realm name).
2. Look at the failed sign-in events in the audit log: concentrated source addresses can be blocked at the edge or upstream firewall.
3. If many accounts are affected, tell the tenant; suggest enabling MFA for their users.
4. Lower the edge credential limits temporarily (`EDGE_RATELIMIT_CREDENTIALS_PER_MINUTE`) if the attack is distributed but slow.

## Drills

The M6.1 completion criterion is that a simulated incident triggers the correct alert and is traceable end to end. The drift drill does exactly that and can be repeated at any time:

```bash
dotnet run --project tools/Dovepeak.Identity.DevTool -- drill-drift --realm <tenant realm> --client <client id>
```

It plants a redirect URI on a tenant's client directly in Keycloak (as an attacker with admin access would), then waits for reconciliation to revert it and for `ConfigurationDriftCorrected` to fire.

| Date | Drill | Result |
| ---- | ----- | ------ |
| 2026-10-10 | Drift on `app_vteqcbydfq2u5hhjgb3nuzuw` (realm `dp-01a124b7…`), workers freshly restarted | **Passed.** Reverted after 26 s; alert firing after 1 min 16 s and delivered to Alertmanager. Trace `11ad019a…`: `dovepeak-workers / reconcile environment` → `GET`/`PUT` → `keycloak / PUT /admin/realms/{realm}/clients/{client-uuid}` (361 spans, workers and Keycloak). A first run found that the alert missed the first correction of a new kind; fixed before this run. |
| 2026-10-10 | End-to-end request trace | **Passed.** Portal `POST …/environments/[environmentId]` → `dovepeak-management-api POST …/applications` → PostgreSQL and `keycloak POST /admin/realms/{realm}/clients` → Keycloak database, in one trace. |
