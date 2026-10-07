import { describe, expect, it, vi } from 'vitest'

import { DavetiyeApiClient } from '../../api/generated/client'

describe('Organization subscription API client', () => {
  it('gets the nullable authenticated snapshot without caching', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('null', { status: 200 }))
    const api = new DavetiyeApiClient({ fetch })

    await expect(api.getOrganizationSubscription()).resolves.toBeNull()
    expect(fetch).toHaveBeenCalledWith('/api/v1/payments/organization-subscription', expect.objectContaining({
      method: 'GET', credentials: 'include', cache: 'no-store',
    }))
  })

  it('posts an owner-scoped cancellation command with antiforgery and no client-supplied lifecycle values', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ outcome: 'Applied', paidThroughAtUtc: '2026-11-06T00:00:00Z' }), { status: 200 }))
    const api = new DavetiyeApiClient({ fetch })

    await expect(api.cancelOrganizationSubscription('sub/id', 'csrf')).resolves.toEqual({
      outcome: 'Applied', paidThroughAtUtc: '2026-11-06T00:00:00Z',
    })
    expect(fetch).toHaveBeenCalledWith('/api/v1/payments/organization-subscription/sub%2Fid/cancel', expect.objectContaining({
      method: 'POST', credentials: 'include', cache: 'no-store', body: undefined,
      headers: { 'X-CSRF-TOKEN': 'csrf' },
    }))
  })
})
