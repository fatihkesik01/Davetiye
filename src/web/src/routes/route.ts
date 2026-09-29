export type RouteZone = 'public' | 'creator' | 'admin' | 'not-found'

export interface RouteMatch {
  zone: RouteZone
  title: string
}

export function matchRoute(pathname: string): RouteMatch {
  if (pathname === '/sablonlar' || /^\/davetiye\/[^/]+-[^/]+$/.test(pathname) || pathname.startsWith('/giris')) {
    return { zone: 'public', title: 'Davetiye' }
  }

  if (pathname === '/panel' || pathname.startsWith('/panel/')) {
    return { zone: 'creator', title: 'Creator Paneli' }
  }

  if (pathname === '/admin' || pathname.startsWith('/admin/')) {
    return { zone: 'admin', title: 'Yönetim Paneli' }
  }

  return { zone: 'not-found', title: 'Sayfa bulunamadı' }
}
