import { useEffect, useState } from 'react'
import { AdminOverviewPage } from '../features/admin/AdminOverviewPage'
import { AdminOperationalPage } from '../features/admin/AdminOperationalPages'
import { AdminBannedAccountsPage } from '../features/admin/AdminBannedAccountsPage'
import { AdminTemplatesPage } from '../features/admin/AdminTemplatesPage'
import { AdminPlansPage } from '../features/admin/AdminPlansPage'
import { AdminSettingsPage } from '../features/admin/AdminSettingsPage'
import { InternalLink } from '../components/ui/InternalLink'
import { RouteShell } from './RouteShell'

export default function AdminRoutes() {
  const [pathname, setPathname] = useState(() => window.location.pathname)

  useEffect(() => {
    const updatePathname = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', updatePathname)
    return () => window.removeEventListener('popstate', updatePathname)
  }, [])

  const activeView = pathname === '/admin/payments' ? 'payments' : pathname === '/admin/audit' ? 'audit' : pathname === '/admin/accounts' ? 'accounts' : pathname === '/admin/templates' ? 'templates' : pathname === '/admin/plans' ? 'plans' : pathname === '/admin/settings' ? 'settings' : 'overview'

  return (
    <RouteShell title="Yönetim Paneli" zone="admin">
      <nav className="admin-nav" aria-label="Yönetim bölümleri">
        <InternalLink className="admin-nav__link" aria-current={activeView === 'overview' ? 'page' : undefined} to="/admin">Platform özeti</InternalLink>
        <InternalLink className="admin-nav__link" aria-current={activeView === 'accounts' ? 'page' : undefined} to="/admin/accounts">Banlı hesaplar</InternalLink>
        <InternalLink className="admin-nav__link" aria-current={activeView === 'plans' ? 'page' : undefined} to="/admin/plans">Planlar ve haklar</InternalLink>
        <InternalLink className="admin-nav__link" aria-current={activeView === 'settings' ? 'page' : undefined} to="/admin/settings">Sistem ayarları</InternalLink>
        <InternalLink className="admin-nav__link" aria-current={activeView === 'templates' ? 'page' : undefined} to="/admin/templates">Şablonlar</InternalLink>
        <InternalLink className="admin-nav__link" aria-current={activeView === 'payments' ? 'page' : undefined} to="/admin/payments">Ödemeler</InternalLink>
        <InternalLink className="admin-nav__link" aria-current={activeView === 'audit' ? 'page' : undefined} to="/admin/audit">Denetim kayıtları</InternalLink>
      </nav>
      {activeView === 'accounts'
        ? <AdminBannedAccountsPage />
        : activeView === 'plans'
        ? <AdminPlansPage />
        : activeView === 'templates'
        ? <AdminTemplatesPage />
        : activeView === 'settings'
        ? <AdminSettingsPage />
        : activeView === 'payments' || activeView === 'audit'
        ? <AdminOperationalPage kind={activeView} />
        : <AdminOverviewPage />}
    </RouteShell>
  )
}
