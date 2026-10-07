import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { NewInvitationPage } from './InvitationDraftPages'

describe('NewInvitationPage', () => {
  afterEach(() => {
    window.history.replaceState({}, '', '/')
    vi.unstubAllGlobals()
  })

  it('does not create a template-less draft while the catalog selection is still resolving', async () => {
    let resolveCatalog: ((response: Response) => void) | undefined
    let createBody: string | undefined
    const fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const path = new URL(String(input), window.location.origin).pathname
      if (path === '/api/v1/templates') {
        return new Promise<Response>(resolve => { resolveCatalog = resolve })
      }
      if (path === '/api/v1/antiforgery/token') return Promise.resolve(jsonResponse({ token: 'csrf' }))
      if (path === '/api/v1/invitations' && init?.method === 'POST') {
        createBody = String(init.body)
        return Promise.resolve(jsonResponse({ id: '11111111-1111-1111-1111-111111111111' }))
      }
      throw new Error(`Unexpected request: ${path}`)
    })
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/panel/davetiyeler/yeni?template=zamansiz-dugun')

    render(<NewInvitationPage />)

    const createButton = screen.getByRole('button', { name: 'Taslağı oluştur' }) as HTMLButtonElement
    expect(createButton.disabled).toBe(true)
    expect(screen.getByRole('status').textContent).toContain('şablon doğrulanıyor')
    expect(fetch).toHaveBeenCalledTimes(1)

    resolveCatalog?.(jsonResponse([{
      key: 'zamansiz-dugun',
      name: 'Zamansız Düğün',
      category: 'Düğün',
      isPremium: false,
      rendererVersion: 1,
      previewImageUrl: null,
      supportedModules: [],
      requiredFields: [],
      recommendedFields: [],
    }]))

    expect(await screen.findByText(/Zamansız Düğün/)).toBeTruthy()
    expect(createButton.disabled).toBe(false)

    fireEvent.click(createButton)
    await waitFor(() => expect(createBody).toBeTruthy())
    expect(JSON.parse(createBody ?? '{}').templateKey).toBe('zamansiz-dugun')
  })

  it('keeps creation disabled and offers retry when template verification fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    window.history.replaceState({}, '', '/panel/davetiyeler/yeni?template=zamansiz-dugun')

    render(<NewInvitationPage />)

    expect((await screen.findByRole('alert')).textContent).toContain('şablon doğrulanamadı')
    expect((screen.getByRole('button', { name: 'Taslağı oluştur' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
  })
})

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}
