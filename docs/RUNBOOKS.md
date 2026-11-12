# Support360 operational runbooks

These steps apply to the local Docker Compose deployment. Production deployments need environment-specific access, secret-management, backup, and escalation procedures.

## Agent offline

1. In Agent Management, check the last heartbeat, last telemetry time, hostname, and collector health.
2. Check cloud readiness and the database container:

   ```powershell
   Invoke-WebRequest http://localhost:8080/health/ready
   docker compose ps
   docker compose logs --tail 100 api
   ```

3. Check the agent container and its latest output:

   ```powershell
   docker compose --profile agent ps
   docker compose --profile agent logs --tail 100 agent
   ```

4. Confirm the agent can resolve `api` on the Compose network and the API is available on port 8080. The local agent uses HTTP only because Compose is an isolated development network; installed agents should use HTTPS.
5. A revoked or invalid identity gets HTTP 401/403 and exits. Revoke it in Agent Management, create a fresh Linux enrollment, and enroll a replacement container. Do not reuse an enrollment token.

## Ingestion backlog or upload failure

1. Check agent logs for collector errors, telemetry upload failures, `queued_events`, and `application result queue status`.
2. The telemetry queue holds at most 5,000 samples and the application result queue at most 500 events; both drop the oldest on overflow and survive restarts in the agent's persistent state. A growing queue indicates API/network trouble; overflow is logged locally, not surfaced to the cloud.
3. Check API and PostgreSQL logs with `docker compose logs --tail 200 api postgres` and verify `/health/ready`.
4. Restore API/database connectivity. The agent retries transient errors with bounded backoff and drains up to 200 telemetry events or 100 check results per request.
5. Do not delete the agent volume during an outage; it contains the pending queue and agent identity.

## Install or remove an agent service
- Linux: use `src/Support360.Agent/packaging/linux/install.sh` on a systemd distribution. It creates a dedicated service account, stores identity and queues with restrictive permissions, and enables `support360-agent.service`. Review the script and confirm the distribution's `systemd`, `runuser`, and `useradd` versions before deployment.

- Windows: run `src/Support360.Agent/packaging/windows/install.ps1` elevated. It installs under LocalService, scopes the identity and queue directory to the service SID and Administrators, and configures restart on failure. Use `Get-Service Support360Agent` and Windows service control manager state for diagnosis; this version does not configure persistent Windows event-log forwarding.
- Uninstall scripts preserve identity and queued events by default. Use the explicit identity-removal option only when credential and queue deletion is intended. Installer scripts have syntax and compile checks only; complete a test install on the exact target OS before production use.

## Agent credential compromise

1. Revoke the agent immediately in Agent Management. Verify its next request receives HTTP 401 and no further heartbeat or telemetry is accepted.
2. Preserve the agent and audit records needed for incident review; restrict access to the identity volume.
3. Remove the compromised agent container and identity volume after preservation requirements are met.
4. Issue a new short-lived enrollment and install a replacement. The current agent does not automatically receive rotated credentials; rotation requires secure reconfiguration/re-enrollment.
5. Review tenant audit events and API logs for unexpected enrollment, telemetry, or administrative activity.

## Deployment failure or request

Deployment execution is disabled by default and has not been validated end to end with a live agent in this workspace. Enabling it requires `Deployment:ExecutionEnabled=true`, a configured RSA private/public key pair (`Deployment:ManifestSigningPrivateKey`, `Deployment:ManifestSigningPublicKey`, `Deployment:ManifestSigningKeyId`), an administrator deployment grant on the agent enrollment, and the local agent opt-in plus pinned public key/deployment root. Never enable it with development or shared keys.

Tenant admins may stage bounded ZIP files in the package catalog, and operators may create requests tied to the existing change approval workflow. Uploads are SHA-256 hashed and structurally checked; the signed manifest carries the digest and the agent re-checks it after download. The local MVP stores package bytes in PostgreSQL; review database backup and retention capacity if using the catalog.

Failure handling:

1. A manifest/result replay or a result reported without a started deployment returns HTTP 409. Check the agent logs and the deployment status before retrying; do not rotate credentials as a first response.
2. A rejected manifest indicates a mismatched agent/tenant/deployment/package, expiry, future issue time, unsupported algorithm/key, or invalid signature. Compare the server key id and pinned public key; do not bypass verification.
3. A digest mismatch, unsafe archive, deployment-root escape, or activation failure marks the deployment failed. The agent restores the previous target when activation fails; inspect `.support360-staging-*`/`.support360-backup-*` siblings under the target parent and preserve logs before cleanup.
4. If signing keys are suspected compromised, disable `Deployment:ExecutionEnabled`, revoke affected agent credentials, replace the key pair and agent pinned keys, then re-enable only after re-testing. Existing manifests expire within the configured lifetime (default 30 minutes; agent maximum 2 hours).
5. Apply `AddDeploymentManifestNonces` before enabling execution against an existing database.

## Local service recovery

1. Inspect service state and recent logs:

   ```powershell
   docker compose --profile agent ps
   docker compose logs --tail 200 api postgres web
   docker compose --profile agent logs --tail 200 agent
   ```

2. Restart a failed service with `docker compose restart <service>`. Recheck PostgreSQL health and the API `/health/ready` endpoint before assessing recovery.
3. Keep the named PostgreSQL and agent volumes when using `docker compose down`. `docker compose down -v` deletes persistent database and agent state.
4. Create a logical database dump:

   ```powershell
   docker exec support360-postgres-1 pg_dump -U support360 -d support360 > support360-backup.sql
   ```

5. Verify a backup by restoring it into a separate empty test database; never overwrite the active database during a restore drill. Protect backup files as tenant data and store production backups encrypted outside the Compose host.
