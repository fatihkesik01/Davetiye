import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { CreatorMediaAsset, DavetiyeApiClient } from '../../api/generated/client'
import { CreatorMediaPanel } from './CreatorMediaPanel'

class FakeXhr {
  static current: FakeXhr | undefined
  static requests: FakeXhr[] = []
  static failNextPut = false
  upload: { onprogress: ((event: ProgressEvent) => void) | null } = { onprogress: null }
  status = 200
  method = ''
  url = ''
  headers: Record<string, string> = {}
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  onabort: (() => void) | null = null

  constructor() { FakeXhr.current = this; FakeXhr.requests.push(this) }
  open(method: string, url: string) { this.method = method; this.url = url }
  setRequestHeader(name: string, value: string) { this.headers[name] = value }
  getResponseHeader(name: string) { return name === 'Upload-Offset' ? '0' : null }
  send() {
    if (this.method === 'PUT' && FakeXhr.failNextPut) {
      FakeXhr.failNextPut = false
      this.status = 503
      this.onload?.()
      return
    }
    if (this.method !== 'HEAD') this.upload.onprogress?.({ lengthComputable: true, loaded: 1, total: 2 } as ProgressEvent)
    this.onload?.()
  }
}

describe('CreatorMediaPanel', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    FakeXhr.requests = []
    FakeXhr.failNextPut = false
  })

  it('keeps a transferred upload in processing until the server finalize result says ready', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    const api = mockApi('processing')
    api.getCreatorMediaLibrary = vi.fn().mockResolvedValue({ assets: [mediaAsset('Processing')] })
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['gallery']} />)

    const file = new File(['image-bytes'], 'photo.jpg', { type: 'image/jpeg' })
    fireEvent.change(screen.getByLabelText('Fotoğraf veya video ekle'), { target: { files: [file] } })

    expect(await screen.findByText('Yükleme alındı. Sağlayıcı doğrulaması tamamlanıyor.')).toBeTruthy()
    expect(screen.queryByText('Dosya sunucu tarafından doğrulandı ve hazır.')).toBeNull()
    expect(api.createCreatorMediaIntent).toHaveBeenCalledWith('invitation-1', {
      kind: 'Image', presentationRole: 'Gallery', declaredByteLength: file.size,
    }, expect.any(String), 'csrf')
    expect(api.finalizeCreatorMedia).toHaveBeenCalledWith('invitation-1', 'asset-1', 'csrf')
    expect(FakeXhr.current?.method).toBe('PUT')
    expect(FakeXhr.current?.url).toBe('https://upload.example.test/capability')
    expect(FakeXhr.current?.headers['X-Media-Capability']).toBe('private-capability')
    expect(screen.queryByText('private-capability')).toBeNull()
  })

  it('uploads videos through a resumable TUS HEAD and PATCH before server verification', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    FakeXhr.requests = []
    const api = mockApi('processing')
    api.getCreatorMediaLibrary = vi.fn().mockResolvedValue({ assets: [mediaAsset('Processing')] })
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['gallery']} />)

    fireEvent.change(screen.getByLabelText('Fotoğraf veya video ekle'), {
      target: { files: [new File(['video-bytes'], 'clip.mp4', { type: 'video/mp4' })] },
    })

    expect(await screen.findByText('Yükleme alındı. Sağlayıcı doğrulaması tamamlanıyor.')).toBeTruthy()
    expect(FakeXhr.requests.map(request => request.method)).toEqual(['HEAD', 'PATCH'])
    expect(FakeXhr.requests[1]?.headers['Tus-Resumable']).toBe('1.0.0')
    expect(FakeXhr.requests[1]?.headers['Content-Type']).toBe('application/offset+octet-stream')
    expect(api.createCreatorMediaIntent).toHaveBeenCalledWith('invitation-1', expect.objectContaining({ kind: 'Video' }), expect.any(String), 'csrf')
  })

  it('retains a failed transfer File and retries with the same asset capability when finalize is still pending', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    FakeXhr.failNextPut = true
    const api = mockApi('ready')
    api.finalizeCreatorMedia = vi.fn()
      .mockResolvedValueOnce({ state: 'processing' })
      .mockResolvedValueOnce({ state: 'ready' })
    api.getCreatorMediaLibrary = vi.fn()
      .mockResolvedValueOnce({ assets: [] })
      .mockResolvedValueOnce({ assets: [mediaAsset('PendingUpload')] })
      .mockResolvedValueOnce({ assets: [mediaAsset('Ready')] })
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['gallery']} />)

    fireEvent.change(screen.getByLabelText('Fotoğraf veya video ekle'), {
      target: { files: [new File(['image-bytes'], 'photo.jpg', { type: 'image/jpeg' })] },
    })
    expect(await screen.findByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
    expect(await screen.findByText('Yükleme bekleniyor')).toBeTruthy()
    expect(api.createCreatorMediaIntent).toHaveBeenCalledTimes(1)
    expect(api.finalizeCreatorMedia).toHaveBeenCalledTimes(1)
    expect(filesCapability(FakeXhr.requests[0])).toBe('private-capability')

    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))

    expect(await screen.findByText('Dosya sunucu tarafından doğrulandı ve hazır.')).toBeTruthy()
    expect(api.createCreatorMediaIntent).toHaveBeenCalledTimes(1)
    expect(api.finalizeCreatorMedia).toHaveBeenCalledTimes(2)
    expect(FakeXhr.requests).toHaveLength(2)
    expect(FakeXhr.requests[1]?.url).toBe(FakeXhr.requests[0]?.url)
    expect(filesCapability(FakeXhr.requests[1])).toBe(filesCapability(FakeXhr.requests[0]))
  })

  it('shows the server-verified Ready state after finalize and library confirmation', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    const api = mockApi('ready')
    api.getCreatorMediaLibrary = vi.fn().mockResolvedValue({ assets: [mediaAsset('Ready')] })
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['gallery']} />)

    fireEvent.change(screen.getByLabelText('Fotoğraf veya video ekle'), {
      target: { files: [new File(['image-bytes'], 'photo.jpg', { type: 'image/jpeg' })] },
    })

    expect(await screen.findByText('Dosya sunucu tarafından doğrulandı ve hazır.')).toBeTruthy()
    expect(api.finalizeCreatorMedia).toHaveBeenCalledWith('invitation-1', 'asset-1', 'csrf')
    expect(await screen.findAllByText('Dosya sunucu tarafından doğrulandı ve hazır.')).toHaveLength(1)
  })

  it('renders Ready only when finalize confirms it and reflects server-pending deletion accurately', async () => {
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
    const api = mockApi('ready')
    const readyAsset = mediaAsset('Ready')
    const getLibrary = vi.fn()
      .mockResolvedValueOnce({ assets: [readyAsset] })
      .mockResolvedValueOnce({ assets: [mediaAsset('PendingDeletion')] })
    api.getCreatorMediaLibrary = getLibrary
    api.deleteCreatorMedia = vi.fn().mockResolvedValue(undefined)
    vi.stubGlobal('confirm', vi.fn(() => true))
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['gallery']} />)

    expect(await screen.findByText('Dosya sunucu tarafından doğrulandı ve hazır.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Kaldır' }))
    await waitFor(() => expect(screen.getByText('Kaldırılıyor')).toBeTruthy())
    expect(screen.queryByText('Kaldırıldı')).toBeNull()
  })

  it.each([
    ['missing assets', {}],
    ['null body', null],
    ['non-array assets', { assets: 'nope' }],
    ['asset without placements', { assets: [{ assetId: 'asset-1', kind: 'Image', state: 'Ready' }] }],
  ])('shows the library failure state instead of crashing for a malformed response (%s)', async (_name, response) => {
    const api = mockApi('ready')
    api.getCreatorMediaLibrary = vi.fn().mockResolvedValue(response)
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['gallery']} />)

    expect((await screen.findByRole('alert', undefined, { timeout: 5_000 })).textContent).toContain('Medya listesi güncellenemedi.')
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Davetiye medyaları' })).toBeNull()
  })

  it('keeps the same Ready asset in both cover and gallery placements', async () => {
    const api = mockApi('ready')
    const getLibrary = vi.fn()
      .mockResolvedValueOnce({ assets: [mediaAsset('Ready')] })
      .mockResolvedValueOnce({ assets: [mediaAsset('Ready', ['Gallery', 'Cover'])] })
    api.getCreatorMediaLibrary = getLibrary
    render(<CreatorMediaPanel api={api} invitationId="invitation-1" hasPublicationGrant supportedModules={['hero', 'gallery']} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Kapağa ekle' }))

    await waitFor(() => expect(api.setCreatorMediaPlacement).toHaveBeenCalledWith('invitation-1', 'asset-1', 'Cover', 0, 'csrf'))
    expect(await screen.findByText('Kapak · Galeri')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Galeriye ekle' })).toBeNull()
  })
})

