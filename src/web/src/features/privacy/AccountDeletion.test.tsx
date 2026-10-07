import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AccountDeletionConfirmationPage, AccountDeletionRequest } from './AccountDeletion'

function json(body: object): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}

describe('account deletion UX', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('requires explicit confirmation, sends an antiforgery-protected request, and shows verification pending', async () => {
    const fetch = vi.fn((url: string) => Promise.resolve(
      url === '/api/v1/antiforgery/token' ? json({ token: 'csrf-token' })
        : url === '/api/v1/account/deletion-requests' ? new Response(null, { status: 202 })
          : new Response(null, { status: 404 }),
    ))
    vi.stubGlobal('fetch', fetch)
    render(<AccountDeletionRequest />)

    fireEvent.click(screen.getByRole('button', { name: 'Hesabı silme talebi oluştur' }))
    expect(screen.getByText(/uygulamadaki otomatik yenilemesi hemen durdurulur/i)).toBeTruthy()
    expect(screen.getByText(/Ödeme sağlayıcısındaki iptal henüz tamamlanmış veya doğrulanmış değildir/i)).toBeTruthy()
    expect(screen.getByText(/Daha önce oluşturulmuş medya teslim bağlantıları.*süresi dolana veya sağlayıcıdaki medya silinene kadar çalışabilir/i)).toBeTruthy()
    expect(screen.getByText(/Otomatik iade yapılmaz/i)).toBeTruthy()
    const confirmation = screen.getByLabelText(/Silme talebini başlatmak istediğimi/i) as HTMLInputElement
    expect(confirmation.checked).toBe(false)
    expect((screen.getByRole('button', { name: 'Doğrulama e-postası gönder' }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.click(confirmation)
    fireEvent.click(screen.getByRole('button', { name: 'Doğrulama e-postası gönder' }))
    await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/doğrulama bağlantısı gönderildi/i))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/v1/account/deletion-requests', expect.objectContaining({
      method: 'POST',
      headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    })))
  })

  it('scrubs the fragment token before contacting the API and posts it only in the JSON body', async () => {
    window.history.replaceState({}, '', '/hesap-silme/onayla#token=fragment-secret')
    const fetch = vi.fn((url: string, init?: RequestInit) => {
      void init
      expect(window.location.hash).toBe('')
      expect(String(url)).not.toContain('fragment-secret')
      return Promise.resolve(url === '/api/v1/antiforgery/token'
        ? json({ token: 'csrf-token' })
        : url === '/api/v1/account/deletion-requests/confirm'
          ? new Response(null, { status: 204 })
          : new Response(null, { status: 404 }))
    })
    vi.stubGlobal('fetch', fetch)
    render(<AccountDeletionConfirmationPage />)

    expect(await screen.findByText(/önceden oluşturulmuş medya teslim bağlantıları.*süresi dolana veya sağlayıcıdaki medya silinene kadar çalışabilir/i)).toBeTruthy()
    expect(screen.getByText(/Ödeme sağlayıcısındaki iptal henüz tamamlanmış veya doğrulanmış değildir/i)).toBeTruthy()
    expect(screen.getByText(/Otomatik iade yapılmaz/i)).toBeTruthy()
    fireEvent.click(await screen.findByRole('button', { name: 'Hesabı silmeyi onayla' }))
    await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/Hesap silme doğrulandı/i))
    const confirmationCall = fetch.mock.calls.find(([url]) => url === '/api/v1/account/deletion-requests/confirm')
    expect(confirmationCall?.[1]).toEqual(expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ token: 'fragment-secret' }),
      headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    }))
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`).toBe('/hesap-silme/onayla')
  })

  it('rejects query-string tokens and shows a generic error when verification fails', async () => {
    window.history.replaceState({}, '', '/hesap-silme/onayla?token=query-secret')
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    render(<AccountDeletionConfirmationPage />)

    expect((await screen.findByRole('alert')).textContent).toMatch(/bağlantı eksik, geçersiz/i)
    expect(fetch).not.toHaveBeenCalled()
    expect(window.location.search).toBe('')
    expect(window.location.pathname).toBe('/hesap-silme/onayla')
  })

  it('uses a generic expired-or-used response and discards the in-memory token after a rejected confirmation', async () => {
    window.history.replaceState({}, '', '/hesap-silme/onayla#token=expired-secret')
    const fetch = vi.fn((url: string, init?: RequestInit) => {
      void init
      return Promise.resolve(url === '/api/v1/antiforgery/token'
        ? json({ token: 'csrf-token' })
        : new Response(null, { status: 400 }))
    })
    vi.stubGlobal('fetch', fetch)
    render(<AccountDeletionConfirmationPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Hesabı silmeyi onayla' }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/bağlantı eksik, geçersiz/i))
    const confirmationCall = fetch.mock.calls.find(([url]) => url === '/api/v1/account/deletion-requests/confirm')
    expect(confirmationCall).toBeDefined()
    expect(String(confirmationCall?.[0])).not.toContain('expired-secret')
    expect(confirmationCall?.[1]).toEqual(expect.objectContaining({ body: JSON.stringify({ token: 'expired-secret' }) }))
    expect(`${window.location.pathname}${window.location.search}${window.location.hash}`).toBe('/hesap-silme/onayla')
  })

  it('keeps the request action retryable after an API error without exposing response details', async () => {
    const fetch = vi.fn((url: string) => Promise.resolve(
      url === '/api/v1/antiforgery/token' ? json({ token: 'csrf-token' })
        : new Response(null, { status: 500 }),
    ))
    vi.stubGlobal('fetch', fetch)
    render(<AccountDeletionRequest />)
    fireEvent.click(screen.getByRole('button', { name: 'Hesabı silme talebi oluştur' }))
    fireEvent.click(screen.getByLabelText(/Silme talebini başlatmak istediğimi/i))
    fireEvent.click(screen.getByRole('button', { name: 'Doğrulama e-postası gönder' }))

    expect((await screen.findByRole('alert')).textContent).toMatch(/başlatılamadı/i)
    expect(screen.getByRole('button', { name: 'Doğrulama e-postası gönder' })).toBeTruthy()
  })
})
