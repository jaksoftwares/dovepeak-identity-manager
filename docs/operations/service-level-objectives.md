# Service-Level Objectives

Dovepeak Identity publishes these objectives so integrators know what to expect, and so operators know when to act (milestone M6.1, problem statement §16). They are starting targets from the implementation plan; they will be revised once production traffic gives a baseline.

An outage of the identity service locks users out of every application that depends on it, so authentication availability and latency are measured where users experience them: at the public edge proxy.

| Objective | Target | Window | Measured by (SLI) |
| --------- | ------ | ------ | ----------------- |
| **Authentication availability** | 99.9% of requests succeed | 30 days | Requests through the public edge that are not answered with a 5xx. Rate-limited requests (429) and client errors (4xx) count as successes: the service worked as designed. |
| **Sign-in latency** | 95% of credential requests complete in under 300 ms | 30 days | Duration at the edge of `POST` requests to the sign-in form and the token, introspection, revocation and logout endpoints (router `credentials`). |
| **Email delivery** | 99% of accepted emails are handed to the provider | 30 days | Mail relay outcomes: `sent ÷ (sent + failed)`. Emails dropped by quota are excluded (dropping them is the intended behaviour). |
| **Recovery point (RPO)** | At most 15 minutes of committed data lost | Per incident | WAL archiving every 60 seconds or 16 MB; verified by the monthly restore drill. |
| **Recovery time (RTO)** | Service restored within 1 hour | Per incident | Time from the decision to restore to a passing smoke test; verified by the monthly restore drill. |

The 99.9% availability target allows about **43 minutes** of failed requests per 30 days. Planned maintenance counts against it; Keycloak upgrades are rolling (two nodes) for that reason.

## Error budgets and alerts

Availability and latency alerts use multi-window burn rates (Google SRE workbook, chapter 5), so a short spike does not page and a slow leak is still caught:

| Alert | Fires when | Severity | Budget consumed if it continues |
| ----- | ---------- | -------- | ------------------------------- |
| `AvailabilityFastBurn` | Error ratio > 14.4 × budget over both 1 h and 5 min | critical | 2% of the monthly budget per hour |
| `AvailabilitySlowBurn` | Error ratio > 6 × budget over both 6 h and 30 min | warning | 5% of the monthly budget per 6 hours |
| `SignInLatencyFastBurn` / `SignInLatencySlowBurn` | Same burn rates applied to requests slower than 300 ms | critical / warning | as above |
| `EmailDeliveryFailing` | More than 5% of emails failed over 10 minutes | critical | — |

The other alerts (authentication anomalies, drift, background jobs, components down) are listed in [observability.md](observability.md#alerts).

## What is not covered

* Resource servers that validate tokens locally keep working while Dovepeak is down, until their access tokens expire. That is a property of JWTs, not an SLO.
* The developer portal and Management API are configuration surfaces, not the sign-in path. They are monitored (latency, errors, `ServiceDown`) but have no published objective yet.
* Delivery by the email provider to the user's inbox (spam filtering, bounces) depends on the provider and on SPF, DKIM and DMARC; the SLO covers the hand-off.

## Reviewing the objectives

The objectives are reviewed every quarter and after any incident that consumed more than half an error budget. A change is recorded in this file with the date and reason.

| Date | Change |
| ---- | ------ |
| 2026-10-10 | First published targets (implementation plan, Phase 6). |
