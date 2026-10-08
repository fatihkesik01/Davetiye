import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

import {
  ApiRequestError,
  type CreatorMediaAsset,
  type CreatorMediaIntent,
  type CreatorMediaKind,
  type CreatorMediaPresentationRole,
  DavetiyeApiClient,
} from '../../api/generated/client'

type MediaStatus = 'uploading' | 'verifying' | 'processing' | 'ready' | 'failed'

interface UploadItem {
  id: string
  name: string
  kind: CreatorMediaKind
  role: CreatorMediaPresentationRole
  status: MediaStatus
  progress: number
  message?: string
  retryable?: boolean
  assetId?: string
  intent?: CreatorMediaIntent
  transferComplete?: boolean
}

interface Props {
  api: DavetiyeApiClient
  invitationId: string
  hasPublicationGrant: boolean
  supportedModules: string[]
}

export function CreatorMediaPanel({ api, invitationId, hasPublicationGrant, supportedModules }: Props) {
  const { t } = useTranslation()
  const input = useRef<HTMLInputElement>(null)
  const files = useRef(new Map<string, File>())
  const [role, setRole] = useState<CreatorMediaPresentationRole>(supportedModules.includes('gallery') ? 'Gallery' : 'Cover')
  const [items, setItems] = useState<UploadItem[]>([])
  const [assets, setAssets] = useState<CreatorMediaAsset[]>([])
  const [libraryFailed, setLibraryFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  const canCover = supportedModules.includes('hero')
  const canGallery = supportedModules.includes('gallery')
  const canUpload = hasPublicationGrant && (canCover || canGallery)

  const refreshLibrary = useCallback(async (signal?: AbortSignal) => {
    try {
      const library = await api.getCreatorMediaLibrary(invitationId, signal)
      if (!isValidLibrary(library)) throw new Error('Invalid media library response')
      setAssets(library.assets)
      setLibraryFailed(false)
      return true
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setLibraryFailed(true)
      return false
    }
  }, [api, invitationId])

  useEffect(() => {
    const controller = new AbortController()
    queueMicrotask(() => { if (!controller.signal.aborted) void refreshLibrary(controller.signal) })
    return () => controller.abort()
  }, [refreshLibrary])

  const updateItem = (id: string, update: Partial<UploadItem>) => {
    setItems(current => current.map(item => item.id === id ? { ...item, ...update } : item))
  }

  const upload = async (file: File, retryItem?: UploadItem) => {
    if (!isAllowedFile(file)) {
      setItems(current => [{ id: crypto.randomUUID(), name: file.name, kind: isVideo(file) ? 'Video' : 'Image', role, status: 'failed', progress: 0, message: t('creatorUi.media.invalidType') }, ...current])
      return
    }
    if (file.size <= 0) {
      setItems(current => [{ id: crypto.randomUUID(), name: file.name, kind: 'Image', role, status: 'failed', progress: 0, message: t('creatorUi.media.invalidSize') }, ...current])
      return
    }

    const itemId = retryItem?.id ?? crypto.randomUUID()
    const kind: CreatorMediaKind = isVideo(file) ? 'Video' : 'Image'
    const currentRole = retryItem?.role ?? role
    const base: UploadItem = retryItem ?? { id: itemId, name: file.name, kind, role: currentRole, status: 'uploading', progress: 0, retryable: true }
    files.current.set(itemId, file)
    if (!retryItem) setItems(current => [base, ...current])
    updateItem(itemId, { status: 'uploading', progress: 0, message: undefined })
    setBusy(true)
    let intent = retryItem?.intent
    let assetId = retryItem?.assetId
    let transferComplete = retryItem?.transferComplete ?? false

    try {
      if (!intent || !assetId) {
        const csrf = await api.getAntiforgeryToken()
        intent = await api.createCreatorMediaIntent(invitationId, {
          kind,
          presentationRole: currentRole,
          declaredByteLength: file.size,
        }, crypto.randomUUID(), csrf)
        assetId = intent.assetId
        updateItem(itemId, { intent, assetId })
      }

      await transferFile(intent, file, kind, percent => updateItem(itemId, { progress: percent }))
      transferComplete = true
      updateItem(itemId, { status: 'verifying', progress: 100, transferComplete: true })
      const csrf = await api.getAntiforgeryToken()
      const result = await api.finalizeCreatorMedia(invitationId, assetId, csrf)
      updateItem(itemId, { status: result.state === 'ready' ? 'ready' : 'processing' })
      if (await refreshLibrary()) {
        files.current.delete(itemId)
        setItems(current => current.filter(item => item.id !== itemId))
      }
    } catch (error) {
      const expired = error instanceof ApiRequestError && error.status === 410
      if (intent && assetId && !transferComplete) {
        try {
          const csrf = await api.getAntiforgeryToken()
          const result = await api.finalizeCreatorMedia(invitationId, assetId, csrf)
          if (result.state === 'ready') {
            updateItem(itemId, { status: 'ready', transferComplete: true })
            if (await refreshLibrary()) {
              files.current.delete(itemId)
              setItems(current => current.filter(item => item.id !== itemId))
            }
          } else {
            // A pending finalize does not prove that an ambiguous transfer reached storage.
            // Retain the File and retry the same asset/capability (TUS resumes by offset).
            updateItem(itemId, { status: 'failed', message: t('creatorUi.media.transferFailed'), transferComplete: false })
            await refreshLibrary()
          }
          return
        } catch {
          // A failed transfer may have reached storage. Retry stays tied to this same asset/capability.
        }
      }
      updateItem(itemId, {
        status: 'failed',
        message: expired ? t('creatorUi.media.expired') : transferComplete ? t('creatorUi.media.finalizeFailed') : t('creatorUi.media.transferFailed'),
        intent: expired ? undefined : intent,
        assetId: expired ? undefined : assetId,
        transferComplete: expired ? false : transferComplete,
      })
    } finally {
      setBusy(false)
    }
  }

  const retry = async (item: UploadItem) => {
    if ((item.status === 'processing' || item.transferComplete) && item.assetId) {
      setBusy(true)
      updateItem(item.id, { status: 'verifying' })
      try {
        const csrf = await api.getAntiforgeryToken()
        const result = await api.finalizeCreatorMedia(invitationId, item.assetId, csrf)
        updateItem(item.id, { status: result.state === 'ready' ? 'ready' : 'processing' })
        if (await refreshLibrary()) {
          files.current.delete(item.id)
          setItems(current => current.filter(candidate => candidate.id !== item.id))
        }
      } catch {
        updateItem(item.id, { status: item.transferComplete ? 'failed' : 'processing', message: item.transferComplete ? t('creatorUi.media.finalizeFailed') : undefined })
      } finally {
        setBusy(false)
      }
      return
    }
    const file = files.current.get(item.id)
    if (file) await upload(file, item)
  }

  const changePlacement = async (asset: CreatorMediaAsset, nextRole: CreatorMediaPresentationRole) => {
    setBusy(true)
    try {
      const csrf = await api.getAntiforgeryToken()
      const nextSortOrder = assets.filter(candidate => candidate.assetId !== asset.assetId &&
        candidate.placements.some(placement => placement.role === nextRole)).length
      await api.setCreatorMediaPlacement(invitationId, asset.assetId, nextRole, nextSortOrder, csrf)
      await refreshLibrary()
    } catch {
      setLibraryFailed(true)
    } finally {
      setBusy(false)
    }
  }

  const removeAsset = async (asset: CreatorMediaAsset) => {
    if (!window.confirm(t('creatorUi.media.confirmRemove'))) return
    setBusy(true)
    try {
      const csrf = await api.getAntiforgeryToken()
      await api.deleteCreatorMedia(invitationId, asset.assetId, csrf)
      await refreshLibrary()
    } catch {
      setLibraryFailed(true)
    } finally {
      setBusy(false)
    }
  }

  const verifyAsset = async (asset: CreatorMediaAsset) => {
    setBusy(true)
    try {
      const csrf = await api.getAntiforgeryToken()
      await api.finalizeCreatorMedia(invitationId, asset.assetId, csrf)
      await refreshLibrary()
    } catch {
      setLibraryFailed(true)
    } finally {
      setBusy(false)
    }
  }

  return <section className="creator-media" aria-labelledby="creator-media-heading">
    <div className="creator-media__intro">
      <h3 id="creator-media-heading">{t('creatorUi.media.title')}</h3>
      <p>{t('creatorUi.media.intro')}</p>
    </div>

    {!hasPublicationGrant
      ? <p className="creator-media__notice" role="status">{t('creatorUi.media.grantRequired')}</p>
      : null}
    {!canCover && !canGallery
      ? <p className="creator-media__notice" role="status">{t('creatorUi.media.unsupported')}</p>
      : null}

    {libraryFailed ? <div className="creator-media__notice" role="alert">
      <p>{t('creatorUi.media.libraryFailed')}</p>
      <button className="button button--secondary" type="button" disabled={busy} onClick={() => void refreshLibrary()}>{t('creatorUi.media.retry')}</button>
    </div> : null}

    {assets.length ? <ul className="creator-media__library" aria-label={t('creatorUi.media.library')}>
      {assets.map(asset => {
        const placedRoles = new Set(asset.placements.map(placement => placement.role))
        return <li className="creator-media__asset" key={asset.assetId}>
          <div>
            <strong>{asset.kind === 'Image' ? t('creatorUi.media.photo') : t('creatorUi.media.video')}</strong>
            <span>{asset.byteLength === null ? '' : formatBytes(asset.byteLength)}{asset.durationSeconds === null ? '' : ` · ${t('creatorUi.media.seconds', { count: asset.durationSeconds })}`}</span>
          </div>
          <p className={`creator-media__state creator-media__state--${asset.state.toLowerCase()}`} role="status">{mediaStateLabel(asset.state, t)}</p>
          <p>{placedRoles.size ? [placedRoles.has('Cover') ? t('creatorUi.media.cover') : null, placedRoles.has('Gallery') ? t('creatorUi.media.gallery') : null].filter(Boolean).join(' · ') : t('creatorUi.media.unplaced')}</p>
          {asset.state === 'Processing' ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => void verifyAsset(asset)}>{t('creatorUi.media.refreshStatus')}</button> : null}
          {asset.state === 'Ready' ? <div className="creator-media__asset-actions">
            {canCover && !placedRoles.has('Cover') ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => void changePlacement(asset, 'Cover')}>{t('creatorUi.media.addCover')}</button> : null}
            {canGallery && !placedRoles.has('Gallery') ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => void changePlacement(asset, 'Gallery')}>{t('creatorUi.media.addGallery')}</button> : null}
            <button className="button button--secondary" type="button" disabled={busy} onClick={() => void removeAsset(asset)}>{t('creatorUi.media.remove')}</button>
          </div> : asset.state !== 'PendingDeletion' && asset.state !== 'Deleted'
            ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => void removeAsset(asset)}>{t('creatorUi.media.cancelUpload')}</button>
            : null}
        </li>
      })}
    </ul> : null}

    <div className="creator-media__controls">
      <div className="form-field">
        <label htmlFor="creator-media-role">{t('creatorUi.media.usage')}</label>
        <select id="creator-media-role" value={role} onChange={event => setRole(event.target.value as CreatorMediaPresentationRole)} disabled={busy || !canUpload}>
          {canCover ? <option value="Cover">{t('creatorUi.media.cover')}</option> : null}
          {canGallery ? <option value="Gallery">{t('creatorUi.media.gallery')}</option> : null}
        </select>
      </div>
      <div className="form-field">
        <label htmlFor="creator-media-file">{t('creatorUi.media.addMedia')}</label>
        <input
          ref={input}
          id="creator-media-file"
          type="file"
          accept="image/jpeg,image/png,image/webp,image/gif,video/mp4,video/webm,video/quicktime,video/x-m4v"
          disabled={busy || !canUpload}
          onChange={event => {
            const file = event.currentTarget.files?.[0]
            if (file) {
              void upload(file)
            }
            event.currentTarget.value = ''
          }}
          aria-describedby="creator-media-help"
        />
        <p id="creator-media-help" className="form-field__help">{t('creatorUi.media.uploadHelp')}</p>
      </div>
    </div>

    {items.length ? <ul className="creator-media__items" aria-label={t('creatorUi.media.uploads')}>
      {items.map(item => <li className="creator-media__item" key={item.id}>
        <div className="creator-media__item-copy">
          <strong>{item.name}</strong>
          <span>{item.role === 'Cover' ? t('creatorUi.media.cover') : t('creatorUi.media.gallery')} · {item.kind === 'Image' ? t('creatorUi.media.photo') : t('creatorUi.media.video')}</span>
        </div>
        <p className={`creator-media__state creator-media__state--${item.status}`} role={item.status === 'failed' ? 'alert' : 'status'} aria-live="polite">
          {item.status === 'failed' ? item.message ?? t('creatorUi.media.transferFailed') : statusLabel(item.status, item.progress, t)}
        </p>
        {item.status === 'uploading' ? <progress max="100" value={item.progress} aria-label={t('creatorUi.media.progress', { name: item.name })} /> : null}
        {item.status === 'processing' || (item.status === 'failed' && item.retryable)
          ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => void retry(item)}>
            {item.status === 'processing' ? t('creatorUi.media.refreshStatus') : t('creatorUi.media.retry')}
          </button>
          : null}
      </li>)}
    </ul> : null}
  </section>
}