function mockApi(finalized: 'ready' | 'processing'): DavetiyeApiClient {
  return {
    getCreatorMediaLibrary: vi.fn().mockResolvedValue({ assets: [] }),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
    createCreatorMediaIntent: vi.fn().mockResolvedValue({
      intentId: 'intent-1', assetId: 'asset-1', expiresAt: '', replayed: false,
      ingressUri: 'https://upload.example.test/capability', capabilityExpiresAt: '',
      ingressHeaders: { 'X-Media-Capability': 'private-capability' },
    }),
    finalizeCreatorMedia: vi.fn().mockResolvedValue({ state: finalized }),
    setCreatorMediaPlacement: vi.fn().mockResolvedValue(undefined),
    deleteCreatorMedia: vi.fn().mockResolvedValue(undefined),
  } as unknown as DavetiyeApiClient
}

function mediaAsset(state: CreatorMediaAsset['state'], roles: CreatorMediaAsset['placements'][number]['role'][] = ['Gallery']): CreatorMediaAsset {
  return {
    assetId: 'asset-1', kind: 'Image', state, byteLength: 1024, durationSeconds: null,
    requestedPresentationRole: 'Gallery', placements: roles.map((role, sortOrder) => ({ role, sortOrder })),
  }
}

function filesCapability(request: FakeXhr | undefined): string | undefined {
  return request?.headers['X-Media-Capability']
}
