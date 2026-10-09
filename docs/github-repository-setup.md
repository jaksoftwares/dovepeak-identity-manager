# GitHub Repository Setup

These settings cannot be stored in the repository and must be applied by a repository administrator after the repository is created on GitHub (milestone M0.1 / M0.2).

## 1. General

* **Default branch:** `main`.
* **Merge options:** allow squash merging only. Enable "Automatically delete head branches".

## 2. Branch Protection for `main`

Settings → Rules → Rulesets → New branch ruleset, targeting `main`:

* Restrict deletions.
* Block force pushes.
* Require a pull request before merging:
  * Required approvals: **1**.
  * Dismiss stale approvals when new commits are pushed.
  * Require review from Code Owners (enforces two-reviewer rule for security-sensitive paths through `CODEOWNERS`).
  * Require conversation resolution before merging.
* Require status checks to pass, with branches up to date:
  * `.NET build and test`
  * `Local stack configuration`
  * `Management API image`
  * `Secret scanning`
  * `Dependency and configuration scanning`
  * `CodeQL (csharp)`
  * `CodeQL (actions)`
* Require signed commits (recommended).

## 3. Security Settings

Settings → Code security:

* Enable **Private vulnerability reporting**.
* Enable **Dependabot alerts** and **Dependabot security updates**.
* Enable **Secret scanning** and **Push protection**.
* Enable **Code scanning** (results arrive from the CodeQL workflow).

## 4. Teams

Create the teams referenced in `.github/CODEOWNERS`, or update that file to match your organization:

* `@dovepeak/maintainers`
* `@dovepeak/security`

## 5. Secrets

* `GITLEAKS_LICENSE` — required by the Gitleaks GitHub Action when the repository belongs to an organization. Free licences are available from gitleaks.io.

## 6. Milestones

Create one GitHub milestone per entry in [implementation-plan.md](../implementation-plan.md), named by identifier, for example `M0.1 — Repository and Governance`.

## 7. Verification (M0.2 "done when")

After setup, open a test pull request that adds a file containing a realistic-looking fake secret (for example an AWS-style key pattern). Confirm that the **Secret scanning** check fails and merging is blocked. Close the pull request without merging.



remaining git setup:
Phase 0 items you need to finish on GitHub (I left them unticked in implementation-plan.md):

Apply branch protection, the security settings and the code-owner teams by following docs/github-repository-setup.md.
Create the GitHub milestones.
Push, then open a test PR containing a fake secret and confirm the Secret scanning check blocks it. That's the "done when" check for M0.2. The CI workflows haven't run on GitHub yet, so that PR will also be their first real test.
Replace the placeholder contact emails in SECURITY.md and CODE_OF_CONDUCT.md.
After that, Phase 1 begins with M1.1: creating Keycloak realms from .NET code, which is also where we answer open question E-01 in the threat model.



