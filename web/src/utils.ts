export function formatDate(value: string | null | undefined): string {
  if (!value) return 'Never'
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

export function relativeTime(value: string | null | undefined): string {
  if (!value) return 'Never'
  const delta = Date.now() - new Date(value).getTime()
  const seconds = Math.round(delta / 1000)
  if (seconds < 0) return 'Just now'
  if (seconds < 45) return `${seconds}s ago`
  const minutes = Math.round(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.round(minutes / 60)
  if (hours < 48) return `${hours}h ago`
  const days = Math.round(hours / 24)
  return `${days}d ago`
}

export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(value)
}

export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) return '—'
  return `${formatNumber(value)}%`
}

export function formatBytes(value: number | null | undefined): string {
  if (value === null || value === undefined) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let size = value
  let unit = 0
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024
    unit += 1
  }
  return `${formatNumber(size)} ${units[unit]}`
}

export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '—'
  const days = Math.floor(seconds / 86400)
  const hours = Math.floor((seconds % 86400) / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${minutes}m`
}

export function metricLabel(metric: string): string {
  const labels: Record<string, string> = {
    'cpu.usage.percent': 'CPU usage',
    'memory.used.percent': 'Memory used',
    'memory.used.bytes': 'Memory used',
    'disk.used.percent': 'Disk used',
    'disk.free.bytes': 'Disk free',
    'network.rx.bytes_total': 'Network received',
    'network.tx.bytes_total': 'Network sent',
    'uptime.seconds': 'Uptime',
  }
  return labels[metric] ?? metric
}

export function metricValue(metric: string, value: number): string {
  if (metric.endsWith('.bytes') || metric.includes('bytes_total')) return formatBytes(value)
  if (metric === 'uptime.seconds') return formatDuration(value)
  if (metric.endsWith('.percent')) return formatPercent(value)
  return formatNumber(value)
}

export function statusTone(value: string | null | undefined): 'ok' | 'warn' | 'bad' | 'neutral' {
  switch (value?.toLowerCase()) {
    case 'active':
    case 'online':
    case 'healthy':
    case 'success':
    case 'resolved':
    case 'running':
    case 'completed':
      return 'ok'
    case 'degraded':
    case 'warning':
    case 'unknown':
    case 'pending':
    case 'acknowledged':
      return 'warn'
    case 'offline':
    case 'revoked':
    case 'critical':
    case 'failed':
    case 'error':
      return 'bad'
    default:
      return 'neutral'
  }
}

export function bytesToPercent(value: number | null | undefined): number {
  if (value === null || value === undefined) return 0
  return Math.max(0, Math.min(100, value))
}

export async function copyText(value: string): Promise<void> {
  await navigator.clipboard.writeText(value)
}
