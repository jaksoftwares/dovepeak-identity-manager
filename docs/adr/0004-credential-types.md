# ADR-0004: Credential Types and Handling

**Status:** Proposed
**Date:** 2026-10-09
**Deciders:** Dovepeak Identity maintainers

## Context

The problem statement (sections 4.7, 8.4 and 17) requires separating public client identifiers, confidential OAuth client secrets, user tokens and developer API keys, and never revealing secrets after creation.

## Decision

The following credential types are distinct and never interchangeable:

| Credential                 | Purpose                                         | Holder                | Storage at rest                            | Visibility                     |
| -------------------------- | ----------------------------------------------- | --------------------- | ------------------------------------------ | ------------------------------ |
| Public client identifier   | Identifies a public client in OAuth flows      | SPA, mobile app       | Plain                                      | Public                         |
| Confidential client secret | Authenticates a backend client to the token endpoint | Backend application | Managed by the identity engine          | Shown once at creation         |
| Developer API key          | Authenticates automation to the Management API | Developer tooling, CI | HMAC-SHA-256 digest with a server-side key | Shown once at creation         |
| Access / refresh / ID token | Represents an end-user session or authorization | End-user client     | Managed by the identity engine             | Never displayed in the portal  |

### Developer API key format

```text
dpk_<environment>_<random>
```

* `environment` is `live` or `test`.
* `random` is at least 256 bits from a cryptographically secure generator, base62-encoded.
* The prefix allows secret scanners (including GitHub secret scanning partner patterns) to detect leaked keys.

### Lifecycle rules

* Secrets are displayed exactly once. No endpoint returns them afterwards.
* Every credential supports expiration, rotation with an overlap window, and immediate revocation.
  *Implementation (Phase 3):* client secrets rotate with a 24-hour overlap through Keycloak's `secret-rotation` client policy
  (preview feature `client-secret-rotation`, limitation L-13); the previous secret can be revoked immediately.
  API keys are revoked immediately and replaced by issuing a new key while the old one is still valid.
* API keys carry scopes and quotas limiting what they can do.
* Credentials, tokens and secrets are never written to logs or error messages.
* Public client configuration endpoints are tested to confirm they contain no secrets.

## Consequences

* The Management API needs a key-derivation secret for API key digests, managed as a deployment secret with rotation support.
* Leaked keys can be detected automatically by prefix.

## Validation

* M3.6: secrets cannot be retrieved after creation; revocation is immediate.
* M3.5: public configuration endpoint contains no secrets.
* M2.5: log scanning confirms no secrets are logged.
