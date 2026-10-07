import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiRequestError, type DavetiyeApiClient, type PublicMemoryItem } from '../../api/generated/client'
import { PublicMemoriesSection } from './PublicMemoriesSection'

const code = 'a'.repeat(64)
const limits = { maxDisplayNameCharacters: 20, maxTextCharacters: 50, maxEmojiCharacters: 8 }
const uploadLimits = { enabled: true, maxMediaItems: 3, maxImages: 100, maxVideos: 10, maxImageSizeMb: 10, maxVideoSizeMb: 100, maxVideoDurationSeconds: 60 }
const xss = '<img src=x onerror="window.__xss=1"><script>window.__xss=1</script> [x](javascript:alert(1)) **b**'
const deliveryUrl = 'https://private-media.example.test/short-lived-token'
const streamIframeUrl = 'https://customer-abc.cloudflarestream.com/short-lived-token/iframe'

afterEach(() => vi.unstubAllGlobals())

function item(id: string, over: Partial<PublicMemoryItem> = {}): PublicMemoryItem {
  return { id, displayName: `Misafir ${id}`, text: `Not ${id}`, emoji: null, createdAt: '2026-10-05T10:00:00Z', media: [], ...over }
}

function itemWithMedia(id: string, media: Array<{ assetId: string; kind: 'Image' | 'Video' }>): PublicMemoryItem {
  return { ...item(id), media }
}

function createApi(over: Record<string, unknown> = {}) {
  return {
    getPublicMemoriesConfiguration: vi.fn().mockResolvedValue({ status: 'available', limits, uploadLimits }),
    getPublicMemories: vi.fn().mockResolvedValue({ page: 1, pageSize: 20, totalCount: 0, items: [] }),
    createPublicMemoryMediaDelivery: vi.fn().mockResolvedValue({ kind: 'image', url: deliveryUrl, expiresAt: '2099-10-05T10:00:00Z' }),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
    createPublicMemory: vi.fn().mockResolvedValue({ memoryId: 'm', createdAt: '2026-10-05T10:00:00Z' }),
    createPublicMemoryWithMedia: vi.fn().mockResolvedValue({ memoryId: 'media-memory', limits: { maxMediaItems: 3, maxImageBytes: 10 * 1024 * 1024, maxVideoBytes: 100 * 1024 * 1024, maxVideoDurationSeconds: 60 } }),
    createPublicMemoryMediaIntent: vi.fn().mockResolvedValue({ assetId: 'asset-1', kind: 'Image', ingressUri: 'https://uploads.example.test/intent', ingressHeaders: { 'Content-Type': 'image/jpeg' }, uploadExpiresAt: '2026-10-05T10:05:00Z', capabilityExpiresAt: '2026-10-05T10:05:00Z', replayed: false }),
    uploadPublicMemoryMedia: vi.fn().mockImplementation(async (_intent, _file, onProgress) => { onProgress?.(10, 10) }),
    getPublicMemoryUploadStatus: vi.fn().mockResolvedValue({ state: 'pendingMedia', uploadExpiresAt: '2026-10-05T10:05:00Z', media: [{ assetId: 'asset-1', kind: 'Image', status: 'ready' }] }),
    finalizePublicMemoryUpload: vi.fn().mockResolvedValue({ state: 'published', acceptedMediaCount: 1, rejectedMediaCount: 0 }),
    ...over,
  } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
}

