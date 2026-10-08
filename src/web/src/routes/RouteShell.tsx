import { type ReactNode, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { SiteHeader } from '../components/layout/SiteHeader'

interface RouteShellProps { children: ReactNode; title: string; zone: 'public' | 'creator' | 'admin' }

export function RouteShell({ children, title, zone }: RouteShellProps) {
  const headingReference = useRef<HTMLHeadingElement>(null)
  const { t } = useTranslation()

  useEffect(() => {
    document.title = `${title} | Kutlio`
    headingReference.current?.focus()
  }, [title])

  return <div className={`route-shell route-shell--${zone}`}>
    <a className="skip-link" href="#main-content">{t('common.skipToContent')}</a>
    <SiteHeader zone={zone} />
    <main id="main-content" className="shell-main" tabIndex={-1}>
      <h1 ref={headingReference} tabIndex={-1}>{title}</h1>
      {children}
    </main>
  </div>
}
