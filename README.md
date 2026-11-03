# Support360

Support360 is an early-stage multi-tenant IT operations platform. The repository includes a .NET 10 API and worker library, PostgreSQL persistence, a React dashboard, and a Go monitoring agent for host telemetry and configured application checks. Windows service and Linux systemd installer packages are included but have not had native target-OS installation tests. Controlled deployment execution is not implemented. See [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md) for verified scope and gaps.

## Local development

Prerequisites: Docker Compose, .NET 10 SDK, and Node.js 24 or newer. The API applies EF Core migrations at startup. Local Compose runs PostgreSQL, the API, and Vite; local authentication and demo seeding are enabled only in the development configuration.

1. Copy `.env.example` to `.env` and replace the development-only signing key and demo password.
2. Start PostgreSQL, API, and web with `docker compose up --build -d`.
3. Open `http://localhost:5173`. The seeded local account is configured with `Seed__AdminEmail` and `Seed__AdminPassword` in `.env`.

The development API uses SQLite when run outside Compose through `appsettings.Development.json`. Compose explicitly selects PostgreSQL. Never reuse local keys/passwords in another environment. Production must configure an OIDC authority and audience, PostgreSQL credentials through a secret manager, HTTPS at the reverse proxy, and `Seed__Enabled=false`. OIDC claim names are configurable (`TenantClaim`, `OidcRoleClaims`, `OidcNameClaim`, `OidcEmailClaim`, `OidcSubjectClaim`), and IdP-specific role values map to `Viewer`, `Operator`, `Approver`, `TenantAdmin`, or `PlatformAdmin` through `OidcRoleMappings`; tokens without a mapped role are rejected while `OidcRequireRoleClaim` is true.

Tenant admins can configure batch-job keys and expected schedules under **Batch jobs** for active agents. Agents submit explicit execution events; the UI records their actual status and history. Schedule text is descriptive and does not control an external scheduler or trigger a rerun.

The **Deployments** page supports a tenant-scoped package catalog and staged deployment requests. Tenant admins can upload ZIPs up to 20 MiB; the API validates archive paths and size limits and records a SHA-256 digest. Operators can bind a request to an active same-tenant agent and a linked change request; request approval follows the change approval status. Requests do not dispatch work, extract files, or execute a deployment. Package bytes are stored in PostgreSQL for this local development MVP; use a durable artifact store before production.

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

The PostgreSQL-specific integration test runs when `SUPPORT360_TEST_POSTGRES_CONNECTION` is set. Operational steps are in [docs/RUNBOOKS.md](docs/RUNBOOKS.md). See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md), and [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).
