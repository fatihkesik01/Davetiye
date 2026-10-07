import { fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AdminOperationalPage } from './AdminOperationalPages'

describe('AdminOperationalPage pagination navigation', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('starts the other operational view at page one after changing tabs', async () => {
    const firstPayment = {
      id: 'p1', reference: 'dv-p1', status: 'Pending', planKey: 'standard', amount: 100, currency: 'TRY',
      createdAtUtc: '2026-10-06T10:00:00Z', updatedAtUtc: '2026-10-06T10:00:00Z', reversalKind: null, reversedAtUtc: null,
    }
    const paymentPageOne = { items: [firstPayment], page: 1, pageSize: 50, totalCount: 100 }
    const paymentPageTwo = { ...paymentPageOne, items: [{ ...firstPayment, id: 'p2', reference: 'dv-p2' }], page: 2 }
    const auditPageOne = {
      items: [{ id: 'a1', actorId: 'actor-1', subjectId: 'subject-1', occurredAtUtc: '2026-10-06T10:00:00Z', eventType: 'AccountBanned' }],
      page: 1, pageSize: 50, totalCount: 1,
    }
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(paymentPageOne), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(paymentPageTwo), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(auditPageOne), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    const { rerender } = render(<AdminOperationalPage kind="payments" />)
    expect(await screen.findByText('dv-p1')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Sonraki' }))
    expect(await screen.findByText('dv-p2')).toBeTruthy()
    rerender(<AdminOperationalPage kind="audit" />)

    expect(await screen.findByText('AccountBanned')).toBeTruthy()
    expect(fetchMock).toHaveBeenLastCalledWith('/api/v1/admin/audit?page=1&pageSize=50', expect.objectContaining({ method: 'GET', cache: 'no-store' }))
    expect(screen.getByText('Sayfa 1 / 1')).toBeTruthy()
  })
})
