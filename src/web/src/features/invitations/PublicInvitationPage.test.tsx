import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { App } from '../../App'
import { PublicInvitationPage } from './PublicInvitationPage'
import { publicCodeFromPath } from './publicInvitationModel'

const code = 'a'.repeat(64)
const active = {
  status: 'active', templateKey: 'minimal-acilis', rendererVersion: 1, contentSchemaVersion: 1,
  content: { headline: 'Yayımlanmış özel başlık', hostNames: ['Ada'], timeZoneId: 'Europe/Istanbul', venue: { name: 'Özel mekân', address: 'Özel adres' } },
}

describe('public invitation access', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    window.history.replaceState({}, '', '/')
  })

  it('uses only the terminal locator, never the decorative slug or an internal id', () => {
    expect(publicCodeFromPath(`/davetiye/${code}`)).toBe(code)
    expect(publicCodeFromPath(`/davetiye/baska-bir-baslik-${code}`)).toBe(code)
    expect(publicCodeFromPath(`/davetiye/${code.toUpperCase()}`)).toBeNull()
    expect(publicCodeFromPath('/davetiye/11111111-1111-1111-1111-111111111111')).toBeNull()
    expect(publicCodeFromPath(`/davetiye/${code}/duzenle`)).toBeNull()
  })

  it('renders Published metadata outside the session guard and counts one anonymous render', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(active))))
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', `/davetiye/different-slug-${code}`)
    render(<App />)

    expect(await screen.findByRole('heading', { name: active.content.headline })).toBeTruthy()
    await waitFor(() => expect(fetch.mock.calls.filter(([url]) => String(url).endsWith('/views'))).toHaveLength(1))
    expect(fetch.mock.calls.every(([url]) => [ `/api/v1/public/invitations/${code}`, `/api/v1/public/invitations/${code}/views`, `/api/v1/public/invitations/${code}/memories/configuration`, '/api/v1/templates' ].includes(String(url)))).toBe(true)
    expect(fetch.mock.calls.filter(([url]) => String(url).endsWith('/views'))).toHaveLength(1)
    expect(document.title).toBe(`${active.content.headline} | Davetiye`)
    expect(document.head.querySelector('meta[name="description"]')?.getAttribute('content')).toBe('Dijital davetiye sayfası.')
    expect(document.head.querySelector('meta[name="robots"]')?.getAttribute('content')).toBe('noindex, nofollow')
    expect(screen.queryByText('Creator Paneli')).toBeNull()
    expect(screen.queryByText('Platform yönetimi')).toBeNull()
  })

  it('requests short-lived delivery only for media in the active snapshot and renders accessible media', async () => {
    const assetId = '11111111-1111-4111-8111-111111111111'
    const withCover = { ...active, media: [{ assetId, kind: 'Image', role: 'Cover', sortOrder: 0 }] }
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (String(url).endsWith('/views')) return Promise.resolve(new Response(null, { status: 204 }))
      if (String(url).endsWith('/media/' + assetId + '/delivery')) {
        return Promise.resolve(new Response(JSON.stringify({ kind: 'image', url: 'https://media.example.test/short/image', expiresAt: new Date(Date.now() + 60_000).toISOString() })))
      }
      if (String(url) === '/api/v1/templates') return Promise.resolve(new Response(JSON.stringify([
        { key: 'minimal-acilis', name: 'Minimal', category: 'Genel', isPremium: false, rendererVersion: 2, previewImageUrl: null, supportedModules: ['hero'], requiredFields: [], recommendedFields: [] },
      ])))
      return Promise.resolve(new Response(JSON.stringify(withCover)))
    })
    vi.stubGlobal('fetch', fetch)
    render(<PublicInvitationPage pathname={`/davetiye/${code}`} />)

    const image = await screen.findByRole('img', { name: 'Davetiye kapak fotoğrafı' })
    expect(image.getAttribute('src')).toBe('https://media.example.test/short/image')
    expect(fetch).toHaveBeenCalledWith(`/api/v1/public/invitations/${code}/media/${assetId}/delivery`, expect.objectContaining({ method: 'POST', credentials: 'omit', cache: 'no-store' }))
    expect(sessionStorage.length).toBe(0)
  })

  it('does not request/render media when its module is unsupported or delivery fails', async () => {
    const assetId = '11111111-1111-4111-8111-111111111111'
    const withGallery = { ...active, media: [{ assetId, kind: 'Image', role: 'Gallery', sortOrder: 0 }] }
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (String(url).endsWith('/views')) return Promise.resolve(new Response(null, { status: 204 }))
      if (String(url) === '/api/v1/templates') return Promise.resolve(new Response(JSON.stringify([
        { key: 'minimal-acilis', name: 'Minimal', category: 'Genel', isPremium: false, rendererVersion: 2, previewImageUrl: null, supportedModules: ['hero'], requiredFields: [], recommendedFields: [] },
      ])))
      return Promise.resolve(new Response(JSON.stringify(withGallery)))
    })
    vi.stubGlobal('fetch', fetch)
    render(<PublicInvitationPage pathname={`/davetiye/${code}`} />)

    expect(await screen.findByRole('heading', { name: active.content.headline })).toBeTruthy()
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/v1/templates', expect.anything()))
    expect(fetch.mock.calls.some(([url]) => String(url).includes('/delivery'))).toBe(false)
    expect(fetch.mock.calls.some(([url]) => String(url).endsWith('/rsvp'))).toBe(false)
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('mounts RSVP only for a template that supports it and reloads only this guest submission for edits', async () => {
    const questionId = '11111111-1111-4111-8111-111111111111'
    const rsvpConfig = {
      status: 'available',
      questions: [{ id: questionId, prompt: 'Adınız', type: 'ShortText', isRequired: true, sortOrder: 0, options: [], minimumNumberValue: null, maximumNumberValue: null }],
      answerLimits: { maxShortTextAnswerCharacters: 200, maxLongTextAnswerCharacters: 2000, minimumParticipantCount: 0, maximumParticipantCount: 20, maxMultipleChoiceSelections: 10 },
    }
    const fetch = vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      if (String(url).endsWith('/views')) return Promise.resolve(new Response(null, { status: 204 }))
      if (String(url) === '/api/v1/templates') return Promise.resolve(new Response(JSON.stringify([
        { key: 'minimal-acilis', name: 'Minimal', category: 'Genel', isPremium: false, rendererVersion: 2, previewImageUrl: null, supportedModules: ['hero', 'rsvp'], requiredFields: [], recommendedFields: [] },
      ])))
      if (String(url).endsWith('/rsvp/submissions') && init?.method === 'POST') return Promise.resolve(new Response(JSON.stringify({ submissionId: 'submission-1', submittedAt: '2026-10-05T10:00:00Z' }), { status: 201 }))
      if (String(url).endsWith('/rsvp/submissions/submission-1') && init?.method === 'GET') return Promise.resolve(new Response(JSON.stringify({ submissionId: 'submission-1', updatedAt: '2026-10-05T10:00:00Z', answers: [{ questionId, textValue: 'Ada' }] })))
      if (String(url).endsWith('/rsvp/submissions/submission-1') && init?.method === 'PUT') return Promise.resolve(new Response(JSON.stringify({ submissionId: 'submission-1', updatedAt: '2026-10-05T10:01:00Z' })))
      if (String(url) === '/api/v1/antiforgery/token') return Promise.resolve(new Response(JSON.stringify({ token: 'csrf-token' })))
      if (String(url).endsWith('/rsvp')) return Promise.resolve(new Response(JSON.stringify(rsvpConfig)))
      return Promise.resolve(new Response(JSON.stringify(active)))
    })
    vi.stubGlobal('fetch', fetch)
    const path = `/davetiye/${code}`
    const firstPage = render(<PublicInvitationPage pathname={path} />)

    fireEvent.change(await screen.findByLabelText('Adınız *'), { target: { value: 'Ada' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(await screen.findByText(/Yanıtınız kaydedildi/)).toBeTruthy()
    expect(window.localStorage.getItem(`davetiye:rsvp-submission:${code}`)).toBe('submission-1')
    expect(fetch).toHaveBeenCalledWith(`/api/v1/public/invitations/${code}/rsvp/submissions`, expect.objectContaining({
      method: 'POST', credentials: 'include', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    }))

    firstPage.unmount()
    render(<PublicInvitationPage pathname={path} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Yanıtımı Güncelle' }))
    expect((screen.getByLabelText('Adınız *') as HTMLInputElement).value).toBe('Ada')
    fireEvent.change(screen.getByLabelText('Adınız *'), { target: { value: 'Ada Lovelace' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtımı kaydet' }))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith(`/api/v1/public/invitations/${code}/rsvp/submissions/submission-1`, expect.objectContaining({
      method: 'PUT', credentials: 'include', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    })))
    expect(fetch.mock.calls.some(([url]) => String(url).includes('/results') || String(url).includes('/export'))).toBe(false)
  })

  it('removes a visible Published snapshot before rechecking and never restores it after unavailable', async () => {
    let finishRefresh: ((response: Response) => void) | undefined
    let reads = 0
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (url.endsWith('/views')) return Promise.resolve(new Response(null, { status: 204 }))
      if (url === `/api/v1/public/invitations/${code}`) {
        reads++
        return reads === 1 ? Promise.resolve(new Response(JSON.stringify(active))) : new Promise<Response>(resolve => { finishRefresh = resolve })
      }
      if (url === '/api/v1/templates') return Promise.resolve(new Response(JSON.stringify([])))
      return Promise.resolve(new Response(null, { status: 404 }))
    })
    vi.stubGlobal('fetch', fetch)
    render(<PublicInvitationPage pathname={`/davetiye/${code}`} />)

    expect(await screen.findByRole('heading', { name: active.content.headline })).toBeTruthy()
    const staleOg = document.createElement('meta')
    staleOg.setAttribute('property', 'og:title')
    staleOg.content = active.content.headline
    document.head.appendChild(staleOg)
    const staleCanonical = document.createElement('link')
    staleCanonical.rel = 'canonical'
    staleCanonical.href = `https://davet.example/davetiye/${code}`
    staleCanonical.dataset.publicInvitationMeta = 'true'
    document.head.appendChild(staleCanonical)
    act(() => { window.dispatchEvent(new Event('focus')) })
    expect(screen.queryByText(active.content.headline)).toBeNull()
    expect(document.head.querySelector('meta[property="og:title"]')).toBeNull()
    expect(document.head.querySelector('link[data-public-invitation-meta]')).toBeNull()
    await waitFor(() => expect(reads).toBe(2))
    await act(async () => { finishRefresh?.(new Response(JSON.stringify({ status: 'unavailable' }))) })
    expect(await screen.findByRole('heading', { name: 'Bu davetiye şu anda yayında değil' })).toBeTruthy()
    expect(screen.queryByText('Özel mekân')).toBeNull()
    expect(screen.queryByText('Özel adres')).toBeNull()
    expect(document.title).toBe('Dijital davetiye | Davetiye')
    expect(fetch.mock.calls.filter(([url]) => String(url).endsWith('/views'))).toHaveLength(1)
  })

  it('fails closed for an unsupported renderer pin instead of choosing another template', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...active, rendererVersion: 999 }))))
    render(<PublicInvitationPage pathname={`/davetiye/${code}`} />)
    expect(await screen.findByRole('heading', { name: 'Davetiye yüklenemedi' })).toBeTruthy()
    expect(screen.queryByText(active.content.headline)).toBeNull()
    expect(screen.queryByText(/şablon sürümü/i)).toBeNull()
  })

  it.each([
    { ...active, content: { ...active.content, timeZoneId: 'Private/UnsupportedZone' } },
    { ...active, content: { ...active.content, hostNames: ['Ada', null] } },
    { ...active, content: { ...active.content, programItems: [{ title: { nested: 'malformed' } }] } },
  ])('shows a generic error for unsupported time zones or malformed projection fields', async response => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(response))))
    render(<PublicInvitationPage pathname={`/davetiye/${code}`} />)
    expect(await screen.findByRole('heading', { name: 'Davetiye yüklenemedi' })).toBeTruthy()
    expect(screen.queryByText(active.content.headline)).toBeNull()
  })

  it('handles malformed locators without reading any invitation or session', () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    render(<PublicInvitationPage pathname="/davetiye/internal-id" />)
    expect(screen.getByRole('heading', { name: 'Davetiye bulunamadı' })).toBeTruthy()
    expect(fetch).not.toHaveBeenCalled()
  })
})
