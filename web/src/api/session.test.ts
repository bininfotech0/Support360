import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { LoginResponse } from './types'

const user: LoginResponse['user'] = {
  id: 'u1',
  tenantId: 't1',
  tenantName: 'Tenant',
  isDemoTenant: false,
  email: 'admin@test',
  displayName: 'Admin',
  role: 'TenantAdmin',
}

const response = (expiresAt = new Date(Date.now() + 60_000).toISOString()): LoginResponse => ({
  accessToken: 'token-1',
  expiresAt,
  user,
})

describe('session', () => {
  beforeEach(() => {
    sessionStorage.clear()
    vi.resetModules()
  })

  it('persists and restores a valid login session', async () => {
    const session = await import('./session')
    session.setSession(response())

    expect(session.getSession()?.token).toBe('token-1')
    expect(JSON.parse(sessionStorage.getItem('support360.session')!)).toMatchObject({
      token: 'token-1',
      user: { email: 'admin@test' },
    })
  })

  it('drops expired or malformed stored sessions', async () => {
    sessionStorage.setItem('support360.session', JSON.stringify(response(new Date(Date.now() - 60_000).toISOString())))
    const expired = await import('./session')
    expect(expired.getSession()).toBeNull()

    vi.resetModules()
    sessionStorage.setItem('support360.session', '{not-json')
    const malformed = await import('./session')
    expect(malformed.getSession()).toBeNull()
  })

  it('expires an in-memory session when read after its expiry', async () => {
    const session = await import('./session')
    session.setSession(response(new Date(Date.now() + 20).toISOString()))
    await new Promise((resolve) => setTimeout(resolve, 40))

    expect(session.getSession()).toBeNull()
    expect(sessionStorage.getItem('support360.session')).toBeNull()
  })

  it('clears the session and notifies subscribers', async () => {
    const session = await import('./session')
    const listener = vi.fn()
    const unsubscribe = session.subscribeSession(listener)

    session.setSession(response())
    session.clearSession()
    expect(listener).toHaveBeenCalledTimes(2)
    expect(session.getSession()).toBeNull()

    unsubscribe()
    session.setSession(response())
    expect(listener).toHaveBeenCalledTimes(2)
  })

  it('maps roles to management, operation, and approval capabilities', async () => {
    const session = await import('./session')

    expect(session.canManage({ ...user, role: 'TenantAdmin' })).toBe(true)
    expect(session.canManage({ ...user, role: 'PlatformAdmin' })).toBe(true)
    expect(session.canManage({ ...user, role: 'Operator' })).toBe(false)
    expect(session.canManage(null)).toBe(false)

    expect(session.canOperate({ ...user, role: 'Operator' })).toBe(true)
    expect(session.canOperate({ ...user, role: 'Approver' })).toBe(false)

    expect(session.canApprove({ ...user, role: 'Approver' })).toBe(true)
    expect(session.canApprove({ ...user, role: 'Viewer' })).toBe(false)
  })
})
