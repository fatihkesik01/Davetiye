import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import QRCode from 'qrcode'

import { DavetiyeApiClient, type InvitationPublicationStatus, type InvitationStatistics } from '../../api/generated/client'

const statisticLabels: Array<[keyof InvitationStatistics, string]> = [
  ['totalPageViews', 'totalViews'],
  ['rsvpResponseCount', 'rsvpCount'],
  ['participantCountTotal', 'participants'],
  ['memoryCount', 'memories'],
  ['readyMediaCount', 'media'],
  ['activeGiftReservationCount', 'gifts'],
]

function isInvitationStatistics(value: InvitationStatistics): boolean {
  return statisticLabels.every(([field]) => Number.isSafeInteger(value[field]) && value[field] >= 0)
}

export function CreatorSharePanel({ status }: { status: InvitationPublicationStatus | null }) {
  const { t, i18n } = useTranslation()
  const [message, setMessage] = useState('')
  const [qr, setQr] = useState<{ url: string; image: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const linkRef = useRef<HTMLInputElement>(null)
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [baseUrl, setBaseUrl] = useState<string | null>(null)
  const [statisticsResult, setStatisticsResult] = useState<{ invitationId: string; statistics: InvitationStatistics | null; error: boolean } | null>(null)
  const [statisticsAttempt, setStatisticsAttempt] = useState(0)
  const invitationId = status?.invitationId
  const shareAllowed = Boolean(status && status.effectiveState !== 'Draft' && status.published)
  useEffect(() => {
    if (!shareAllowed || !invitationId) return
    const controller = new AbortController()
    void api.getPublicInvitationCapabilities(controller.signal).then(result => {
      if (controller.signal.aborted) return
      try {
        const value = new URL(result.canonicalBaseUrl ?? '')
        if (!['http:', 'https:'].includes(value.protocol) || value.username || value.password || value.search || value.hash) return
        setBaseUrl(value.href)
      } catch { /* Sharing waits for a valid server-configured canonical origin. */ }
    }).catch(() => {})
    void api.getInvitationStatistics(invitationId, controller.signal).then(result => {
      if (controller.signal.aborted) return
      if (isInvitationStatistics(result)) setStatisticsResult({ invitationId, statistics: result, error: false })
      else setStatisticsResult({ invitationId, statistics: null, error: true })
    }).catch(() => {
      if (!controller.signal.aborted) setStatisticsResult({ invitationId, statistics: null, error: true })
    })
    return () => controller.abort()
  }, [api, invitationId, shareAllowed, statisticsAttempt])
  if (!status || status.effectiveState === 'Draft' || !status.published || !/^[a-f0-9]{64}$/.test(status.publicCode)) return null
  const url = baseUrl ? new URL(`/davetiye/${status.publicCode}`, baseUrl).href : null
  const currentStatisticsResult = statisticsResult?.invitationId === invitationId ? statisticsResult : null
  const statistics = currentStatisticsResult?.statistics
  const statisticsLoading = Boolean(shareAllowed && invitationId && !currentStatisticsResult)
  const copy = async () => {
    if (!url) return
    try {
      await navigator.clipboard.writeText(url)
      setMessage(t('creatorUi.share.copied'))
    } catch {
      linkRef.current?.focus()
      linkRef.current?.select()
      setMessage(t('creatorUi.share.copyHelp'))
    }
  }
  const share = async () => {
    if (!url) return
    try { await navigator.share({ title: t('creatorUi.share.shareMessage'), url }); setMessage(t('creatorUi.share.shared')) }
    catch (error) { if (!(error instanceof DOMException && error.name === 'AbortError')) setMessage(t('creatorUi.share.shareError')) }
  }
  const showQr = async () => {
    if (!url) return
    setBusy(true)
    try {
      const image = await QRCode.toDataURL(url, { width: 512, margin: 4, errorCorrectionLevel: 'M' })
      setQr({ url, image })
      setMessage(t('creatorUi.share.qrReady'))
    } catch { setMessage(t('creatorUi.share.qrError')) }
    finally { setBusy(false) }
  }
  return <section className="creator-share-panel" aria-labelledby="creator-share-heading">
    <h3 id="creator-share-heading">{t('creatorUi.share.title')}</h3>
    <section aria-labelledby="creator-statistics-heading" aria-busy={statisticsLoading}>
      <h4 id="creator-statistics-heading">{t('creatorUi.share.statistics')}</h4>
      {statisticsLoading ? <p role="status">{t('creatorUi.share.loading')}</p> : null}
      {currentStatisticsResult?.error ? <div role="alert"><p>{t('creatorUi.share.failed')}</p><button type="button" onClick={() => { setStatisticsResult(null); setStatisticsAttempt(value => value + 1) }}>{t('creatorUi.share.retry')}</button></div> : null}
      {statistics ? <dl aria-label={t('creatorUi.share.statistics')}>
        {statisticLabels.map(([field, label]) => <div key={field}><dt>{t(`creatorUi.share.${label}`)}</dt><dd>{statistics[field].toLocaleString(i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR')}</dd></div>)}
      </dl> : null}
    </section>
    {status.effectiveState !== 'Active' ? <p>{t('creatorUi.share.protected')}</p> : null}
    {!url ? <p>{t('creatorUi.share.unavailable')}</p> : <>
    <label htmlFor="creator-public-url">{t('creatorUi.share.link')}</label>
    <input id="creator-public-url" ref={linkRef} value={url} readOnly onFocus={event => event.currentTarget.select()} />
    <div className="button-row">
      <button className="button button--primary" type="button" onClick={() => void copy()}>{t('creatorUi.share.copy')}</button>
      <a className="button button--secondary" href={`https://wa.me/?text=${encodeURIComponent(`${t('creatorUi.share.shareMessage')}: ${url}`)}`} target="_blank" rel="noopener noreferrer">{t('creatorUi.share.whatsapp')}</a>
      {typeof navigator.share === 'function' ? <button className="button button--secondary" type="button" onClick={() => void share()}>{t('creatorUi.share.deviceShare')}</button> : null}
      <button className="button button--secondary" type="button" disabled={busy} onClick={() => void showQr()}>{busy ? t('creatorUi.share.qrLoading') : t('creatorUi.share.showQr')}</button>
    </div>
    {qr?.url === url ? <div className="creator-share-qr"><img src={qr.image} width={256} height={256} alt={t('creatorUi.share.qrAlt')} /><a className="button button--secondary" href={qr.image} download="davetiye-qr.png">{t('creatorUi.share.downloadQr')}</a></div> : null}
    </>}
    <p role="status" aria-live="polite">{message}</p>
  </section>
}