function isValidLibrary(library: unknown): library is { assets: CreatorMediaAsset[] } {
  const assets = (library as { assets?: unknown } | null | undefined)?.assets
  return Array.isArray(assets) && assets.every(asset => typeof asset === 'object' && asset !== null &&
    typeof (asset as CreatorMediaAsset).assetId === 'string' && Array.isArray((asset as CreatorMediaAsset).placements))
}

// Keep local File references only in memory for retry; capabilities and provider URLs are never persisted or rendered.
function isAllowedFile(file: File): boolean {
  return file.type.startsWith('image/') && ['image/jpeg', 'image/png', 'image/webp', 'image/gif'].includes(file.type.toLowerCase()) ||
    ['video/mp4', 'video/webm', 'video/quicktime', 'video/x-m4v'].includes(file.type.toLowerCase())
}

function isVideo(file: File): boolean { return file.type.toLowerCase().startsWith('video/') }

function statusLabel(status: MediaStatus, progress: number, t: TFunction): string {
  switch (status) {
    case 'uploading': return t('creatorUi.media.uploading', { progress })
    case 'verifying': return t('creatorUi.media.waitingVerification')
    case 'processing': return t('creatorUi.media.processing')
    case 'ready': return t('creatorUi.media.ready')
    case 'failed': return t('creatorUi.media.transferFailed')
  }
}

