import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { ApiRequestError, DavetiyeApiClient, type OrganizationSubscriptionSnapshot } from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'

type PageState = 'loading' | 'ready' | 'error'

export function OrganizationSubscriptionPage({ api: providedApi }: { api?: DavetiyeApiClient }) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR'
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
        setMessage({ kind: 'success', text: t('creatorUi.subscription.cancelReached') })
        requestAnimationFrame(() => liveMessage.current?.focus())
        return
      }
      setSubscription(latest)
      setMessage({
        kind: 'success',
        text: latest?.status === 'Canceled'
          ? t('creatorUi.subscription.renewalOff', { date: formatDate(latest.paidThroughAtUtc, locale) })
          : result.outcome === 'Duplicate'
            ? t('creatorUi.subscription.duplicate')
            : t('creatorUi.subscription.accepted'),
      })
      requestAnimationFrame(() => liveMessage.current?.focus())
    } catch (error) {
      const isConflict = error instanceof ApiRequestError && error.status === 409
      setMessage({ kind: 'error', text: cancellationErrorMessage(error, t) })
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

  if (pageState === 'loading') return <LoadingState label={t('creatorUi.subscription.loading')} />

  if (pageState === 'error') {
    return (
      <section className="organization-subscription" aria-labelledby="organization-subscription-title">
        <h2 id="organization-subscription-title">{t('creatorUi.subscription.title')}</h2>
        <p ref={liveMessage} tabIndex={-1} role="alert">{message?.text ?? t('creatorUi.subscription.loadError')}</p>
        <button className="button button--secondary" type="button" onClick={() => { setMessage(null); setPageState('loading'); void loadSubscription() }}>
          {t('creatorUi.subscription.retry')}
        </button>
      </section>
    )
  }

  return (
    <section className="organization-subscription" aria-labelledby="organization-subscription-title">
      <header className="organization-subscription__header">
        <div>
          <p className="organization-subscription__eyebrow">{t('creatorUi.subscription.billing')}</p>
          <h2 id="organization-subscription-title">{t('creatorUi.subscription.title')}</h2>
        </div>
        {subscription ? <span className={`organization-subscription__status organization-subscription__status--${subscription.status.toLowerCase()}`}>
          {statusLabel(subscription.status, t)}
        </span> : null}
      </header>

      {message ? <p ref={liveMessage} tabIndex={-1} className={`organization-subscription__message organization-subscription__message--${message.kind}`} role={message.kind === 'error' ? 'alert' : 'status'} aria-live={message.kind === 'success' ? 'polite' : undefined}>{message.text}</p> : null}

      {!subscription ? (
        <div className="organization-subscription__empty">
          <h3>{t('creatorUi.subscription.missingTitle')}</h3>
          <p>{t('creatorUi.subscription.missingBody')}</p>
        </div>
      ) : (
        <>
          <dl className="organization-subscription__details">
            <div>
              <dt>{t('creatorUi.subscription.plan')}</dt>
              <dd>{subscription.planDisplayName}</dd>
            </div>
            <div>
              <dt>{t('creatorUi.subscription.price')}</dt>
              <dd>{formatPrice(subscription.priceAmount, subscription.currency, locale)} / {billingPeriodLabel(subscription.billingPeriod, t)}</dd>
            </div>
            <div>
              <dt>{subscription.status === 'Active' ? t('creatorUi.subscription.nextRenewal') : t('creatorUi.subscription.accessEnd')}</dt>
              <dd><time dateTime={subscription.paidThroughAtUtc}>{formatDate(subscription.paidThroughAtUtc, locale)}</time></dd>
            </div>
          </dl>

          {subscription.status === 'Active' ? (
            <>
              <p className="organization-subscription__explanation">{t('creatorUi.subscription.activeExplanation')}</p>
              {!confirming ? (
                <button ref={cancelButton} className="button button--secondary" type="button" onClick={beginCancellation}>
                  {t('creatorUi.subscription.cancel')}
                </button>
              ) : (
                <section className="organization-subscription__confirmation" aria-labelledby="cancel-subscription-title" aria-describedby="cancel-subscription-description">
                  <h3 id="cancel-subscription-title" ref={confirmationHeading} tabIndex={-1}>{t('creatorUi.subscription.confirmTitle')}</h3>
                  <p id="cancel-subscription-description">{t('creatorUi.subscription.confirmBody', { date: formatDate(subscription.paidThroughAtUtc, locale) })}</p>
                  <div className="organization-subscription__actions">
                    <button className="button button--danger" type="button" disabled={busy} onClick={() => void confirmCancellation()}>
                      {busy ? t('creatorUi.subscription.cancelBusy') : t('creatorUi.subscription.confirm')}
                    </button>
                    <button className="button button--secondary" type="button" disabled={busy} onClick={() => {
                      setConfirming(false)
                      requestAnimationFrame(() => cancelButton.current?.focus())
                    }}>
                      {t('common.cancel')}
                    </button>
                  </div>
                </section>
              )}
            </>
          ) : subscription.status === 'Canceled' ? (
            <p className="organization-subscription__explanation">{t('creatorUi.subscription.canceledExplanation', { date: formatDate(subscription.paidThroughAtUtc, locale) })}</p>
          ) : (
            <p className="organization-subscription__explanation">{t('creatorUi.subscription.expiredExplanation')}</p>
          )}
        </>
      )}
    </section>
  )
}

function statusLabel(status: OrganizationSubscriptionSnapshot['status'], t: (key: string) => string) {
  if (status === 'Active') return t('creatorUi.subscription.active')
  if (status === 'Canceled') return t('creatorUi.subscription.canceled')
  return t('creatorUi.subscription.expired')
}

function formatDate(value: string, locale: string) {
  const date = new Date(value)
  if (!Number.isFinite(date.getTime())) return '—'
    return new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeStyle: 'short', timeZone: 'Europe/Istanbul' }).format(date)
}

function formatPrice(amount: number, currency: string, locale: string) {
  try {
    return new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(amount) + ` ${currency}`
  } catch {
    return `${amount} ${currency}`
  }
}

function billingPeriodLabel(period: string, t: (key: string) => string) {
  return period === 'monthly' ? t('creatorUi.subscription.monthly') : period
}

function cancellationErrorMessage(error: unknown, t: (key: string) => string) {
  if (error instanceof ApiRequestError && error.status === 409) {
    return t('creatorUi.subscription.changed')
  }
  return t('creatorUi.subscription.cancelError')
}
