import { useEffect, useMemo, useState } from 'react'

import { DavetiyeApiClient } from '../../api/generated/client'
import { CheckoutApiError, createInvitationCheckout, listIndividualPurchasePlans, type IndividualPurchasePlan, type InvitationCheckout } from './checkoutApi'

interface InvitationCheckoutPanelProps {
  invitationId: string
  api: DavetiyeApiClient
  disabled?: boolean
  redirectTo?: (url: string) => void
}

type CheckoutState = 'idle' | 'creating' | 'uncertain' | 'conflict' | 'ready' | 'failed' | 'unavailable'

export function InvitationCheckoutPanel({ invitationId, api, disabled = false, redirectTo = url => window.location.assign(url) }: InvitationCheckoutPanelProps) {
  const [plans, setPlans] = useState<IndividualPurchasePlan[]>([])
  const [plansState, setPlansState] = useState<'loading' | 'ready' | 'failed' | 'unavailable'>('loading')
  const [selectedKey, setSelectedKey] = useState<IndividualPurchasePlan['key'] | ''>('')
  const [idempotency, setIdempotency] = useState<{ key: string; planKey: IndividualPurchasePlan['key'] } | null>(null)
  const [checkout, setCheckout] = useState<InvitationCheckout | null>(null)
  const [checkoutState, setCheckoutState] = useState<CheckoutState>('idle')
  const [message, setMessage] = useState('')

  useEffect(() => {
    let active = true
    const controller = new AbortController()
    void listIndividualPurchasePlans(api, controller.signal).then(result => {
      if (!active) return
      setPlans(result)
      setPlansState(result.length ? 'ready' : 'unavailable')
    }).catch(error => {
      if (!active) return
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (error instanceof CheckoutApiError && (error.status === 403 || error.status === 404)) {
        setPlans([])
        setPlansState('unavailable')
        return
      }
      setPlansState('failed')
    })
    return () => { active = false; controller.abort() }
  }, [api])

  const selectedPlan = useMemo(() => plans.find(plan => plan.key === selectedKey) ?? null, [plans, selectedKey])
  const busy = disabled || checkoutState === 'creating'
  const canRetrySameAttempt = checkoutState === 'uncertain' && idempotency !== null

  async function beginCheckout(retrySame = false) {
    if (busy || !selectedPlan) return
    if (checkoutState === 'conflict' || checkoutState === 'ready' || checkoutState === 'unavailable') return

    let request = idempotency
    if (retrySame) {
      if (!request || request.planKey !== selectedPlan.key) return
    } else {
      request = { key: crypto.randomUUID(), planKey: selectedPlan.key }
      setIdempotency(request)
    }

    setCheckoutState('creating')
    setCheckout(null)
    setMessage('Ödeme bağlantısı hazırlanıyor…')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const result = await createInvitationCheckout(api, invitationId, request.planKey, request.key, csrfToken)
      if (result.status === 'Failed' || result.status === 'Canceled') {
        setIdempotency(null)
        setCheckout(null)
        setCheckoutState('failed')
        setMessage(result.status === 'Failed'
          ? 'Ödeme sağlayıcısı bu denemeyi başarısız olarak doğruladı. Yeni bir ödeme denemesi başlatabilirsiniz.'
          : 'Ödeme denemesi sağlayıcı tarafından iptal edildi. Yeni bir ödeme denemesi başlatabilirsiniz.')
        return
      }
      if (result.status === 'Succeeded') {
        setIdempotency(null)
        setCheckout(null)
        setCheckoutState('conflict')
        setMessage('Ödeme sağlayıcısı işlemi başarılı olarak doğruladı. Yayın hakkı durumu sunucuda güncelleniyor; yeni bir ödeme başlatmayın.')
        return
      }
      if (result.status === 'Unknown') {
        setCheckoutState('uncertain')
        setMessage('Ödeme sağlayıcısının sonucu henüz doğrulanamadı. Yayın hakkı verilmedi; aynı isteği yeniden deneyebilirsiniz.')
        return
      }
      if (!isSafeCheckoutUrl(result.checkoutUrl)) {
        setCheckoutState('uncertain')
        setMessage('Ödeme denemesi başlatıldı ancak güvenli bir ödeme bağlantısı doğrulanamadı. Bu deneme yayın hakkı oluşturmaz. Aynı isteği yeniden deneyin; yeni bir deneme oluşturulmadı.')
        return
      }
      setCheckout(result)
      setCheckoutState('ready')
      setMessage('Bu deneme henüz ödeme veya yayın hakkı anlamına gelmez. Sağlayıcıda ödeme tamamlanıp sunucu tarafından doğrulanmalıdır.')
    } catch (error) {
      if (error instanceof CheckoutApiError && error.status === 409) {
        setIdempotency(null)
        setCheckoutState('conflict')
        setMessage('Bu davetiye için bekleyen veya sonucu doğrulanamayan başka bir ödeme denemesi var. Yeni bir deneme başlatılamaz; mevcut denemenin sonucu netleşmeden tekrar ödeme yapmayın.')
      } else if (error instanceof CheckoutApiError && error.status === 404) {
        setIdempotency(null)
        setCheckoutState('failed')
        setMessage('Davetiye bulunamadı veya bu işlem için erişim izniniz yok.')
      } else if (error instanceof CheckoutApiError && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
        setIdempotency(null)
        setCheckoutState('failed')
        setMessage('Ödeme isteği doğrulanamadı. Davetiye ve paket seçiminizi kontrol edip yeniden deneyin.')
      } else {
        setCheckoutState('uncertain')
        setMessage('Ödeme isteğinin sonucu doğrulanamadı. Yayın hakkı verildiğini varsaymayın; aynı isteği güvenle yeniden deneyebilirsiniz.')
      }
    }
  }

  function continueToProvider() {
    if (checkoutState !== 'ready' || !checkout?.checkoutUrl || !isSafeCheckoutUrl(checkout.checkoutUrl)) return
    redirectTo(checkout.checkoutUrl)
  }

  return <section className="invitation-checkout" aria-labelledby="checkout-heading" aria-busy={busy || plansState === 'loading'}>
    <h4 id="checkout-heading">Bireysel yayın hakkı</h4>
    <p>Standard veya Premium tek seferlik yayın hakkı seçebilirsiniz. Ödeme doğrulanmadan davetiye için yeni bir hak açılmaz.</p>
    {plansState === 'loading' ? <p role="status">Güncel paket fiyatları yükleniyor…</p> : null}
    {plansState === 'failed' ? <div className="inline-alert" role="alert">
      <p>Paket fiyatları şu anda alınamıyor. Tutarı doğrulamadan ödeme başlatamazsınız.</p>
      <button type="button" className="button button--secondary" onClick={() => {
        setPlansState('loading')
        void listIndividualPurchasePlans(api).then(result => {
          setPlans(result)
          setPlansState(result.length ? 'ready' : 'unavailable')
        }).catch(() => setPlansState('failed'))
      }}>Fiyatları yeniden yükle</button>
    </div> : null}
    {plansState === 'unavailable' ? <p className="invitation-checkout__muted" role="status">Bu hesap için bireysel Standard/Premium paket seçeneği sunulmuyor.</p> : null}
    {plansState === 'ready' ? <>
      <fieldset className="invitation-checkout__plans" disabled={busy || checkoutState === 'ready' || checkoutState === 'conflict' || canRetrySameAttempt}>
        <legend>Yayın hakkı paketi</legend>
        {plans.map(plan => <label className="invitation-checkout__plan" key={plan.key}>
          <input type="radio" name={`checkout-plan-${invitationId}`} value={plan.key} checked={selectedKey === plan.key} onChange={() => {
            setSelectedKey(plan.key)
            setCheckout(null)
            setCheckoutState('idle')
            setMessage('')
            setIdempotency(null)
          }} />
          <span>
            <strong>{plan.displayName}</strong>
            <span>{formatPlanPrice(plan)}</span>
          </span>
        </label>)}
      </fieldset>
      {checkoutState === 'ready' && checkout ? <div className="invitation-checkout__summary" aria-label="Ödeme özeti">
        <p><strong>{planName(plans, checkout.planKey)}</strong></p>
        <p className="invitation-checkout__price">{formatPlanPrice(checkout)}</p>
      </div> : null}
      {message ? <p className={`invitation-checkout__message${checkoutState === 'conflict' || checkoutState === 'failed' ? ' invitation-checkout__message--error' : ''}`} role={checkoutState === 'conflict' || checkoutState === 'failed' ? 'alert' : 'status'} aria-live="polite">{message}</p> : null}
      {checkoutState === 'ready' ? <button type="button" className="button button--primary" disabled={disabled} onClick={continueToProvider}>Ödemeye geç</button> : null}
      {checkoutState !== 'ready' && checkoutState !== 'conflict' ? <button type="button" className="button button--primary" disabled={busy || !selectedPlan} onClick={() => void beginCheckout(canRetrySameAttempt)}>
        {checkoutState === 'creating' ? 'Bağlantı hazırlanıyor…' : canRetrySameAttempt ? 'Aynı isteği tekrar dene' : 'Ödemeye geç'}
      </button> : null}
    </> : null}
  </section>
}

function formatPlanPrice(plan: Pick<IndividualPurchasePlan, 'amount' | 'currency' | 'billingPeriod'> | Pick<InvitationCheckout, 'amount' | 'currency' | 'billingPeriod'>): string {
  const amount = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 2 }).format(plan.amount)
  const period = plan.billingPeriod === 'one-time' ? 'tek sefer' : plan.billingPeriod
  return `${amount} ${plan.currency} / ${period}`
}

function planName(plans: IndividualPurchasePlan[], planKey: string): string {
  return plans.find(plan => plan.key === planKey)?.displayName ?? planKey
}

function isSafeCheckoutUrl(value: string | null): value is string {
  if (!value) return false
  try {
    const url = new URL(value)
    if (url.username || url.password) return false
    if (url.protocol === 'https:') {
      return ['api.iyzipay.com', 'sandbox-api.iyzipay.com'].includes(url.hostname) &&
        (url.port === '' || url.port === '443')
    }
    return import.meta.env.DEV && url.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname) &&
      url.port === '5173'
  } catch {
    return false
  }
}
