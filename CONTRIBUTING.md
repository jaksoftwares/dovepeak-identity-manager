# Contributing to Dovepeak Identity

Thank you for your interest in contributing. This guide explains how to set up the project, how changes are reviewed and what we expect from every contribution.

## Before You Start

* **Security issues** must be reported privately. See [SECURITY.md](SECURITY.md).
* For significant changes, open an issue first to discuss the approach.
* Architectural changes require an Architecture Decision Record (ADR) in [docs/adr](docs/adr).
* All participants must follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Development Setup

See the "Getting Started" section of the [README](README.md).

## Branching Strategy

* `main` is always releasable and protected.
* Create short-lived branches from `main`:

| Prefix       | Use                                  |
| ------------ | ------------------------------------ |
| `feat/`      | New functionality                    |
| `fix/`       | Bug fixes                            |
| `docs/`      | Documentation only                   |
| `chore/`     | Tooling, dependencies, CI            |
| `security/`  | Security hardening (non-embargoed)   |

* Merge to `main` through pull requests only, using squash merge.

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):

```text
feat(management-api): add project environment provisioning
fix(portal): hide client secret after initial display
docs(adr): record token revocation semantics
```

## Pull Request Requirements

Every pull request must:

* Pass all CI checks (build, tests, CodeQL, dependency, container and secret scanning).
* Include tests for new behaviour.
* Include tenant-isolation tests for any new Management API endpoint.
* Update documentation affected by the change.
* Never include secrets, credentials or real personal data, including in tests and fixtures.

### Review rules

* At least **one** approving review is required.
* Changes to authentication, authorization, credential handling, token handling, cryptography configuration or tenant isolation require **two** approving reviews, including one from a maintainer listed in `CODEOWNERS`.

## Definition of Done

A change is complete only when it includes:

* Unit and integration tests.
* Tenant-isolation tests for new endpoints.
* Updated documentation.
* Audit events where the change affects security-relevant actions.
* Confirmation that no secrets appear in logs or error messages.
* Threat model notes for security-relevant changes.

## Coding Standards

* **.NET:** follow `.editorconfig`. Nullable reference types are enabled and warnings are treated as errors.
* **TypeScript:** strict mode, ESLint and Prettier.
* Do not implement cryptographic primitives, password hashing or token-signing protocols. Use established libraries and the identity engine.

## Licence

By contributing, you agree that your contributions are licensed under the [Apache License 2.0](LICENSE).
