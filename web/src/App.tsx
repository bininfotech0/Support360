import { useEffect } from 'react'
import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom'
import { getSession } from './api/session'
import { AuthProvider } from './auth/AuthProvider'
import { Layout } from './components/Layout'
import { AgentDetailPage } from './pages/AgentDetailPage'
import { AgentsPage } from './pages/AgentsPage'
import { ApplicationsPage } from './pages/ApplicationsPage'
import { AlertsPage } from './pages/AlertsPage'
import { AuditPage } from './pages/AuditPage'
import { DashboardPage } from './pages/DashboardPage'
import { IncidentsPage } from './pages/IncidentsPage'
import { ChangeRequestsPage } from './pages/ChangeRequestsPage'
import { DeploymentsPage } from './pages/DeploymentsPage'
import { JobsPage } from './pages/JobsPage'
import { LoginPage } from './pages/LoginPage'
import { ServiceRequestsPage } from './pages/ServiceRequestsPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { UsagePage } from './pages/UsagePage'
import { WorkDnaPage } from './pages/WorkDnaPage'

const pageTitles: Record<string, string> = {
  '/': 'Overview',
  '/agents': 'Agents',
  '/applications': 'Application health',
  '/alerts': 'Alerts',
  '/incidents': 'Incidents',
  '/service-requests': 'Service requests',
  '/work-dna': 'Work DNA',
  '/changes': 'Change requests',
  '/jobs': 'Batch job health',
  '/deployments': 'CI/CD deployments',
  '/audit': 'Audit log',
  '/usage': 'Usage',
  '/login': 'Sign in',
}

function pageTitle(pathname: string): string {
  if (pathname.startsWith('/agents/')) return 'Agent details'
  return pageTitles[pathname] ?? 'Page not found'
}

function RouteEffects() {
  const { pathname } = useLocation()

  useEffect(() => {
    document.title = `${pageTitle(pathname)} · Support360`
    document.documentElement.scrollTop = 0
    document.body.scrollTop = 0
  }, [pathname])

  return null
}

function ProtectedRoute() {
  const location = useLocation()
  const session = getSession()

  if (!session) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}

export default function App() {
  return (
    <AuthProvider>
      <RouteEffects />
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route element={<Layout />}>
            <Route index element={<DashboardPage />} />
            <Route path="agents" element={<AgentsPage />} />
            <Route path="agents/:id" element={<AgentDetailPage />} />
            <Route path="applications" element={<ApplicationsPage />} />
            <Route path="alerts" element={<AlertsPage />} />
            <Route path="incidents" element={<IncidentsPage />} />
            <Route path="service-requests" element={<ServiceRequestsPage />} />
            <Route path="work-dna" element={<WorkDnaPage />} />
            <Route path="changes" element={<ChangeRequestsPage />} />
            <Route path="jobs" element={<JobsPage />} />
            <Route path="deployments" element={<DeploymentsPage />} />
            <Route path="audit" element={<AuditPage />} />
            <Route path="usage" element={<UsagePage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Route>
      </Routes>
    </AuthProvider>
  )
}
