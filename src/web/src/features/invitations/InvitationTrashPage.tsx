import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

import { ApiRequestError, DavetiyeApiClient, type InvitationTrashItem, type InvitationTrashPage as TrashPage } from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { InternalLink } from '../../components/ui/InternalLink'
import { navigate } from '../../routes/navigation'

export function InvitationTrashPage() {
  const { t, i18n } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [page, setPage] = useState<TrashPage | null>(null)
  const [pageNumber, setPageNumber] = useState(1)
  const [attempt, setAttempt] = useState(0)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [selected, setSelected] = useState<InvitationTrashItem | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const confirmationHeading = useRef<HTMLHeadingElement>(null)
  const errorHeading = useRef<HTMLHeadingElement>(null)
  const displayZone = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, [])

  useEffect(() => {
    const controller = new AbortController()
    queueMicrotask(() => {
      if (controller.signal.aborted) return
      setLoading(true)
      setFailed(false)
    })
    void api.listInvitationTrash(pageNumber, 20, controller.signal).then(result => {
      if (controller.signal.aborted) return
      setPage(result)
      setLoading(false)
    }).catch(error => {
      if (controller.signal.aborted) return
      setLoading(false)
      setFailed(true)
      setMessage(trashErrorMessage(error, t))
    })
    return () => controller.abort()
  }, [api, pageNumber, attempt, t])

  useEffect(() => {
    if (selected) confirmationHeading.current?.focus()
  }, [selected])
  useEffect(() => {
    if (message) errorHeading.current?.focus()
  }, [message])

  const expired = (item: InvitationTrashItem) => Boolean(page?.serverNowUtc && Date.parse(page.serverNowUtc) >= Date.parse(item.purgeAfterUtc))
  const currentItem = selected ? page?.items.find(item => item.invitationId === selected.invitationId) : null
  const validConfirmation = Boolean(selected && currentItem && JSON.stringify(selected.expected) === JSON.stringify(currentItem.expected) && !expired(currentItem))

  const restore = async () => {
    if (!selected || !validConfirmation || busy || loading || failed) return
    setBusy(true)
    setMessage('')
    try {
      const token = await api.getAntiforgeryToken()
      const restored = await api.restoreInvitation(selected.invitationId, { expected: selected.expected }, token)
      navigate(`/panel/davetiyeler/${restored.invitationId}/duzenle`)
    } catch (error) {
      setSelected(null)
      setMessage(trashErrorMessage(error, t))
      if (error instanceof ApiRequestError && [404, 409].includes(error.status)) setAttempt(value => value + 1)
    } finally {
      setBusy(false)
    }
  }

  const retry = () => {
    setSelected(null)
    setMessage('')
    setAttempt(value => value + 1)
  }

  const locale = i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR'
  return <section className="trash-page" aria-labelledby="trash-heading" aria-busy={busy || loading}>
    <div className="dashboard-heading">
      <div><p className="eyebrow">{t('creatorUi.trash.eyebrow')}</p><h2 id="trash-heading">{t('creatorUi.trash.title')}</h2>
        <p>{t('creatorUi.trash.intro')}</p></div>
      <InternalLink className="button button--secondary" to="/panel/davetiyeler">{t('creatorUi.trash.back')}</InternalLink>
    </div>
    <p>{t('creatorUi.trash.deadlines', { zone: displayZone })}</p>
    {loading ? <LoadingState label={t('creatorUi.trash.loading')} /> : null}
    {message ? <section className="inline-alert" role="alert" aria-labelledby="trash-error-heading">
      <h3 id="trash-error-heading" ref={errorHeading} tabIndex={-1}>{t('creatorUi.trash.errorTitle')}</h3><p>{message}</p>
      <button className="button button--secondary" type="button" disabled={busy || loading} onClick={retry}>{t('creatorUi.trash.retry')}</button>
    </section> : null}
    {!loading && !failed && page?.items.length === 0 ? <div className="catalog-empty"><h3>{t('creatorUi.trash.emptyTitle')}</h3><p>{t('creatorUi.trash.emptyBody')}</p></div> : null}
    {page && !failed ? <ul className="draft-list trash-list">
      {page.items.map(item => <li key={item.invitationId}>
        <article className="draft-list__item">
          <div>
            <h3>{item.headline?.trim() || t('creatorUi.trash.unnamed')}</h3>
            <p>{t('creatorUi.trash.deleted')} <time dateTime={item.deletedAtUtc}>{formatTrashTime(item.deletedAtUtc, displayZone, locale)}</time></p>
            <p>{t('creatorUi.trash.restoreDeadline')} <time dateTime={item.purgeAfterUtc}>{formatTrashTime(item.purgeAfterUtc, displayZone, locale)}</time></p>
            {expired(item) ? <p>{t('creatorUi.trash.expired')}</p> : page.serverNowUtc ? <p>{remainingTime(item.purgeAfterUtc, page.serverNowUtc, t)}</p> : null}
          </div>
          <button className="button button--secondary" type="button" disabled={busy || loading || expired(item)} onClick={() => {
            setSelected(item)
            setMessage('')
          }}>{t('creatorUi.trash.restore')}<span className="visually-hidden">: {item.headline?.trim() || t('creatorUi.trash.unnamed')}</span></button>
        </article>
      </li>)}
    </ul> : null}
    {selected && validConfirmation ? <section className="publication-confirmation" aria-labelledby="trash-restore-heading">
      <h3 id="trash-restore-heading" ref={confirmationHeading} tabIndex={-1}>{t('creatorUi.trash.confirmTitle')}</h3>
      <p><strong>{selected.headline?.trim() || t('creatorUi.trash.unnamed')}</strong> {t('creatorUi.trash.publicOff')}</p>
      <p>{t('creatorUi.trash.oldGrant')}</p>
      <div className="button-row">
        <button className="button button--primary" type="button" disabled={busy || loading || failed} onClick={() => void restore()}>{busy ? t('creatorUi.trash.restoring') : t('creatorUi.trash.restoreDraft')}</button>
        <button className="button button--secondary" type="button" disabled={busy} onClick={() => setSelected(null)}>{t('creatorUi.trash.cancel')}</button>
      </div>
    </section> : null}
    {selected && !validConfirmation ? <p role="status">{t('creatorUi.trash.changed')}</p> : null}
    {page && page.totalCount > page.pageSize ? <nav className="button-row trash-pagination" aria-label={t('creatorUi.trash.pages')}>
      <button className="button button--secondary" type="button" disabled={busy || loading || pageNumber <= 1} onClick={() => { setSelected(null); setPageNumber(value => value - 1) }}>{t('creatorUi.trash.previous')}</button>
      <p>{t('creatorUi.trash.page', { current: page.page, total: Math.ceil(page.totalCount / page.pageSize) })}</p>
      <button className="button button--secondary" type="button" disabled={busy || loading || pageNumber >= Math.ceil(page.totalCount / page.pageSize)} onClick={() => { setSelected(null); setPageNumber(value => value + 1) }}>{t('creatorUi.trash.next')}</button>
    </nav> : null}
  </section>
}

function remainingTime(deadline: string, serverNow: string, t: TFunction): string {
  const minutes = Math.max(0, Math.floor((Date.parse(deadline) - Date.parse(serverNow)) / 60_000))
  if (minutes >= 24 * 60) return t('creatorUi.trash.remainingDays', { days: Math.floor(minutes / (24 * 60)), hours: Math.floor(minutes % (24 * 60) / 60) })
  if (minutes >= 60) return t('creatorUi.trash.remainingHours', { hours: Math.floor(minutes / 60), minutes: minutes % 60 })
  return t('creatorUi.trash.remainingMinutes', { minutes })
}

function formatTrashTime(instant: string, timeZone: string, locale: string): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short', timeZone }).format(new Date(instant))
  } catch {
    return instant
  }
}

function trashErrorMessage(error: unknown, t: TFunction): string {
  if (error instanceof ApiRequestError) {
    if (/expired|deadline|purge/i.test(error.problem?.code ?? '')) return t('creatorUi.trash.expiredError')
    if (error.status === 409) return t('creatorUi.trash.conflictError')
    if (error.status === 404) return t('creatorUi.trash.notFoundError')
    if (error.status === 403) return t('creatorUi.trash.forbiddenError')
  }
  return t('creatorUi.trash.generalError')
}
