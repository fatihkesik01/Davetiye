import { useEffect, useMemo, useRef, useState } from 'react'

import { ApiRequestError, DavetiyeApiClient, type InvitationTrashItem, type InvitationTrashPage as TrashPage } from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { InternalLink } from '../../components/ui/InternalLink'
import { navigate } from '../../routes/navigation'
import { formatPublicationTime } from './publicationPresentation'

export function InvitationTrashPage() {
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
      setMessage(trashErrorMessage(error))
    })
    return () => controller.abort()
  }, [api, pageNumber, attempt])

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
      setMessage(trashErrorMessage(error))
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

  return <section className="trash-page" aria-labelledby="trash-heading" aria-busy={busy || loading}>
    <div className="dashboard-heading">
      <div><p className="eyebrow">Creator paneli</p><h2 id="trash-heading">Silinen davetiyeleriniz</h2>
        <p>Geri yükleme yalnızca taslağı geri getirir. Davetiye otomatik olarak yayına açılmaz.</p></div>
      <InternalLink className="button button--secondary" to="/panel/davetiyeler">Davetiyelere dön</InternalLink>
    </div>
    <p>Son tarihler {displayZone} saat dilimiyle gösterilir. Belirtilen süreden sonra davetiye ve ona bağlı içerikler kalıcı silme sürecine alınır.</p>
    {loading ? <LoadingState label="Çöp kutusu yükleniyor" /> : null}
    {message ? <section className="inline-alert" role="alert" aria-labelledby="trash-error-heading">
      <h3 id="trash-error-heading" ref={errorHeading} tabIndex={-1}>İşlem tamamlanamadı</h3><p>{message}</p>
      <button className="button button--secondary" type="button" disabled={busy || loading} onClick={retry}>Listeyi yenile</button>
    </section> : null}
    {!loading && !failed && page?.items.length === 0 ? <div className="catalog-empty"><h3>Çöp kutunuz boş</h3><p>Geri yüklenebilecek silinmiş bir davetiye bulunmuyor.</p></div> : null}
    {page && !failed ? <ul className="draft-list trash-list">
      {page.items.map(item => <li key={item.invitationId}>
        <article className="draft-list__item">
          <div>
            <h3>{item.headline?.trim() || 'İsimsiz davetiye'}</h3>
            <p>Silinme: <time dateTime={item.deletedAtUtc}>{formatPublicationTime(item.deletedAtUtc, displayZone)}</time></p>
            <p>Geri yükleme için son tarih: <time dateTime={item.purgeAfterUtc}>{formatPublicationTime(item.purgeAfterUtc, displayZone)}</time></p>
            {expired(item) ? <p>Geri yükleme süresi doldu.</p> : page.serverNowUtc ? <p>{remainingTime(item.purgeAfterUtc, page.serverNowUtc)}</p> : null}
          </div>
          <button className="button button--secondary" type="button" disabled={busy || loading || expired(item)} onClick={() => {
            setSelected(item)
            setMessage('')
          }}>Geri yükle<span className="visually-hidden">: {item.headline?.trim() || 'İsimsiz davetiye'}</span></button>
        </article>
      </li>)}
    </ul> : null}
    {selected && validConfirmation ? <section className="publication-confirmation" aria-labelledby="trash-restore-heading">
      <h3 id="trash-restore-heading" ref={confirmationHeading} tabIndex={-1}>Taslağı geri yükleyin</h3>
      <p><strong>{selected.headline?.trim() || 'İsimsiz davetiye'}</strong> taslak olarak geri yüklenir. Public erişim açılmaz. Mevcut davetiye ve public bağlantısı korunur.</p>
      <p>Yayın hakkınızın eski süresi devam ediyorsa ayrıca açıkça yeniden yayınlayabilirsiniz; süre uzamaz. Eski süre bitmişse uygun yeni bir yayın hakkı gerekir.</p>
      <div className="button-row">
        <button className="button button--primary" type="button" disabled={busy || loading || failed} onClick={() => void restore()}>{busy ? 'Geri yükleniyor…' : 'Taslak olarak geri yükle'}</button>
        <button className="button button--secondary" type="button" disabled={busy} onClick={() => setSelected(null)}>Vazgeç</button>
      </div>
    </section> : null}
    {selected && !validConfirmation ? <p role="status">Geri yükleme bilgileri değişti veya süre doldu. Listeyi yenileyip yeniden seçin.</p> : null}
    {page && page.totalCount > page.pageSize ? <nav className="button-row trash-pagination" aria-label="Çöp kutusu sayfaları">
      <button className="button button--secondary" type="button" disabled={busy || loading || pageNumber <= 1} onClick={() => { setSelected(null); setPageNumber(value => value - 1) }}>Önceki sayfa</button>
      <p>Sayfa {page.page} / {Math.ceil(page.totalCount / page.pageSize)}</p>
      <button className="button button--secondary" type="button" disabled={busy || loading || pageNumber >= Math.ceil(page.totalCount / page.pageSize)} onClick={() => { setSelected(null); setPageNumber(value => value + 1) }}>Sonraki sayfa</button>
    </nav> : null}
  </section>
}

function remainingTime(deadline: string, serverNow: string): string {
  const minutes = Math.max(0, Math.floor((Date.parse(deadline) - Date.parse(serverNow)) / 60_000))
  if (minutes >= 24 * 60) return `Son kontrol sırasında yaklaşık ${Math.floor(minutes / (24 * 60))} gün ${Math.floor(minutes % (24 * 60) / 60)} saat kalmıştı.`
  if (minutes >= 60) return `Son kontrol sırasında yaklaşık ${Math.floor(minutes / 60)} saat ${minutes % 60} dakika kalmıştı.`
  return `Son kontrol sırasında yaklaşık ${minutes} dakika kalmıştı.`
}

function trashErrorMessage(error: unknown): string {
  if (error instanceof ApiRequestError) {
    if (/expired|deadline|purge/i.test(error.problem?.code ?? '')) return 'Geri yükleme süresi dolduğu için davetiye geri yüklenemiyor.'
    if (error.status === 409) return 'Davetiye başka bir işlemle değişti veya geri yükleme süresi doldu. Güncel listeyi yenileyip yeniden gözden geçirin.'
    if (error.status === 404) return 'Davetiye bulunamadı veya artık geri yüklenemiyor.'
    if (error.status === 403) return 'Bu işlem için hesabınızın kullanıma açık olması gerekir.'
  }
  return 'Çöp kutusu işlemi tamamlanamadı. Bağlantınızı kontrol edip yeniden deneyin.'
}
