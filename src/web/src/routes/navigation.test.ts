import { describe, expect, it, vi } from 'vitest'

import { consumeAccountDeletionToken, consumeSensitiveLinkParameters, navigate, resolveSafeReturnPath } from './navigation'

describe('safe application navigation', () => {
  it.each([
    ['https://evil.example/panel'],
    ['//evil.example/panel'],
    ['/\\evil.example/panel'],
    ['/panel\u0000evil'],
    ['panel'],
    [''],
  ])('rejects a non-local return target: %s', (candidate) => {
    expect(resolveSafeReturnPath(candidate, '/panel')).toBe('/panel')
  })

  it('preserves an application-local path, query and fragment', () => {
    expect(resolveSafeReturnPath('/panel/davetiyeler?sekme=taslak#ilk', '/panel')).toBe('/panel/davetiyeler?sekme=taslak#ilk')
  })

  it('updates history and notifies the router without a full page load', () => {
    const listener = vi.fn()
    window.addEventListener('popstate', listener)

    navigate('/giris/kayit')

    expect(window.location.pathname).toBe('/giris/kayit')
    expect(listener).toHaveBeenCalledOnce()
    window.removeEventListener('popstate', listener)
  })

  it('consumes fragment auth secrets without sending them in the request URL and removes them from browser history', () => {
    window.history.replaceState({}, '', '/giris/e-posta-dogrula?lang=tr#userId=user-1&token=secret')

    expect(consumeSensitiveLinkParameters()).toEqual({ userId: 'user-1', token: 'secret' })
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`)
      .toBe('/giris/e-posta-dogrula?lang=tr')
  })

  it('discards legacy query auth credentials and scrubs them from browser history', () => {
    window.history.replaceState({}, '', '/auth/reset-password?userId=user-1&token=secret&lang=tr')

    expect(consumeSensitiveLinkParameters()).toEqual({ userId: '', token: '' })
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`)
      .toBe('/auth/reset-password?lang=tr')
  })

  it('scrubs auth secrets from both URL components even when a link is malformed or mixed', () => {
    window.history.replaceState({}, '', '/auth/reset-password?token=query-secret&lang=tr#userId=user-1&token=fragment-secret&step=form')

    expect(consumeSensitiveLinkParameters()).toEqual({ userId: 'user-1', token: 'fragment-secret' })
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`)
      .toBe('/auth/reset-password?lang=tr#step=form')
  })

  it('consumes account deletion token only from the fragment and immediately scrubs the visible location', () => {
    window.history.replaceState({}, '', '/hesap-silme/onayla?lang=tr#token=opaque-secret&source=email')

    expect(consumeAccountDeletionToken()).toEqual({ token: 'opaque-secret', validLocation: true })
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`)
      .toBe('/hesap-silme/onayla?lang=tr#source=email')
  })

  it('scrubs and rejects account deletion tokens placed in the query string', () => {
    window.history.replaceState({}, '', '/hesap-silme/onayla?token=must-not-use&lang=tr')

    expect(consumeAccountDeletionToken()).toEqual({ token: '', validLocation: false })
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`)
      .toBe('/hesap-silme/onayla?lang=tr')
  })
})
