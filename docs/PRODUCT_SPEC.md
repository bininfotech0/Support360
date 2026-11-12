# SUPPORT360

## Product Redesign – Mid-Market IT Service & Operations Platform

> Note: §4.6 and §8 specify a .NET (C#) monitoring agent, matching the implemented agent in this repository.

**Product positioning:** Simple, modular and affordable IT management platform for small and medium-sized businesses and growing enterprises.

**Product objective:** Manage IT support, assets, infrastructure monitoring, routine operations and controlled deployments through one unified platform.

---

# 1. Product strategy

Support360 should not attempt to compete with every advanced capability of ServiceNow in its first release.

It should deliver five outcomes exceptionally well:

1. Manage IT support requests efficiently.
2. Track IT assets and ownership.
3. Monitor servers and critical applications.
4. Automate repetitive IT operations.
5. Give managers visibility into IT performance.

### Target customers

- Small and medium-sized businesses
- Manufacturing companies
- IT service providers and MSPs
- Corporate IT departments
- Organizations with 5–200 IT users initially
- Growing organizations with multiple locations

### Product principles

- Easy installation and configuration
- Minimal training requirements
- Simple, consistent user experience
- Modular subscriptions
- Secure by default
- Integration-first architecture
- Practical AI, not unnecessary AI features
- No unnecessary enterprise complexity

# 2. Product editions

## Support360 Essential

For small IT teams.

- Ticket and incident management
- Service requests
- User and team management
- IT asset inventory
- Basic SLA
- Email notifications
- Knowledge base
- Basic reports
- Audit history

## Support360 Professional

For growing organizations.

Everything in Essential, plus:

- Server and application monitoring
- Windows/Linux monitoring agent
- Automated ticket assignment
- SLA escalation automation
- Microsoft Teams and Outlook integration
- Advanced asset management
- Application and service health dashboard
- Controlled software deployment
- AI ticket assistant
- Custom dashboards and reports
- API integrations

## Support360 Enterprise

For organizations with advanced requirements.

Everything in Professional, plus optional modules:

- Advanced CMDB
- Multiple organizations and locations
- Advanced approval workflows
- SAP and ERP integration
- Advanced deployment governance
- SSO and directory synchronization
- Advanced security policies
- Private and on-premises deployment
- Advanced audit and compliance evidence
- Custom connector development

Enterprise should be an extension of the same product, not a separate codebase.

# 3. Redesigned application navigation

Keep the primary navigation short and understandable.

1. Dashboard
2. Service Desk
3. Assets
4. Monitoring
5. Automation
6. Knowledge
7. Integrations
8. Reports
9. Administration

### Global application header

- Global search
- Create new ticket
- Notifications
- Help
- User profile
- Organization selector, when applicable

### Service Desk submenu

- My Work
- All Tickets
- Incidents
- Service Requests
- Problems
- Approvals
- SLA Overview

### Assets submenu

- All Assets
- Computers
- Servers
- Applications
- Software
- Asset Assignments
- Asset History

### Monitoring submenu

- Overview
- Servers
- Applications
- Services
- Alerts
- Monitoring Agent

### Automation submenu

- Automation Rules
- Scheduled Tasks
- Execution History

Avoid exposing technical configuration screens to ordinary users.

# 4. Core modules

## 4.1 Dashboard

Create role-based dashboards.

**IT Manager**
- Open tickets
- Critical incidents
- SLA compliance
- Team workload
- Asset overview
- Infrastructure health

**Technician**
- My assigned tickets
- Priority incidents
- Pending approvals
- SLA approaching breach
- Recent activity

**Administrator**
- User and license count
- Agent health
- Integration health
- System usage
- Failed background jobs

**Management**
- Ticket trends
- IT service performance
- Availability
- Department-wise requests
- Monthly operational summary

Keep dashboards configurable without building a complex dashboard designer initially.

## 4.2 Service Desk

This is the primary product module.

### Ticket types
- Incident
- Service Request
- Problem
- Change Request — initially a basic controlled process

### Ticket functionality
- Unique ticket number
- Subject and description
- Requester
- Assigned technician
- Support group
- Category and subcategory
- Priority and impact
- Status
- Due date
- Attachments
- Internal notes
- Public replies
- Worklogs
- Ticket history
- Related tickets
- Email conversation history

### Ticket lifecycle

New → Open → In Progress → Pending → Resolved → Closed

Allow controlled reopening and configurable status transitions.

### Priority matrix

Calculate priority using impact and urgency.

- Critical
- High
- Medium
- Low

Allow administrators to configure the matrix.

### Technician features

- Assign and reassign
- Transfer between teams
- Add internal notes
- Reply to requester
- Attach documents
- Record work time
- Link related tickets
- Resolve and close
- Escalate tickets

### Requester portal

Users can:
- Create tickets
- View their tickets
- Reply to support
- Upload attachments
- Search knowledge articles
- View ticket progress
- Give feedback after resolution

## 4.3 SLA Management

Start with straightforward SLA configuration.

- First response target
- Resolution target
- Priority-based policies
- Business hours
- Holidays
- Pause and resume
- Warning notifications
- Escalation rules
- Breach reporting

SLA calculations must use business calendars and persist clock history. Do not rely only on frontend timers.

## 4.4 Asset Management

Maintain a central inventory of IT assets.

### Asset categories
- Desktop
- Laptop
- Server
- Virtual machine
- Network device
- Printer
- Mobile device
- Software
- Application

### Asset details
- Asset ID
- Name
- Serial number
- Manufacturer
- Model
- Operating system
- IP address
- Hostname
- Location
- Department
- Assigned employee
- Purchase date
- Warranty expiry
- Status
- Related tickets

### Asset operations
- Register asset
- Assign and transfer
- Update information
- Track maintenance
- Track warranty
- View history
- Link assets to incidents

Begin with manual inventory and agent-based discovery. Advanced automatic discovery can come later.

## 4.5 Infrastructure Monitoring

Make this a key differentiator of Support360 Professional.

### Server monitoring

Windows and Linux:

- CPU utilization
- Memory utilization
- Disk utilization
- Network utilization
- Uptime
- Operating system
- Running services
- Agent status

### Application monitoring

- HTTP/HTTPS availability
- Response time
- Error status
- SSL certificate expiry
- Configured application process health
- Application version

### Middleware monitoring

Initially support:
- IIS
- Nginx
- Apache
- Tomcat

Start with health checks, service status and basic operational metrics. Add advanced middleware administration later.

### Monitoring alert lifecycle

Normal → Warning → Critical → Acknowledged → Resolved

Include:
- Configurable thresholds
- Alert deduplication
- Maintenance windows
- Notification rules
- Ticket creation from critical alerts
- Alert history

Do not promise full network discovery or complete observability in the first release.

## 4.6 Monitoring Agent

Build a lightweight cross-platform agent using .NET (C#).

### Agent responsibilities

- Secure enrollment
- Server inventory
- Health metrics
- Configured service monitoring
- Application health checks
- Heartbeat
- Policy synchronization
- Monitoring configuration updates
- Secure deployment tasks, when licensed and enabled

### Security requirements

- Tenant-bound identity
- Revocable credentials
- Outbound TLS
- Signed deployment manifests
- Artifact checksum verification
- Least-privilege permissions
- Work-item audit
- No unrestricted remote shell execution

Keep monitoring permissions separate from deployment permissions.

## 4.7 Automation

Do not begin with a large visual workflow engine.

Start with a simple rule-based automation system.

### Trigger examples
- Ticket created
- Ticket priority changed
- SLA warning
- SLA breached
- Alert received
- Asset warranty approaching expiry
- Scheduled time reached

### Conditions
- Priority
- Category
- Department
- Support group
- Status
- Location
- Asset type

### Actions
- Assign technician
- Change priority
- Add internal note
- Send notification
- Escalate ticket
- Create related ticket
- Create approved incident from monitoring alert

Include execution logs, retry handling and an enable/disable switch.

Advanced workflow designer can be added in Enterprise later.

## 4.8 Knowledge Base

- Articles
- Categories
- Tags
- Search
- Draft and published status
- Article version history
- Internal and public visibility
- Related ticket references
- Article feedback

Add AI-based article suggestions using approved knowledge sources.

## 4.9 Integrations

Build a reusable connector framework.

### First-release integrations

**Email**
- SMTP
- Microsoft 365 mailbox
- Email-to-ticket
- Ticket reply by email
- Notification delivery

**Microsoft Teams**
- Incident notifications
- Ticket updates
- Approval notifications
- Message links to tickets

**Identity**
- Microsoft Entra ID
- Active Directory integration where deployment permits

**REST API**
- API keys or OAuth
- Webhooks
- Ticket creation
- Ticket status updates
- Asset lookup

### Later integrations

- Google Workspace
- OneDrive
- SharePoint
- SAP
- GitHub
- GitLab
- Jenkins
- SQL Server
- PostgreSQL
- External HRMS and ERP systems

Every connector must support secure credential storage, connection testing, permissions, health status and execution logs.

## 4.10 AI Assistant

Keep AI practical and controlled.

### Initial AI features

- Ticket classification
- Priority recommendation
- Ticket summarization
- Suggested responses
- Similar ticket search
- Knowledge article recommendation
- Incident troubleshooting suggestions
- Natural-language knowledge search

### AI architecture

- Provider-independent AI interface
- Groq API as an initial provider
- Configurable model selection
- Retrieval-augmented generation for internal knowledge
- Tenant-specific configuration
- Token and cost tracking
- Prompt versioning
- Feedback and evaluation

AI must not independently execute production deployments, privileged database operations or business transactions.

Require explicit authorization for consequential actions.

# 5. Communication Center

Keep communication simple initially.

- Email notifications
- Ticket email threads
- Internal ticket comments
- Microsoft Teams notifications
- User notifications
- Delivery status
- Retry failed notifications

Keep internal technician discussions separate from messages visible to requesters.

A full unified omnichannel inbox can be introduced later if customers demonstrate demand.

# 6. Administration

Create one centralized administration area.

### Organization
- Organization profile
- Locations
- Departments
- Teams
- Support groups

### Users
- Users
- Invitations
- Roles
- Permissions
- User preferences
- Account status

### Configuration
- Ticket categories
- Priority settings
- Status settings
- Numbering rules
- SLA policies
- Business calendars
- Notification templates
- Custom fields

### Security
- Password policy
- MFA
- SSO configuration
- API credentials
- Session management
- Audit logs

### Platform
- Modules and entitlements
- Subscription
- Usage
- Storage
- System health

Use a simple organization hierarchy initially:

Organization → Department → Team → User

Allow multiple locations and support groups without forcing a deep corporate hierarchy.

# 7. Reports and Analytics

Include useful operational reports.

- Tickets created vs resolved
- Open tickets by priority
- Ticket aging
- SLA compliance
- Technician workload
- Team performance
- Incident trends
- Asset inventory
- Warranty expiry
- Server availability
- Monitoring alert trends
- Automation execution results

Support:

- Date filters
- Team filters
- Department filters
- CSV export
- PDF export for selected reports

# 8. Technical architecture

### Frontend
- React
- TypeScript
- Vite
- Reusable component library
- Responsive layout
- Accessible forms and tables

### Backend
- ASP.NET Core .NET 10
- C#
- Clean Architecture
- Modular monolith
- REST APIs
- MediatR where useful
- FluentValidation
- EF Core 10

### Data and infrastructure
- PostgreSQL
- Redis where justified
- SignalR
- MinIO or S3-compatible storage
- .NET Worker Services
- Docker
- Docker Compose
- Traefik
- Serilog
- OpenTelemetry

### Monitoring agent
- .NET (C#)
- Windows Service
- Linux systemd service
- Outbound secure communication

### AI
- Groq API initially
- Provider abstraction
- RAG knowledge retrieval

Do not introduce microservices, Kubernetes, Kafka or an elaborate workflow engine without a demonstrated requirement.

# 9. Core data model

Design the database around these domains.

### Identity and organization
- Tenants
- Organizations
- Locations
- Departments
- Teams
- Users
- Roles
- Permissions
- UserRoles

### Service Desk
- Tickets
- TicketComments
- TicketAttachments
- TicketAssignments
- TicketActivities
- TicketWorklogs
- TicketCategories
- TicketStatusHistory
- TicketLinks
- TicketWatchers

### SLA
- SLAPolicies
- SLATracking
- BusinessCalendars
- CalendarExceptions
- SLAEscalationRules

### Assets
- Assets
- AssetTypes
- AssetAssignments
- AssetHistory
- AssetMaintenance
- AssetWarranty

### Monitoring
- Agents
- AgentEnrollments
- AgentCredentials
- MonitoredResources
- MonitoringChecks
- MonitoringResults
- Alerts
- AlertRules
- MaintenanceWindows

### Automation
- AutomationRules
- AutomationConditions
- AutomationActions
- AutomationExecutions
- AutomationExecutionLogs

### Knowledge
- KnowledgeArticles
- KnowledgeCategories
- KnowledgeTags
- KnowledgeArticleVersions
- KnowledgeFeedback

### Integrations
- IntegrationConnections
- IntegrationCredentials
- IntegrationEvents
- WebhookSubscriptions
- IntegrationExecutionLogs

### AI
- AIProviderConfigurations
- AIPromptVersions
- AIUsageRecords
- AIFeedback
- AIEvaluationRuns

### Administration
- OrganizationSettings
- UserPreferences
- NotificationTemplates
- Notifications
- NotificationDeliveries
- AuditEvents
- SubscriptionPlans
- FeatureEntitlements
- UsageRecords

Use proper relational constraints, indexes, tenant isolation and UTC timestamps. Store attachments in object storage, not as large database blobs. Add partitioning and retention for high-volume monitoring data when required.

This is a proposed model, not a claim that these tables already exist.

# 10. Security baseline

Security is a product requirement, not a future feature.

- Tenant isolation
- Role-based access control
- Resource-level authorization
- Secure password handling
- MFA and SSO options
- Encrypted credentials
- HTTPS everywhere
- API rate limiting
- Webhook signature verification
- File upload validation and scanning
- Audit history
- Backup and restore testing
- Agent revocation
- Restricted deployment operations
- No secrets in application logs

# 11. UI/UX design direction

### Visual style

Professional, clean and modern enterprise SaaS.

- Light theme by default
- Optional dark theme
- Consistent spacing
- Neutral backgrounds
- Clear typography
- Limited accent colors
- Compact but readable tables
- Simple charts
- Consistent forms and dialogs

### Main screens to build

1. Login
2. Main dashboard
3. Ticket list
4. Ticket details
5. Create ticket
6. My Work
7. Service catalogue
8. Asset inventory
9. Asset details
10. Monitoring overview
11. Server details
12. Application health
13. Alert details
14. Automation rules
15. Knowledge articles
16. Integrations
17. Reports
18. User management
19. Organization settings
20. SLA configuration

Every screen must include loading, empty, error and permission-denied states.

# 12. Development roadmap

## Phase 1 – Essential MVP

Build a complete usable service desk:

- Authentication
- Organization and user management
- Roles and permissions
- Ticket management
- Ticket comments and attachments
- Team assignment
- SLA management
- Email notifications
- Knowledge base
- Basic dashboard
- Audit history

**Release outcome:** A working helpdesk product that can be deployed for a small organization.

## Phase 2 – Professional IT Management

- Asset management
- Windows/Linux agent
- Server monitoring
- Application monitoring
- Alert management
- Monitoring-to-ticket automation
- Microsoft 365 email integration
- Teams notifications
- Advanced reports
- Rule-based automation

**Release outcome:** A unified IT service and basic infrastructure operations platform.

## Phase 3 – AI and Deployment

- AI ticket assistant
- Knowledge RAG
- Similar ticket search
- Controlled IIS/Nginx/Tomcat deployments
- Deployment approval
- Deployment history
- Health verification
- API and webhook integrations

**Release outcome:** A more efficient IT operations platform.

## Phase 4 – Enterprise Extensions

- Advanced CMDB
- SAP and ERP connectors
- Advanced approval workflows
- Git and CI/CD integrations
- SSO and SCIM
- Advanced organization hierarchy
- Private/on-premises deployment options
- Advanced governance and reporting

**Release outcome:** An extensible enterprise product without compromising the simplicity of the core platform.

# 13. Definition of Done

A module is not complete merely because its screen exists.

Each feature must have:

- Database schema and migrations
- Backend API
- Authorization and validation
- Frontend integration
- Error handling
- Audit logging where applicable
- Unit and integration tests
- Operational logging
- Documentation
- Deployment configuration
- Verified end-to-end workflow

Clearly classify each capability as:

- Implemented and tested
- Implemented but not fully tested
- Partially implemented
- Planned
- Not implemented

Never present a planned feature as implemented.

# 14. Development instructions for the coding agent

Before changing code:

1. Inspect the complete existing repository.
2. Identify the current architecture and technology versions.
3. Review database schema and migrations.
4. Inspect frontend routes and components.
5. Review authentication and authorization.
6. Identify existing functionality and gaps.
7. Prepare an implementation plan.

During development:

- Preserve existing working functionality.
- Avoid unnecessary rewrites.
- Follow existing project conventions where sound.
- Implement complete vertical features rather than disconnected screens.
- Use database migrations.
- Validate every API.
- Test authorization and tenant isolation.
- Do not add unnecessary dependencies.
- Do not hardcode demo data into production logic.

After development:

- Build frontend and backend.
- Run automated tests.
- Verify key workflows end to end.
- Check database migrations.
- Document incomplete work.
- Report actual implementation status.

# Final product vision

**Support360 is a practical IT Service & Operations Platform for growing organizations.**

It brings together:

- IT Helpdesk
- SLA Management
- IT Asset Management
- Server and Application Monitoring
- IT Automation
- Knowledge Management
- Integrations
- AI Assistance
- Controlled Deployment

The product must be easy to understand, easy to deploy, affordable to maintain and capable of growing with the customer.

**Build a reliable mid-market product first. Expand into advanced enterprise capabilities only when customer requirements justify the complexity.**
