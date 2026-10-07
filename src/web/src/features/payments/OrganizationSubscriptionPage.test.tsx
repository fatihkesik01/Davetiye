import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError, type DavetiyeApiClient, type OrganizationSubscriptionSnapshot } from '../../api/generated/client'
import { OrganizationSubscriptionPage } from './OrganizationSubscriptionPage'

const active: OrganizationSubscriptionSnapshot = {
  subscriptionId: 'subscription-1',
  status: 'Active',
  paidThroughAtUtc: '2026-11-06T00:00:00Z',
  cancelAtPeriodEnd: false,
  cancelRequestedAtUtc: null,
  planDisplayName: 'Organization',
  priceAmount: 2399,
  currency: 'TRY',
  billingPeriod: 'monthly',
}

function createApi(overrides: Partial<DavetiyeApiClient> = {}) {
  return {
    getOrganizationSubscription: vi.fn().mockResolvedValue(active),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf-token'),
    cancelOrganizationSubscription: vi.fn().mockResolvedValue({ outcome: 'Applied', paidThroughAtUtc: active.paidThroughAtUtc }),
    ...overrides,
  } as unknown as DavetiyeApiClient
}

describe('OrganizationSubscriptionPage', () => {
  it('shows server plan and renewal data, then confirms cancellation and refreshes the snapshot', async () => {
    const canceled = { ...active, status: 'Canceled' as const, cancelAtPeriodEnd: true, cancelRequestedAtUtc: '2026-10-06T10:00:00Z' }
    const api = createApi({
      getOrganizationSubscription: vi.fn().mockResolvedValueOnce(active).mockResolvedValueOnce(canceled),
    })
    render(<OrganizationSubscriptionPage api={api} />)

    expect(await screen.findByText('2.399 TRY / ay')).toBeTruthy()
    expect(screen.getByText(/6 Kasım 2026/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Aboneliği iptal et' }))

    expect(screen.getByRole('heading', { name: 'Abonelik iptalini onayla' })).toBeTruthy()
    expect(screen.getByText(/Bir sonraki aylık yenileme durur/)).toBeTruthy()
    expect(screen.getByText(/davetiye ve içerik verileriniz korunur/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'İptali onayla' }))

    await waitFor(() => expect(api.cancelOrganizationSubscription).toHaveBeenCalledWith('subscription-1', 'csrf-token'))
    expect(api.getOrganizationSubscription).toHaveBeenCalledTimes(2)
    expect(await screen.findByText(/Yenileme kapalı\. Erişim 6 Kasım 2026.*tarihine kadar sürer/)).toBeTruthy()
    expect(screen.getByRole('status').textContent).toContain('İptal onayı e-posta adresinize gönderilecek')
    expect(screen.queryByRole('button', { name: 'Aboneliği iptal et' })).toBeNull()
  })

  it('shows an explanatory empty state without inventing a checkout or reactivation action', async () => {
    const api = createApi({ getOrganizationSubscription: vi.fn().mockResolvedValue(null) })
    render(<OrganizationSubscriptionPage api={api} />)

    expect(await screen.findByRole('heading', { name: 'Abonelik bilgisi bulunmuyor' })).toBeTruthy()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('renders expired state only from the server snapshot and keeps it read-only', async () => {
    const api = createApi({ getOrganizationSubscription: vi.fn().mockResolvedValue({ ...active, status: 'Expired' }) })
    render(<OrganizationSubscriptionPage api={api} />)

    expect(await screen.findByText('Süresi doldu')).toBeTruthy()
    expect(screen.getByText(/Abonelik süresi sona erdi/)).toBeTruthy()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('announces a failed cancellation and leaves the subscription snapshot unchanged', async () => {
    const api = createApi({ cancelOrganizationSubscription: vi.fn().mockRejectedValue(new Error('network')) })
    render(<OrganizationSubscriptionPage api={api} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Aboneliği iptal et' }))
    fireEvent.click(screen.getByRole('button', { name: 'İptali onayla' }))

    expect((await screen.findByRole('alert')).textContent).toContain('İptal isteği tamamlanamadı')
    expect(screen.getByText('Aktif')).toBeTruthy()
  })

  it('restores focus when cancellation is dismissed and refreshes the snapshot after a state conflict', async () => {
    const canceled = { ...active, status: 'Canceled' as const, cancelAtPeriodEnd: true }
    const api = createApi({
      getOrganizationSubscription: vi.fn().mockResolvedValueOnce(active).mockResolvedValueOnce(canceled),
      cancelOrganizationSubscription: vi.fn().mockRejectedValue(new ApiRequestError(409, { title: 'Conflict' })),
    })
    render(<OrganizationSubscriptionPage api={api} />)

    const cancelButton = await screen.findByRole('button', { name: 'Aboneliği iptal et' })
    fireEvent.click(cancelButton)
    fireEvent.click(screen.getByRole('button', { name: 'Vazgeç' }))
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Aboneliği iptal et' })))

    fireEvent.click(screen.getByRole('button', { name: 'Aboneliği iptal et' }))
    fireEvent.click(screen.getByRole('button', { name: 'İptali onayla' }))
    expect(await screen.findByText('Yenileme kapalı')).toBeTruthy()
    expect(screen.getByRole('alert').textContent).toContain('güncel abonelik durumu yenilendi')
    expect(api.getOrganizationSubscription).toHaveBeenCalledTimes(2)
  })
})
