import { lazy, Suspense, useEffect, useState } from 'react'

import { LoadingState } from './components/feedback/LoadingState'
import { NotFoundPage } from './components/feedback/NotFoundPage'
import { matchRoute } from './routes/route'
import { ProtectedRoute } from './routes/ProtectedRoute'

const PublicRoutes = lazy(() => import('./routes/PublicRoutes'))
const CreatorRoutes = lazy(() => import('./routes/CreatorRoutes'))
const AdminRoutes = lazy(() => import('./routes/AdminRoutes'))

function usePathname(): string {
  const [pathname, setPathname] = useState(() => window.location.pathname)

  useEffect(() => {
    const updatePathname = () => setPathname(window.location.pathname)
    window.addEventListener('popstate', updatePathname)
    return () => window.removeEventListener('popstate', updatePathname)
  }, [])

  return pathname
}

export function App() {
  const route = matchRoute(usePathname())

  useEffect(() => {
    document.title = `${route.title} | Davetiye`
  }, [route.title])

  if (route.zone === 'not-found') {
    return <NotFoundPage />
  }

  return (
    <Suspense fallback={<LoadingState />}>
      {route.zone === 'public' ? <PublicRoutes /> : null}
      {route.zone === 'creator' ? <ProtectedRoute key="creator" requiredAccess="creator"><CreatorRoutes /></ProtectedRoute> : null}
      {route.zone === 'admin' ? <ProtectedRoute key="admin" requiredAccess="mfa-complete-super-admin"><AdminRoutes /></ProtectedRoute> : null}
    </Suspense>
  )
}