describe('PublicMemoriesSection', () => {
  it('renders nothing when the configuration is unavailable (neutral 404) or fails', async () => {
    const api = createApi({ getPublicMemoriesConfiguration: vi.fn().mockRejectedValue(new ApiRequestError(404, null)) })
    const { container } = render(<PublicMemoriesSection api={api} publicCode={code} />)
    await waitFor(() => expect(api.getPublicMemoriesConfiguration).toHaveBeenCalled())
    expect(container.innerHTML).toBe('')
    expect(api.getPublicMemories).not.toHaveBeenCalled()
  })

  it('renders guest text, names and emoji inertly as text', async () => {
    const api = createApi({ getPublicMemories: vi.fn().mockResolvedValue({ page: 1, pageSize: 20, totalCount: 1, items: [item('1', { displayName: xss, text: xss, emoji: '🎉' })] }) })
    const { container } = render(<PublicMemoriesSection api={api} publicCode={code} />)
    await screen.findByRole('list', { name: 'Paylaşılan anılar' })
    expect(container.querySelector('img, script, a[href^="javascript"]')).toBeNull()
    expect(container.querySelector('li')?.textContent).toContain('<img src=x')
    expect(container.querySelector('strong')?.textContent).toBe(xss)
    expect((window as unknown as { __xss?: number }).__xss).toBeUndefined()
    expect(screen.getByText('🎉')).toBeTruthy()
  })

  it('shows public text and emoji while processing media remains omitted with an empty media projection', async () => {
    const processingMemory = item('processing-memory', { text: 'Anı notum', emoji: '💌', media: [] })
    const api = createApi({ getPublicMemories: vi.fn().mockResolvedValue({ page: 1, pageSize: 20, totalCount: 1, items: [processingMemory] }) })
    const { container } = render(<PublicMemoriesSection api={api} publicCode={code} />)

    expect(await screen.findByText('Anı notum')).toBeTruthy()
    expect(screen.getByText('💌')).toBeTruthy()
    expect(container.querySelector('.public-memory__media-list, img, video, iframe')).toBeNull()
    expect(api.createPublicMemoryMediaDelivery).not.toHaveBeenCalled()
  })

  it('requests a short-lived delivery only when a ready photo approaches the viewport', async () => {
    const observers: Array<{ trigger: () => void }> = []
    class TestIntersectionObserver {
      private callback: IntersectionObserverCallback
      constructor(callback: IntersectionObserverCallback) {
        this.callback = callback
        observers.push({ trigger: () => this.callback([{ isIntersecting: true } as IntersectionObserverEntry], this as unknown as IntersectionObserver) })
      }
      observe() {}
      disconnect() {}
      unobserve() {}
      takeRecords(): IntersectionObserverEntry[] { return [] }
      root = null
      rootMargin = '0px'
      thresholds = [0]
    }
    vi.stubGlobal('IntersectionObserver', TestIntersectionObserver)
    const processingMemory = item('processing-memory', { text: 'Metin görünür', emoji: '💌', media: [] })
    const readyMemory = itemWithMedia('memory-1', [{ assetId: 'ready-photo', kind: 'Image' }])
    const api = createApi({ getPublicMemories: vi.fn().mockResolvedValue({ page: 1, pageSize: 20, totalCount: 2, items: [processingMemory, readyMemory] }) })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    await screen.findByText('Metin görünür')
    expect(screen.getByText('💌')).toBeTruthy()
    expect(api.createPublicMemoryMediaDelivery).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/Misafir memory-1/).getAttribute('aria-label')).toBe('Misafir memory-1 taraf\u0131ndan payla\u015f\u0131lan medya')
    observers.forEach(observer => observer.trigger())
    const photo = await screen.findByRole('img', {}, { timeout: 5_000 })
    expect(api.getAntiforgeryToken).toHaveBeenCalledWith(expect.any(AbortSignal))
    expect(api.createPublicMemoryMediaDelivery).toHaveBeenCalledWith(code, 'memory-1', 'ready-photo', 'csrf', expect.any(AbortSignal))
    expect(photo.getAttribute('alt')).toContain('Misafir memory-1')
    expect(photo.getAttribute('src')).toBe(deliveryUrl)
    expect(photo.getAttribute('loading')).toBe('lazy')
    expect(screen.getAllByRole('img')).toHaveLength(1)
    expect(api.createPublicMemoryMediaDelivery).toHaveBeenCalledTimes(1)
  })

  it('keeps video delivery failures neutral and lets the guest retry', async () => {
    const createPublicMemoryMediaDelivery = vi.fn()
      .mockRejectedValueOnce(new Error('provider detail must stay private'))
      .mockResolvedValueOnce({ kind: 'video', url: streamIframeUrl, expiresAt: '2099-10-05T10:00:00Z' })
    const api = createApi({
      getPublicMemories: vi.fn().mockResolvedValue({ page: 1, pageSize: 20, totalCount: 1, items: [itemWithMedia('memory-video', [{ assetId: 'ready-video', kind: 'Video' }])] }),
      createPublicMemoryMediaDelivery,
    })
    const { container } = render(<PublicMemoriesSection api={api} publicCode={code} />)
    expect((await screen.findByRole('alert')).textContent).toContain('Bu medya')
    expect(screen.queryByText(/provider detail/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Misafir memory-video/ }))
    await waitFor(() => expect(container.querySelector('iframe')).not.toBeNull())
    const video = container.querySelector('iframe')!
    expect(video.getAttribute('src')).toBe(streamIframeUrl)
    expect(video.getAttribute('src')).toMatch(/\/iframe$/)
    expect(video.getAttribute('title')).toContain('Misafir memory-video')
    expect(video.getAttribute('sandbox')).toBe('allow-scripts allow-same-origin allow-presentation')
    expect(video.getAttribute('referrerpolicy')).toBe('no-referrer')
    expect(video.getAttribute('allow')).toBe('accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture')
    expect(video.hasAttribute('allowfullscreen')).toBe(true)
    expect(video.getAttribute('loading')).toBe('lazy')
    expect(container.querySelector('video')).toBeNull()
    expect(createPublicMemoryMediaDelivery).toHaveBeenCalledTimes(2)
  })

  it('paginates with daha fazla göster and moves focus to the first new item', async () => {
    const getPublicMemories = vi.fn()
      .mockResolvedValueOnce({ page: 1, pageSize: 20, totalCount: 2, items: [item('1')] })
      .mockResolvedValueOnce({ page: 2, pageSize: 20, totalCount: 2, items: [item('2')] })
    const api = createApi({ getPublicMemories })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Daha fazla göster' }))
    await screen.findByText('Not 2')
    expect(getPublicMemories).toHaveBeenLastCalledWith(code, 2, 20, undefined)
    await waitFor(() => expect(document.activeElement?.textContent).toContain('Not 2'))
    expect(screen.queryByRole('button', { name: 'Daha fazla göster' })).toBeNull()
  })

  it('validates client-side with live counters and never calls the API for invalid input', async () => {
    const api = createApi()
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const text = await screen.findByLabelText('Notunuz')
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    expect((await screen.findAllByText(/Bir not yazın veya bir emoji ekleyin\./)).length).toBeGreaterThan(0)
    fireEvent.change(text, { target: { value: 'x'.repeat(51) } })
    expect(screen.getByText(/51 \/ 50 birim/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    expect((await screen.findAllByText(/en fazla 50 birim/)).length).toBeGreaterThan(0)
    fireEvent.change(text, { target: { value: '' } })
    fireEvent.change(screen.getByLabelText('Emoji (isteğe bağlı)'), { target: { value: '🎉🎉' } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    expect((await screen.findAllByText(/Tek bir emoji girin\./)).length).toBeGreaterThan(0)
    expect(api.createPublicMemory).not.toHaveBeenCalled()
  })

  it('submits trimmed values with antiforgery, resets the form, announces success and stores nothing', async () => {
    const api = createApi()
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    fireEvent.change(await screen.findByLabelText('Notunuz'), { target: { value: '  Merhaba\n\nDünya  ' } })
    fireEvent.change(screen.getByLabelText('Emoji (isteğe bağlı)'), { target: { value: '🎉' } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    const status = await screen.findByText(/anınız kaydedildi/)
    expect(api.createPublicMemory).toHaveBeenCalledWith(code, { displayName: null, text: 'Merhaba\n\nDünya', emoji: '🎉' }, 'csrf')
    expect((screen.getByLabelText('Notunuz') as HTMLTextAreaElement).value).toBe('')
    await waitFor(() => expect(document.activeElement).toBe(status))
    expect(window.localStorage.length + window.sessionStorage.length).toBe(0)
    await waitFor(() => expect(api.getPublicMemories).toHaveBeenCalledTimes(2))
  })

  it('maps 400, 429, generic and 409 quota to Turkish messages', async () => {
    const createPublicMemory = vi.fn()
      .mockRejectedValueOnce(new ApiRequestError(400, { errors: { text: ['Value contains control'] } }))
      .mockRejectedValueOnce(new ApiRequestError(429, null))
      .mockRejectedValueOnce(new ApiRequestError(500, null))
      .mockRejectedValueOnce(new ApiRequestError(409, { code: 'memory_quota_reached' }))
    const api = createApi({ createPublicMemory })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    fireEvent.change(await screen.findByLabelText('Notunuz'), { target: { value: 'Merhaba' } })
    const send = () => fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    send()
    expect((await screen.findAllByText(/Not kabul edilmedi\./)).length).toBeGreaterThan(0)
    send()
    expect(await screen.findByText(/çok fazla deneme/)).toBeTruthy()
    send()
    expect(await screen.findByText(/gönderilemedi/)).toBeTruthy()
    expect((screen.getByLabelText('Notunuz') as HTMLTextAreaElement).value).toBe('Merhaba')
    send()
    expect(await screen.findByText(/anı sınırına ulaşıldı/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Anıyı gönder' })).toBeNull()
  })

  it('shows a neutral closed state when submission returns 404', async () => {
    const api = createApi({ createPublicMemory: vi.fn().mockRejectedValue(new ApiRequestError(404, null)) })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    fireEvent.change(await screen.findByLabelText('Notunuz'), { target: { value: 'Merhaba' } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    expect(await screen.findByText('Şu anda anı bırakılamıyor.')).toBeTruthy()
  })

  it('creates a media memory, reports byte progress, and finalizes after processing', async () => {
    let finishUpload: (() => void) | undefined
    const uploadPublicMemoryMedia = vi.fn((_intent: unknown, _file: File, onProgress?: (loaded: number, total: number) => void) => {
      onProgress?.(5, 10)
      return new Promise<void>(resolve => { finishUpload = resolve })
    })
    const finalizePublicMemoryUpload = vi.fn()
      .mockResolvedValueOnce({ state: 'processing', acceptedMediaCount: 0, rejectedMediaCount: 0 })
      .mockResolvedValueOnce({ state: 'published', acceptedMediaCount: 1, rejectedMediaCount: 0 })
    const api = createApi({ uploadPublicMemoryMedia, finalizePublicMemoryUpload })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photo = new File(['0123456789'], 'guest.jpg', { type: 'image/jpeg' })
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: [photo] } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))

    await waitFor(() => expect(api.createPublicMemoryWithMedia).toHaveBeenCalledWith(code, { displayName: null, text: null, emoji: null }, 'csrf'))
    await waitFor(() => expect(api.createPublicMemoryMediaIntent).toHaveBeenCalled())
    const intentCall = (api.createPublicMemoryMediaIntent as ReturnType<typeof vi.fn>).mock.calls[0]!
    expect(intentCall[0]).toBe(code)
    expect(intentCall[1]).toBe('media-memory')
    expect(intentCall[2]).toEqual({ kind: 'Image', declaredByteLength: photo.size })
    expect(intentCall[3]).toMatch(/^[0-9a-f-]{36}$/i)
    expect(intentCall[4]).toBe('csrf')
    expect(await screen.findByText('50%')).toBeTruthy()
    finishUpload?.()
    expect(await screen.findByText('Anınız alındı. Medya güvenlik kontrolünden geçti.', {}, { timeout: 8_000 })).toBeTruthy()
    expect(api.getPublicMemoryUploadStatus).toHaveBeenCalledWith(code, 'media-memory')
    expect(finalizePublicMemoryUpload).toHaveBeenCalledTimes(2)
    expect(window.localStorage.length + window.sessionStorage.length).toBe(0)
  }, 10_000)

  it('starts verification with finalize while provider assets still report pending', async () => {
    const getPublicMemoryUploadStatus = vi.fn()
      .mockResolvedValueOnce({ state: 'pendingMedia', uploadExpiresAt: '2026-10-05T10:05:00Z', media: [{ assetId: 'asset-1', kind: 'Image', status: 'pending' }] })
      .mockResolvedValue({ state: 'pendingMedia', uploadExpiresAt: '2026-10-05T10:05:00Z', media: [{ assetId: 'asset-1', kind: 'Image', status: 'ready' }] })
    const finalizePublicMemoryUpload = vi.fn()
      .mockResolvedValueOnce({ state: 'processing', acceptedMediaCount: 0, rejectedMediaCount: 0 })
      .mockResolvedValueOnce({ state: 'published', acceptedMediaCount: 1, rejectedMediaCount: 0 })
    const api = createApi({ getPublicMemoryUploadStatus, finalizePublicMemoryUpload })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photo = new File(['photo'], 'pending.jpg', { type: 'image/jpeg' })
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: [photo] } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))

    await waitFor(() => expect(finalizePublicMemoryUpload).toHaveBeenCalledTimes(1))
    expect(finalizePublicMemoryUpload).toHaveBeenCalledWith(code, 'media-memory', 'csrf')
    expect(await screen.findByText('Anınız alındı. Medya güvenlik kontrolünden geçti.', {}, { timeout: 8_000 })).toBeTruthy()
    expect(finalizePublicMemoryUpload).toHaveBeenCalledTimes(2)
  }, 10_000)

  it('retries a failed file with the same idempotency key', async () => {
    const uploadPublicMemoryMedia = vi.fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(undefined)
    const api = createApi({ uploadPublicMemoryMedia })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photo = new File(['photo'], 'retry.jpg', { type: 'image/jpeg' })
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: [photo] } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    await screen.findByRole('button', { name: 'Tekrar dene' })
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await screen.findByText('Anınız alındı. Medya güvenlik kontrolünden geçti.')
    const keys = (api.createPublicMemoryMediaIntent as ReturnType<typeof vi.fn>).mock.calls.map(call => call[3])
    expect(keys).toHaveLength(2)
    expect(keys[0]).toBe(keys[1])
  })

  it('keeps the upload session retryable when processing checks are rate limited', async () => {
    const getPublicMemoryUploadStatus = vi.fn()
      .mockRejectedValueOnce(new ApiRequestError(429, null))
      .mockResolvedValueOnce({ state: 'pendingMedia', uploadExpiresAt: '2026-10-05T10:05:00Z', media: [{ assetId: 'asset-1', kind: 'Image', status: 'ready' }] })
    const api = createApi({ getPublicMemoryUploadStatus })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photo = new File(['photo'], 'limited.jpg', { type: 'image/jpeg' })
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: [photo] } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))

    expect(await screen.findByText('Medya durumu şu anda alınamadı. Biraz bekleyip yeniden kontrol edin.')).toBeTruthy()
    const retryButton = screen.getByRole('button', { name: 'Durumu kontrol et' })
    fireEvent.click(retryButton)

    expect(await screen.findByText('Anınız alındı. Medya güvenlik kontrolünden geçti.')).toBeTruthy()
    expect(getPublicMemoryUploadStatus).toHaveBeenCalledTimes(2)
    expect(api.createPublicMemoryWithMedia).toHaveBeenCalledTimes(1)
  })

  it('rejects more files than the configured per-memory limit before creating a memory', async () => {
    const api = createApi()
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photos = [1, 2, 3, 4].map(index => new File([String(index)], `${index}.jpg`, { type: 'image/jpeg' }))
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: photos } })
    expect((await screen.findByRole('alert')).textContent).toContain('En fazla 3 fotoğraf veya video seçebilirsiniz.')
    expect(api.createPublicMemoryWithMedia).not.toHaveBeenCalled()
  })

  it('rechecks the authoritative byte cap returned by the upload session before requesting an intent', async () => {
    const api = createApi({ createPublicMemoryWithMedia: vi.fn().mockResolvedValue({
      memoryId: 'limited-memory',
      limits: { maxMediaItems: 3, maxImageBytes: 4, maxVideoBytes: 100, maxVideoDurationSeconds: 60 },
    }) })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photo = new File(['12345'], 'too-large.jpg', { type: 'image/jpeg' })
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: [photo] } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    expect(await screen.findByText(/bu yükleme oturumu için izin verilen sınırı aşıyor/)).toBeTruthy()
    expect(api.createPublicMemoryMediaIntent).not.toHaveBeenCalled()
  })

  it('shows a neutral quota state when media submission is over the guest quota', async () => {
    const api = createApi({ createPublicMemoryWithMedia: vi.fn().mockRejectedValue(new ApiRequestError(409, { code: 'media_quota_reached' })) })
    render(<PublicMemoriesSection api={api} publicCode={code} />)
    const photo = new File(['photo'], 'quota.jpg', { type: 'image/jpeg' })
    fireEvent.change(await screen.findByLabelText('Fotoğraf veya video (isteğe bağlı)'), { target: { files: [photo] } })
    fireEvent.click(screen.getByRole('button', { name: 'Anıyı gönder' }))
    expect(await screen.findByText(/anı sınırına ulaşıldı/)).toBeTruthy()
    expect(api.createPublicMemoryMediaIntent).not.toHaveBeenCalled()
  })
})
