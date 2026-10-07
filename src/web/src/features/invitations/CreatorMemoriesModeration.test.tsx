import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { type CreatorMemoriesPage, type CreatorMemoryItem, type DavetiyeApiClient } from '../../api/generated/client'
import { CreatorMemoriesModeration } from './CreatorMemoriesModeration'

function memory(id: string, state: CreatorMemoryItem['state'] = 'Published', over: Partial<CreatorMemoryItem> = {}): CreatorMemoryItem {
  return {
    id,
    displayName: `Guest ${id}`,
    text: `Message ${id}`,
    emoji: null,
    state,
    createdAt: '2026-10-05T10:00:00Z',
    media: [],
    ...over,
  }
}

function page(items: CreatorMemoryItem[], totalCount = items.length): CreatorMemoriesPage {
  return { page: 1, pageSize: 25, totalCount, items }
}

function createApi(over: Record<string, unknown> = {}) {
  return {
    getCreatorMemories: vi.fn().mockResolvedValue(page([])),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
    hideCreatorMemory: vi.fn().mockResolvedValue(undefined),
    deleteCreatorMemory: vi.fn().mockResolvedValue(undefined),
    createCreatorMemoryMediaDelivery: vi.fn().mockResolvedValue({
      mediaKind: 'image', deliveryUrl: 'https://media.example.test/short-lived/image', expiresAt: '2099-10-05T12:00:00Z',
    }),
    ...over,
  } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
}

