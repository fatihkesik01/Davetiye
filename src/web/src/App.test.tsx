import { fireEvent, render, screen } from '@testing-library/react'
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

  it('gates an existing Creator on explicit service-notice acknowledgement until the server accepts it', async () => {
    const snapshot = {
      serviceNotice: { acknowledged: true, acknowledgedAt: '2026-10-06T12:00:00Z', noticeVersion: 'service-notice-draft-v1', textStatus: 'draft-pending-phase11-review' },
      marketing: { optedIn: false, updatedAt: null, version: 'marketing-preference-v1' },
      history: [{ kind: 'serviceNoticeAcknowledgement', granted: true, source: 'existingAccountAcknowledgement', version: 'service-notice-draft-v1', recordedAt: '2026-10-06T12:00:00Z' }],
    }
    const fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url === '/api/v1/auth/session') return Promise.resolve(new Response(JSON.stringify({ authenticated: true, access: 'creator', serviceNoticeRequired: true })))
      if (url === '/api/v1/antiforgery/token') return Promise.resolve(new Response(JSON.stringify({ token: 'csrf-token' })))
      if (url === '/api/v1/account/consents/service-notice' && init?.method === 'POST') return Promise.resolve(new Response(JSON.stringify(snapshot)))
      if (url === '/api/v1/account/consents') return Promise.resolve(new Response(JSON.stringify(snapshot)))
      if (url === '/api/v1/invitations?page=1&pageSize=50') return Promise.resolve(new Response(JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0 })))
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/panel')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Hizmet bildirimi', level: 1 })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Davetiye taslakları' })).toBeNull()
    const checkbox = screen.getByLabelText(/hizmet bildirimini okudum/i) as HTMLInputElement
    expect(checkbox.getAttribute('aria-required')).toBe('true')
    expect(checkbox.required).toBe(true)
    fireEvent.submit(screen.getByRole('button', { name: 'Onayla ve panele devam et' }).closest('form')!)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(fetch).not.toHaveBeenCalledWith('/api/v1/account/consents/service-notice', expect.anything())

    fireEvent.click(checkbox)
    fireEvent.click(screen.getByRole('button', { name: 'Onayla ve panele devam et' }))
    expect(await screen.findByRole('heading', { name: 'Davetiye taslakları' })).toBeTruthy()
    expect(fetch).toHaveBeenCalledWith('/api/v1/account/consents/service-notice', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ acknowledged: true }),
      headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    }))
  })

  it('routes a first-factor-only Super Admin from the Admin shell into MFA setup', async () => {
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/v1/auth/session')) return Promise.resolve(new Response(JSON.stringify({ authenticated: true, access: 'mfa-setup-required-super-admin' })))
      if (url.includes('/api/v1/antiforgery/token')) return Promise.resolve(new Response(JSON.stringify({ token: 'csrf' })))
      if (url.includes('/api/v1/admin/mfa/enroll')) return Promise.resolve(new Response(JSON.stringify({ sharedKey: 'SETUP-KEY', authenticatorUri: 'otpauth://totp/Davetiye:admin' })))
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/admin')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Yönetici hesabı için MFA kurulumu' })).toBeTruthy()
    expect(screen.queryByRole('heading', { name: 'Platform özeti' })).toBeNull()
    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/mfa/enroll', expect.objectContaining({ method: 'POST', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf' }) }))
  })

  it('renders the settings editor from the protected Admin navigation', async () => {
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url === '/api/v1/auth/session') return Promise.resolve(new Response(JSON.stringify({ authenticated: true, access: 'mfa-complete-super-admin' })))
      if (url === '/api/v1/admin/settings') return Promise.resolve(new Response(JSON.stringify({ items: [
        { key: 'deletedInvitationRetentionDays', displayName: 'Deleted invitations', description: null, value: 3, minimum: 0, maximum: 365, revision: 1 },
        { key: 'abandonedMemoryRetentionDays', displayName: 'Abandoned memories', description: null, value: 30, minimum: 0, maximum: 365, revision: 1 },
      ] })))
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/admin/settings')
    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Sistem ayarları' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Sistem ayarları' }).getAttribute('aria-current')).toBe('page')
    expect(screen.queryByRole('heading', { name: 'Platform özeti' })).toBeNull()
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

  it('renders distinct creator and admin shells without crossing protected content', async () => {
    let access = 'creator'
    const fetchMock = vi.fn().mockImplementation((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/v1/auth/session')) {
        return Promise.resolve(new Response(JSON.stringify({ authenticated: true, access })))
      }
      if (url.includes('/api/v1/invitations')) {
        return Promise.resolve(new Response(JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0 })))
      }
      if (url.includes('/api/v1/admin/overview')) {
        return Promise.resolve(new Response(JSON.stringify({
          generatedAtUtc: '2026-10-06T10:00:00Z',
          accounts: { total: 0, individual: 0, organization: 0, banned: 0 },
          invitations: { draft: 0, scheduled: 0, active: 0, paused: 0, expired: 0, deleted: 0 },
          grants: { total: 0, free: 0, individualPurchase: 0, organizationSubscription: 0, revoked: 0 },
          plans: { total: 0, active: 0, inactive: 0 },
          payments: { pending: 0, unknown: 0, succeeded: 0, failed: 0, canceled: 0, reversed: 0 },
          storage: { assets: 0, ready: 0, pendingUpload: 0, processing: 0, pendingDeletion: 0, deleted: 0, rejected: 0, verifiedBytes: 0 },
          health: { api: 'Healthy', database: 'Healthy' },
        })))
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetchMock)
    window.history.replaceState({}, '', '/panel')
    const { unmount } = render(<App />)

    expect(await screen.findByRole('heading', { name: 'Davetiye taslakları' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Davetiyeleriniz' })).toBeTruthy()
    expect(screen.queryByText('Platform yönetimi')).toBeNull()
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/auth/session', expect.objectContaining({ credentials: 'include' }))
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/invitations?page=1&pageSize=50', expect.objectContaining({ method: 'GET' }))

    unmount()
    access = 'mfa-complete-super-admin'
    window.history.replaceState({}, '', '/admin')
    render(<App />)
    expect(await screen.findByRole('heading', { name: 'Yönetim Paneli' })).toBeTruthy()
    expect(await screen.findByRole('heading', { name: 'Platform özeti' })).toBeTruthy()
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/admin/overview', expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }))
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
