import type { LoginResponse } from './types'

const STORAGE_KEY = 'support360.session'

export interface Session {
  token: string
  expiresAt: string
  user: LoginResponse['user']
}

type Listener = () => void

const listeners = new Set<Listener>()
let current = readStoredSession()

function readStoredSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Session
    if (!parsed.token || !parsed.user || !parsed.expiresAt) return null
    if (new Date(parsed.expiresAt).getTime() <= Date.now()) {
      sessionStorage.removeItem(STORAGE_KEY)
      return null
    }
    return parsed
  } catch {
    return null
  }
}

function emit() {
  listeners.forEach((listener) => listener())
}

export function getSession(): Session | null {
  if (current && new Date(current.expiresAt).getTime() <= Date.now()) {
    current = null
    sessionStorage.removeItem(STORAGE_KEY)
  }
  return current
}

export function setSession(response: LoginResponse): void {
  current = {
    token: response.accessToken,
    expiresAt: response.expiresAt,
    user: response.user,
  }
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(current))
  emit()
}

export function clearSession(): void {
  current = null
  sessionStorage.removeItem(STORAGE_KEY)
  emit()
}

export function subscribeSession(listener: Listener): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export function canManage(user: Session['user'] | null): boolean {
  return user?.role === 'TenantAdmin' || user?.role === 'PlatformAdmin'
}

export function canOperate(user: Session['user'] | null): boolean {
  return user?.role === 'TenantAdmin' || user?.role === 'Operator' || user?.role === 'PlatformAdmin'
}

export function canApprove(user: Session['user'] | null): boolean {
  return user?.role === 'Approver' || user?.role === 'TenantAdmin' || user?.role === 'PlatformAdmin'
}
