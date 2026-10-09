# Security Policy

Dovepeak Identity is authentication infrastructure. We treat every security report as a priority.

## Reporting a Vulnerability

**Do not open a public issue, pull request or discussion for security vulnerabilities.**

Report vulnerabilities privately using one of these channels:

1. **GitHub private vulnerability reporting** — use the "Report a vulnerability" button on the repository's Security tab.
2. **Email** — security@dovepeak.example *(placeholder: replace with the monitored security address before the repository is made public)*.

Please include:

* A description of the vulnerability and its impact.
* Steps to reproduce, or a proof of concept.
* The affected version, commit or deployment configuration.
* Any suggested mitigation.

## Our Commitment

| Stage                          | Target                                   |
| ------------------------------ | ---------------------------------------- |
| Acknowledgement of report      | Within 3 business days                   |
| Initial assessment and severity | Within 7 business days                  |
| Fix for critical severity      | Within 14 days of confirmation           |
| Fix for high severity          | Within 30 days of confirmation           |
| Public disclosure              | After a fix is released, coordinated with the reporter |

We will credit reporters in the release notes unless they prefer to remain anonymous.

## Scope

In scope:

* The Management API, workers and developer portal in this repository.
* Keycloak realm templates, themes and extensions in `/identity`.
* The official SDKs in `/sdks`.
* Deployment configuration in `/deploy`.

Out of scope:

* Vulnerabilities in upstream projects (for example Keycloak itself). Report these to the upstream project. If the issue affects Dovepeak Identity's configuration of the upstream project, it is in scope.
* Findings that require a compromised host or administrator account.
* Denial of service through volumetric traffic.
* Missing security headers without a demonstrated impact.

## Supported Versions

Until version 1.0 is released, only the latest commit on `main` receives security fixes.

## Safe Harbour

We will not pursue legal action against researchers who act in good faith, avoid privacy violations and service disruption, do not access or modify other users' data, and give us reasonable time to respond before public disclosure.
