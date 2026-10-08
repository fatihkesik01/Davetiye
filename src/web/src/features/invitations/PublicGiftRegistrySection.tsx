import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { type DavetiyeApiClient, type PublicGiftGuestReservation, type PublicGiftRegistry } from '../../api/generated/client'

export function PublicGiftRegistrySection({ api, publicCode, templateKey }: {
  api: DavetiyeApiClient
  publicCode: string
  templateKey: string
}) {
  const { t, i18n } = useTranslation()
  const [registry, setRegistry] = useState<PublicGiftRegistry | null>(null)
  const [fullName, setFullName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const storageKey = `davetiye-gift-reservations:${publicCode}`
  const [ownedReservations, setOwnedReservations] = useState<PublicGiftGuestReservation[]>(() => {
    try {
      const cached = JSON.parse(localStorage.getItem(storageKey) ?? '[]') as Array<{ reservationId?: string }>
      return cached.filter(item => typeof item.reservationId === 'string').map(item => ({
        reservationId: item.reservationId!, itemId: '', itemName: '', quantity: 0,
      }))
    } catch { return [] }
  })
  const [busyId, setBusyId] = useState<string | null>(null)
  const [message, setMessage] = useState('')
  const reload = async () => {
    try { setRegistry(await api.getPublicGiftRegistry(publicCode)) } catch { setRegistry(null) }
    try {
      const mine = await api.getMyPublicGiftReservations(publicCode)
      setOwnedReservations(mine)
      localStorage.setItem(storageKey, JSON.stringify(mine.map(({ reservationId }) => ({ reservationId }))))
    } catch { /* On a downgrade, keep this device’s cached cancellation handles; the public list remains hidden. */ }
  }
  useEffect(() => {
    const controller = new AbortController()
    void api.listTemplates(controller.signal).then(async templates => {
      if (controller.signal.aborted || !templates.find(item => item.key === templateKey)?.supportedModules.includes('giftRegistry')) return
      const result = await api.getPublicGiftRegistry(publicCode, controller.signal)
      if (!controller.signal.aborted) {
        setRegistry(result)
        try {
          const mine = await api.getMyPublicGiftReservations(publicCode, controller.signal)
          setOwnedReservations(mine)
          localStorage.setItem(storageKey, JSON.stringify(mine.map(({ reservationId }) => ({ reservationId }))))
        } catch { /* A closed feature hides the public list while prior same-browser cancellations remain usable. */ }
      }
    }).catch(() => { /* Optional public module: failures keep it hidden. */ })
    return () => controller.abort()
  }, [api, publicCode, storageKey, templateKey])

  if (!registry?.items.length && ownedReservations.length === 0) return null
  async function reserve(itemId: string, remaining: number) {
    const quantity = Number((document.getElementById(`gift-quantity-${itemId}`) as HTMLInputElement | null)?.value || '1')
    const emailInput = document.getElementById('public-gift-email') as HTMLInputElement | null
    if (!Number.isSafeInteger(quantity) || quantity < 1 || quantity > remaining || !fullName.trim()) { setMessage(t('creatorUi.gifts.validate')); return }
    if (email.trim() && (!emailInput?.checkValidity() || email.trim().length < 3)) { setMessage(t('creatorUi.gifts.invalidEmail')); emailInput?.focus(); return }
    setBusyId(itemId); setMessage('')
    try {
      const csrf = await api.getAntiforgeryToken()
      const result = await api.reservePublicGift(publicCode, { itemId, quantity, fullName, email: email || null, phone: phone || null }, csrf)
      const next = [...ownedReservations.filter(item => item.reservationId !== result.reservationId),
        { reservationId: result.reservationId, itemId, itemName: registry?.items.find(item => item.id === itemId)?.name ?? '', quantity }]
      setOwnedReservations(next)
      localStorage.setItem(storageKey, JSON.stringify(next.map(({ reservationId }) => ({ reservationId }))))
      await reload(); setMessage(t('creatorUi.gifts.saved'))
    } catch { setMessage(t('creatorUi.gifts.reserveFailed')) }
    finally { setBusyId(null) }
  }
  async function cancel(reservationId: string) {
    setBusyId(reservationId); setMessage('')
    try {
      const csrf = await api.getAntiforgeryToken()
      await api.cancelPublicGiftReservation(publicCode, reservationId, csrf)
      const next = ownedReservations.filter(item => item.reservationId !== reservationId)
      setOwnedReservations(next)
      localStorage.setItem(storageKey, JSON.stringify(next.map(({ reservationId }) => ({ reservationId }))))
      await reload(); setMessage(t('creatorUi.gifts.canceled'))
    } catch { setMessage(t('creatorUi.gifts.cancelFailed')) }
    finally { setBusyId(null) }
  }
  return <section className="public-gift-registry" aria-labelledby="public-gift-registry-title">
    <h2 id="public-gift-registry-title">{t('creatorUi.gifts.title')}</h2>
    <p>{t('creatorUi.gifts.intro')}</p>
    {registry?.items.length ? <>
    <div className="public-gift-registry__contact">
      <label>{t('creatorUi.gifts.fullName')}<input autoComplete="name" required maxLength={200} value={fullName} onChange={event => setFullName(event.target.value)} /></label>
      <label>{t('creatorUi.gifts.email')}<input id="public-gift-email" autoComplete="email" type="email" minLength={3} maxLength={320} value={email} onChange={event => setEmail(event.target.value)} /></label>
      <label>{t('creatorUi.gifts.phone')}<input autoComplete="tel" type="tel" maxLength={32} value={phone} onChange={event => setPhone(event.target.value)} /></label>
    </div>
    <ul aria-label={t('creatorUi.gifts.list')}>
      {registry.items.map(item => <li key={item.id}>
        <span>{item.name}</span>
        <span>{t('creatorUi.gifts.left', { remaining: new Intl.NumberFormat(i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR').format(item.remainingQuantity), requested: new Intl.NumberFormat(i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR').format(item.requestedQuantity) })}</span>
        {item.remainingQuantity > 0 ? <><label htmlFor={`gift-quantity-${item.id}`}>{t('creatorUi.gifts.quantity')}</label>
          <input id={`gift-quantity-${item.id}`} type="number" min={1} max={item.remainingQuantity} defaultValue={1} disabled={busyId !== null} />
          <button type="button" disabled={busyId !== null || !fullName.trim()} onClick={() => void reserve(item.id, item.remainingQuantity)}>{t('creatorUi.gifts.reserve')}</button></> : <span>{t('creatorUi.gifts.allReserved')}</span>}
      </li>)}
    </ul>
    </> : <p>{t('creatorUi.gifts.hidden')}</p>}
    {ownedReservations.map(reservation => <button key={reservation.reservationId} type="button" className="button button--secondary" disabled={busyId !== null} onClick={() => void cancel(reservation.reservationId)}>
      {reservation.itemName ? t('creatorUi.gifts.cancelReservation', { name: reservation.itemName, quantity: new Intl.NumberFormat(i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR').format(reservation.quantity) }) : t('creatorUi.gifts.cancelSaved')}
    </button>)}
    <p role="status" aria-live="polite">{message}</p>
  </section>
}
