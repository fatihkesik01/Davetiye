import { type ReactNode, useEffect, useState } from 'react'

import { DavetiyeApiClient, type SessionAccess } from '../api/generated/client'
import { InternalLink } from '../components/ui/InternalLink'
import { ServiceNoticeAcknowledgementPage } from '../features/privacy/ServiceNoticeAcknowledgementPage'
import { navigate } from './navigation'
import { RouteShell } from './RouteShell'

type RequiredAccess = Exclude<SessionAccess, 'none'>
type GuardState = 'loading' | 'allowed' | 'service-notice-required' | 'authentication-required' | 'mfa-required' | 'not-allowed'

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
        if (controller.signal.aborted) return
        if (session.access === requiredAccess) {
          if (requiredAccess === 'creator' && session.serviceNoticeRequired === true) setState('service-notice-required')
          else setState('allowed')
        }
        else if (!session.authenticated) setState('authentication-required')
        else if (session.access === 'mfa-setup-required-super-admin' && requiredAccess !== 'mfa-setup-required-super-admin') {
          navigate('/admin/mfa/setup')
        }
        else setState(requiredAccess === 'mfa-complete-super-admin' ? 'mfa-required' : 'not-allowed')
      })
      .catch(() => {
        if (!controller.signal.aborted) setState('authentication-required')
      })

    return () => controller.abort()
  }, [requiredAccess])

  if (state === 'allowed') return <>{children}</>
  if (state === 'service-notice-required') return <RouteShell title="Hizmet bildirimi" zone="creator">
    <ServiceNoticeAcknowledgementPage onAcknowledged={() => setState('allowed')} />
  </RouteShell>

  const title = state === 'mfa-required' ? 'Ek doğrulama gerekli' : 'Oturum gerekli'
  const message = state === 'loading'
    ? 'Oturum doğrulanıyor.'
    : state === 'mfa-required'
      ? 'Yönetim alanına erişmek için çok adımlı doğrulamayı tamamlayın.'
      : state === 'not-allowed'
        ? 'Bu alana erişim izniniz yok.'
        : 'Bu alana erişmek için güvenli bir oturum açın.'

  const returnPath = `${window.location.pathname}${window.location.search}`
  const loginPath = `/giris?returnUrl=${encodeURIComponent(returnPath)}`

  return (
    <RouteShell title={title} zone="public">
      <section className="route-placeholder" aria-live="polite">
        <p>{message}</p>
        {state === 'authentication-required' ? <InternalLink to={loginPath}>Giriş yap</InternalLink> : null}
      </section>
    </RouteShell>
  )
}
