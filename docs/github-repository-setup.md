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
