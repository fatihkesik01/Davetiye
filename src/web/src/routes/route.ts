export type RouteZone = 'public' | 'creator' | 'admin' | 'not-found'

export interface RouteMatch {
  zone: RouteZone
  title: string
}

export function matchRoute(pathname: string): RouteMatch {
  if (pathname.startsWith('/davetiye/')) return { zone: 'public', title: 'Dijital davetiye' }
  if (import.meta.env.DEV && /^\/__visual\/templates\/[^/]+$/.test(pathname)) {
    return { zone: 'public', title: 'Şablon görsel doğrulaması' }
  }

  if (
    pathname === '/sablonlar' ||
    /^\/sablonlar\/[^/]+$/.test(pathname) ||
    pathname === '/giris' ||
    pathname === '/giris/kayit' ||
    pathname === '/giris/e-posta-dogrula' ||
    pathname === '/giris/sifremi-unuttum' ||
    pathname === '/giris/sifre-sifirla' ||
    pathname === '/giris/google-baglanti' ||
    pathname === '/auth/confirm-email' ||
    pathname === '/auth/reset-password' ||
    pathname === '/hesap-silme/onayla' ||
    pathname === '/gizlilik' ||
    pathname === '/kullanim-kosullari'
  ) {
    return { zone: 'public', title: 'Kutlio' }
  }

  if (pathname === '/panel' || pathname.startsWith('/panel/')) {
    return { zone: 'creator', title: 'Creator Paneli' }
  }

  if (pathname === '/admin' || pathname.startsWith('/admin/')) {
    return { zone: 'admin', title: 'Yönetim Paneli' }
  }

  return { zone: 'not-found', title: 'Sayfa bulunamadı' }
}
