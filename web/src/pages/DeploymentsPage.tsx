import { useMemo, useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { useAuth } from '../auth/authContext'
import { canManage, canOperate } from '../api/session'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { useRealtime } from '../hooks/useRealtime'
import { formatDate } from '../utils'

const statusFilters = ['all', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled', 'Queued', 'Executing', 'Succeeded', 'Failed']

export function DeploymentsPage() {
  const { user } = useAuth()
  const overview = useLoad('deployment-overview', () => api.overview())
  const packages = useLoad('deployment-packages', () => api.deploymentPackages())
  const deployments = useLoad('deployment-requests', () => api.deployments())
  const agents = useLoad('deployment-agents', () => api.agents())
  const changes = useLoad('deployment-change-requests', () => api.changeRequests())
  useRealtime(() => { deployments.refresh(); changes.refresh() })
  const [packageName, setPackageName] = useState('')
  const [version, setVersion] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [targetAgentId, setTargetAgentId] = useState('')
  const [deploymentPackageId, setDeploymentPackageId] = useState('')
  const [changeRequestId, setChangeRequestId] = useState('')
  const [targetPath, setTargetPath] = useState('')
  const [requestError, setRequestError] = useState<string | null>(null)
  const [requesting, setRequesting] = useState(false)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('all')
  const visibleDeployments = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return (deployments.data ?? []).filter(item => {
      const matchesStatus = status === 'all' || item.status === status
      const matchesSearch = !needle || [item.packageName, item.targetHostname, item.changeRequestNumber].some(value => value.toLowerCase().includes(needle))
      return matchesStatus && matchesSearch
    })
  }, [deployments.data, search, status])

  const upload = async (event: FormEvent) => {
    event.preventDefault()
    if (!file) return
    setUploadError(null); setUploading(true)
    try {
      await api.uploadDeploymentPackage(packageName, version, file)
      setPackageName(''); setVersion(''); setFile(null); packages.refresh()
      const input = document.getElementById('deployment-package-file') as HTMLInputElement | null
      if (input) input.value = ''
    } catch (error) { setUploadError(error instanceof Error ? error.message : 'Could not upload package.') }
    finally { setUploading(false) }
  }

  const createRequest = async (event: FormEvent) => {
    event.preventDefault(); setRequestError(null); setRequesting(true)
    try {
      await api.createDeployment({ targetAgentId, deploymentPackageId, changeRequestId, targetPath })
      setTargetAgentId(''); setDeploymentPackageId(''); setChangeRequestId(''); setTargetPath(''); deployments.refresh()
    } catch (error) { setRequestError(error instanceof Error ? error.message : 'Could not create deployment request.') }
    finally { setRequesting(false) }
  }

  if (overview.loading && !overview.data) {
    return <Loading label="Loading deployment status" />
  }

  if (overview.error && !overview.data) {
    return <ErrorBanner message={overview.error} onRetry={overview.refresh} />
  }

  const enabled = overview.data?.deploymentExecutionEnabled ?? false

  return (
    <>
      <PageHeader
        eyebrow="Change management"
        title="CI/CD deployments"
        description="Signed, target-bound manifests gate agent-side package execution."
        actions={<StatusBadge value={enabled ? 'Enabled' : 'Disabled'} label={enabled ? 'Execution enabled' : 'Execution disabled'} />}
      />

      <div className="notice">
        <strong>{enabled ? 'Deployment execution is enabled.' : 'Deployment execution is not enabled in this build.'}</strong>
        <span>
          {enabled
            ? 'Approved requests can issue short-lived RSA-PSS signed manifests. Agents verify the signature, target binding, artifact digest, and path allowlist before installing.'
            : 'Requests can be linked to approved change records, but manifest signing is disabled. Agents cannot receive or execute deployment work, and no deployment is simulated or claimed.'}
        </span>
      </div>

      <div className="split-grid">
        <PageSection title="Safety controls" description="Required server-side controls before any execution path is exposed.">
          <ul className="check-list">
            <li>
              <span className="check-icon" aria-hidden="true">1</span>
              SHA-256 verified package artifacts with bounded upload size.
            </li>
            <li>
              <span className="check-icon" aria-hidden="true">2</span>
              Tenant-scoped target and package binding to a linked change request.
            </li>
            <li>
              <span className="check-icon" aria-hidden="true">3</span>
              RSA-PSS signed manifests bind the deployment to the target agent, package digest, nonce, and expiry.
            </li>
            <li>
              <span className="check-icon" aria-hidden="true">4</span>
              The agent enforces a deployment-root allowlist, verifies the artifact digest, and reports the result.
            </li>
          </ul>
          <p className="muted small">
            Requests capture the package digest, target agent, target path, and linked change approval state. Agent dispatch requires deployment execution to be enabled and a signing key to be configured server-side.
          </p>
        </PageSection>

        <PageSection title="Current state" description="Read directly from the authenticated overview endpoint.">
          <dl className="detail-list">
            <div>
              <dt>Execution gate</dt>
              <dd>{enabled ? 'Enabled by configuration' : 'Disabled by configuration'}</dd>
            </div>
            <div>
              <dt>Deployment requests</dt>
              <dd>{deployments.data?.length ?? 'Loading'}</dd>
            </div>
            <div>
              <dt>Generated</dt>
              <dd>{formatDate(overview.data?.generatedAt)}</dd>
            </div>
            <div>
              <dt>Agent manifest</dt>
              <dd>{enabled ? 'Signed on dispatch' : 'Unavailable'}</dd>
            </div>
          </dl>
        </PageSection>
      </div>

      {canOperate(user) ? <PageSection title="Create deployment request" description="Select an active agent, a tenant package, a change request, and an absolute target path under the agent deployment root.">
        {requestError ? <ErrorBanner message={requestError} /> : null}
        {(agents.error || changes.error) ? <ErrorBanner message={agents.error ?? changes.error ?? 'Could not load deployment choices.'} onRetry={() => { agents.refresh(); changes.refresh() }} /> : null}
        <form className="enrollment-controls" onSubmit={createRequest}>
          <label>Target agent<select required value={targetAgentId} onChange={e => setTargetAgentId(e.target.value)}><option value="">Select active agent</option>{agents.data?.filter(agent => agent.status === 'Active').map(agent => <option key={agent.id} value={agent.id}>{agent.hostname}</option>)}</select></label>
          <label>Package<select required value={deploymentPackageId} onChange={e => setDeploymentPackageId(e.target.value)}><option value="">Select package</option>{packages.data?.map(item => <option key={item.id} value={item.id}>{item.name} {item.version} · {item.sha256.slice(0, 12)}</option>)}</select></label>
          <label>Change request<select required value={changeRequestId} onChange={e => setChangeRequestId(e.target.value)}><option value="">Select pending or approved change</option>{changes.data?.filter(change => ['PendingApproval', 'Approved'].includes(change.status)).map(change => <option key={change.id} value={change.id}>{change.number} · {change.title} · {change.status}</option>)}</select></label>
          <label>Target path<input required maxLength={500} placeholder="/srv/support360/web" value={targetPath} onChange={e => setTargetPath(e.target.value)} /><span className="muted small">Must be an absolute path inside the agent deployment root.</span></label>
          <button type="submit" className="button button-primary" disabled={!targetAgentId || !deploymentPackageId || !changeRequestId || !targetPath.trim() || requesting || !packages.data?.length}>{requesting ? 'Creating…' : 'Create request'}</button>
        </form>
      </PageSection> : null}

      <PageSection title="Deployment requests" description={`${visibleDeployments.length} of ${deployments.data?.length ?? 0} requests. Approved requests dispatch to the bound agent under a short-lived signed manifest.`} actions={<div className="inline-actions"><input aria-label="Search deployment requests" placeholder="Search package, target, change" value={search} onChange={e => setSearch(e.target.value)} /><select value={status} onChange={e => setStatus(e.target.value)} aria-label="Deployment status filter">{statusFilters.map(value => <option key={value} value={value}>{value === 'all' ? 'All statuses' : value === 'PendingApproval' ? 'Pending approval' : value}</option>)}</select></div>}>
        {deployments.loading && !deployments.data ? <Loading label="Loading deployment requests" /> : null}
        {deployments.error ? <ErrorBanner message={deployments.error} onRetry={deployments.refresh} /> : null}
        {!deployments.loading && deployments.data?.length === 0 ? (
          <EmptyState
            title="No deployment requests"
            detail="Create a request once an approved change and package are available."
          />
        ) : null}
        {!deployments.loading && (deployments.data?.length ?? 0) > 0 && visibleDeployments.length === 0 ? <EmptyState title="No matching deployment requests" detail="Try a different search or status filter." /> : null}
        {visibleDeployments.length ? <div className="table-wrap"><table><thead><tr><th>Package</th><th>Target</th><th>Environment</th><th>Change</th><th>Status</th><th>Requested by</th><th>Requested</th></tr></thead><tbody>{visibleDeployments.map(item => <tr key={item.id}><td>{item.packageName} {item.packageVersion}<span className="muted block"><code>{item.packageSha256}</code></span>{item.gitRepoUrl ? <span className="muted block"><code>{item.gitRepoUrl}{item.gitBranch ? ` (${item.gitBranch})` : ''}</code></span> : null}</td><td>{item.targetHostname}<span className="muted block"><code>{item.targetPath ?? 'No target path'}</code></span></td><td>{item.environment}</td><td>{item.changeRequestNumber}<span className="muted block">{item.receivedApprovals}/{item.requiredApprovals} approvals</span></td><td><StatusBadge value={item.status} /></td><td>{item.requestedByDisplay}</td><td>{formatDate(item.requestedAt)}</td></tr>)}</tbody></table></div> : null}
      </PageSection>

      <PageSection title="Package catalog" description="Tenant-isolated ZIP artifacts are stored with a computed SHA-256 digest. Uploading a package does not authorize or execute it.">
        {uploadError ? <ErrorBanner message={uploadError} /> : null}
        {canManage(user) ? <form className="enrollment-controls" onSubmit={upload}>
          <label>Package name<input required maxLength={200} value={packageName} onChange={e => setPackageName(e.target.value)} /></label>
          <label>Version<input required maxLength={100} value={version} onChange={e => setVersion(e.target.value)} /></label>
          <label>ZIP package<input id="deployment-package-file" type="file" accept=".zip,application/zip" required onChange={e => setFile(e.target.files?.[0] ?? null)} /><span className="muted small">Maximum size: 20 MiB. Package contents are not extracted.</span></label>
          <button type="submit" className="button button-primary" disabled={!file || uploading}>{uploading ? 'Uploading…' : 'Upload package'}</button>
        </form> : null}
        {packages.loading && !packages.data ? <Loading label="Loading package catalog" /> : null}
        {packages.error ? <ErrorBanner message={packages.error} onRetry={packages.refresh} /> : null}
        {!packages.loading && packages.data?.length === 0 ? (
          <EmptyState
            title="No packages uploaded"
            detail="Upload a ZIP package to make it available for deployment requests."
          />
        ) : null}
        {packages.data?.length ? <div className="table-wrap"><table><thead><tr><th>Package</th><th>Version</th><th>SHA-256</th><th>Size</th><th>Uploaded</th></tr></thead><tbody>{packages.data.map(item => <tr key={item.id}><td>{item.name}</td><td>{item.version}</td><td><code>{item.sha256}</code></td><td>{item.sizeBytes.toLocaleString()} bytes</td><td>{formatDate(item.uploadedAt)}</td></tr>)}</tbody></table></div> : null}
      </PageSection>
    </>
  )
}
