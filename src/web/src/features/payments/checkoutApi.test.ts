import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError, type DavetiyeApiClient } from '../../api/generated/client'
import { createInvitationCheckout, listIndividualPurchasePlans } from './checkoutApi'

describe('payment checkout API adapter', () => {
  it('uses the generated client for display-safe Standard/Premium catalog data', async () => {
    const api = { listPaymentPlans: vi.fn().mockResolvedValue([
      { key: 'standard', displayName: 'Standard', amount: 699, currency: 'TRY', billingPeriod: 'one-time' },
      { key: 'organization', displayName: 'Organization', amount: 2499, currency: 'TRY', billingPeriod: 'monthly' },
    ]) } as unknown as DavetiyeApiClient
    const signal = new AbortController().signal

    await expect(listIndividualPurchasePlans(api, signal)).resolves.toEqual([
      { key: 'standard', displayName: 'Standard', amount: 699, currency: 'TRY', billingPeriod: 'one-time' },
    ])
    expect(api.listPaymentPlans).toHaveBeenCalledWith(signal)
  })

  it('rejects malformed catalog values rather than displaying client-invented terms', async () => {
    const api = { listPaymentPlans: vi.fn().mockResolvedValue([
      { key: 'premium', displayName: 'Premium', amount: 1199, currency: 'USD', billingPeriod: 'one-time' },
    ]) } as unknown as DavetiyeApiClient
    await expect(listIndividualPurchasePlans(api)).resolves.toEqual([])
  })

  it('delegates checkout with the selected plan, same-attempt key and antiforgery token', async () => {
    const checkout = { attemptId: 'attempt-1', reference: 'ref-1', status: 'Pending', planKey: 'standard', amount: 699, currency: 'TRY', billingPeriod: 'one-time', checkoutUrl: 'https://pay.example.test/start' }
    const api = { startInvitationCheckout: vi.fn().mockResolvedValue(checkout) } as unknown as DavetiyeApiClient
    const signal = new AbortController().signal

    await expect(createInvitationCheckout(api, 'invitation/1', 'standard', 'idempotency-1', 'csrf', signal)).resolves.toEqual(checkout)
    expect(api.startInvitationCheckout).toHaveBeenCalledWith('invitation/1', 'standard', 'idempotency-1', 'csrf', signal)
  })

  it('preserves generated-client 409/503 failures for safe UI handling', async () => {
    const api = { startInvitationCheckout: vi.fn()
      .mockRejectedValueOnce(new ApiRequestError(409, { title: 'Conflict' }))
      .mockRejectedValueOnce(new ApiRequestError(503, { title: 'Unavailable' })) } as unknown as DavetiyeApiClient

    await expect(createInvitationCheckout(api, 'invitation-1', 'standard', 'key', 'csrf'))
      .rejects.toMatchObject({ status: 409, message: 'Conflict' })
    await expect(createInvitationCheckout(api, 'invitation-1', 'standard', 'key', 'csrf'))
      .rejects.toMatchObject({ status: 503, message: 'Unavailable' })
  })
})
