import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AdminOperationalPage } from './AdminOperationalPages'

describe('AdminOperationalPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('requests paginated payments with no-store and renders only the approved operational fields', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [{
        id: 'payment-id', reference: 'REF-108', status: 'Succeeded', planKey: 'premium', amount: 1199,
        currency: 'TRY', createdAtUtc: '2026-10-01T10:00:00Z', updatedAtUtc: '2026-10-02T11:00:00Z',
        reversalKind: 'FullRefund', reversedAtUtc: '2026-10-03T12:00:00Z',
        accountId: 'must-not-render-account', invitationId: 'must-not-render-invitation', providerPayload: 'secret-provider-payload',
      }], page: 1, pageSize: 50, totalCount: 1,
    }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    render(<AdminOperationalPage kind="payments" />)

    expect(await screen.findByText('REF-108')).toBeTruthy()
    expect(screen.getByText('Başarılı')).toBeTruthy()
    expect(screen.getByText('Premium')).toBeTruthy()
    expect(screen.getByText(/₺1\.199,00/)).toBeTruthy()
    expect(screen.getByText(/Tam iade/)).toBeTruthy()
    expect(screen.queryByText('payment-id')).toBeNull()
    expect(screen.queryByText('must-not-render-account')).toBeNull()
    expect(screen.queryByText('must-not-render-invitation')).toBeNull()
    expect(screen.queryByText('secret-provider-payload')).toBeNull()
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/admin/payments?page=1&pageSize=50', expect.objectContaining({
      method: 'GET', credentials: 'include', cache: 'no-store',
    }))
  })

  it('paginates the minimized audit projection and states its retention behavior', async () => {
    const first = {
      items: [{ id: 'audit-1', actorId: 'actor-1', subjectId: 'subject-1', occurredAtUtc: '2026-10-06T10:00:00Z', eventType: 'AccountBanned', payload: 'private-data' }],
      page: 1, pageSize: 50, totalCount: 51,
    }
    const second = { ...first, items: [{ ...first.items[0], id: 'audit-2', eventType: 'AccountUnbanned' }], page: 2 }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(first), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(second), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    render(<AdminOperationalPage kind="audit" />)

    expect(await screen.findByText('AccountBanned')).toBeTruthy()
    expect(screen.getByText(/otomatik silinmez/)).toBeTruthy()
    expect(screen.queryByText('private-data')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Sonraki' }))
    expect(await screen.findByText('AccountUnbanned')).toBeTruthy()
    expect(fetchMock).toHaveBeenLastCalledWith('/api/v1/admin/audit?page=2&pageSize=50', expect.objectContaining({ method: 'GET', cache: 'no-store' }))
    expect(screen.getByText('Sayfa 2 / 2')).toBeTruthy()
  })

  it('shows a localized empty state and a safe authorization error', async () => {
    vi.stubGlobal('fetch', vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0 }), { status: 200 }))
      .mockResolvedValueOnce(new Response('', { status: 403 })))

    const { rerender } = render(<AdminOperationalPage kind="payments" />)
    expect(await screen.findByText('Henüz kayıt yok')).toBeTruthy()

    rerender(<AdminOperationalPage kind="audit" />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.getByText(/MFA doğrulaması tamamlanmış yönetici oturumu/i)).toBeTruthy()
  })
})
