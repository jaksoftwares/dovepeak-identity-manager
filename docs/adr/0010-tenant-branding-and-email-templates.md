# ADR-0010: Tenant Branding and Email Templates

**Status:** Proposed
**Date:** 2026-10-10
**Deciders:** Dovepeak Identity maintainers

## Context

ADR-0006 shipped a CSS-only login theme and deferred per-tenant logos, colours and email templates to Phase 4, with Keycloakify as the option to re-evaluate. Tenants (projects) need their own logo and colour on hosted pages and in emails, their own wording for verification and password-reset emails, and users need security alert emails. Tenant-controlled content on pages and in emails sent from the platform is also a phishing and injection risk.

## Decision

1. **Values live in Keycloak realm localization texts.** The Management API stores a project's branding (`PUT …/projects/{id}/branding`) and writes it to each environment realm's localization overrides. Keycloak merges these per realm into both login and email message bundles, so no per-tenant theme or extension is needed. Reconciliation reverts any manual change, and new environments receive the branding when they are provisioned.
2. **Hosted login: one forked template, two marked blocks.** `template.ftl` is copied from Keycloak 26.4.0 with two `DOVEPEAK` blocks: a `<style>` setting the brand colour variables, and the tenant logo. Keycloakify is not adopted: it adds a React build and a JAR pipeline for what amounts to a logo and a colour. On a Keycloak upgrade, the new upstream template is copied and the two blocks re-applied; the integration tests detect a missed step.
3. **Emails: a `dovepeak` email theme.** A branded layout wraps every email, including security alerts. The verification and password-reset templates render the tenant's subject and introduction as escaped text; buttons, links, expiry notices and the "Secured by Dovepeak Identity" footer are always added by the platform. Tenants cannot change links or inject markup, and users can always recognise platform email.
4. **Validation in depth.** The API only accepts HTTPS logo URLs without credentials or quotes, `#rrggbb` colours, single-line subjects (preventing header injection) and texts without MessageFormat braces. The templates check colours and URLs again before using them, and HTML-escape every value.
5. **Readable colours without rejecting brands.** Button text is white or Dovepeak navy, whichever contrasts more with the brand colour (WCAG relative luminance), so any colour stays readable.
6. **Security alerts.** The realm template enables Keycloak's `email` event listener, which sends alerts for credential changes (password, OTP, passkeys) in the tenant's branding. Keycloak's lockout alert templates are styled too, but Keycloak 26.4 does not emit the lockout event in this configuration, so lockout alerts are not promised. `LOGIN_ERROR` is excluded, because otherwise an attacker could flood a user's inbox by guessing passwords. Keycloak 26's legacy duplicates (`UPDATE_PASSWORD`, `UPDATE_TOTP`, `REMOVE_TOTP`) are also excluded, so each change produces exactly one alert.

## Consequences

- **Positive:** per-tenant branding with no Keycloak extension or build step; changes apply immediately to every environment; drift is reverted.
- **Positive:** tenant content cannot alter links or markup in platform emails.
- **Negative:** the forked `template.ftl` must be re-applied on Keycloak upgrades (documented in its header).
- **Negative:** logos are hot-linked from the tenant's HTTPS URL; an unavailable host shows no logo. Hosting uploaded logos is a later option.
- **Negative:** branding is per project, not per application or environment.
