import { useCallback, useEffect, useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { api } from '../api/client'
import { clearSession, getSession, setSession, subscribeSession } from '../api/session'
import { AuthContext, type AuthContextValue } from './authContext'

export function AuthProvider({ children }: { children: ReactNode }) {
  const session = useSyncExternalStore(subscribeSession, getSession, () => null)
  const token = session?.token
  const expiresAt = session?.expiresAt

  useEffect(() => {
    const onUnauthorized = () => clearSession()
    window.addEventListener('support360:unauthorized', onUnauthorized)
    return () => window.removeEventListener('support360:unauthorized', onUnauthorized)
  }, [])

  useEffect(() => {
    if (!token || !expiresAt) return
    let cancelled = false
    api
      .me()
      .then((response) => {
        if (!cancelled) {
          setSession({ accessToken: token, expiresAt, user: response.user })
        }
      })
      .catch(() => {
        // A failed refresh is handled by the API client for 401 responses.
      })
    return () => {
      cancelled = true
    }
  }, [token, expiresAt])

  const login = useCallback(async (email: string, password: string) => {
    const response = await api.login(email, password)
    setSession(response)
  }, [])

  const logout = useCallback(() => clearSession(), [])

  const value = useMemo<AuthContextValue>(
    () => ({ session, user: session?.user ?? null, login, logout }),
    [session, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}