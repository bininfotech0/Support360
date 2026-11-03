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
import { NotFoundPage } from './pages/NotFoundPage'
import { UsagePage } from './pages/UsagePage'

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
