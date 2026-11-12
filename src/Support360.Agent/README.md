# Support360 monitoring agent (C#/.NET)

The agent enrolls with a short-lived single-use token, writes its individual API key to a local identity file, collects CPU, memory, system-disk, network-counter, and uptime samples, and submits authenticated heartbeats and telemetry. HTTP is refused unless the explicit `--allow-http` development flag is used. A 401/403 stops the agent so revoked credentials do not continue operating.

Telemetry is persisted in an atomic JSON queue capped at 5,000 events. Application-check results use a separate atomic queue capped at 500 events. Both queues send bounded batches, retain data across transient upload failures and restarts, and remove accepted, duplicate, or permanently rejected events after server acknowledgement. Oldest events are dropped when a queue reaches capacity.

Configured checks support HTTP/HTTPS status checks, process existence, Windows service state, and Linux systemd service state. HTTP checks consider 2xx responses healthy, send a `support360-agent/<version>` User-Agent, and do not follow redirects or honor environment proxies. Service checks consider only the running/active state healthy. A present process reports `Present`, never `Healthy`, because process presence does not prove application health. Linux systemd checks require `systemctl`; Windows service checks query the Service Control Manager. Unsupported platforms report `Unsupported` rather than healthy.

## Enroll and run

Use an enrollment token generated in the Support360 UI. The token is single-use and expires according to the tenant policy.

```sh
support360-agent enroll --server https://support360.example --token '<short-lived-token>'
support360-agent run --server https://support360.example
```

For a local Docker Compose API only, use `--server http://localhost:8080 --allow-http`. Never use this flag outside an isolated development environment. The identity defaults to the operating system user configuration directory; the telemetry queue defaults to its cache directory; the check queue uses `<queue path>.checks`. Override locations with `--identity` and `--queue` (or the `SUPPORT360_IDENTITY_FILE` and `SUPPORT360_QUEUE_FILE` environment variables).

Monitoring-only is the default. A tenant administrator must grant deployment permission when creating the enrollment token; the host must separately opt in with `support360-agent enroll --enable-deployments`. The flag is available in the Linux installer as its optional third argument and in Windows setup as `-EnableDeployments`. The local choice is saved in the protected identity and reported as an agent capability.

When deployment execution is enabled for an agent (local opt-in plus `SUPPORT360_MANIFEST_PUBLIC_KEY` pointing to the pinned server manifest public key PEM and `SUPPORT360_DEPLOY_ROOT` defining the deployment root), the agent polls for pending work once per minute, fetches the short-lived RSA-PSS signed manifest, verifies it against the pinned key (deployment, agent, tenant, package digest, target path, nonce, and expiry are bound into the signature), downloads the artifact with a SHA-256 re-check, extracts it with traversal/symlink/duplicate-path rejection under the deployment-root allowlist, swaps the target directory with rollback on failure, and reports the result. This execution path is unit-tested but has not been exercised against a live server; keep server-side execution disabled until that verification is done.

## Service installation

On a systemd host, publish/copy the Linux binary and run `sudo src/Support360.Agent/packaging/linux/install.sh /path/to/support360-agent https://support360.example`. The script prompts for the one-time enrollment token without echoing it, creates a dedicated `support360` user, restricts identity/queue permissions, and enables the hardened unit. Check it with `systemctl status support360-agent` and `journalctl -u support360-agent`. Uninstall with `sudo src/Support360.Agent/packaging/linux/uninstall.sh`; add `--remove-identity` to delete the local credential and queues.

On Windows, use an elevated PowerShell session: `src/Support360.Agent/packaging/windows/install.ps1 -BinaryPath C:\path\support360-agent.exe -ServerUrl https://support360.example`. The script prompts for the enrollment token as a secure string, installs the service under LocalService, restricts the ProgramData identity and queue directory to the service SID and Administrators, and configures service restart. The service runs `support360-agent service --config <service.json>` with HTTPS only. Check with `Get-Service Support360Agent`. Uninstall with `src/Support360.Agent/packaging/windows/uninstall.ps1`; add `-RemoveIdentity` to delete local credentials and queues. Native Windows Server and Linux distribution installation tests have not been run in this workspace; these scripts are not a platform support certification.

## Build and test

```sh
dotnet build src/Support360.Agent/Support360.Agent.csproj -c Release
dotnet test tests/Support360.Tests.Agent/Support360.Tests.Agent.csproj
dotnet publish src/Support360.Agent/Support360.Agent.csproj -c Release -r linux-x64 --self-contained false -o dist/linux
dotnet publish src/Support360.Agent/Support360.Agent.csproj -c Release -r win-x64 --self-contained false -o dist/windows
```

Self-contained publishes (drop `--self-contained false`) produce single binaries that do not require the .NET runtime on the host; installer scripts expect those. Build the Linux container image from the repository root with `docker build -t support360-agent:local -f src/Support360.Agent/Dockerfile .` (the Compose agent service uses the same context). Keep the identity file and queues on persistent, access-restricted storage. The identity file uses mode `0600` on POSIX systems.

## Data behavior

Metric names and units match the backend catalog. CPU collection samples over a one-second interval; memory and disk percentages use OS totals; disk capacity uses the system volume (`C:\` on Windows and `/` on Linux); network byte counters are cumulative totals across interfaces; uptime is seconds since boot. Samples are UTC timestamps. Temporary collector failures are logged and do not fabricate samples.
