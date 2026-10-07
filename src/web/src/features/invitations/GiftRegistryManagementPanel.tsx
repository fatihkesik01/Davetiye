import { useEffect, useState } from 'react'

import {
  ApiRequestError,
  type CreatorGiftItem,
  type CreatorGiftReservation,
  type DavetiyeApiClient,
} from '../../api/generated/client'

interface Props { api: DavetiyeApiClient; invitationId: string }

export function GiftRegistryManagementPanel({ api, invitationId }: Props) {
  const [items, setItems] = useState<CreatorGiftItem[]>([])
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const [busy, setBusy] = useState(false)
  const [name, setName] = useState('')
  const [quantity, setQuantity] = useState('1')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editName, setEditName] = useState('')
  const [editQuantity, setEditQuantity] = useState('1')
  const [message, setMessage] = useState('')
  const [reservations, setReservations] = useState<CreatorGiftReservation[]>([])

  useEffect(() => {
    const controller = new AbortController()
    void api.getCreatorGiftItems(invitationId, controller.signal).then(result => {
      if (controller.signal.aborted) return
      setItems(result)
      setLoading(false)
      setFailed(false)
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setFailed(true)
      setLoading(false)
    })
    return () => controller.abort()
  }, [api, invitationId, attempt])

  useEffect(() => {
    const controller = new AbortController()
    void api.getCreatorGiftReservations(invitationId, controller.signal).then(result => {
      if (!controller.signal.aborted) setReservations(result)
    }).catch(() => { if (!controller.signal.aborted) setReservations([]) })
    return () => controller.abort()
  }, [api, invitationId, attempt])

  async function withCsrf<T>(action: (token: string) => Promise<T>, success: (value: T) => void | Promise<void>) {
    if (busy) return
    setBusy(true)
    setMessage('Kaydediliyor…')
    try {
      const token = await api.getAntiforgeryToken()
      const result = await action(token)
      await success(result)
      setMessage('Hediye listesi güncellendi.')
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        try {
          setItems(await api.getCreatorGiftItems(invitationId))
          setMessage('Bu item değişti veya aktif rezervasyonları var. Güncel liste yüklendi.')
        } catch {
          setMessage('Bu item değişti veya aktif rezervasyonları var. Liste yenilenemedi; tekrar deneyin.')
        }
      }
      else if (error instanceof ApiRequestError && error.status === 400) setMessage('Bilgileri kontrol edip tekrar deneyin.')
      else if (error instanceof ApiRequestError && error.status === 404) setMessage('Davetiyeye erişilemiyor. Sayfayı yenileyip tekrar deneyin.')
      else if (error instanceof ApiRequestError && error.status === 429) setMessage('Kısa sürede çok fazla işlem yapıldı. Biraz bekleyip tekrar deneyin.')
      else setMessage('İşlem tamamlanamadı. Bağlantınızı kontrol edip tekrar deneyin.')
    } finally {
      setBusy(false)
    }
  }

  function startEdit(item: CreatorGiftItem) {
    setEditingId(item.id)
    setEditName(item.name)
    setEditQuantity(String(item.requestedQuantity))
  }

  function move(item: CreatorGiftItem, direction: -1 | 1) {
    const next = [...items]
    const index = next.findIndex(value => value.id === item.id)
    const target = index + direction
    if (index < 0 || target < 0 || target >= next.length) return
    const current = next[index]!
    next[index] = next[target]!
    next[target] = current
    void withCsrf(token => api.reorderCreatorGiftItems(invitationId, {
      items: next.map(value => ({ id: value.id, revision: value.revision })),
    }, token), setItems)
  }

  const titleId = `gift-registry-title-${invitationId}`
  return <section className="rsvp-panel gift-registry-panel" aria-labelledby={titleId}>
    <div className="rsvp-panel__heading"><div><h3 id={titleId}>Hediye listesi</h3><p>İstenen ürünleri ve miktarları yönetin.</p></div></div>
    {loading ? <p role="status">Hediye listesi yükleniyor…</p> : null}
    {failed ? <div className="inline-alert" role="alert"><p>Hediye listesi yüklenemedi.</p>
      <button type="button" className="button button--secondary" onClick={() => { setLoading(true); setFailed(false); setAttempt(value => value + 1) }}>Tekrar dene</button></div> : null}
    {!loading && !failed ? <>
      <form className="gift-registry-panel__create" onSubmit={event => {
        event.preventDefault()
        const requestedQuantity = Number(quantity)
        if (!Number.isSafeInteger(requestedQuantity) || requestedQuantity < 1) { setMessage('Miktar en az 1 olmalıdır.'); return }
        void withCsrf(token => api.createCreatorGiftItem(invitationId, { name, requestedQuantity }, token), item => {
          setItems(current => [...current, item].sort((a, b) => a.ordinal - b.ordinal))
          setName('')
          setQuantity('1')
        })
      }}>
        <label>Ürün adı<input required maxLength={200} value={name} disabled={busy} onChange={event => setName(event.target.value)} /></label>
        <label>İstenen miktar<input required type="number" min={1} step={1} value={quantity} disabled={busy} onChange={event => setQuantity(event.target.value)} /></label>
        <button className="button button--primary" type="submit" disabled={busy || !name.trim()}>Ürün ekle</button>
      </form>
      {items.length === 0 ? <p>Henüz hediye eklenmedi.</p> : <ol className="gift-registry-panel__list">
        {items.map((item, index) => <li key={item.id}>
          {editingId === item.id ? <form className="gift-registry-panel__edit" onSubmit={event => {
            event.preventDefault()
            const requestedQuantity = Number(editQuantity)
            if (!Number.isSafeInteger(requestedQuantity) || requestedQuantity < item.reservedQuantity) {
              setMessage(`Miktar rezerve edilmiş ${item.reservedQuantity} adetten az olamaz.`); return
            }
            void withCsrf(token => api.updateCreatorGiftItem(invitationId, item.id, {
              name: editName, requestedQuantity, expectedRevision: item.revision,
            }, token), updated => {
              setItems(current => current.map(value => value.id === updated.id ? updated : value))
              setEditingId(null)
            })
          }}>
            <label>Ürün adı<input required maxLength={200} value={editName} disabled={busy} onChange={event => setEditName(event.target.value)} /></label>
            <label>İstenen miktar<input required type="number" min={item.reservedQuantity} step={1} value={editQuantity} disabled={busy} onChange={event => setEditQuantity(event.target.value)} /></label>
            <button className="button button--primary" type="submit" disabled={busy || !editName.trim()}>Kaydet</button>
            <button className="button button--secondary" type="button" disabled={busy} onClick={() => setEditingId(null)}>Vazgeç</button>
          </form> : <>
            <div className="gift-registry-panel__details">
              <strong>{item.name}</strong><span>İstenen {item.requestedQuantity} · Rezerve {item.reservedQuantity} · Kalan {item.remainingQuantity}</span>
            </div>
            <div className="gift-registry-panel__actions">
              <button className="button button--secondary" type="button" disabled={busy || index === 0} aria-label={`${item.name} ürününü yukarı taşı`} onClick={() => move(item, -1)}>Yukarı</button>
              <button className="button button--secondary" type="button" disabled={busy || index === items.length - 1} aria-label={`${item.name} ürününü aşağı taşı`} onClick={() => move(item, 1)}>Aşağı</button>
              <button className="button button--secondary" type="button" disabled={busy} onClick={() => startEdit(item)}>Düzenle</button>
              <button className="button button--secondary" type="button" disabled={busy || item.reservedQuantity > 0}
                aria-describedby={item.reservedQuantity > 0 ? `gift-delete-hint-${item.id}` : undefined}
                onClick={() => void withCsrf(token => api.deleteCreatorGiftItem(invitationId, item.id, item.revision, token), async () => setItems(await api.getCreatorGiftItems(invitationId)))}>Sil</button>
              {item.reservedQuantity > 0 ? <span id={`gift-delete-hint-${item.id}`}>Önce aktif rezervasyonları tek tek kaldırın.</span> : null}
            </div>
          </>}
        </li>)}
      </ol>}
      <p className="gift-registry-panel__privacy">Davetliler ürünün rezerve edildiğini ve kalan miktarı görür; rezervasyon yapan kişinin adı ve iletişim bilgileri yalnızca size gösterilir.</p>
      {reservations.length ? <section aria-labelledby={`gift-reservations-${invitationId}`}>
        <h4 id={`gift-reservations-${invitationId}`}>Aktif rezervasyonlar</h4>
        <ul>{reservations.map(reservation => <li key={reservation.id}>
          <span><strong>{reservation.guestFullName}</strong> · {items.find(item => item.id === reservation.itemId)?.name ?? 'Ürün'} · {reservation.quantity} adet
            {reservation.email ? ` · ${reservation.email}` : ''}{reservation.phone ? ` · ${reservation.phone}` : ''}</span>
          <button type="button" className="button button--secondary" disabled={busy} onClick={() => void withCsrf(token =>
            api.removeCreatorGiftReservation(invitationId, reservation.id, token), async () => {
              setReservations(await api.getCreatorGiftReservations(invitationId))
              setItems(await api.getCreatorGiftItems(invitationId))
            })}>Rezervasyonu kaldır</button>
        </li>)}</ul>
      </section> : null}
    </> : null}
    <p role="status" aria-live="polite">{message}</p>
  </section>
}
