---
"@dovepeak/identity": patch
---

`createTokenVerifier` now fetches signing keys through the `fetch` option as well as discovery. Previously the JWKS
was always fetched with the global `fetch`, bypassing proxies or TLS settings supplied through the option.
