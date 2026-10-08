import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import type { CreatorMemoriesPage, CreatorMemoryItem, CreatorMemoryMediaStatus, CreatorMemoryPreview, DavetiyeApiClient } from '../../api/generated/client'

type ModerationMemory = CreatorMemoryItem
type ModerationPage = CreatorMemoriesPage

interface Props {
  api: DavetiyeApiClient
  invitationId: string
}

const pageSize = 25

export function CreatorMemoriesModeration({ api, invitationId }: Props) {
  const { t, i18n } = useTranslation()
  const [page, setPage] = useState<ModerationPage | null>(null)
  const [currentPage, setCurrentPage] = useState(1)
  const [refreshToken, setRefreshToken] = useState(0)
  const [listState, setListState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [busyMemoryId, setBusyMemoryId] = useState<string | null>(null)
  const [deleteMemory, setDeleteMemory] = useState<ModerationMemory | null>(null)
  const [actionError, setActionError] = useState('')
  const [announcement, setAnnouncement] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    void api.getCreatorMemories(invitationId, currentPage, pageSize, controller.signal)
      .then(result => {
        if (controller.signal.aborted) return
        setPage(result)
        setListState('ready')
      })
      .catch(() => {
        if (!controller.signal.aborted) setListState('error')
      })
    return () => controller.abort()
  }, [api, invitationId, currentPage, refreshToken])

  async function hide(memory: ModerationMemory) {
    if (memory.state !== 'Published' || busyMemoryId) return
    setBusyMemoryId(memory.id)
    setActionError('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      await api.hideCreatorMemory(invitationId, memory.id, csrfToken)
      setAnnouncement(t('creatorUi.moderation.hideAnnouncement', { guest: memory.displayName || t('creatorUi.memories.guest') }))
      setListState('loading')
      setRefreshToken(value => value + 1)
    } catch {
      setActionError(t('creatorUi.moderation.hideFailed'))
    } finally {
      setBusyMemoryId(null)
    }
  }

  async function permanentlyDelete(memory: ModerationMemory) {
    if (busyMemoryId) return
    setBusyMemoryId(memory.id)
    setActionError('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      await api.deleteCreatorMemory(invitationId, memory.id, csrfToken)
      setDeleteMemory(null)
      setAnnouncement(t('creatorUi.moderation.deleteAnnouncement', { guest: memory.displayName || t('creatorUi.memories.guest') }))
      setListState('loading')
      if (page && page.items.length === 1 && currentPage > 1) setCurrentPage(value => value - 1)
      else setRefreshToken(value => value + 1)
    } catch {
      setActionError(t('creatorUi.moderation.deleteFailed'))
    } finally {
      setBusyMemoryId(null)
    }
  }

  const titleId = `creator-memories-title-${invitationId}`
  const hasPrevious = currentPage > 1
  const hasNext = Boolean(page && currentPage * pageSize < page.totalCount)

  return <section className="memories-moderation" aria-labelledby={titleId}>
    <div className="rsvp-panel__heading">
      <div><h4 id={titleId}>{t('creatorUi.moderation.title')}</h4><p>{t('creatorUi.moderation.intro')}</p></div>
      {page ? <span aria-label={t('creatorUi.moderation.total', { count: page.totalCount })}>{page.totalCount}</span> : null}
    </div>

    <div aria-busy={listState === 'loading'}>
      {listState === 'loading' ? <p role="status">{t('creatorUi.moderation.loading')}</p> : null}
      {listState === 'error' ? <div role="alert" className="inline-alert">
        <p>{t('creatorUi.moderation.failed')}</p>
        <button type="button" className="button button--secondary" onClick={() => { setListState('loading'); setRefreshToken(value => value + 1) }}>{t('creatorUi.moderation.retry')}</button>
      </div> : null}
      {listState === 'ready' && page?.items.length === 0 ? <p>{t('creatorUi.moderation.empty')}</p> : null}
      {page?.items.length ? <ul className="public-memories__items" aria-label={t('creatorUi.moderation.managed')}>
        {page.items.map(memory => {
          const guest = memory.displayName?.trim() || t('creatorUi.memories.guest')
          const confirmId = `memory-delete-confirm-${memory.id}`
          return <li key={memory.id} className="public-memory">
            <div className="public-memory__head">
              {memory.emoji ? <span className="public-memory__emoji" aria-label={t('creatorUi.moderation.guestEmoji')}>{memory.emoji}</span> : null}
              <strong className="public-memory__name">{guest}</strong>
              <time dateTime={memory.createdAt}>{formatMemoryDate(memory.createdAt, i18n.resolvedLanguage)}</time>
              <span className="memories-moderation__state">{memory.state === 'Published' ? t('creatorUi.moderation.published') : t('creatorUi.moderation.hidden')}</span>
            </div>
            {memory.text ? <p className="public-memory__text">{memory.text}</p> : null}
            {memory.media.length ? <ul className="memories-moderation__media" aria-label={t('creatorUi.moderation.mediaOf', { guest })}>
              {memory.media.map(media => <li key={media.assetId}>
                <span>{media.kind === 'Image' ? t('creatorUi.moderation.image') : t('creatorUi.moderation.video')} · {mediaStatusLabel(media.status, t)}</span>
                {media.status === 'Ready' ? <CreatorMemoryMediaPreview api={api} invitationId={invitationId}
                  memoryId={memory.id} assetId={media.assetId} kind={media.kind} /> : null}
              </li>)}
            </ul> : null}

            <div className="button-row">
              {memory.state === 'Published' ? <button type="button" className="button button--secondary"
                aria-label={t('creatorUi.moderation.hide', { guest })} disabled={busyMemoryId === memory.id || busyMemoryId !== null}
                onClick={() => void hide(memory)}>{busyMemoryId === memory.id ? t('creatorUi.moderation.processing') : t('creatorUi.moderation.hideAction')}</button> : null}
              <button type="button" className="button button--secondary" aria-label={t('creatorUi.moderation.deleteLabel', { guest })}
                disabled={busyMemoryId === memory.id || busyMemoryId !== null} onClick={() => { setDeleteMemory(memory); setActionError('') }}>{t('creatorUi.moderation.deleteAction')}</button>
            </div>

            {deleteMemory?.id === memory.id ? <div className="inline-alert" role="group" aria-labelledby={confirmId}>
              <h5 id={confirmId}>{t('creatorUi.moderation.confirmTitle')}</h5>
              <p>{t('creatorUi.moderation.confirmBody', { guest })}</p>
              {actionError ? <p role="alert">{actionError}</p> : null}
              <div className="button-row">
                <button type="button" className="button button--secondary" disabled={busyMemoryId === memory.id} onClick={() => { setDeleteMemory(null); setActionError('') }}>{t('creatorUi.moderation.cancel')}</button>
                <button type="button" className="button button--danger" disabled={busyMemoryId === memory.id} onClick={() => void permanentlyDelete(memory)}>
                  {busyMemoryId === memory.id ? t('creatorUi.moderation.deleting') : t('creatorUi.moderation.deletePermanently')}
                </button>
              </div>
            </div> : null}
          </li>
        })}
      </ul> : null}
    </div>

    {actionError && !deleteMemory ? <p role="alert">{actionError}</p> : null}
    <p role="status" aria-live="polite">{announcement}</p>
    {page && page.totalCount > pageSize ? <nav aria-label={t('creatorUi.moderation.pages')} className="button-row">
      <button type="button" className="button button--secondary" disabled={!hasPrevious || listState === 'loading'} onClick={() => { setListState('loading'); setCurrentPage(value => value - 1) }}>{t('creatorUi.moderation.previous')}</button>
      <span>{t('creatorUi.moderation.page', { current: page.page, total: Math.ceil(page.totalCount / pageSize) })}</span>
      <button type="button" className="button button--secondary" disabled={!hasNext || listState === 'loading'} onClick={() => { setListState('loading'); setCurrentPage(value => value + 1) }}>{t('creatorUi.moderation.next')}</button>
    </nav> : null}
  </section>
}

