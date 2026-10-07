import { render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AdminOverviewPage } from './AdminOverviewPage'

const overview = {
  generatedAtUtc: '2026-10-06T10:00:00Z',
  accounts: { total: 81, individual: 72, organization: 9, banned: 1 },
  invitations: { draft: 20, scheduled: 3, active: 31, paused: 2, expired: 18, deleted: 7 },
  grants: { total: 52, free: 22, individualPurchase: 12, organizationSubscription: 18, revoked: 1 },
  plans: { total: 4, active: 3, inactive: 1 },
  payments: { pending: 2, unknown: 0, succeeded: 21, failed: 3, canceled: 1, reversed: 1 },
  storage: { assets: 140, ready: 92, pendingUpload: 4, processing: 2, pendingDeletion: 1, deleted: 38, rejected: 3, verifiedBytes: 7340032 },
  health: { api: 'Healthy', database: 'Healthy' },
}

describe('AdminOverviewPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows aggregate-only platform metrics and never requests private content', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(overview), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    render(<AdminOverviewPage />)

    expect(await screen.findByText('81')).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Sistem durumu' })).toBeTruthy()
    expect(screen.getByText('7 MB')).toBeTruthy()
    expect(screen.getByText(/yalnızca toplu operasyon göstergeleri/i)).toBeTruthy()
    expect(screen.getByText(/veritabanında doğrulanmış medya baytlarını.*Cloudflare kullanım veya faturalama miktarı değildir/i)).toBeTruthy()
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/admin/overview', expect.objectContaining({
      method: 'GET', credentials: 'include', cache: 'no-store',
    }))
    const requestedPaths = fetchMock.mock.calls.map(([input]) => String(input))
    expect(requestedPaths.every(path => path === '/api/v1/admin/overview')).toBe(true)
    expect(screen.queryByText(/RSVP|misafir|hediye rezervasyonu/i)).toBeNull()
  })

  it('shows a safe message when the server denies or cannot load the overview', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 403 })))
    render(<AdminOverviewPage />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByText(/MFA doğrulaması tamamlanmış yönetici oturumu/i)).toBeTruthy()
    await waitFor(() => expect(screen.queryByText('Platform özeti yükleniyor…')).toBeNull())
  })
})
