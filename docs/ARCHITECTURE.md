# Support360 architecture

## Current implementation

The cloud application is a modular monolith. `Support360.Api` exposes user and agent HTTP endpoints, OpenAPI, health checks, and a SignalR dashboard hub. Application abstractions separate tenant context, persistence, security services, and realtime notifications. Infrastructure implements those services with EF Core; Workers hosts periodic retention and alert processing in the same deployable API process today. PostgreSQL is the configured default provider; SQLite is reserved for local development and tests.

The React/Vite application consumes the API and subscribes to dashboard updates through SignalR. The Go monitoring agent enrolls with a single-use token, stores its individual credential, collects CPU, memory, system disk, network totals, and uptime, and posts authenticated heartbeat/telemetry. It buffers telemetry and application-check results in separate bounded local queues and retries transient failures. Configured checks include HTTP/HTTPS, process existence, Windows service state, and Linux systemd service state; only HTTP 2xx and active service state are treated as healthy. Windows SCM and Linux systemd install scripts are included but have not been exercised on native supported operating systems. Deployment execution is configured off. A tenant-scoped package catalog accepts bounded ZIP artifacts, rejects unsafe archive paths/links, and records a computed SHA-256 digest; artifacts are stored as PostgreSQL BLOBs in this local MVP. Operators can stage a request against an active tenant agent, an artifact digest, and a linked change request; approval state follows that change record. No signed manifest, agent dispatch, package download/extraction, execution, or verification workflow exists yet.

Incident records are tenant-scoped and can link alert records or batch-job executions. Their lifecycle is validated by the API. Change requests capture risk, target environment, and a planned window; tenant policy sets the approval threshold and requester/approver separation. Decisions are unique per approver and use optimistic concurrency; approvals and lifecycle changes are audited. These APIs manage approvals only: they do not execute deployments or other changes.

Batch-job inventory supports both explicit agent-reported discovery and tenant-admin configuration against an active agent. Admin-configured scheduler and expected-schedule metadata is kept when future execution events arrive. Agents report outcomes with correlation IDs; these events are idempotent and include duration, exit code, failure reason, and timestamps. Schedule text is informational: Support360 does not trigger jobs, evaluate missed runs, or provide vendor scheduler connectors yet.

## Tenant boundary

Interactive requests receive tenant, role, name, email, and subject claims from configurable OIDC mappings or a local development token. IdP role values can be aliased to Support360 roles, and OIDC tokens without a mapped role are rejected by default. Tenant resolution populates the scoped tenant context; EF query filters constrain tenant-owned rows, and `SaveChangesAsync` rejects writes whose tenant does not match the request context. Agent API keys are individually hashed and map requests to a specific agent and tenant. API authorization and service-level ownership checks remain required; query filters are defense in depth, not a substitute for them. PostgreSQL row-level security is not configured.

## Data and operations

EF migrations own relational schema changes. Telemetry is currently stored as indexed `MetricSamples` records with a configured retention worker; high-volume production workloads may require a measured partitioning or external time-series store decision. PostgreSQL is not exposed on a host port in Compose. The local stack uses a persistent named volume and is intended for development, not production deployment.

## Intended process boundaries

The API currently hosts business APIs, agent ingestion, SignalR, and workers in one process to keep early deployment simple. If ingestion or background processing needs independent scaling, it can be split behind the existing application interfaces and a durable queue. No cloud durable queue is implemented today. The Go agent uses outbound HTTPS by default, a bounded disk-backed queue, idempotent event IDs, and platform-specific system disk selection. Deployment execution must be a separately enabled capability with signed, expiring, target-bound manifests and a strict allowlisted executor.