interface MediaPreviewProps {
  api: DavetiyeApiClient
  invitationId: string
  memoryId: string
  assetId: string
  kind: 'Image' | 'Video'
}

function CreatorMemoryMediaPreview({ api, invitationId, memoryId, assetId, kind }: MediaPreviewProps) {
  const { t } = useTranslation()
  const hostRef = useRef<HTMLDivElement>(null)
  const [isVisible, setIsVisible] = useState(false)
  const [preview, setPreview] = useState<CreatorMemoryPreview | null>(null)
  const [state, setState] = useState<'idle' | 'loading' | 'ready' | 'error'>('idle')
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const host = hostRef.current
    if (!host || isVisible) return
    if (typeof IntersectionObserver === 'undefined') {
      const timer = window.setTimeout(() => setIsVisible(true), 0)
      return () => window.clearTimeout(timer)
    }
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        setIsVisible(true)
        observer.disconnect()
      }
    }, { rootMargin: '160px' })
    observer.observe(host)
    return () => observer.disconnect()
  }, [isVisible])

  useEffect(() => {
    if (!isVisible) return
    const controller = new AbortController()
    void (async () => {
      await Promise.resolve()
      if (controller.signal.aborted) return
      setState('loading')
      setPreview(null)
      try {
        const csrfToken = await api.getAntiforgeryToken(controller.signal)
        const result = await api.createCreatorMemoryMediaDelivery(invitationId, memoryId, assetId, csrfToken, controller.signal)
        if (controller.signal.aborted) return
        if (!isValidCreatorPreview(result)) throw new Error('Invalid media delivery')
        setPreview(result)
        setState('ready')
      } catch {
        if (!controller.signal.aborted) setState('error')
      }
    })()
    return () => controller.abort()
  }, [api, assetId, invitationId, isVisible, memoryId, attempt])

  const label = kind === 'Image' ? t('creatorUi.moderation.image') : t('creatorUi.moderation.video')
  return <div ref={hostRef} className="memories-moderation__preview">
    {state === 'idle' ? <span>{t('creatorUi.moderation.idle', { kind: label })}</span> : null}
    {state === 'loading' ? <span role="status">{t('creatorUi.moderation.previewLoading')}</span> : null}
    {state === 'error' ? <div role="group" aria-label={t('creatorUi.moderation.preview', { kind: label })}>
      <span>{t('creatorUi.moderation.unavailable')}</span>
      <button type="button" className="button button--secondary" onClick={() => setAttempt(value => value + 1)}>{t('creatorUi.moderation.previewRetry')}</button>
    </div> : null}
    {state === 'ready' && preview?.mediaKind === 'image' ? <img className="public-invitation-media__image"
      src={preview.deliveryUrl} alt={t('creatorUi.moderation.memoryPreview', { kind: label })} loading="lazy" referrerPolicy="no-referrer" /> : null}
    {state === 'ready' && preview?.mediaKind === 'video' ? <iframe className="public-invitation-media__video"
      src={preview.deliveryUrl} title={t('creatorUi.moderation.memoryPreview', { kind: label })} referrerPolicy="no-referrer"
      sandbox="allow-scripts allow-same-origin allow-presentation"
      allow="accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture" allowFullScreen loading="lazy" /> : null}
  </div>
}

function isValidCreatorPreview(value: CreatorMemoryPreview): boolean {
  if ((value.mediaKind !== 'image' && value.mediaKind !== 'video') || typeof value.deliveryUrl !== 'string' ||
    typeof value.expiresAt !== 'string' || Date.parse(value.expiresAt) <= Date.now()) return false
  try {
    const url = new URL(value.deliveryUrl)
    return url.protocol === 'https:' && !url.username && !url.password && !url.hash
  } catch {
    return false
  }
}

function mediaStatusLabel(status: CreatorMemoryMediaStatus, t: (key: string) => string): string {
  switch (status) {
    case 'Pending': return t('creatorUi.moderation.pending')
    case 'Ready': return t('creatorUi.moderation.ready')
    case 'Rejected': return t('creatorUi.moderation.rejected')
    case 'Deleted': return t('creatorUi.moderation.deleted')
  }
}

function formatMemoryDate(value: string, language?: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(language === 'en' ? 'en-US' : 'tr-TR', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}
