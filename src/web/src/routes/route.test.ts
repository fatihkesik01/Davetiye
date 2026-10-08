import { describe, expect, it } from 'vitest'

import { matchRoute } from './route'

describe('auth routes', () => {
  it.each([
    '/giris',
    '/giris/kayit',
    '/giris/e-posta-dogrula',
    '/giris/sifremi-unuttum',
    '/giris/sifre-sifirla',
    '/giris/google-baglanti',
    '/auth/confirm-email',
    '/auth/reset-password',
    '/hesap-silme/onayla',
    '/gizlilik',
    '/kullanim-kosullari',
  ])('maps %s to the public shell', (pathname) => {
    expect(matchRoute(pathname).zone).toBe('public')
  })

  it('maps the landing page / to the public shell', () => {
    expect(matchRoute('/')).toEqual({ zone: 'public', title: 'Dijital davetiye' })
  })

  it('does not accept arbitrary paths under the login prefix', () => {
    expect(matchRoute('/giris/bilinmeyen').zone).toBe('not-found')
  })
})
