import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import { clearSession, getSession, setSession } from '../api/session'
import { AuthProvider } from '../auth/AuthProvider'
import { LoginPage } from './LoginPage'

const user = {
  id: 'u1',
  tenantId: 't1',
  tenantName: 'Tenant',
  isDemoTenant: false,
  email: 'admin@test',
  displayName: 'Admin',
  role: 'TenantAdmin' as const,
}

const loginResponse = {
  accessToken: 'token-1',
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
  user,
}

function renderLogin(from?: string) {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[{ pathname: '/login', state: from ? { from } : null }]}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/incidents" element={<div>Incidents target</div>} />
          <Route path="/agents" element={<div>Agents target</div>} />
        </Routes>
      </MemoryRouter>
    </AuthProvider>,
  )
}

describe('LoginPage', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearSession()
    vi.spyOn(api, 'me').mockResolvedValue({ user })
  })

  afterEach(() => vi.restoreAllMocks())

  it('signs in, stores the session, and returns to the requested route', async () => {
    const login = vi.spyOn(api, 'login').mockResolvedValue(loginResponse)
    renderLogin('/incidents')

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: '  admin@test  ' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'secret' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Incidents target')).toBeInTheDocument()
    expect(login).toHaveBeenCalledWith('admin@test', 'secret')
    expect(getSession()?.token).toBe('token-1')
  })

  it('shows the API error and keeps the visitor signed out', async () => {
    vi.spyOn(api, 'login').mockRejectedValue(new Error('Invalid email or password.'))
    renderLogin()

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'admin@test' } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
    expect(getSession()).toBeNull()
  })

  it('redirects an existing session away from the login page', async () => {
    setSession(loginResponse)
    renderLogin('/agents')

    expect(await screen.findByText('Agents target')).toBeInTheDocument()
  })
})
