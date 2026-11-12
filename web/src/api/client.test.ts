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

  it('maps incident requests onto the unified ticket API', async () => {
    const ticket = {
      id: 't1',
      number: 'INC-000001',
      type: 'Incident',
      title: 'Database latency',
      description: 'Database response time increased.',
      category: null,
      status: 'New',
      priority: 'High',
      impact: 'Medium',
      urgency: 'Medium',
      requestedByUserId: 'u1',
      requestedByDisplay: 'Admin',
      assignedToUserId: null,
      relatedBatchJobExecutionId: null,
      risk: null,
      targetEnvironment: null,
      plannedStartAt: null,
      plannedEndAt: null,
      requiredApprovals: 1,
      openedAt: new Date().toISOString(),
      acknowledgedAt: null,
      resolvedAt: null,
      closedAt: null,
      alerts: [],
      approvals: [],
      activities: [],
      relatedTickets: [{ ticketId: 'c1', number: 'CHG-000001', type: 'Change', linkType: 'RelatesTo' }],
    }
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify([ticket]), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const incidents = await api.incidents('New')

    expect(fetchMock.mock.calls[0][0]).toBe('/api/tickets?type=incident&status=New')
    expect(incidents[0].severity).toBe('High')
    expect(incidents[0].relatedChangeRequestIds).toEqual(['c1'])
  })

  it('creates change tickets with the related ticket binding', async () => {
    const ticket = {
      id: 'c1',
      number: 'CHG-000001',
      type: 'Change',
      title: 'Deploy signed package',
      description: 'Deploy through the allowlisted agent executor.',
      category: null,
      status: 'PendingApproval',
      priority: 'Medium',
      impact: 'Medium',
      urgency: 'Medium',
      requestedByUserId: 'u1',
      requestedByDisplay: 'Admin',
      assignedToUserId: null,
      relatedBatchJobExecutionId: null,
      risk: 'Low risk.',
      targetEnvironment: 'test',
      plannedStartAt: new Date(Date.now() + 3_600_000).toISOString(),
      plannedEndAt: new Date(Date.now() + 7_200_000).toISOString(),
      requiredApprovals: 1,
      openedAt: new Date().toISOString(),
      acknowledgedAt: null,
      resolvedAt: null,
      closedAt: null,
      alerts: [],
      approvals: [],
      activities: [],
      relatedTickets: [{ ticketId: 'i1', number: 'INC-000001', type: 'Incident', linkType: 'RelatesTo' }],
    }
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify(ticket), { status: 201, headers: { 'Content-Type': 'application/json' } }),
    )
    vi.stubGlobal('fetch', fetchMock)

    const change = await api.createChangeRequest({
      title: 'Deploy signed package',
      description: 'Deploy through the allowlisted agent executor.',
      risk: 'Low risk.',
      targetEnvironment: 'test',
      plannedStartAt: ticket.plannedStartAt,
      plannedEndAt: ticket.plannedEndAt,
      relatedIncidentId: 'i1',
    })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/tickets')
    const body = JSON.parse(String(fetchMock.mock.calls[0][1]?.body))
    expect(body).toMatchObject({ type: 'Change', risk: 'Low risk.', targetEnvironment: 'test', relatedTicketId: 'i1' })
    expect(change.relatedIncidentId).toBe('i1')
  })

  it('posts collaboration updates to the ticket endpoints', async () => {
    const ticket = {
      id: 't1', number: 'INC-000001', type: 'Incident', title: 'Database latency', description: 'Database response time increased.',
      category: null, status: 'New', priority: 'High', impact: 'Medium', urgency: 'Medium', requestedByUserId: 'u1',
      requestedByDisplay: 'Admin', assignedToUserId: null, relatedBatchJobExecutionId: null, risk: null,
      targetEnvironment: null, plannedStartAt: null, plannedEndAt: null, requiredApprovals: 1,
      openedAt: new Date().toISOString(), acknowledgedAt: null, resolvedAt: null, closedAt: null,
      alerts: [], approvals: [], activities: [], relatedTickets: [], comments: [], worklogs: [], watchers: [], attachments: [],
    }
    const fetchMock = vi.fn().mockImplementation(() =>
      Promise.resolve(new Response(JSON.stringify(ticket), { status: 200, headers: { 'Content-Type': 'application/json' } })),
    )
    vi.stubGlobal('fetch', fetchMock)

    await api.addTicketComment('t1', 'Internal triage.', true)
    await api.addTicketWorklog('t1', 30, 'Investigated.')
    await api.setTicketWatching('t1', true)
    await api.setTicketWatching('t1', false)

    expect(fetchMock.mock.calls.map(([url, init]) => [url, init?.method])).toEqual([
      ['/api/tickets/t1/comments', 'POST'],
      ['/api/tickets/t1/worklogs', 'POST'],
      ['/api/tickets/t1/watch', 'POST'],
      ['/api/tickets/t1/watch', 'DELETE'],
    ])
  })

  it('uploads, downloads and deletes ticket attachments', async () => {
    const ticket = {
      id: 't1', number: 'INC-000001', type: 'Incident', title: 'Database latency', description: 'Database response time increased.',
      category: null, status: 'New', priority: 'High', impact: 'Medium', urgency: 'Medium', requestedByUserId: 'u1',
      requestedByDisplay: 'Admin', assignedToUserId: null, relatedBatchJobExecutionId: null, risk: null,
      targetEnvironment: null, plannedStartAt: null, plannedEndAt: null, requiredApprovals: 1,
      openedAt: new Date().toISOString(), acknowledgedAt: null, resolvedAt: null, closedAt: null,
      alerts: [], approvals: [], activities: [], relatedTickets: [], comments: [], worklogs: [], watchers: [], attachments: [],
    }
    const fetchMock = vi.fn()
      .mockImplementationOnce(() =>
        Promise.resolve(new Response(JSON.stringify(ticket), { status: 200, headers: { 'Content-Type': 'application/json' } })),
      )
      .mockImplementationOnce(() =>
        Promise.resolve(new Response('evidence', { status: 200, headers: { 'Content-Type': 'application/octet-stream' } })),
      )
      .mockImplementationOnce(() =>
        Promise.resolve(new Response(JSON.stringify(ticket), { status: 200, headers: { 'Content-Type': 'application/json' } })),
      )
    vi.stubGlobal('fetch', fetchMock)

    const file = new File(['evidence'], 'evidence.txt', { type: 'text/plain' })
    await api.uploadTicketAttachment('t1', file)
    const blob = await api.downloadTicketAttachment('t1', 'a1')
    await api.deleteTicketAttachment('t1', 'a1')

    expect(fetchMock.mock.calls.map(([url, init]) => [url, init?.method])).toEqual([
      ['/api/tickets/t1/attachments', 'POST'],
      ['/api/tickets/t1/attachments/a1', undefined],
      ['/api/tickets/t1/attachments/a1', 'DELETE'],
    ])
    const uploadBody = fetchMock.mock.calls[0][1]?.body
    expect(uploadBody).toBeInstanceOf(FormData)
    expect((uploadBody as FormData).get('file')).toBe(file)
    expect(blob.size).toBe(8)
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
