import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { ApiRequestError, DavetiyeApiClient, type OrganizationSubscriptionSnapshot } from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'

type PageState = 'loading' | 'ready' | 'error'

export function OrganizationSubscriptionPage({ api: providedApi }: { api?: DavetiyeApiClient }) {
  const api = useMemo(() => providedApi ?? new DavetiyeApiClient(), [providedApi])
  const [subscription, setSubscription] = useState<OrganizationSubscriptionSnapshot | null>(null)
  const [pageState, setPageState] = useState<PageState>('loading')
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null)
  const confirmationHeading = useRef<HTMLHeadingElement>(null)
  const liveMessage = useRef<HTMLParagraphElement>(null)
  const cancelButton = useRef<HTMLButtonElement>(null)

  const loadSubscription = useCallback(async (signal?: AbortSignal) => {
    try {
      const current = await api.getOrganizationSubscription(signal)
      setSubscription(current)
      setPageState('ready')
    } catch {
      if (!signal?.aborted) setPageState('error')
    }
  }, [api])

  useEffect(() => {
    const controller = new AbortController()
    void Promise.resolve().then(() => loadSubscription(controller.signal))
    return () => controller.abort()
  }, [loadSubscription])

  function beginCancellation() {
    setMessage(null)
    setConfirming(true)
    requestAnimationFrame(() => confirmationHeading.current?.focus())
  }

  async function confirmCancellation() {
    if (!subscription || busy) return
    setBusy(true)
    setMessage(null)
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const result = await api.cancelOrganizationSubscription(subscription.subscriptionId, csrfToken)
      setConfirming(false)
      let latest: OrganizationSubscriptionSnapshot | null
      try {
        latest = await api.getOrganizationSubscription()
      } catch {
        setPageState('error')
        setMessage({ kind: 'success', text: 'İptal isteği sunucuya ulaştı. Güncel abonelik durumunu görmek için yeniden yükleyin.' })
        requestAnimationFrame(() => liveMessage.current?.focus())
        return
      }
      setSubscription(latest)
      setMessage({
        kind: 'success',
        text: latest?.status === 'Canceled'
          ? `Yenileme kapatıldı. Erişiminiz ${formatDate(latest.paidThroughAtUtc)} tarihine kadar sürecek. İptal onayı e-posta adresinize gönderilecek.`
          : result.outcome === 'Duplicate'
            ? 'İptal isteğiniz daha önce işlenmiş. Abonelik durumu yenilendi.'
            : 'İptal isteğiniz alındı ve abonelik durumu yenilendi.',
      })
      requestAnimationFrame(() => liveMessage.current?.focus())
    } catch (error) {
      const isConflict = error instanceof ApiRequestError && error.status === 409
      setMessage({ kind: 'error', text: cancellationErrorMessage(error) })
      if (isConflict) {
        try {
          const latest = await api.getOrganizationSubscription()
          setSubscription(latest)
          setPageState('ready')
          if (latest?.status !== 'Active') setConfirming(false)
        } catch {
          setPageState('error')
        }
      }
      requestAnimationFrame(() => liveMessage.current?.focus())
    } finally {
      setBusy(false)
    }
  }

  if (pageState === 'loading') return <LoadingState label="Abonelik bilgileri yükleniyor…" />

  if (pageState === 'error') {
    return (
      <section className="organization-subscription" aria-labelledby="organization-subscription-title">
        <h2 id="organization-subscription-title">Organization aboneliği</h2>
        <p ref={liveMessage} tabIndex={-1} role="alert">{message?.text ?? 'Abonelik bilgileri şu anda yüklenemiyor. Biraz sonra yeniden deneyin.'}</p>
        <button className="button button--secondary" type="button" onClick={() => { setMessage(null); setPageState('loading'); void loadSubscription() }}>
          Yeniden dene
        </button>
      </section>
    )
  }

  return (
    <section className="organization-subscription" aria-labelledby="organization-subscription-title">
      <header className="organization-subscription__header">
        <div>
          <p className="organization-subscription__eyebrow">Plan ve ödeme</p>
          <h2 id="organization-subscription-title">Organization aboneliği</h2>
        </div>
        {subscription ? <span className={`organization-subscription__status organization-subscription__status--${subscription.status.toLowerCase()}`}>
          {statusLabel(subscription.status)}
        </span> : null}
      </header>

      {message ? <p ref={liveMessage} tabIndex={-1} className={`organization-subscription__message organization-subscription__message--${message.kind}`} role={message.kind === 'error' ? 'alert' : 'status'} aria-live={message.kind === 'success' ? 'polite' : undefined}>{message.text}</p> : null}

      {!subscription ? (
        <div className="organization-subscription__empty">
          <h3>Abonelik bilgisi bulunmuyor</h3>
          <p>Bu hesap için görüntülenecek bir Organization aboneliği bulunmuyor. Abonelik durumunuz sunucudan doğrulandığında bu alanda gösterilir.</p>
        </div>
      ) : (
        <>
          <dl className="organization-subscription__details">
            <div>
              <dt>Plan</dt>
              <dd>{subscription.planDisplayName}</dd>
            </div>
            <div>
              <dt>Ücret</dt>
              <dd>{formatPrice(subscription.priceAmount, subscription.currency)} / {billingPeriodLabel(subscription.billingPeriod)}</dd>
            </div>
            <div>
              <dt>{subscription.status === 'Active' ? 'Sonraki yenileme' : 'Erişim bitişi'}</dt>
              <dd><time dateTime={subscription.paidThroughAtUtc}>{formatDate(subscription.paidThroughAtUtc)}</time></dd>
            </div>
          </dl>

          {subscription.status === 'Active' ? (
            <>
              <p className="organization-subscription__explanation">Aboneliğiniz aylık olarak otomatik yenilenir. İptal etmediğiniz sürece yenileme devam eder.</p>
              {!confirming ? (
                <button ref={cancelButton} className="button button--secondary" type="button" onClick={beginCancellation}>
                  Aboneliği iptal et
                </button>
              ) : (
                <section className="organization-subscription__confirmation" aria-labelledby="cancel-subscription-title" aria-describedby="cancel-subscription-description">
                  <h3 id="cancel-subscription-title" ref={confirmationHeading} tabIndex={-1}>Abonelik iptalini onayla</h3>
                  <p id="cancel-subscription-description">Bir sonraki aylık yenileme durur. Yayın ve public erişim {formatDate(subscription.paidThroughAtUtc)} tarihine kadar sürer. Bu tarihten sonra erişim durur; davetiye ve içerik verileriniz korunur. İptal onayı e-posta ile gönderilir.</p>
                  <div className="organization-subscription__actions">
                    <button className="button button--danger" type="button" disabled={busy} onClick={() => void confirmCancellation()}>
                      {busy ? 'İptal ediliyor…' : 'İptali onayla'}
                    </button>
                    <button className="button button--secondary" type="button" disabled={busy} onClick={() => {
                      setConfirming(false)
                      requestAnimationFrame(() => cancelButton.current?.focus())
                    }}>
                      Vazgeç
                    </button>
                  </div>
                </section>
              )}
            </>
          ) : subscription.status === 'Canceled' ? (
            <p className="organization-subscription__explanation">Yenileme kapalı. Erişim {formatDate(subscription.paidThroughAtUtc)} tarihine kadar sürer. Bu tarihte yayın ve public erişim durur; davetiye ve içerik verileriniz korunur.</p>
          ) : (
            <p className="organization-subscription__explanation">Abonelik süresi sona erdi. Public erişim ve aktif yayın durdu; davetiye ve içerik verileriniz korunuyor.</p>
          )}
        </>
      )}
    </section>
  )
}

function statusLabel(status: OrganizationSubscriptionSnapshot['status']) {
  if (status === 'Active') return 'Aktif'
  if (status === 'Canceled') return 'Yenileme kapalı'
  return 'Süresi doldu'
}

function formatDate(value: string) {
  const date = new Date(value)
  if (!Number.isFinite(date.getTime())) return '—'
  return new Intl.DateTimeFormat('tr-TR', { dateStyle: 'long', timeStyle: 'short', timeZone: 'Europe/Istanbul' }).format(date)
}

function formatPrice(amount: number, currency: string) {
  try {
    return new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 2 }).format(amount) + ` ${currency}`
  } catch {
    return `${amount} ${currency}`
  }
}

function billingPeriodLabel(period: string) {
  return period === 'monthly' ? 'ay' : period
}

function cancellationErrorMessage(error: unknown) {
  if (error instanceof ApiRequestError && error.status === 409) {
    return 'Abonelik durumu değişti. Sunucudaki güncel abonelik durumu yenilendi.'
  }
  return 'İptal isteği tamamlanamadı. Abonelik durumunu yeniden yükleyip tekrar deneyin.'
}
