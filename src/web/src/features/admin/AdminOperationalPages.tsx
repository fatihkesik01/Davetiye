import { useEffect, useState } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'
import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminAuditListItem,
  type AdminPaymentListItem,
} from '../../api/generated/client'

const pageSize = 50

type OperationalPage = 'payments' | 'audit'

export function AdminOperationalPage({ kind }: { kind: OperationalPage }) {
  return <AdminOperationalContent key={kind} kind={kind} />
}

function AdminOperationalContent({ kind }: { kind: OperationalPage }) {
  const { t, i18n } = useTranslation()
  const tRef = useLatestT()
  const locale = i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR'
  const numberFormat = new Intl.NumberFormat(locale)
  const [api] = useState(() => new DavetiyeApiClient())
  const [page, setPage] = useState(1)
  const [payments, setPayments] = useState<AdminPaymentListItem[]>([])
  const [audit, setAudit] = useState<AdminAuditListItem[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [loadedRequestKey, setLoadedRequestKey] = useState('')
  const [error, setError] = useState<{ requestKey: string; message: string } | null>(null)
  const [reloadVersion, setReloadVersion] = useState(0)
  const requestKey = `${kind}:${page}:${reloadVersion}`
  const loading = loadedRequestKey !== requestKey
  const visibleError = error?.requestKey === requestKey ? error.message : null

  useEffect(() => {
    const controller = new AbortController()

    const request = kind === 'payments'
      ? api.getAdminPayments(page, pageSize, controller.signal)
      : api.getAdminAudit(page, pageSize, controller.signal)

    void request
      .then(result => {
        setTotalCount(result.totalCount)
        if (kind === 'payments') setPayments(result.items as AdminPaymentListItem[])
        else setAudit(result.items as AdminAuditListItem[])
        setLoadedRequestKey(requestKey)
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return
        setError({
          requestKey,
          message: reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)
            ? tRef.current('adminUi.operational.authRequired')
            : tRef.current('adminUi.operational.loadError'),
        })
        setLoadedRequestKey(requestKey)
      })

    return () => controller.abort()
  }, [api, kind, page, reloadVersion, requestKey, tRef])

  const title = t(kind === 'payments' ? 'adminUi.operational.payments' : 'adminUi.operational.audit')
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <section className="admin-operational" aria-labelledby="admin-operational-title" aria-busy={loading}>
      <header className="admin-overview__heading">
        <div>
          <h2 id="admin-operational-title">{title}</h2>
          <p>{kind === 'payments'
          ? t('adminUi.operational.paymentsDescription')
          : t('adminUi.operational.auditDescription')}</p>
        </div>
        <p className="admin-operational__count">{t('adminUi.operational.total', { count: numberFormat.format(totalCount) })}</p>
      </header>

      {loading ? <p className="admin-operational__status" role="status">{t('adminUi.operational.loading', { title })}</p> : null}
      {!loading && visibleError ? (
        <div className="admin-overview__error" role="alert">
          <h3>{t('adminUi.operational.loadFailed', { title })}</h3>
          <p>{visibleError}</p>
          <button className="button button--secondary" type="button" onClick={() => setReloadVersion(current => current + 1)}>{t('adminUi.operational.retry')}</button>
        </div>
      ) : null}

      {!loading && !visibleError && totalCount === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>{t('adminUi.operational.emptyTitle')}</h3>
          <p>{t(kind === 'payments' ? 'adminUi.operational.paymentsEmpty' : 'adminUi.operational.auditEmpty')}</p>
        </div>
      ) : null}

      {!loading && !visibleError && totalCount > 0 ? (
        <div className="admin-operational__table-wrap" tabIndex={0} aria-label={t('adminUi.operational.tableScroll', { title })}>
          {kind === 'payments' ? <PaymentsTable items={payments} locale={locale} /> : <AuditTable items={audit} locale={locale} />}
        </div>
      ) : null}

      {!loading && !visibleError && totalCount > 0 ? (
        <nav className="admin-operational__pagination" aria-label={t('adminUi.operational.pages', { title })}>
          <button className="button button--secondary" type="button" disabled={page <= 1} onClick={() => setPage(current => Math.max(1, current - 1))}>{t('adminUi.operational.previous')}</button>
          <span aria-live="polite">{t('adminUi.operational.page', { current: numberFormat.format(page), total: numberFormat.format(totalPages) })}</span>
          <button className="button button--secondary" type="button" disabled={page >= totalPages} onClick={() => setPage(current => current + 1)}>{t('adminUi.operational.next')}</button>
        </nav>
      ) : null}
    </section>
  )
}

