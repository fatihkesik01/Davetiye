import { useEffect, useMemo, useRef, useState } from 'react'
import QRCode from 'qrcode'

import { DavetiyeApiClient, type InvitationPublicationStatus, type InvitationStatistics } from '../../api/generated/client'

const statisticLabels: Array<[keyof InvitationStatistics, string]> = [
  ['totalPageViews', 'Toplam sayfa görüntüleme'],
  ['rsvpResponseCount', 'RSVP yanıtı sayısı'],
  ['participantCountTotal', 'Toplam katılımcı sayısı'],
  ['memoryCount', 'Anı sayısı'],
  ['readyMediaCount', 'Hazır medya sayısı'],
  ['activeGiftReservationCount', 'Aktif hediye rezervasyonu sayısı'],
]

function isInvitationStatistics(value: InvitationStatistics): boolean {
  return statisticLabels.every(([field]) => Number.isSafeInteger(value[field]) && value[field] >= 0)
}

export function CreatorSharePanel({ status }: { status: InvitationPublicationStatus | null }) {
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
      setMessage('Davetiye bağlantısı kopyalandı.')
    } catch {
      linkRef.current?.focus()
      linkRef.current?.select()
      setMessage('Bağlantıyı seçtik. Cihazınızın kopyalama seçeneğini kullanabilirsiniz.')
    }
  }
  const share = async () => {
    if (!url) return
    try { await navigator.share({ title: 'Davetiyemiz', url }); setMessage('Paylaşım tamamlandı.') }
    catch (error) { if (!(error instanceof DOMException && error.name === 'AbortError')) setMessage('Paylaşım açılamadı. Bağlantıyı kopyalayabilirsiniz.') }
  }
  const showQr = async () => {
    if (!url) return
    setBusy(true)
    try {
      const image = await QRCode.toDataURL(url, { width: 512, margin: 4, errorCorrectionLevel: 'M' })
      setQr({ url, image })
      setMessage('QR kodu hazır.')
    } catch { setMessage('QR kodu hazırlanamadı. Yeniden deneyebilirsiniz.') }
    finally { setBusy(false) }
  }
  return <section className="creator-share-panel" aria-labelledby="creator-share-heading">
    <h3 id="creator-share-heading">Davetiyenizi paylaşın</h3>
    <section aria-labelledby="creator-statistics-heading" aria-busy={statisticsLoading}>
      <h4 id="creator-statistics-heading">Davetiye istatistikleri</h4>
      {statisticsLoading ? <p role="status">İstatistikler yükleniyor…</p> : null}
      {currentStatisticsResult?.error ? <div role="alert"><p>İstatistikler şu anda alınamadı.</p><button type="button" onClick={() => { setStatisticsResult(null); setStatisticsAttempt(value => value + 1) }}>Tekrar dene</button></div> : null}
      {statistics ? <dl aria-label="Davetiye istatistikleri">
        {statisticLabels.map(([field, label]) => <div key={field}><dt>{label}</dt><dd>{statistics[field].toLocaleString('tr-TR')}</dd></div>)}
      </dl> : null}
    </section>
    {status.effectiveState !== 'Active' ? <p>Bağlantınız korunur. Davetiye yayında olduğunda davetliler içeriği görebilir.</p> : null}
    {!url ? <p>Paylaşım bağlantısı şu anda hazırlanamadı. Yayın durumunu yenileyerek yeniden deneyebilirsiniz.</p> : <>
    <label htmlFor="creator-public-url">Davetiye bağlantısı</label>
    <input id="creator-public-url" ref={linkRef} value={url} readOnly onFocus={event => event.currentTarget.select()} />
    <div className="button-row">
      <button className="button button--primary" type="button" onClick={() => void copy()}>Linki kopyala</button>
      <a className="button button--secondary" href={`https://wa.me/?text=${encodeURIComponent(`Davetiyemiz: ${url}`)}`} target="_blank" rel="noopener noreferrer">WhatsApp'ta paylaş</a>
      {typeof navigator.share === 'function' ? <button className="button button--secondary" type="button" onClick={() => void share()}>Cihazla paylaş</button> : null}
      <button className="button button--secondary" type="button" disabled={busy} onClick={() => void showQr()}>{busy ? 'QR hazırlanıyor…' : 'QR kodunu göster'}</button>
    </div>
    {qr?.url === url ? <div className="creator-share-qr"><img src={qr.image} width={256} height={256} alt="Davetiye bağlantısını açan QR kodu" /><a className="button button--secondary" href={qr.image} download="davetiye-qr.png">QR kodunu indir</a></div> : null}
    </>}
    <p role="status" aria-live="polite">{message}</p>
  </section>
}
