import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import { clearSession, getSession, setSession } from '../api/session'
import type { UserDto } from '../api/types'
import { AuthProvider } from '../auth/AuthProvider'
import { Layout } from './Layout'

const admin = {
  id: 'u1',
  tenantId: 't1',
  tenantName: 'Tenant',
  isDemoTenant: false,
  email: 'admin@test',
  displayName: 'Admin',
  role: 'TenantAdmin' as const,
}

function renderLayout(user: UserDto = admin) {
  setSession({ accessToken: 'token-1', expiresAt: new Date(Date.now() + 60_000).toISOString(), user })
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<div>Dashboard content</div>} />
          </Route>
          <Route path="/login" element={<div>Login route</div>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  )
}

describe('Layout', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearSession()
    vi.spyOn(api, 'me').mockImplementation(async () => ({ user: getSession()?.user ?? admin }))
  })

  afterEach(() => vi.restoreAllMocks())

  it('shows administrative navigation to tenant admins', async () => {
    renderLayout()

    expect(await screen.findByText('Dashboard content')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Audit log' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Usage' })).toBeInTheDocument()
    expect(screen.getByText('TenantAdmin')).toBeInTheDocument()
  })

  it('hides administrative navigation from viewers', async () => {
    renderLayout({ ...admin, role: 'Viewer' })

    expect(await screen.findByText('Dashboard content')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Audit log' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Usage' })).not.toBeInTheDocument()
    expect(screen.getByText('Viewer')).toBeInTheDocument()
  })

  it('links keyboard users to the main landmark', async () => {
    renderLayout()

    expect(await screen.findByText('Dashboard content')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Skip to content' })).toHaveAttribute('href', '#main-content')
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main-content')
  })

  it('signs out and clears the stored session', async () => {
    renderLayout()

    fireEvent.click(await screen.findByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Login route')).toBeInTheDocument()
    expect(getSession()).toBeNull()
  })
})
