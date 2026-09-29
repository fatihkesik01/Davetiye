import { type ReactNode, useEffect, useState } from 'react'

import { DavetiyeApiClient, type SessionAccess } from '../api/generated/client'
import { RouteShell } from './RouteShell'

type RequiredAccess = Exclude<SessionAccess, 'none'>
type GuardState = 'loading' | 'allowed' | 'authentication-required' | 'mfa-required' | 'not-allowed'

interface ProtectedRouteProps {
  children: ReactNode
  requiredAccess: RequiredAccess
}

export function ProtectedRoute({ children, requiredAccess }: ProtectedRouteProps) {
  const [state, setState] = useState<GuardState>('loading')

  useEffect(() => {
    const controller = new AbortController()

    const apiClient = new DavetiyeApiClient()
    void apiClient.getSessionAccess(controller.signal)
      .then(session => {
        if (session.access === requiredAccess) return setState('allowed')
        if (!session.authenticated) return setState('authentication-required')
        return setState(requiredAccess === 'mfa-complete-super-admin' ? 'mfa-required' : 'not-allowed')
      })
      .catch(() => setState('authentication-required'))

    return () => controller.abort()
  }, [requiredAccess])

  if (state === 'allowed') return <>{children}</>

  const title = state === 'mfa-required' ? 'Ek doğrulama gerekli' : 'Oturum gerekli'
  const message = state === 'loading'
    ? 'Oturum doğrulanıyor.'
    : state === 'mfa-required'
      ? 'Yönetim alanına erişmek için çok adımlı doğrulamayı tamamlayın.'
      : state === 'not-allowed'
        ? 'Bu alana erişim izniniz yok.'
        : 'Bu alana erişmek için güvenli bir oturum açın.'

  return <RouteShell title={title} zone="public"><section className="route-placeholder" aria-live="polite"><p>{message}</p></section></RouteShell>
}
