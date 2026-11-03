# Support360 threat model (initial)

This is an initial design review of the current repository, not a security certification. Package catalog and staged deployment request APIs exist; deployment execution remains disabled and is not implemented.

## Assets and trust boundaries

- Tenant records, users, agent identities and credentials, telemetry, audit events, and future deployment approvals are sensitive assets.
- Browser-to-API calls cross an untrusted network and must use OIDC in deployed environments with TLS.
- Agent-to-API calls cross an untrusted network; each agent key is independently issued, stored as a hash by the server, and revocable.
- PostgreSQL is trusted infrastructure but contains data for multiple tenants; tenant filters and service authorization must both be applied.
- Development local authentication, demo seed credentials, and SQLite are for local use only.

## Threats and current controls

| Threat | Current control | Remaining work |
| --- | --- | --- |
| Malicious tenant reads another tenant's data | Tenant claim resolution, EF query filters, write guard, integration coverage for agent list scoping | Add cross-tenant tests for every resource and administrative route; consider PostgreSQL RLS as another layer. |
| Stolen agent credential | Per-agent key hash, authentication handler checks credential state, revoke endpoint, integration test confirms revoked key fails; agent stops after 401/403 | Add rotation/expiry policy, validate OS-specific identity-file ACLs, compromise runbook, and rate-limit/credential-abuse monitoring. |
| Enrollment token replay or theft | Short-lived hashed enrollment token and single-use enrollment path; replay integration test | Add explicit enrollment attempt audit and production abuse monitoring. |
| Malicious or duplicate telemetry | Contract validation, allowed metric catalog, event ID uniqueness/deduplication | Add payload-size/resource quotas, fuzzing, ingestion load limits, and durable queue/backpressure. |
| Database credential exposure | Compose requires values via local `.env`, which is ignored by Git; PostgreSQL has no host port | Use an external secrets manager for deployed environments; rotate credentials and verify backup encryption. |
| Local token signing key misuse | Development-only local auth guard blocks non-development unless explicitly overridden | Use only OIDC outside Development; remove permissive overrides from deployed configuration. |
| OIDC claim spoofing or role misconfiguration | JWT signature, issuer, audience, and lifetime are validated; tenant and identity claims are configurable, IdP role aliases map to a fixed Support360 role set, and OIDC tokens without a mapped role are rejected by default. | Test against the chosen external identity provider, verify claim names cannot be set by unprivileged users, and monitor role-mapping changes. |
| Deployment replay, tampering, or privilege escalation | Deployment execution defaults off; package catalog is tenant-scoped and digest-addressed, ZIP inspection is bounded, and staged requests bind a package digest and active tenant agent to a change request. Future dispatch is gated by both an administrator grant on enrollment and a separate local agent opt-in; both default off. | Requests are not dispatched and agent execution does not exist. Before enabling execution, require signed manifest verification, target/tenant/digest binding, expiry, one-time consumption, approval enforcement, allowlisted actions, and audit. |
| Agent queue tampering or disk exhaustion | Queue capped at 5,000 events, written atomically with restrictive POSIX mode, overflow drops oldest events, restart reload is tested | Add queue integrity protection and verify file ACLs on Windows; overflow is currently drop-oldest and should be surfaced to the API. |
| HTTP check target abuse (SSRF or internal scanning) | Operators and tenant admins can create checks; the API requires absolute HTTP(S) URLs without embedded credentials, and the agent revalidates targets, issues GET only, disables redirects and environment proxies, ignores response bodies, and applies connect/header timeouts. Internal and private targets remain intentionally reachable because monitoring internal applications is a core use case. | Add per-tenant target allowlists or a network egress policy if checks are exposed to less-trusted tenants; review cloud metadata endpoints, IPv6 special ranges, and DNS rebinding. |
| Package substitution or unsafe archive extraction | Upload computes SHA-256, accepts only bounded ZIPs, rejects traversal/duplicate paths and symlinks, and does not extract files | Trusted package signing, approved artifact source policy, agent-side digest validation, and safe extraction are not implemented; preserve execution gate until tested. |

## Security invariants

1. Never trust a tenant identifier from an untrusted browser or agent without resolving ownership from authenticated identity.
2. Never log passwords, bearer tokens, API keys, signing keys, or private keys.
3. Keep deployment disabled until approval, signature, replay, and allowlisted execution tests pass.
4. Use UTC for stored timestamps; convert only for presentation.
5. Treat all agent payloads and package metadata as untrusted input.

## Review gaps

No formal penetration test, PostgreSQL RLS policy, external identity-provider integration test, or deployment security test has been run, and HTTP checks have no per-tenant target allowlist or egress control. The current automated tests provide useful foundation evidence but do not establish production readiness.
