import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import {
  ApiRequestError,
  type DavetiyeApiClient,
  type PublicMemoriesConfiguration,
  type PublicMemoryMediaDelivery,
  type PublicMemoryItem,
  type PublicMemoryReadyMedia,
} from '../../api/generated/client'

interface Props {
  api: DavetiyeApiClient
  publicCode: string
}

const pageSize = 20

/**
 * Public "Anılarımız" section. Any configuration failure (including the neutral 404 the backend returns when any
 * gate is closed) renders nothing, so the guest never learns which gate failed.
 */
export function PublicMemoriesSection({ api, publicCode }: Props) {
  const [configuration, setConfiguration] = useState<PublicMemoriesConfiguration | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void api.getPublicMemoriesConfiguration(publicCode, controller.signal).then(result => {
      if (!controller.signal.aborted && result.status === 'available') setConfiguration(result)
    }).catch(() => {
      // Memories are optional public content; every failure hides the section.
    })
    return () => controller.abort()
  }, [api, publicCode])

  return configuration ? <MemoriesBoard key={publicCode} api={api} publicCode={publicCode} configuration={configuration} /> : null
}

type FieldKey = 'displayName' | 'text' | 'emoji'
type FieldErrors = Partial<Record<FieldKey, string>>
type SubmitState = 'idle' | 'submitting' | 'success' | 'quota' | 'closed'
type GuestMediaKind = 'Image' | 'Video'
type GuestMediaLimits = { enabled: boolean; maxMediaItems: number; maxImages: number; maxVideos: number; maxImageSizeMb: number; maxVideoSizeMb: number; maxVideoDurationSeconds: number }
type GuestSessionLimits = { maxMediaItems: number; maxImageBytes: number; maxVideoBytes: number; maxVideoDurationSeconds: number }
type GuestMediaIntent = { assetId: string; kind: GuestMediaKind; ingressUri: string; ingressHeaders: Record<string, string>; uploadExpiresAt: string; capabilityExpiresAt: string; replayed: boolean }
type GuestUploadContext = { memoryId: string; csrfToken: string; limits: GuestSessionLimits; files: File[]; keys: string[]; durations: Array<number | undefined> }
type MediaState = { phase: 'uploading' | 'processing' | 'published' | 'rejected' | 'failed'; progress: number[]; failedIndex: number | null; message: string }
type GuestMediaApi = {
  createPublicMemoryWithMedia(code: string, request: { displayName: string | null; text: string | null; emoji: string | null }, csrf: string): Promise<{ memoryId: string; limits: GuestSessionLimits }>
  createPublicMemoryMediaIntent(code: string, memoryId: string, request: { kind: GuestMediaKind; declaredByteLength: number; declaredDurationSeconds?: number }, idempotencyKey: string, csrf: string): Promise<GuestMediaIntent>
  uploadPublicMemoryMedia(intent: GuestMediaIntent, file: File, onProgress?: (loaded: number, total: number) => void): Promise<void>
  getPublicMemoryUploadStatus(code: string, memoryId: string): Promise<{ state: string; media: Array<{ assetId: string; kind: GuestMediaKind; status: string }> }>
  finalizePublicMemoryUpload(code: string, memoryId: string, csrf: string): Promise<{ state: string; acceptedMediaCount: number; rejectedMediaCount: number }>
}

