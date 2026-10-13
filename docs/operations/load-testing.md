# Load Testing

How sign-in capacity is measured, and the results so far (milestone M6.4). The sign-in latency objective is p95 under 300 ms ([service-level-objectives.md](service-level-objectives.md)).

## Method

[`tests/load/signin.js`](../../tests/load/signin.js) (k6) creates a throwaway realm with 50 users and a public client, then each virtual user repeats a complete browser sign-in — authorization request, password form, code exchange with PKCE — followed by a token refresh and one second of think time. Requests go through the full production path: Caddy (TLS), the edge and both Keycloak nodes. Every step has its own p95 threshold of 300 ms; any failed request fails the run.

```bash
# Against deploy/production (lifts the per-IP edge limits for the run and restores them afterwards)
VUS=10 DURATION=2m tests/load/run.sh
```

The load comes from one address, so the per-IP rate limits would otherwise cap it at 30 sign-ins a minute. They are tested separately (`EdgeProtectionTests`); this test measures what the identity engine can serve.

## Results

Rehearsal host: one laptop (Windows 11, Docker Desktop VM with 7.5 GB of memory) running the whole production stack, both Keycloak nodes sharing its CPU. Production numbers will be higher on dedicated hardware and must be measured there before launch.

| Date | Load | Sign-ins/s | Password step p95 | Code exchange p95 | Refresh p95 | Errors | Objective |
| ---- | ---- | ---------- | ----------------- | ----------------- | ----------- | ------ | --------- |
| 2026-10-10 | 10 VUs, 2 min | 7.3 | 122 ms | 39 ms | 35 ms | 0 of 5,171 requests | **Met** |
| 2026-10-10 | 40 VUs, 2 min | 25.7 | 378 ms | 123 ms | 96 ms | 0 of 17,763 requests | Missed on the password step |

**Reading the results.** Even at 40 concurrent users nothing failed; latency grew instead. The password step is dominated by password hashing, which is deliberately expensive and CPU-bound, so capacity scales with CPU cores across the Keycloak nodes. On the rehearsal host the objective holds up to roughly 20 sign-ins per second.

## When to run

* Before launch, on the production hardware, to set the capacity baseline and the `SignInLatency` alert expectations.
* After every Keycloak upgrade and every change to password hashing or the realm template.
* Before a tenant expected to bring large traffic is onboarded.

Record each run in the table above.
