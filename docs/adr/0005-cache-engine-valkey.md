# ADR-0005: Cache and Transient State Engine

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The problem statement (section 6) proposes Redis for rate-limit state, caching and short-lived coordination.

Redis changed its licence in 2024 from BSD-3-Clause to a dual RSALv2/SSPLv1 licence, and Redis 8 added AGPLv3 as a third option. These licences place conditions on offering the software as a service, which conflicts with the project's goal of a permissively licensed, self-hostable platform that others may operate commercially (problem statement section 13).

## Options Considered

1. **Redis 7.2 (last BSD release)** — permissive, but no longer receives upstream updates under that licence.
2. **Redis 8 (AGPLv3)** — current, but copyleft obligations for network use.
3. **Valkey** — Linux Foundation fork of Redis 7.2, BSD-3-Clause, protocol-compatible, actively maintained.

## Decision

Use **Valkey** for cache and transient state.

Application code uses standard Redis clients (StackExchange.Redis for .NET), so the engine remains swappable for any Redis-protocol-compatible server, including Redis itself for operators who prefer it.

## Consequences

* Licence remains permissive throughout the default stack.
* Documentation refers to "Valkey (or any Redis-compatible server)".
* As stated in the problem statement (section 11), Valkey is never the only store for data that must survive a restart.

## Validation

* M0.3: local stack runs on Valkey with the standard Redis health check.
* M2.4: distributed rate limiting works against Valkey.