function mediaStateLabel(state: CreatorMediaAsset['state'], t: TFunction): string {
  switch (state) {
    case 'PendingUpload': return t('creatorUi.media.pendingUpload')
    case 'Processing': return t('creatorUi.media.processing')
    case 'Ready': return t('creatorUi.media.ready')
    case 'Rejected': return t('creatorUi.media.rejected')
    case 'PendingDeletion': return t('creatorUi.media.removing')
    case 'Deleted': return t('creatorUi.media.removed')
  }
}

function formatBytes(bytes: number): string {
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function transferFile(intent: CreatorMediaIntent, file: File, kind: CreatorMediaKind, onProgress: (percent: number) => void): Promise<void> {
  return kind === 'Image'
    ? xhrRequest(intent.ingressUri, 'PUT', file, { ...intent.ingressHeaders, 'Content-Type': file.type || 'application/octet-stream' }, onProgress)
    : uploadTus(intent.ingressUri, file, onProgress)
}

function xhrRequest(uri: string, method: string, body: Blob, headers: Record<string, string>, onProgress: (percent: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open(method, uri)
    request.withCredentials = false
    Object.entries(headers).forEach(([key, value]) => request.setRequestHeader(key, value))
    request.upload.onprogress = event => {
      if (event.lengthComputable) onProgress(Math.min(99, Math.floor(event.loaded / event.total * 100)))
    }
    request.onload = () => request.status >= 200 && request.status < 300 ? resolve() : reject(new Error('Media transfer failed.'))
    request.onerror = () => reject(new Error('Media transfer failed.'))
    request.onabort = () => reject(new Error('Media transfer was interrupted.'))
    request.send(body)
  })
}

