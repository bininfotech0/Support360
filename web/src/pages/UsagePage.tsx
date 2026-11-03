import { api } from '../api/client'
import { canManage } from '../api/session'
import { useAuth } from '../auth/authContext'
import { ErrorBanner, Loading, PageHeader, PageSection, StatCard } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate, formatNumber } from '../utils'

export function UsagePage() {
  const { user } = useAuth()
  const allowed = canManage(user)
  const usage = useLoad('usage', () => (allowed ? api.usage() : Promise.resolve(null)))

  if (!allowed) {
    return (
      <>
        <PageHeader eyebrow="Billing" title="Usage" />
        <PageSection title="Administrator access required">
          <p className="muted">Only tenant administrators can view usage counters.</p>
        </PageSection>
      </>
    )
  }

  return (
    <>
      <PageHeader
        eyebrow="Billing"
        title="Usage"
        description="Recorded platform usage for the last 30 days. Demo data is labelled in the tenant header."
        actions={
          <button type="button" className="button button-ghost" onClick={usage.refresh}>
            Refresh
          </button>
        }
      />

      {usage.loading && !usage.data ? <Loading label="Loading usage" /> : null}
      {usage.error ? <ErrorBanner message={usage.error} onRetry={usage.refresh} /> : null}

      {usage.data ? (
        <>
          <div className="stats-grid">
            <StatCard
              label="Telemetry samples"
              value={formatNumber(usage.data.telemetrySamplesLast30Days)}
              detail="Last 30 days"
            />
            <StatCard
              label="Agent heartbeats"
              value={formatNumber(usage.data.agentHeartbeatsLast30Days)}
              detail="Last 30 days"
            />
            <StatCard
              label="Job events"
              value={formatNumber(usage.data.jobEventsLast30Days)}
              detail="Last 30 days"
            />
            <StatCard label="Generated" value={relativeLabel(usage.data.generatedAt)} detail={formatDate(usage.data.generatedAt)} />
          </div>

          <PageSection
            title="How usage is counted"
            description="Counters are derived from tenant-scoped usage events, not estimated from agent inventory."
          >
            <ul className="plain-list">
              <li>Each accepted telemetry sample creates a usage event.</li>
              <li>Each authenticated heartbeat creates a usage event.</li>
              <li>Each accepted batch job event creates a usage event.</li>
              <li>Rejected and duplicate payloads are excluded.</li>
              <li>Retention workers remove expired telemetry while audit and usage history follow their own retention windows.</li>
            </ul>
          </PageSection>
        </>
      ) : null}
    </>
  )
}

function relativeLabel(value: string): string {
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(value))
}