function MemoriesBoard({ api, publicCode, configuration }: Props & { configuration: PublicMemoriesConfiguration }) {
  const { t, i18n } = useTranslation()
  const { limits } = configuration
  const [items, setItems] = useState<PublicMemoryItem[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [nextPage, setNextPage] = useState(1)
  const [listState, setListState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [loadingMore, setLoadingMore] = useState(false)
  const [displayName, setDisplayName] = useState('')
  const [text, setText] = useState('')
  const [emoji, setEmoji] = useState('')
  const [mediaFiles, setMediaFiles] = useState<File[]>([])
  const [mediaDurations, setMediaDurations] = useState<Array<number | undefined>>([])
  const [mediaError, setMediaError] = useState('')
  const [mediaState, setMediaState] = useState<MediaState | null>(null)
  const uploadContext = useRef<GuestUploadContext | null>(null)
  const lastFinalizeAt = useRef(new Map<string, number>())
  const [errors, setErrors] = useState<FieldErrors>({})
  const [formError, setFormError] = useState('')
  const [submitState, setSubmitState] = useState<SubmitState>('idle')
  const pendingFocus = useRef<number | null>(null)
  const summaryRef = useRef<HTMLDivElement>(null)
  const statusRef = useRef<HTMLParagraphElement>(null)
  const itemRefs = useRef<Array<HTMLLIElement | null>>([])
  const [reloadToken, setReloadToken] = useState(0)

  const loadPage = useCallback(async (page: number, signal?: AbortSignal) => {
    const result = await api.getPublicMemories(publicCode, page, pageSize, signal)
    return result
  }, [api, publicCode])

  useEffect(() => {
    const controller = new AbortController()
    void loadPage(1, controller.signal).then(result => {
      if (controller.signal.aborted) return
      setItems(result.items)
      setTotalCount(result.totalCount)
      setNextPage(2)
      setListState('ready')
    }).catch(() => {
      if (!controller.signal.aborted) setListState('error')
    })
    return () => controller.abort()
  }, [loadPage, reloadToken])

  useEffect(() => {
    if (pendingFocus.current !== null) {
      itemRefs.current[pendingFocus.current]?.focus()
      pendingFocus.current = null
    }
  }, [items])

  useEffect(() => {
    if (formError && Object.keys(errors).length) summaryRef.current?.focus()
  }, [errors, formError])

  useEffect(() => {
    if (submitState === 'success' || submitState === 'quota' || submitState === 'closed') statusRef.current?.focus()
  }, [submitState])

  async function loadMore() {
    const previousLength = items.length
    setLoadingMore(true)
    try {
      const result = await loadPage(nextPage)
      setItems(current => {
        const known = new Set(current.map(item => item.id))
        return [...current, ...result.items.filter(item => !known.has(item.id))]
      })
      setTotalCount(result.totalCount)
      setNextPage(value => value + 1)
      pendingFocus.current = previousLength
    } catch {
      setListState('error')
    } finally {
      setLoadingMore(false)
    }
  }

  function validate(): FieldErrors {
    const next: FieldErrors = {}
    if (displayName.trim().length > limits.maxDisplayNameCharacters) next.displayName = t('creatorUi.memories.nameTooLong', { limit: limits.maxDisplayNameCharacters })
    if (text.trim().length > limits.maxTextCharacters) next.text = t('creatorUi.memories.textTooLong', { limit: limits.maxTextCharacters })
    if (emoji.trim().length > limits.maxEmojiCharacters || countGraphemes(emoji.trim()) > 1) next.emoji = t('creatorUi.memories.emojiInvalid')
    if (!next.text && !next.emoji && !text.trim() && !emoji.trim() && mediaFiles.length === 0) next.text = t('creatorUi.memories.emptyEntry')
    return next
  }

  async function chooseMedia(files: FileList | null) {
    const selected = Array.from(files ?? [])
    setMediaError('')
    setMediaFiles([])
    setMediaDurations([])
    setMediaState(null)
    if (!selected.length) return
    const configured = (configuration as PublicMemoriesConfiguration & { uploadLimits?: GuestMediaLimits }).uploadLimits
    if (!configured?.enabled) {
      setMediaError('Medya ekleme şu anda kullanılamıyor. Lütfen daha sonra tekrar deneyin.')
      return
    }
    const maxItems = configured.maxMediaItems
    if (selected.length > maxItems) {
      setMediaError(`En fazla ${maxItems} fotoğraf veya video seçebilirsiniz.`)
      return
    }
    const durations: Array<number | undefined> = []
    for (const file of selected) {
      const kind = mediaKind(file)
      if (!kind) {
        setMediaError('Yalnızca fotoğraf ve video dosyaları ekleyebilirsiniz.')
        return
      }
      const kindCount = selected.filter(candidate => mediaKind(candidate) === kind).length
      const kindCountLimit = kind === 'Image' ? configured.maxImages : configured.maxVideos
      if (kindCount > kindCountLimit || kindCountLimit === 0) {
        setMediaError(kind === 'Image' ? 'Bu davetiye için fotoğraf yükleme kullanılamıyor veya sınırına ulaşıldı.' : 'Bu davetiye için video yükleme kullanılamıyor veya sınırına ulaşıldı.')
        return
      }
      const sizeLimit = (kind === 'Image' ? configured.maxImageSizeMb : configured.maxVideoSizeMb) * 1024 * 1024
      if (file.size > sizeLimit) {
        setMediaError(`${file.name}: Dosya boyutu izin verilen sınırı aşıyor.`)
        return
      }
      if (kind === 'Video') {
        const duration = await readVideoDuration(file)
        if (!Number.isFinite(duration) || duration <= 0 || duration > configured.maxVideoDurationSeconds) {
          setMediaError(`${file.name}: Video süresi en fazla ${configured.maxVideoDurationSeconds} saniye olabilir.`)
          return
        }
        durations.push(duration)
      } else durations.push(undefined)
    }
    setMediaFiles(selected)
    setMediaDurations(durations)
  }

  async function runUploads(context: GuestUploadContext, startAt: number) {
    const mediaApi = api as DavetiyeApiClient & GuestMediaApi
    for (let index = startAt; index < context.files.length; index += 1) {
      const file = context.files[index]!
      const kind = mediaKind(file)!
      const sessionLimit = kind === 'Image' ? context.limits.maxImageBytes : context.limits.maxVideoBytes
      const durationLimitExceeded = kind === 'Video' && (context.durations[index] ?? Infinity) > context.limits.maxVideoDurationSeconds
      if (file.size > sessionLimit || durationLimitExceeded || context.files.length > context.limits.maxMediaItems) {
        setMediaState(current => ({ phase: 'failed', progress: current?.progress ?? context.files.map(() => 0), failedIndex: index, message: 'Dosya, bu yükleme oturumu için izin verilen sınırı aşıyor. Dosyaları yeniden seçin.' }))
        uploadContext.current = null
        return
      }
      setMediaState(current => ({ phase: 'uploading', progress: current?.progress ?? context.files.map(() => 0), failedIndex: null, message: `${file.name} yükleniyor.` }))
      try {
        const intent = await mediaApi.createPublicMemoryMediaIntent(publicCode, context.memoryId, {
          kind,
          declaredByteLength: file.size,
          ...(context.durations[index] === undefined ? {} : { declaredDurationSeconds: context.durations[index] }),
        }, context.keys[index]!, context.csrfToken)
        await mediaApi.uploadPublicMemoryMedia(intent, file, (loaded, total) => {
          setMediaState(current => {
            const progress = [...(current?.progress ?? context.files.map(() => 0))]
            progress[index] = total > 0 ? Math.min(100, Math.round((loaded / total) * 100)) : 0
            return { phase: 'uploading', progress, failedIndex: null, message: `${file.name} yükleniyor.` }
          })
        })
        setMediaState(current => {
          const progress = [...(current?.progress ?? context.files.map(() => 0))]
          progress[index] = 100
          return { phase: 'uploading', progress, failedIndex: null, message: `${file.name} yüklendi.` }
        })
      } catch {
        setMediaState(current => ({ phase: 'failed', progress: current?.progress ?? context.files.map(() => 0), failedIndex: index, message: 'Medya yüklenemedi. Bağlantınızı kontrol edip tekrar deneyin.' }))
        return
      }
    }
    await checkMediaProcessing(context)
  }

  async function checkMediaProcessing(context: GuestUploadContext) {
    const mediaApi = api as DavetiyeApiClient & GuestMediaApi
    setMediaState(current => ({ phase: 'processing', progress: current?.progress ?? context.files.map(() => 100), failedIndex: null, message: 'Medyanız güvenlik ve biçim kontrolünden geçiyor.' }))
    try {
      for (let attempt = 0; attempt < 30; attempt += 1) {
        await mediaApi.getPublicMemoryUploadStatus(publicCode, context.memoryId)
        // The first finalize starts server-side inspection while assets may still be PendingUpload.
        // Retry only every five seconds so a slow provider cannot consume the 20/minute finalize limit.
        const previousFinalizeAt = lastFinalizeAt.current.get(context.memoryId) ?? 0
        const shouldFinalize = previousFinalizeAt === 0 || Date.now() - previousFinalizeAt >= 5_000
        if (shouldFinalize) {
          lastFinalizeAt.current.set(context.memoryId, Date.now())
          const result = await mediaApi.finalizePublicMemoryUpload(publicCode, context.memoryId, context.csrfToken)
          if (result.state === 'published') {
            const message = result.rejectedMediaCount > 0
              ? `${result.acceptedMediaCount} medya dosyası kabul edildi; ${result.rejectedMediaCount} dosya güvenlik veya biçim kontrolünü geçemedi.`
              : 'Anınız alındı. Medya güvenlik kontrolünden geçti.'
            setMediaState(current => ({ phase: 'published', progress: current?.progress ?? context.files.map(() => 100), failedIndex: null, message }))
            uploadContext.current = null
            setDisplayName('')
            setText('')
            setEmoji('')
            setMediaFiles([])
            setMediaDurations([])
            setReloadToken(value => value + 1)
            lastFinalizeAt.current.delete(context.memoryId)
            return
          }
          if (result.state === 'rejected') {
            setMediaState(current => ({ phase: 'rejected', progress: current?.progress ?? context.files.map(() => 100), failedIndex: null, message: 'Medya dosyaları kabul edilemedi. Dosyaları kontrol edip yeni bir anı gönderebilirsiniz.' }))
            uploadContext.current = null
            lastFinalizeAt.current.delete(context.memoryId)
            return
          }
        }
        await new Promise(resolve => window.setTimeout(resolve, 2_000))
      }
      setMediaState(current => ({ phase: 'processing', progress: current?.progress ?? context.files.map(() => 100), failedIndex: null, message: 'Medya kontrolü devam ediyor. Durumu daha sonra yeniden kontrol edebilirsiniz.' }))
    } catch {
      setMediaState(current => ({ phase: 'processing', progress: current?.progress ?? context.files.map(() => 100), failedIndex: null, message: 'Medya durumu şu anda alınamadı. Biraz bekleyip yeniden kontrol edin.' }))
    }
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (submitState === 'submitting' || mediaState?.phase === 'uploading' || mediaState?.phase === 'processing' || uploadContext.current) return
    const validation = validate()
    setErrors(validation)
    setFormError('')
    if (Object.keys(validation).length) {
      setFormError(t('creatorUi.memories.validation'))
      return
    }

    setSubmitState('submitting')
    setMediaState(null)
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const request = {
        displayName: displayName.trim() || null,
        text: text.trim() || null,
        emoji: emoji.trim() || null,
      }
      if (mediaFiles.length) {
        const session = await (api as DavetiyeApiClient & GuestMediaApi).createPublicMemoryWithMedia(publicCode, request, csrfToken)
        const context: GuestUploadContext = {
          memoryId: session.memoryId,
          csrfToken,
          limits: session.limits,
          files: mediaFiles,
          keys: mediaFiles.map(() => crypto.randomUUID()),
          durations: mediaDurations,
        }
        uploadContext.current = context
        setSubmitState('idle')
        await runUploads(context, 0)
        return
      }
      await api.createPublicMemory(publicCode, request, csrfToken)
      setDisplayName('')
      setText('')
      setEmoji('')
      setMediaFiles([])
      setMediaDurations([])
      setSubmitState('success')
      setReloadToken(value => value + 1)
    } catch (error) {
      setSubmitState('idle')
      if (error instanceof ApiRequestError && error.status === 400) {
        const serverErrors: FieldErrors = {}
        const keys = Object.keys(error.problem?.errors ?? {}).map(key => key.toLowerCase())
        if (keys.includes('displayname')) serverErrors.displayName = t('creatorUi.memories.invalidName')
        if (keys.includes('text')) serverErrors.text = t('creatorUi.memories.invalidText')
        if (keys.includes('emoji')) serverErrors.emoji = t('creatorUi.memories.invalidEmoji')
        setErrors(serverErrors)
        setFormError(t('creatorUi.memories.rejected'))
        if (!Object.keys(serverErrors).length) setErrors({ text: t('creatorUi.memories.rejected') })
      } else if (error instanceof ApiRequestError && error.status === 409 && ['memory_quota_reached', 'media_quota_reached'].includes(error.problem?.code ?? '')) {
        setSubmitState('quota')
      } else if (error instanceof ApiRequestError && error.status === 404) {
        setSubmitState('closed')
      } else if (error instanceof ApiRequestError && error.status === 429) {
        setFormError(t('creatorUi.memories.tooMany'))
      } else {
        setFormError(t('creatorUi.memories.sendFailed'))
      }
    }
  }

  const submitting = submitState === 'submitting' || mediaState?.phase === 'uploading' || mediaState?.phase === 'processing'
  const formClosed = submitState === 'quota' || submitState === 'closed'
  const errorList = Object.entries(errors) as Array<[FieldKey, string]>
  const fieldLabel: Record<FieldKey, string> = { displayName: t('creatorUi.memories.name'), text: t('creatorUi.memories.text'), emoji: 'Emoji' }
  const uploadLimits = (configuration as PublicMemoriesConfiguration & { uploadLimits?: GuestMediaLimits }).uploadLimits

  return <section className="public-memories" aria-labelledby="public-memories-heading">
    <h2 id="public-memories-heading">{t('creatorUi.memories.title')}</h2>

    <div className="public-memories__list" aria-busy={listState === 'loading'}>
      {listState === 'loading' ? <p role="status">{t('creatorUi.memories.loading')}</p> : null}
      {listState === 'error' ? <div role="alert">
        <p>{t('creatorUi.memories.failed')}</p>
        <button type="button" className="button button--secondary" onClick={() => { setListState('loading'); setReloadToken(value => value + 1) }}>{t('creatorUi.memories.retry')}</button>
      </div> : null}
      {listState === 'ready' && items.length === 0 ? <p className="public-memories__empty">{t('creatorUi.memories.empty')}</p> : null}
      {items.length ? <ul className="public-memories__items" aria-label={t('creatorUi.memories.shared')}>
        {items.map((item, index) => <li key={item.id} ref={element => { itemRefs.current[index] = element }} tabIndex={-1} className="public-memory">
          <div className="public-memory__head">
            {item.emoji ? <span className="public-memory__emoji">{item.emoji}</span> : null}
            <strong className="public-memory__name">{item.displayName ?? t('creatorUi.memories.guest')}</strong>
            <time dateTime={item.createdAt}>{formatMinute(item.createdAt, i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR')}</time>
          </div>
          {item.text ? <p className="public-memory__text">{item.text}</p> : null}
          {item.media.length ? <div className="public-memory__media-list" aria-label={`${item.displayName ?? 'Misafir'} taraf\u0131ndan payla\u015f\u0131lan medya`}>
            {item.media.map(media => <PublicMemoryMedia key={media.assetId} api={api} publicCode={publicCode} memoryId={item.id} media={media} guestName={item.displayName ?? 'Misafir'} />)}
          </div> : null}
        </li>)}
      </ul> : null}
      {listState !== 'loading' && items.length < totalCount
        ? <button type="button" className="button button--secondary" disabled={loadingMore} onClick={() => void loadMore()}>
          {loadingMore ? t('common.loading') : t('creatorUi.memories.more')}
        </button>
        : null}
    </div>

    {formClosed ? <p ref={statusRef} tabIndex={-1} role="status" className="public-memories__notice">
      {submitState === 'quota' ? t('creatorUi.memories.quota') : t('creatorUi.memories.closed')}
    </p> : <form onSubmit={submit} noValidate aria-labelledby="public-memories-form-heading">
      <h3 id="public-memories-form-heading">{t('creatorUi.memories.leave')}</h3>
      {formError ? <div ref={summaryRef} className="public-rsvp__summary" role="alert" tabIndex={-1}>
        <p>{formError}</p>
        {errorList.length ? <ul>{errorList.map(([key, message]) => <li key={key}><a href={`#memory-${key}`}>{fieldLabel[key]}: {message}</a></li>)}</ul> : null}
      </div> : null}
      {mediaError ? <p role="alert" className="form-field__error">{mediaError}</p> : null}
      {mediaState ? <div className="public-memories__notice" role={mediaState.phase === 'failed' || mediaState.phase === 'rejected' ? 'alert' : 'status'} aria-live="polite">
        <p>{mediaState.message}</p>
        {mediaState.phase === 'uploading' ? mediaFiles.map((file, index) => <div key={`${file.name}-${index}`}>
          <span>{file.name}</span><progress aria-label={`${file.name} yükleme durumu`} max={100} value={mediaState.progress[index] ?? 0} />
          <span>{mediaState.progress[index] ?? 0}%</span>
        </div>) : null}
        {mediaState.phase === 'failed' ? <>
          {mediaState.failedIndex !== null && uploadContext.current ? <button type="button" className="button button--secondary" onClick={() => {
            const context = uploadContext.current
            if (context) void runUploads(context, mediaState.failedIndex!)
          }}>Tekrar dene</button> : null}
          <button type="button" className="button button--secondary" onClick={() => {
            uploadContext.current = null
            setMediaState(null)
            setMediaFiles([])
            setMediaDurations([])
          }}>Dosyaları yeniden seçin</button>
        </> : null}
        {mediaState.phase === 'processing' ? <button type="button" className="button button--secondary" onClick={() => { const context = uploadContext.current; if (context) void checkMediaProcessing(context) }}>Durumu kontrol et</button> : null}
      </div> : null}
      <p ref={submitState === 'success' ? statusRef : undefined} tabIndex={submitState === 'success' ? -1 : undefined} role="status" aria-live="polite" className="public-memories__notice" hidden={submitState !== 'success'}>
        {submitState === 'success' ? t('creatorUi.memories.thanks') : ''}
      </p>

      <div className="form-field">
        <label htmlFor="memory-displayName">{t('creatorUi.memories.displayName')}</label>
        <input id="memory-displayName" type="text" autoComplete="off" value={displayName} disabled={submitting}
          aria-invalid={Boolean(errors.displayName)} aria-describedby={describedBy('displayName', errors)}
          onChange={event => { setDisplayName(event.target.value); clearError('displayName', setErrors) }} />
        <p className="form-field__help" id="memory-displayName-help">{t('creatorUi.memories.displayHelp', { count: displayName.length, limit: limits.maxDisplayNameCharacters })}</p>
        {errors.displayName ? <p className="form-field__error" id="memory-displayName-error">{errors.displayName}</p> : null}
      </div>

      <div className="form-field">
        <label htmlFor="memory-text">{t('creatorUi.memories.text')}</label>
        <textarea id="memory-text" rows={5} value={text} disabled={submitting}
          aria-invalid={Boolean(errors.text)} aria-describedby={describedBy('text', errors)}
          onChange={event => { setText(event.target.value); clearError('text', setErrors) }} />
        <p className="form-field__help" id="memory-text-help">{t('creatorUi.memories.textHelp', { count: text.length, limit: limits.maxTextCharacters })}</p>
        {errors.text ? <p className="form-field__error" id="memory-text-error">{errors.text}</p> : null}
      </div>

      <div className="form-field">
        <label htmlFor="memory-emoji">{t('creatorUi.memories.emoji')}</label>
        <input id="memory-emoji" type="text" autoComplete="off" value={emoji} disabled={submitting}
          aria-invalid={Boolean(errors.emoji)} aria-describedby={describedBy('emoji', errors)}
          onChange={event => { setEmoji(event.target.value); clearError('emoji', setErrors) }} />
        <p className="form-field__help" id="memory-emoji-help">{t('creatorUi.memories.emojiHelp', { count: emoji.length, limit: limits.maxEmojiCharacters })}</p>
        {errors.emoji ? <p className="form-field__error" id="memory-emoji-error">{errors.emoji}</p> : null}
      </div>

      {uploadLimits?.enabled ? <div className="form-field">
        <label htmlFor="memory-media">{t('creatorUi.memories.media')}</label>
        <input id="memory-media" type="file" accept="image/*,video/*" multiple disabled={submitting || Boolean(uploadContext.current)} onChange={event => { void chooseMedia(event.target.files); event.target.value = '' }} />
        <p className="form-field__help">{t('creatorUi.memories.mediaHelp', { count: uploadLimits.maxMediaItems })}</p>
        {mediaFiles.length ? <ul aria-label="Seçilen medya dosyaları">{mediaFiles.map((file, index) => <li key={`${file.name}-${index}`}>{file.name} ({formatBytes(file.size)})</li>)}</ul> : null}
      </div> : null}

      <button type="submit" className="button button--primary" disabled={submitting}>{submitting ? t('creatorUi.memories.submitting') : t('creatorUi.memories.submit')}</button>
    </form>}
  </section>
}

function PublicMemoryMedia({ api, publicCode, memoryId, media, guestName }: {
  api: DavetiyeApiClient
  publicCode: string
  memoryId: string
  media: PublicMemoryReadyMedia
  guestName: string
}) {
  const { t } = useTranslation()
  const containerRef = useRef<HTMLElement>(null)
  const [visible, setVisible] = useState(false)
  const [retryToken, setRetryToken] = useState(0)
  const [delivery, setDelivery] = useState<PublicMemoryMediaDelivery | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    if (visible) return
    const element = containerRef.current
    if (!element || typeof IntersectionObserver === 'undefined') {
      const timeout = window.setTimeout(() => setVisible(true), 0)
      return () => window.clearTimeout(timeout)
    }
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) {
        setVisible(true)
        observer.disconnect()
      }
    }, { rootMargin: '160px' })
    observer.observe(element)
    return () => observer.disconnect()
  }, [visible])

  useEffect(() => {
    if (!visible) return
    const controller = new AbortController()
    let active = true
    void api.getAntiforgeryToken(controller.signal).then(csrfToken =>
      api.createPublicMemoryMediaDelivery(publicCode, memoryId, media.assetId, csrfToken, controller.signal),
    )
      .then(result => {
        if (active) setDelivery(result)
      })
      .catch(() => {
        if (active && !controller.signal.aborted) setFailed(true)
      })
    return () => {
      active = false
      controller.abort()
    }
  }, [api, publicCode, memoryId, media.assetId, visible, retryToken])

  const loading = visible && delivery === null && !failed
  const showFailure = failed
  const retry = () => {
    setFailed(false)
    setDelivery(null)
    setRetryToken(value => value + 1)
  }

  return <figure ref={containerRef} className="public-memory__media" style={{ margin: 0, minWidth: 0 }}>
    {loading ? <p role="status">{t('creatorUi.memories.loadingMedia')}</p> : null}
    {showFailure ? <div role="alert">
      <p>{t('creatorUi.memories.mediaUnavailable')}</p>
      <button type="button" className="button button--secondary" aria-label={t('creatorUi.memories.guestMediaAlt', { name: guestName })} onClick={retry}>{t('creatorUi.memories.uploadRetry')}</button>
    </div> : null}
    {!showFailure && delivery?.kind === 'image' ? <img
      src={delivery.url}
      alt={t('creatorUi.memories.guestMediaAlt', { name: guestName })}
      loading="lazy"
      referrerPolicy="no-referrer"
      onError={() => setFailed(true)}
      style={{ display: 'block', width: '100%', maxWidth: '100%', maxHeight: '70vh', objectFit: 'contain' }}
    /> : null}
    {!showFailure && delivery?.kind === 'video' ? <iframe
      className="public-invitation-media__video"
      src={delivery.url}
      title={t('creatorUi.memories.guestMediaAlt', { name: guestName })}
      referrerPolicy="no-referrer"
      sandbox="allow-scripts allow-same-origin allow-presentation"
      allow="accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture"
      allowFullScreen
      loading="lazy"
      onError={() => setFailed(true)}
    /> : null}
    <figcaption className="visually-hidden">{media.kind === 'Image' ? 'PaylaÅŸÄ±lan fotoÄŸraf' : 'PaylaÅŸÄ±lan video'}</figcaption>
  </figure>
}

