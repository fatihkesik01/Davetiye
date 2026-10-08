import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { AdminOverviewPage } from '../features/admin/AdminOverviewPage'
import { AdminOperationalPage } from '../features/admin/AdminOperationalPages'
import { AdminBannedAccountsPage } from '../features/admin/AdminBannedAccountsPage'
import { AdminTemplatesPage } from '../features/admin/AdminTemplatesPage'
import { AdminPlansPage } from '../features/admin/AdminPlansPage'
import { AdminSettingsPage } from '../features/admin/AdminSettingsPage'
import { RouteShell } from './RouteShell'

export default function AdminRoutes() {
  const { t } = useTranslation()
  const [pathname, setPathname] = useState(() => window.location.pathname)
  useEffect(() => {
    const updatePathname = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', updatePathname)
    return () => window.removeEventListener('popstate', updatePathname)
  }, [])

  const activeView = pathname === '/admin/payments' ? 'payments' : pathname === '/admin/audit' ? 'audit' : pathname === '/admin/accounts' ? 'accounts' : pathname === '/admin/templates' ? 'templates' : pathname === '/admin/plans' ? 'plans' : pathname === '/admin/settings' ? 'settings' : 'overview'
  return <RouteShell title={t('navigation.adminTitle')} zone="admin">
    {activeView === 'accounts' ? <AdminBannedAccountsPage />
      : activeView === 'plans' ? <AdminPlansPage />
      : activeView === 'templates' ? <AdminTemplatesPage />
      : activeView === 'settings' ? <AdminSettingsPage />
      : activeView === 'payments' || activeView === 'audit' ? <AdminOperationalPage kind={activeView} />
      : <AdminOverviewPage />}
  </RouteShell>
}
