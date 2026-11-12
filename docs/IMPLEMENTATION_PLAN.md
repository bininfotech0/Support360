# Support360 implementation plan (spec gap analysis)

This plan maps [docs/PRODUCT_SPEC.md](PRODUCT_SPEC.md) against the repository as verified on 2026-11-11 (see [IMPLEMENTATION_STATUS.md](../IMPLEMENTATION_STATUS.md) for evidence). Two decisions are already settled: the monitoring agent stays on .NET (C#) (spec §4.6/§8 amended accordingly), and existing modules are mapped and extended rather than rebuilt from scratch.

## 1. Current state summary (verified)

- **Backend**: .NET 10 modular monolith (`Api`, `Application`, `Contracts`, `Domain`, `Infrastructure`, `Workers`), EF Core 10 with PostgreSQL (SQLite for local dev), SignalR live updates, rate limiting, tenant query filters and write guard, migrations at startup.
- **Frontend**: React 19 + TypeScript + Vite SPA with REST client, SignalR reconnect, 45 interaction tests; pages for overview, agents, applications, alerts, incidents, service requests, Work DNA, changes, jobs, deployments, audit, usage.
- **Agent**: .NET 10 (C#) monitoring agent with enrollment, heartbeat/telemetry, HTTP/process/service checks, signed deployment execution (default off); 32 unit tests plus live Compose verification.
- **Auth**: local dev auth + configurable OIDC claim mapping; one role per user (`Viewer`, `Operator`, `Approver`, `TenantAdmin`, plus `PlatformAdmin` claim); authorization policies `TenantUser`/`Operator`/`TenantAdmin`/`Approver`.
- **Existing domains**: agents (enrollment/credentials/heartbeat/metrics/checks), alert rules and alerts, batch jobs, unified service-desk tickets (incidents, service requests, problems, and changes with approvals), deployment package catalog and requests with RSA-PSS manifests, Work DNA, audit and usage events.
- **Workers**: alert evaluation, agent liveness, Work DNA schedule materialization.
- **Tests/CI**: 55 unit, 32 agent, 28 integration, and 52 web tests pass; GitHub Actions backend/frontend/agent jobs; Compose stack running.

## 2. Spec-to-reality gap analysis

| Spec section | Current state | Gap | Effort |
| --- | --- | --- | --- |
| §4.1 Dashboard | Single overview page (agent health, applications, alerts, SignalR) | Role-based dashboards (manager/technician/admin/management), configurable widgets | Medium |
| §4.2 Service Desk | Unified `Ticket` model (Incident/ServiceRequest/Problem/ChangeRequest) with per-type sequential numbering, impact x urgency priority matrix, validated transitions with reopen, activity history, alert/batch-execution/related-ticket links, change approvals, public/internal comments, worklogs, watchers, attachments, due dates, and ticket-backed incident/service-request/change UI | Category/subcategory hierarchy, requester portal, configurable transitions | Medium |
| §4.3 SLA Management | Not implemented (only Work DNA calendars/leaves exist) | SLA policies, tracking with persisted clock history, business calendars, escalation rules, warning/breach notifications | Large |
| §4.4 Asset Management | Not implemented | Asset entities, assignment/transfer, history, maintenance, warranty, agent-based discovery, linking to tickets | Large |
| §4.5 Infrastructure Monitoring | Largely implemented: agent metrics (CPU/memory/disk/network/uptime), application checks (HTTP/process/Windows service/systemd), alert rules with thresholds and consecutive breaches, alert lifecycle Active→Acknowledged→Resolved with severity, batch-job tracking | Maintenance windows, SSL certificate expiry checks, middleware checks (IIS/Nginx/Apache/Tomcat) beyond current service/process checks, alert dedup tuning, alert-to-ticket automation | Medium |
| §4.6 Monitoring Agent | Implemented in .NET (C#); live-verified; monitoring and deployment permissions already separated behind opt-in, grant, and server gate | Spec wording already amended; middleware inventory depth can grow later | Small |
| §4.7 Automation | Not implemented (alert rule evaluation is the closest seed) | Trigger/condition/action rule engine, execution logs, retries, enable/disable, scheduled tasks (extends batch-job concepts) | Large |
| §4.8 Knowledge Base | Not implemented | Articles, categories, tags, versions, visibility, search, feedback | Medium |
| §4.9 Integrations | Not implemented | Connector framework, SMTP/M365 email, email-to-ticket, Teams, webhooks, API keys, connection health | Large |
| §4.10 AI Assistant | Not implemented | Provider abstraction (Groq first), RAG over knowledge base, ticket classification/summarization, similar tickets, usage and prompt tracking | Large |
| §5 Communication Center | SignalR UI updates only | Notification delivery pipeline, templates, retries, delivery status | Medium |
| §6 Administration | Users with a single role; audit page; no org hierarchy | Organizations/locations/departments/teams/support groups, invitations, user preferences, category/status/numbering configuration, notification templates, custom fields, MFA/SSO configuration, session management | Large |
| §7 Reports | Overview metrics and usage page only | Operational report set, filters, CSV/PDF export | Medium |
| §8 Technical architecture | Aligned (React/TS/Vite, .NET 10, EF Core, PostgreSQL, SignalR, Compose) | MediatR, FluentValidation, Redis, MinIO/S3, Traefik, Serilog, OpenTelemetry are not present. Spec marks most as optional ("where useful/justified"); current hand-rolled validation follows existing conventions. Adopt only when a module needs them | Small (deliberate) |
| §9 Core data model | 23 entities across monitoring, service desk, deployment, Work DNA, audit | Most spec tables absent (tickets domain, SLA, assets, automation, knowledge, integrations, AI, administration). Proposed tables align cleanly with existing conventions (UTC timestamps, tenant columns, GUID keys) | - |
| §10 Security baseline | Mostly present: tenant isolation, RBAC with resource-level checks, rate limiting, agent revocation, restricted deployments, audit, ZIP validation | MFA, webhook signature verification (no webhooks yet), file scanning, encrypted integration credentials, backup/restore drills | Medium |
| §11 UI/UX | Existing pages have tested loading/empty/error states; light theme only | Dark theme, new screens (ticket list/detail/create, My Work, catalogue, asset screens, KB, integrations, reports, admin), permission-denied states | Large |
| §3 Navigation | Flat nav: overview/agents/applications/alerts/incidents/service requests/Work DNA/changes/jobs/deployments/audit/usage | Restructure to spec's 9-item primary nav with submenus; hide technical pages from ordinary users | Small |

## 3. Architecture decisions for the ticket unification

The single largest structural change is evolving the three separate service-desk models into the unified ticket model without breaking working flows.

**Recommended path (map and extend):**

1. Introduce the spec §9 Service Desk tables (`Tickets`, `TicketComments`, `TicketAttachments`, `TicketAssignments`, `TicketActivities`, `TicketWorklogs`, `TicketCategories`, `TicketStatusHistory`, `TicketLinks`, `TicketWatchers`) with `TicketType` (Incident, ServiceRequest, Problem, ChangeRequest).
2. Data migration converts each existing `Incident`, `ServiceRequest`, and `ChangeRequest` row into a typed `Ticket`, preserving numbers, timestamps, assignment, and descriptions; approval data moves onto the ChangeRequest-type tickets; `IncidentAlert` and change↔incident links become `TicketLinks`.
3. `DeploymentRequests` follow their change request to the new ticket key (update the FK binding to the ChangeRequest-type ticket). Deployment approval logic keeps operating on approval records attached to that ticket type.
4. Legacy controllers/endpoints are retired in the same change rather than kept as parallel APIs; integration tests migrate with them.

**Alternatives rejected:** keeping three entities behind a facade (permanent split-model complexity, no single ticket list/numbering), or a rewrite without data migration (violates map-and-extend).

Other decisions:

- **Organization hierarchy**: keep `Tenant` as the organization boundary (matches spec's single-organization start); add `Location`, `Department`, `Team`, `SupportGroup` beneath it; ticket assignment targets become user or support group.
- **Roles**: keep the current single-role enum with policy checks; add new permissions only when a module needs finer control (e.g., requester-portal self-service scope).
- **Priority matrix**: configurable `Impact` x `Urgency` -> `Priority` table in administration, applied at ticket create/update; existing incident severity maps to priority on migration.
- **Numbering**: per-tenant sequences (`INC-000001`, `SR-000001`, `CHG-000001`, `PRB-000001`) replacing the current free-form `Number` strings, configurable in administration.
- **Attachments**: MVP stores metadata and bytes together in PostgreSQL (10 MiB per file, 20 per ticket, SHA-256 recorded), matching the deployment package BLOB approach; an S3-compatible store is a future migration and the BLOB debt is tracked in IMPLEMENTATION_STATUS.
- **Work DNA**: retained as an existing module outside the spec's navigation core; it stays available to operators/admins and can later feed capacity reporting.

## 4. Phased implementation plan

Sequencing follows the spec roadmap. The monitoring/deployment strengths already cover much of Phases 2–3; the service desk (Phase 1) is the critical gap and goes first.

### Phase 1 – Essential MVP (service desk core)

| # | Workstream | Includes | Key risks |
| --- | --- | --- | --- |
| 1.1 | Unified ticket domain | §9 tables, ticket CRUD API with validation/authorization, priority matrix, numbering rules, status transitions with reopen, migration of existing incidents/service requests/change requests, deployment binding update | Data migration correctness; deployment/change approval regression |
| 1.2 | Ticket collaboration | Comments (internal vs public), attachments (PostgreSQL BLOB MVP; S3-compatible storage planned), worklogs, activity/history, related-ticket links, watchers | Storage wiring; file validation |
| 1.3 | Org structure | Locations, departments, teams, support groups; assignment to groups; user membership | Touches authorization queries |
| 1.4 | SLA management | Policies (first response/resolution by priority), business calendars + exceptions (reuse Work DNA calendar concepts), SLA tracking with persisted clock history, pause/resume, warning/escalation worker, breach reporting | Calendar math correctness; clock persistence |
| 1.5 | Email notifications | SMTP connector, notification templates, delivery records with retry, ticket reply threading groundwork | Credential storage; deliverability |
| 1.6 | Knowledge base | Articles, categories, tags, draft/published, version history, visibility, search | Search scope and permissions |
| 1.7 | Requester portal | Self-service ticket create/view/reply scoped to requester; feedback after resolution | Authorization isolation |
| 1.8 | Dashboards and reports | Role-based dashboard widgets (open tickets, critical incidents, SLA compliance, workload), ticket trend reports, CSV export | Query performance |
| 1.9 | Navigation restructure | Spec §3 primary nav and submenus; hide technical pages from ordinary users; My Work view | UI test updates |

Definition of Done per spec §13 applies to each workstream (migrations, API, authorization, frontend, error handling, audit, tests, logging, docs, deployment config, verified end-to-end).

### Phase 2 – Professional IT management

- **2.1 Asset management**: entities, assignment/transfer, maintenance/warranty tracking, history, linking to tickets; agent-based discovery matching hostnames to server assets.
- **2.2 Monitoring depth**: maintenance windows, SSL certificate expiry checks, middleware health (IIS/Nginx/Apache/Tomcat via existing service/process checks first), alert deduplication rules.
- **2.3 Monitoring-to-ticket automation**: critical alert → incident ticket creation (manual approve first).
- **2.4 Automation engine**: trigger/condition/action rules with execution logs, retries, enable/disable; scheduled tasks building on batch-job tracking.
- **2.5 Integrations**: connector framework (credentials, connection testing, health, execution logs), Microsoft 365 email-to-ticket, Teams notifications, webhooks with signature verification, API keys.
- **2.6 Advanced reports**: technician workload, team performance, asset inventory, availability, CSV/PDF export.

### Phase 3 – AI and deployment maturity

- **3.1 AI assistant**: provider-independent interface with Groq first, tenant configuration, ticket classification/priority suggestion/summarization, similar-ticket search, knowledge RAG, token/cost tracking, prompt versioning, feedback; no autonomous execution of deployments or transactions.
- **3.2 Deployment maturity**: health verification after installs, failure-path acceptance tests (tampered artifact, expired/replayed manifest, rollback), key rotation/revocation procedures, artifact storage migration off PostgreSQL BLOBs to S3-compatible storage, live end-to-end execution verification before enabling `Deployment:ExecutionEnabled` outside tests.

### Phase 4 – Enterprise extensions

- Advanced CMDB (extends assets with relationships), SAP/ERP connectors, advanced approval workflows, Git/CI/CD integrations, SSO/SCIM beyond current OIDC mapping, multi-location governance, on-premises packaging, advanced audit/compliance evidence.

## 5. Deferred-by-design items

- MediatR, FluentValidation, Redis, Traefik, Serilog, OpenTelemetry: adopt only when a workstream justifies them (spec §8 allows this; §14 forbids unnecessary dependencies).
- Kubernetes, microservices, Kafka, visual workflow designer: not in scope until demonstrated requirement.
- Full network discovery / observability, omnichannel inbox: explicitly out of first releases per the spec.
