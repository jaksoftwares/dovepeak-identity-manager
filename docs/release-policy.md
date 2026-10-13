# Versioning and Release Policy

How Dovepeak Identity is versioned, released and patched (milestone M6.6).

## What is versioned

| Artifact | Version scheme | Published as |
| -------- | -------------- | ------------ |
| Platform (Management API, workers, portal, mail relay, deployment files, realm template) | Semantic Versioning, git tag `vMAJOR.MINOR.PATCH` | Source release on GitHub; images are built from the tag (`deploy/production`) |
| TypeScript SDK `@dovepeak/identity` | Semantic Versioning, managed by Changesets | npm |
| .NET SDK `Dovepeak.Identity` | Semantic Versioning, git tag `dotnet-sdk-vMAJOR.MINOR.PATCH` | NuGet |
| Management API | URL version (`/v1/…`) | Part of the platform |

The SDKs are versioned independently from the platform. Each SDK release states the oldest platform version it supports.

## Compatibility promises

From 1.0 onwards:

* **Patch releases** (1.0.x): bug and security fixes only. No configuration changes are required to upgrade.
* **Minor releases** (1.x.0): new features, new optional settings, new API endpoints and fields. Existing integrations keep working. Deprecations are announced in the release notes and kept for at least two minor releases or six months, whichever is longer.
* **Major releases** (x.0.0): may remove deprecated features. A migration guide is published with the release.

Never changed within a major version:

* `/v1` Management API paths, request and response fields (new fields may be added; clients must ignore unknown fields), problem `code` values and API key format.
* Token issuer URLs, claim names (`sub`, `azp`, `aud`, `scope`, `roles`) and signing algorithm (RS256).
* Realm names of existing environments.
* SDK public APIs (removals only in an SDK major version).

The identity engine (Keycloak) is an implementation detail: it is upgraded in minor and patch releases ([keycloak-upgrade.md](runbooks/keycloak-upgrade.md)), and Keycloak concepts never appear in the public API or SDKs.

## Supported versions

| Version | Status |
| ------- | ------ |
| Latest minor release of the current major version | Bug fixes and security fixes |
| Previous minor release | Security fixes for 3 months after the next minor release |
| Older | Unsupported: upgrade |

Before 1.0, only the latest commit on `main` is supported.

## Release process

1. All CI checks pass on `main`: build, unit tests, integration and end-to-end suites, CodeQL, Trivy, secret scanning.
2. Release notes are written in `docs/releases/vX.Y.Z.md`: changes, upgrade steps, security fixes (with advisory links), known issues.
3. A maintainer tags the commit (`git tag -s vX.Y.Z`) and publishes a GitHub release with the notes.
4. SDKs: merging the Changesets "Version packages" pull request publishes npm packages; pushing `dotnet-sdk-vX.Y.Z` publishes NuGet packages (`.github/workflows/release-sdks.yml`).
5. Changes to authentication, authorization or credential handling need two reviewers before release (implementation plan §12).

## Security patch process

For a reported or discovered vulnerability (see [SECURITY.md](../SECURITY.md) for reporting):

1. **Triage** within the times in `SECURITY.md`: confirm, assign a CVSS score, decide affected versions.
2. **Fix privately** in a GitHub security advisory's temporary private fork. Add a regression test.
3. **Request a CVE** through the GitHub advisory when the issue affects released versions.
4. **Release patch versions** for every supported version at the same time, with release notes that state the security fix without exploit details.
5. **Publish the advisory** once patched versions are available. Notify self-hosting operators through the advisory and the release announcement; the reporter is credited unless they decline.
6. **Mitigations**: when a fix needs time, publish a configuration workaround in the advisory first if one exists.

Critical fixes ship within 14 days of confirmation and high within 30 days; the release is not delayed for unrelated changes.
