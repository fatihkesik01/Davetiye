import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AccountConsentSettingsPage } from './AccountConsentSettingsPage'

function okJson(body: object): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}

const initialSnapshot = {
  serviceNotice: {
    acknowledged: true,
    acknowledgedAt: '2026-10-06T09:00:00Z',
    noticeVersion: 'service-notice-draft-v1',
    textStatus: 'draft-pending-phase11-review',
  },
  marketing: { optedIn: false, updatedAt: null, version: 'marketing-preference-v1' },
  history: [],
}

describe('account consent preferences', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('loads marketing off by default and saves an explicit opt-in with antiforgery protection', async () => {
    const savedSnapshot = {
      ...initialSnapshot,
      marketing: { optedIn: true, updatedAt: '2026-10-06T10:00:00Z', version: 'marketing-preference-v1' },
      history: [{ kind: 'marketingPreference', granted: true, version: 'marketing-preference-v1', recordedAt: '2026-10-06T10:00:00Z' }],
    }
    const fetch = vi.fn((url: string) => Promise.resolve(
      url === '/api/v1/account/consents' ? okJson(initialSnapshot)
        : url === '/api/v1/antiforgery/token' ? okJson({ token: 'csrf-token' })
          : url === '/api/v1/account/consents/marketing' ? okJson(savedSnapshot)
            : new Response(null, { status: 404 }),
    ))
    vi.stubGlobal('fetch', fetch)
    render(<AccountConsentSettingsPage />)

    const checkbox = await screen.findByLabelText(/ürün haberleri ve kampanyalar/i) as HTMLInputElement
    expect(checkbox.checked).toBe(false)
    expect(screen.getByText(/taslak; Phase 11 hukuk incelemesi bekliyor/i)).toBeTruthy()
    fireEvent.click(checkbox)
    fireEvent.click(screen.getByRole('button', { name: 'Tercihi kaydet' }))

    expect(await screen.findByText(/Tercih geçmişi \(1\)/i)).toBeTruthy()
    expect((screen.getByLabelText(/ürün haberleri ve kampanyalar/i) as HTMLInputElement).checked).toBe(true)
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/v1/account/consents/marketing', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify({ optedIn: true }),
      headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    })))
  })

  it('shows a clear load error when the account preference request fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })))
    render(<AccountConsentSettingsPage />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect((screen.getByLabelText(/ürün haberleri ve kampanyalar/i) as HTMLInputElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Tercihi kaydet' }) as HTMLButtonElement).disabled).toBe(true)
  })
})
