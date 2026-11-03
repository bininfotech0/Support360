import { createContext, useContext } from 'react'
import type { Session } from '../api/session'

export interface AuthContextValue {
  session: Session | null
  user: Session['user'] | null
  login: (email: string, password: string) => Promise<void>
  logout: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used inside AuthProvider.')
  }
  return context
}