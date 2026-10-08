import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'

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
  const { t, i18n } = useTranslation()
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
    setMessage(t('creatorUi.checkout.preparingMessage'))
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const result = await createInvitationCheckout(api, invitationId, request.planKey, request.key, csrfToken)
      if (result.status === 'Failed' || result.status === 'Canceled') {
        setIdempotency(null)
        setCheckout(null)
        setCheckoutState('failed')
        setMessage(result.status === 'Failed'
          ? t('creatorUi.checkout.providerFailed')
          : t('creatorUi.checkout.canceled'))
        return
      }
      if (result.status === 'Succeeded') {
        setIdempotency(null)
        setCheckout(null)
        setCheckoutState('conflict')
        setMessage(t('creatorUi.checkout.succeeded'))
        return
      }
      if (result.status === 'Unknown') {
        setCheckoutState('uncertain')
        setMessage(t('creatorUi.checkout.unknown'))
        return
      }
      if (!isSafeCheckoutUrl(result.checkoutUrl)) {
        setCheckoutState('uncertain')
        setMessage(t('creatorUi.checkout.unsafeUrl'))
        return
      }
      setCheckout(result)
      setCheckoutState('ready')
      setMessage(t('creatorUi.checkout.ready'))
    } catch (error) {
      if (error instanceof CheckoutApiError && error.status === 409) {
        setIdempotency(null)
        setCheckoutState('conflict')
        setMessage(t('creatorUi.checkout.conflict'))
      } else if (error instanceof CheckoutApiError && error.status === 404) {
        setIdempotency(null)
        setCheckoutState('failed')
        setMessage(t('creatorUi.checkout.invitationMissing'))
      } else if (error instanceof CheckoutApiError && error.status >= 400 && error.status < 500 && error.status !== 408 && error.status !== 429) {
        setIdempotency(null)
        setCheckoutState('failed')
        setMessage(t('creatorUi.checkout.invalidRequest'))
      } else {
        setCheckoutState('uncertain')
        setMessage(t('creatorUi.checkout.uncertain'))
      }
    }
  }

  function continueToProvider() {
    if (checkoutState !== 'ready' || !checkout?.checkoutUrl || !isSafeCheckoutUrl(checkout.checkoutUrl)) return
    redirectTo(checkout.checkoutUrl)
  }

  return <section className="invitation-checkout" aria-labelledby="checkout-heading" aria-busy={busy || plansState === 'loading'}>
    <h4 id="checkout-heading">{t('creatorUi.checkout.title')}</h4>
    <p>{t('creatorUi.checkout.intro')}</p>
    {plansState === 'loading' ? <p role="status">{t('creatorUi.checkout.loading')}</p> : null}
    {plansState === 'failed' ? <div className="inline-alert" role="alert">
      <p>{t('creatorUi.checkout.failed')}</p>
      <button type="button" className="button button--secondary" onClick={() => {
        setPlansState('loading')
        void listIndividualPurchasePlans(api).then(result => {
          setPlans(result)
          setPlansState(result.length ? 'ready' : 'unavailable')
        }).catch(() => setPlansState('failed'))
      }}>{t('creatorUi.checkout.reloadPrices')}</button>
    </div> : null}
    {plansState === 'unavailable' ? <p className="invitation-checkout__muted" role="status">{t('creatorUi.checkout.unavailable')}</p> : null}
    {plansState === 'ready' ? <>
      <fieldset className="invitation-checkout__plans" disabled={busy || checkoutState === 'ready' || checkoutState === 'conflict' || canRetrySameAttempt}>
        <legend>{t('creatorUi.checkout.planLegend')}</legend>
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
            <span>{formatPlanPrice(plan, i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR', t('creatorUi.checkout.oneTime'))}</span>
          </span>
        </label>)}
      </fieldset>
      {checkoutState === 'ready' && checkout ? <div className="invitation-checkout__summary" aria-label={t('creatorUi.checkout.summary')}>
        <p><strong>{planName(plans, checkout.planKey)}</strong></p>
        <p className="invitation-checkout__price">{formatPlanPrice(checkout, i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR', t('creatorUi.checkout.oneTime'))}</p>
      </div> : null}
      {message ? <p className={`invitation-checkout__message${checkoutState === 'conflict' || checkoutState === 'failed' ? ' invitation-checkout__message--error' : ''}`} role={checkoutState === 'conflict' || checkoutState === 'failed' ? 'alert' : 'status'} aria-live="polite">{message}</p> : null}
      {checkoutState === 'ready' ? <button type="button" className="button button--primary" disabled={disabled} onClick={continueToProvider}>{t('creatorUi.checkout.pay')}</button> : null}
      {checkoutState !== 'ready' && checkoutState !== 'conflict' ? <button type="button" className="button button--primary" disabled={busy || !selectedPlan} onClick={() => void beginCheckout(canRetrySameAttempt)}>
        {checkoutState === 'creating' ? t('creatorUi.checkout.preparing') : canRetrySameAttempt ? t('creatorUi.checkout.retrySame') : t('creatorUi.checkout.pay')}
      </button> : null}
    </> : null}
  </section>
}

function formatPlanPrice(plan: Pick<IndividualPurchasePlan, 'amount' | 'currency' | 'billingPeriod'> | Pick<InvitationCheckout, 'amount' | 'currency' | 'billingPeriod'>, locale: string, oneTime: string): string {
  const amount = new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(plan.amount)
  const period = plan.billingPeriod === 'one-time' ? oneTime : plan.billingPeriod
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
