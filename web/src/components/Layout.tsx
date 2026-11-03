import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { canManage, canOperate } from '../api/session'
import { useAuth } from '../auth/authContext'

const navigation = [
  { to: '/', label: 'Overview', end: true },
  { to: '/agents', label: 'Agents' },
  { to: '/applications', label: 'App checks' },
  { to: '/alerts', label: 'Alerts' },
  { to: '/incidents', label: 'Incidents' },
  { to: '/changes', label: 'Changes' },
  { to: '/jobs', label: 'Batch jobs' },
  { to: '/deployments', label: 'Deployments' },
]

const adminNavigation = [
  { to: '/audit', label: 'Audit log' },
  { to: '/usage', label: 'Usage' },
]

export function Layout() {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const admin = canManage(user)
  const operator = canOperate(user)

  const signOut = () => {
    logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand">
          <span className="brand-mark" aria-hidden="true">
            S3
          </span>
          <div>
            <strong>Support360</strong>
            <span>Operations console</span>
          </div>
        </div>

        <nav aria-label="Primary navigation">
          {navigation.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end} className="nav-link">
              {item.label}
            </NavLink>
          ))}
          {admin
            ? adminNavigation.map((item) => (
                <NavLink key={item.to} to={item.to} className="nav-link">
                  {item.label}
                </NavLink>
              ))
            : null}
        </nav>

        <div className="sidebar-footer">
          <span className={`role-chip ${operator ? 'role-operator' : ''}`}>{user?.role ?? 'Viewer'}</span>
          <p className="tenant-name">{user?.tenantName}</p>
          {user?.isDemoTenant ? <span className="demo-chip">Demo tenant</span> : null}
        </div>
      </aside>

      <div className="app-main">
        <header className="topbar">
          <div className="topbar-context">
            <span className="live-dot" aria-hidden="true" />
            <span>Tenant-scoped session</span>
          </div>
          <div className="topbar-user">
            <div>
              <strong>{user?.displayName}</strong>
              <span>{user?.email}</span>
            </div>
            <button type="button" className="button button-ghost" onClick={signOut}>
              Sign out
            </button>
          </div>
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