describe('CreatorMemoriesModeration', () => {
  it('shows published and hidden text/media metadata, and only offers hide for published memories', async () => {
    const published = memory('published', 'Published', {
      emoji: '🎉',
      media: [
        { assetId: 'photo-1', kind: 'Image', status: 'Ready' },
        { assetId: 'video-1', kind: 'Video', status: 'Pending' },
      ],
    })
    const hidden = memory('hidden', 'Hidden')
    const api = createApi({ getCreatorMemories: vi.fn().mockResolvedValue(page([published, hidden])) })
    const { container } = render(<CreatorMemoriesModeration api={api} invitationId="invitation-1" />)

    expect(await screen.findByText('Message published', undefined, { timeout: 5_000 })).toBeTruthy()
    expect(screen.getByText('Message hidden')).toBeTruthy()
    expect(screen.getByText('🎉')).toBeTruthy()
    expect(screen.getByText('Yayında')).toBeTruthy()
    expect(screen.getByText('Gizli')).toBeTruthy()
    const mediaMetadata = screen.getByRole('list', { name: /Guest published/ })
    expect(mediaMetadata.textContent).toContain('Fotoğraf')
    expect(mediaMetadata.textContent).toContain('Hazır')
    expect(mediaMetadata.textContent).toContain('Video')
    expect(mediaMetadata.textContent).toContain('Bekliyor')
    expect(screen.getByRole('button', { name: /Guest published.*gizle/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Guest hidden.*gizle/ })).toBeNull()
    expect(container.querySelector('img, video, iframe')).toBeNull()
    expect(api.createCreatorMemoryMediaDelivery).not.toHaveBeenCalled()
    expect(api.getCreatorMemories).toHaveBeenCalledWith('invitation-1', 1, 25, expect.any(AbortSignal))
  })

  it('issues a CSRF-protected delivery only when a Ready image becomes visible and renders its returned URL', async () => {
    const observer = installIntersectionObserverMock()
    const ready = memory('ready', 'Published', {
      media: [{ assetId: 'asset-ready', kind: 'Image', status: 'Ready' }],
    })
    const api = createApi({ getCreatorMemories: vi.fn().mockResolvedValue(page([ready])) })
    render(<CreatorMemoriesModeration api={api} invitationId="invitation-1" />)

    await screen.findByText('Fotoğraf · Hazır', undefined, { timeout: 5_000 })
    expect(api.createCreatorMemoryMediaDelivery).not.toHaveBeenCalled()
    observer.trigger()
    const image = await screen.findByRole('img', { name: 'Fotoğraf anı önizlemesi' }, { timeout: 5_000 })
    expect(image.getAttribute('src')).toBe('https://media.example.test/short-lived/image')
    expect(api.getAntiforgeryToken).toHaveBeenCalled()
    expect(api.createCreatorMemoryMediaDelivery).toHaveBeenCalledWith(
      'invitation-1', 'ready', 'asset-ready', 'csrf', expect.any(AbortSignal),
    )
  })

  it('renders Stream delivery as a sandboxed iframe and allows retry after neutral delivery failure', async () => {
    const observer = installIntersectionObserverMock()
    const video = memory('video', 'Published', {
      media: [{ assetId: 'stream-ready', kind: 'Video', status: 'Ready' }],
    })
    const api = createApi({
      getCreatorMemories: vi.fn().mockResolvedValue(page([video])),
      createCreatorMemoryMediaDelivery: vi.fn()
        .mockRejectedValueOnce(new Error('unavailable'))
        .mockResolvedValueOnce({
          mediaKind: 'video', deliveryUrl: 'https://customer-abc.cloudflarestream.com/token/iframe',
          expiresAt: '2099-10-05T12:00:00Z',
        }),
    })
    const { container } = render(<CreatorMemoriesModeration api={api} invitationId="invitation-1" />)

    await screen.findByText('Video · Hazır', undefined, { timeout: 5_000 })
    observer.trigger()
    fireEvent.click(await screen.findByRole('button', { name: 'Önizlemeyi tekrar dene' }, { timeout: 5_000 }))
    const frame = await screen.findByTitle('Video anı önizlemesi', undefined, { timeout: 5_000 })
    expect(frame.tagName).toBe('IFRAME')
    expect(frame.getAttribute('src')).toBe('https://customer-abc.cloudflarestream.com/token/iframe')
    expect(frame.getAttribute('sandbox')).toBe('allow-scripts allow-same-origin allow-presentation')
    expect(frame.getAttribute('referrerpolicy')).toBe('no-referrer')
    expect(container.querySelector('video')).toBeNull()
    expect(api.createCreatorMemoryMediaDelivery).toHaveBeenCalledTimes(2)
  })

  it('hides a published memory with antiforgery and refreshes its terminal Hidden state', async () => {
    const published = memory('alice', 'Published')
    const hidden = memory('alice', 'Hidden')
    const api = createApi({ getCreatorMemories: vi.fn().mockResolvedValueOnce(page([published])).mockResolvedValueOnce(page([hidden])) })
    render(<CreatorMemoriesModeration api={api} invitationId="invitation-1" />)

    fireEvent.click(await screen.findByRole('button', { name: /Guest alice.*gizle/ }, { timeout: 5_000 }))
    await waitFor(() => expect(api.hideCreatorMemory).toHaveBeenCalledWith('invitation-1', 'alice', 'csrf'), { timeout: 5_000 })
    expect(api.getAntiforgeryToken).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Gizli', undefined, { timeout: 5_000 })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Guest alice.*gizle/ })).toBeNull()
    expect(api.getCreatorMemories).toHaveBeenCalledTimes(2)
  })

  it('requires explicit confirmation before deleting either state and then refreshes', async () => {
    const published = memory('published')
    const hidden = memory('hidden', 'Hidden')
    const api = createApi({ getCreatorMemories: vi.fn().mockResolvedValueOnce(page([published, hidden])).mockResolvedValueOnce(page([published])) })
    render(<CreatorMemoriesModeration api={api} invitationId="invitation-1" />)

    const publishedRow = (await screen.findByText('Message published', undefined, { timeout: 5_000 })).closest('li')!
    fireEvent.click(within(publishedRow).getByRole('button', { name: /Guest published.*sil/ }))
    expect(within(publishedRow).getByRole('group')).toBeTruthy()
    expect(api.deleteCreatorMemory).not.toHaveBeenCalled()
    fireEvent.click(within(publishedRow).getByRole('button', { name: /vazgeç/i }))
    expect(within(publishedRow).queryByRole('group')).toBeNull()

    const hiddenRow = (await screen.findByText('Message hidden', undefined, { timeout: 5_000 })).closest('li')!
    fireEvent.click(within(hiddenRow).getByRole('button', { name: /Guest hidden.*sil/ }))
    expect(within(hiddenRow).getAllByText(/Guest hidden/)).toHaveLength(2)
    fireEvent.click(within(hiddenRow).getByRole('button', { name: 'Kalıcı olarak sil' }))
    await waitFor(() => expect(api.deleteCreatorMemory).toHaveBeenCalledWith('invitation-1', 'hidden', 'csrf'), { timeout: 5_000 })
    expect(api.getAntiforgeryToken).toHaveBeenCalledTimes(1)
    expect(await screen.findByText('Message published', undefined, { timeout: 5_000 })).toBeTruthy()
    await waitFor(() => expect(screen.queryByText('Message hidden')).toBeNull(), { timeout: 5_000 })
  })

  it('shows a retry for list failures and a clear empty state', async () => {
    const api = createApi({ getCreatorMemories: vi.fn().mockRejectedValueOnce(new Error('unavailable')).mockResolvedValueOnce(page([])) })
    render(<CreatorMemoriesModeration api={api} invitationId="invitation-1" />)

    fireEvent.click(await screen.findByRole('button', { name: 'Tekrar dene' }, { timeout: 5_000 }))
    expect(await screen.findByText('Anı bulunmuyor.', undefined, { timeout: 5_000 })).toBeTruthy()
    expect(api.getCreatorMemories).toHaveBeenCalledTimes(2)
  })
})

function installIntersectionObserverMock() {
  const observers: Array<{ callback: IntersectionObserverCallback; disconnect: ReturnType<typeof vi.fn> }> = []
  class MockIntersectionObserver {
    callback: IntersectionObserverCallback
    disconnect = vi.fn()
    constructor(callback: IntersectionObserverCallback) {
      this.callback = callback
      observers.push(this)
    }
    observe() {}
    unobserve() {}
    takeRecords() { return [] }
    root = null
    rootMargin = '0px'
    thresholds = [0]
  }
  vi.stubGlobal('IntersectionObserver', MockIntersectionObserver)
  return {
    trigger() {
      for (const observer of observers) {
        observer.callback([{ isIntersecting: true } as IntersectionObserverEntry], observer as unknown as IntersectionObserver)
      }
    },
  }
}
