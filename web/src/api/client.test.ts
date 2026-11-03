import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from './client'
import { clearSession, getSession, setSession } from './session'

const user = {
  id: 'u1',
  tenantId: 't1',
  tenantName: 'Tenant',
  isDemoTenant: false,
  email: 'admin@test',
  displayName: 'Admin',
  role: 'TenantAdmin' as const,
}

describe('api client', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearSession()
  })

  afterEach(() => vi.unstubAllGlobals())

  it('attaches the bearer token and clears the session on 401', async () => {
    setSession({ accessToken: 'token-1', expiresAt: new Date(Date.now() + 60_000).toISOString(), user })
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ title: 'Unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)
    const onUnauthorized = vi.fn()
    window.addEventListener('support360:unauthorized', onUnauthorized)

    await expect(api.agents()).rejects.toBeInstanceOf(ApiError)

    expect(getSession()).toBeNull()
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/agents')
    expect(new Headers(init?.headers as HeadersInit).get('Authorization')).toBe('Bearer token-1')

    window.removeEventListener('support360:unauthorized', onUnauthorized)
  })

  it('surfaces API problem details without clearing an anonymous request', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ detail: 'Invalid email or password.' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const failure = await api.login('admin@test', 'wrong').catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).message).toBe('Invalid email or password.')
    expect((failure as ApiError).status).toBe(401)
    expect(getSession()).toBeNull()
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers as HeadersInit).has('Authorization')).toBe(false)
  })
})