function PaymentsTable({ items, locale }: { items: AdminPaymentListItem[]; locale: string }) {
  const { t } = useTranslation()
  const translate = (key: string) => t(key)
  return (
    <table className="admin-operational__table">
      <caption className="visually-hidden">{t('adminUi.operational.paymentCaption')}</caption>
      <thead><tr><th scope="col">{t('adminUi.operational.reference')}</th><th scope="col">{t('adminUi.operational.status')}</th><th scope="col">{t('adminUi.operational.plan')}</th><th scope="col">{t('adminUi.operational.amount')}</th><th scope="col">{t('adminUi.operational.created')}</th><th scope="col">{t('adminUi.operational.updated')}</th><th scope="col">{t('adminUi.operational.reversal')}</th></tr></thead>
      <tbody>{items.map(payment => (
        <tr key={payment.id}>
          <td data-label={t('adminUi.operational.reference')}><span className="admin-operational__reference">{payment.reference}</span></td>
          <td data-label={t('adminUi.operational.status')}>{paymentStatus(payment.status, translate)}</td>
          <td data-label={t('adminUi.operational.plan')}>{planLabel(payment.planKey, translate)}</td>
          <td data-label={t('adminUi.operational.amount')}>{formatMoney(payment.amount, payment.currency, locale)}</td>
          <td data-label={t('adminUi.operational.created')}>{formatDate(payment.createdAtUtc, locale)}</td>
          <td data-label={t('adminUi.operational.updated')}>{formatDate(payment.updatedAtUtc, locale)}</td>
          <td data-label={t('adminUi.operational.reversal')}>{payment.reversalKind ? `${reversalLabel(payment.reversalKind, translate)}${payment.reversedAtUtc ? ` · ${formatDate(payment.reversedAtUtc, locale)}` : ''}` : '—'}</td>
        </tr>
      ))}</tbody>
    </table>
  )
}

function AuditTable({ items, locale }: { items: AdminAuditListItem[]; locale: string }) {
  const { t } = useTranslation()
  return (
    <table className="admin-operational__table admin-operational__table--audit">
      <caption className="visually-hidden">{t('adminUi.operational.auditCaption')}</caption>
      <thead><tr><th scope="col">{t('adminUi.operational.event')}</th><th scope="col">{t('adminUi.operational.date')}</th><th scope="col">{t('adminUi.operational.actor')}</th><th scope="col">{t('adminUi.operational.subject')}</th></tr></thead>
      <tbody>{items.map(record => (
        <tr key={record.id}>
          <td data-label={t('adminUi.operational.event')}>{record.eventType}</td>
          <td data-label={t('adminUi.operational.date')}>{formatDate(record.occurredAtUtc, locale)}</td>
          <td data-label={t('adminUi.operational.actor')}><span className="admin-operational__identifier">{record.actorId}</span></td>
          <td data-label={t('adminUi.operational.subject')}><span className="admin-operational__identifier">{record.subjectId}</span></td>
        </tr>
      ))}</tbody>
    </table>
  )
}

function formatDate(value: string, locale: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function formatMoney(amount: number, currency: string, locale: string): string {
  try {
    return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount)
  } catch {
    return `${new Intl.NumberFormat(locale).format(amount)} ${currency}`
  }
}

function paymentStatus(status: string, t: (key: string) => string): string {
  const known: Record<string, string> = { Pending: 'Pending', Unknown: 'Unknown', Succeeded: 'Succeeded', Failed: 'Failed', Canceled: 'Canceled', Reversed: 'Reversed' }
  return t(known[status] ? `adminUi.operational.statusValues.${known[status]}` : 'adminUi.operational.statusValues.other')
}

function reversalLabel(kind: string, t: (key: string) => string): string {
  const known: Record<string, string> = { FullRefund: 'FullRefund', FinalLostChargeback: 'FinalLostChargeback' }
  return t(known[kind] ? `adminUi.operational.reversalValues.${known[kind]}` : 'adminUi.operational.reversalValues.other')
}

function planLabel(planKey: string, t: (key: string) => string): string {
  const known: Record<string, string> = { standard: 'standard', premium: 'premium' }
  return t(known[planKey] ? `adminUi.operational.plans.${known[planKey]}` : 'adminUi.operational.plans.other')
}
