import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from './App'
import { api } from './api/client'
import { clearSession, setSession } from './api/session'

const user = {
  id: 'u1',
  tenantId: 't1',
  tenantName: 'Tenant',
  isDemoTenant: false,
  email: 'admin@test',
  displayName: 'Admin',
  role: 'TenantAdmin' as const,
}

describe('App routing', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearSession()
  })

  afterEach(() => vi.restoreAllMocks())

  it('redirects anonymous visitors to the login page', () => {
    render(
      <MemoryRouter initialEntries={['/agents']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Primary navigation' })).not.toBeInTheDocument()
  })

  it('treats expired stored sessions as signed out', () => {
    setSession({ accessToken: 'stale-token', expiresAt: new Date(Date.now() - 1_000).toISOString(), user })
    render(
      <MemoryRouter initialEntries={['/agents']}>
        <App />
      </MemoryRouter>,
    )

    expect(screen.getByLabelText('Email')).toBeInTheDocument()
  })

  it('renders the protected layout for authenticated sessions', async () => {
    setSession({ accessToken: 'token-1', expiresAt: new Date(Date.now() + 60_000).toISOString(), user })
    vi.spyOn(api, 'me').mockResolvedValue({ user })
    render(
      <MemoryRouter initialEntries={['/missing-route']}>
        <App />
      </MemoryRouter>,
    )

    expect(await screen.findByText('Page not found')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Primary navigation' })).toBeInTheDocument()
    expect(document.title).toBe('Page not found · Support360')
  })
})
