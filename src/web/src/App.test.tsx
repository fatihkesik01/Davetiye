import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { App } from './App'

describe('App', () => {
  it.each([
    ['creator', '/admin', 'Ek doğrulama gerekli'],
    ['none', '/admin', 'Ek doğrulama gerekli'],
  ])('withholds admin content for authenticated %s access', async (access, path, expectedTitle) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      authenticated: true,
      access,
    }))))
    window.history.replaceState({}, '', path)
    render(<App />)

    expect(await screen.findByRole('heading', { name: expectedTitle })).toBeTruthy()
    expect(screen.queryByText('Yönetim Paneli')).toBeNull()
  })

  it('withholds protected content when the access probe fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network unavailable')))
    window.history.replaceState({}, '', '/panel')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Oturum gerekli' })).toBeTruthy()
    expect(screen.queryByText('Creator Paneli')).toBeNull()
  })

  afterEach(() => {
    window.history.replaceState({}, '', '/')
    vi.unstubAllGlobals()
  })

  it('renders a separate public shell without management navigation', async () => {
    window.history.replaceState({}, '', '/sablonlar')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Şablonlar' })).toBeTruthy()
    expect(screen.getByText('Ana içeriğe geç').getAttribute('href')).toBe('#main-content')
    expect(screen.queryByText('Creator Paneli')).toBeNull()
  })

  it('renders distinct creator and admin placeholders without private data', async () => {
    let access = 'creator'
    vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      authenticated: true,
      access,
    })))))
    window.history.replaceState({}, '', '/panel')
    const { unmount } = render(<App />)

    expect(await screen.findByRole('heading', { name: 'Creator Paneli' })).toBeTruthy()
    expect(screen.getByText(/özel içerik gösterilmez/i)).toBeTruthy()

    unmount()
    access = 'mfa-complete-super-admin'
    window.history.replaceState({}, '', '/admin')
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Yönetim Paneli' })).toBeTruthy()
    expect(screen.getByText(/MFA-tamamlanmış Super Admin/i)).toBeTruthy()
  })

  it('does not render a protected shell before anonymous access is denied', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      authenticated: false,
      access: 'none',
    }))))
    window.history.replaceState({}, '', '/panel/davetiyeler')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Oturum gerekli' })).toBeTruthy()
    expect(screen.queryByText('Creator Paneli')).toBeNull()
    expect(screen.queryByText(/özel içerik gösterilmez/i)).toBeNull()
  })
})