function describedBy(key: FieldKey, errors: FieldErrors) {
  return errors[key] ? `memory-${key}-help memory-${key}-error` : `memory-${key}-help`
}

function clearError(key: FieldKey, setErrors: React.Dispatch<React.SetStateAction<FieldErrors>>) {
  setErrors(current => {
    if (!current[key]) return current
    const next = { ...current }
    delete next[key]
    return next
  })
}

function countGraphemes(value: string): number {
  if (!value) return 0
  const Segmenter = (Intl as unknown as { Segmenter?: new (locale?: string, options?: { granularity: 'grapheme' }) => { segment(input: string): Iterable<unknown> } }).Segmenter
  if (!Segmenter) return 1
  return Array.from(new Segmenter(undefined, { granularity: 'grapheme' }).segment(value)).length
}

function formatMinute(value: string, locale: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function mediaKind(file: File): GuestMediaKind | null {
  if (file.type.startsWith('image/')) return 'Image'
  if (file.type.startsWith('video/')) return 'Video'
  return null
}

function readVideoDuration(file: File): Promise<number> {
  return new Promise(resolve => {
    const video = document.createElement('video')
    const objectUrl = URL.createObjectURL(file)
    const finish = (duration: number) => {
      URL.revokeObjectURL(objectUrl)
      video.removeAttribute('src')
      video.load()
      resolve(duration)
    }
    video.preload = 'metadata'
    video.onloadedmetadata = () => finish(video.duration)
    video.onerror = () => finish(Number.NaN)
    video.src = objectUrl
  })
}

function formatBytes(value: number): string {
  if (value < 1024 * 1024) return `${Math.ceil(value / 1024)} KB`
  return `${(value / (1024 * 1024)).toFixed(1)} MB`
}
