# Support360

Support360 is an early-stage multi-tenant IT operations platform. The repository includes a .NET 10 API and worker library, PostgreSQL persistence, a React dashboard, and a .NET (C#) monitoring agent for host telemetry and configured application checks. Windows service and Linux systemd installer packages are included but have not had native target-OS installation tests. Controlled deployment execution is implemented behind a default-off gate with signed, target-bound manifests; live agent execution remains to be verified. See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) for verified scope and gaps.

## Local development

Prerequisites: Docker Compose, .NET 10 SDK, and Node.js 24 or newer. The API applies EF Core migrations at startup. Local Compose runs PostgreSQL, the API, and Vite; local authentication and demo seeding are enabled only in the development configuration.

1. Copy `.env.example` to `.env` and replace the development-only signing key and demo password.
2. Start PostgreSQL, API, and web with `docker compose up --build -d`.
3. Open `http://localhost:5173`. The seeded local account is configured with `Seed__AdminEmail` and `Seed__AdminPassword` in `.env`.

The development API uses SQLite when run outside Compose through `appsettings.Development.json`. Compose explicitly selects PostgreSQL. Never reuse local keys/passwords in another environment. Production must configure an OIDC authority and audience, PostgreSQL credentials through a secret manager, HTTPS at the reverse proxy, and `Seed__Enabled=false`; the API now refuses to start when seeding is enabled outside Development unless `Seed__AllowInProduction=true` is set deliberately. Deployment execution also stays off until a managed `Deployment:ManifestSigningPrivateKey`/`ManifestSigningPublicKey` pair, agent pinned public keys, deployment roots, and live failure-path tests are in place. OIDC claim names are configurable (`TenantClaim`, `OidcRoleClaims`, `OidcNameClaim`, `OidcEmailClaim`, `OidcSubjectClaim`), and IdP-specific role values map to `Viewer`, `Operator`, `Approver`, `TenantAdmin`, or `PlatformAdmin` through `OidcRoleMappings`; tokens without a mapped role are rejected while `OidcRequireRoleClaim` is true.

The **Work DNA** page captures the DNA of each activity (project, category, target mode, effort, severity, `Ignore OFF`, and business impact), plans daily/weekly/monthly recurring work, and materializes dated activities. It also shows monthly capacity using `(working days - holidays - leave days) x 8h`, a Work Pulse pendency view, calendar OFF/holiday overrides, and leave capture. A background worker materializes active schedules for every tenant.

Tenant admins can configure batch-job keys and expected schedules under **Batch jobs** for active agents. Agents submit explicit execution events; the UI records their actual status and history. Schedule text is descriptive and does not control an external scheduler or trigger a rerun.

The **Incidents**, **Service requests**, and **Changes** pages share one tenant-scoped ticket model (`/api/tickets`) with type-specific numbering (`INC-`, `SRV-`, `CHG-`), an impact/urgency priority matrix, validated lifecycle transitions, activity history, public/internal comments, worklogs, watchers, due dates, attachments, and alert, batch-execution, and related-ticket links. Change tickets use the tenant approval policy with requester/approver separation and synchronize linked deployment requests.

The **Deployments** page supports a tenant-scoped package catalog and deployment requests. Tenant admins can upload ZIPs up to 20 MiB; the API validates archive paths and size limits and records a SHA-256 digest. Operators bind a request to an active same-tenant agent, target path, package, and linked change ticket; request approval follows the change ticket status. When execution is enabled with managed signing keys, the agent fetches an expiring RSA-PSS manifest, verifies the pinned signature and bindings, downloads and digest-checks the artifact, extracts it under its deployment root, and reports the result. Execution is off by default and live agent installation is not yet verified. Package bytes are stored in PostgreSQL for this local development MVP; use a durable artifact store before production.

## Local Docker agent

The API and web containers are available at `http://localhost:8080` and `http://localhost:5173`. To enroll a Compose agent, create a Linux enrollment in the UI, then use the short-lived token:

```powershell
docker compose --profile agent run --rm --entrypoint support360-agent agent enroll --server http://api:8080 --identity /var/lib/support360/identity.json --allow-http --token '<single-use-token>'
docker compose --profile agent up -d agent
```

The `--allow-http` flag is only for this local development network. The agent defaults to HTTPS elsewhere. The local Compose UI uses a demo seeded account; its generated password is in the ignored `.env` file.

## Verification

```powershell
dotnet test Support360.slnx
Push-Location web
npm ci
npm run build
Pop-Location
```

The PostgreSQL-specific integration test runs when `SUPPORT360_TEST_POSTGRES_CONNECTION` is set. Operational steps are in [docs/RUNBOOKS.md](docs/RUNBOOKS.md). See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md), [docs/PRODUCT_SPEC.md](docs/PRODUCT_SPEC.md), [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md), and [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).