function uploadTus(uri: string, file: File, onProgress: (percent: number) => void): Promise<void> {
  return new Promise((resolve, reject) => {
    const head = new XMLHttpRequest()
    head.open('HEAD', uri)
    head.withCredentials = false
    head.setRequestHeader('Tus-Resumable', '1.0.0')
    head.onload = () => {
      if (head.status < 200 || head.status >= 300) { reject(new Error('Media transfer failed.')); return }
      const offset = Number(head.getResponseHeader('Upload-Offset'))
      if (!Number.isSafeInteger(offset) || offset < 0 || offset > file.size) { reject(new Error('Media transfer failed.')); return }
      if (offset === file.size) { onProgress(100); resolve(); return }
      const patch = new XMLHttpRequest()
      patch.open('PATCH', uri)
      patch.withCredentials = false
      patch.setRequestHeader('Tus-Resumable', '1.0.0')
      patch.setRequestHeader('Upload-Offset', String(offset))
      patch.setRequestHeader('Content-Type', 'application/offset+octet-stream')
      patch.upload.onprogress = event => {
        if (event.lengthComputable) onProgress(Math.min(99, Math.floor((offset + event.loaded) / file.size * 100)))
      }
      patch.onload = () => patch.status >= 200 && patch.status < 300 ? (onProgress(100), resolve()) : reject(new Error('Media transfer failed.'))
      patch.onerror = () => reject(new Error('Media transfer failed.'))
      patch.onabort = () => reject(new Error('Media transfer was interrupted.'))
      patch.send(file.slice(offset))
    }
    head.onerror = () => reject(new Error('Media transfer failed.'))
    head.send()
  })
}
