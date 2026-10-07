import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { App } from '../App'

describe('Creator organization subscription route', () => {
  afterEach(() => {
    window.history.replaceState({}, '', '/')
    vi.unstubAllGlobals()
  })

  it('shows the plan navigation entry and renders the server-backed plan page at its route', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.includes('/api/v1/auth/session')) {
        return Promise.resolve(new Response(JSON.stringify({ authenticated: true, access: 'creator' })))
      }
      if (url.includes('/api/v1/payments/organization-subscription')) {
        return Promise.resolve(new Response(JSON.stringify({
          subscriptionId: 'subscription-1', status: 'Active', paidThroughAtUtc: '2026-11-06T00:00:00Z',
          cancelAtPeriodEnd: false, cancelRequestedAtUtc: null, planDisplayName: 'Organization',
          priceAmount: 2399, currency: 'TRY', billingPeriod: 'monthly',
        })))
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    }))
    window.history.replaceState({}, '', '/panel/plan-odeme')

    render(<App />)

    expect(await screen.findByRole('heading', { name: 'Organization aboneliği' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Plan ve ödeme' }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByText('2.399 TRY / ay')).toBeTruthy()
  })
})
