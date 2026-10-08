import { useEffect, useState } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'
import { ApiRequestError, DavetiyeApiClient, type AdminOverview } from '../../api/generated/client'

export function AdminOverviewPage() {
  const { t, i18n } = useTranslation()
  const tRef = useLatestT()
  const locale = i18n.language === 'en' ? 'en-US' : 'tr-TR'
  const numberFormat = new Intl.NumberFormat(locale)
  const dateFormat = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' })
  const [api] = useState(() => new DavetiyeApiClient())
  const [overview, setOverview] = useState<AdminOverview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const controller = new AbortController()
    void api.getAdminOverview(controller.signal)
      .then(setOverview)
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return
        setError(reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)
          ? tRef.current('adminOverview.loadError')
          : tRef.current('adminOverview.failed'))
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [api, tRef])

  if (loading) {
    return <section className="admin-overview" aria-busy="true" aria-live="polite"><p>{t('adminOverview.loading')}</p></section>
  }

  if (error || !overview) {
    return (
      <section className="admin-overview" aria-live="polite">
        <div className="admin-overview__error" role="alert">
          <h2>{t('adminOverview.heading')}</h2>
          <p>{error ?? t('adminOverview.unexpected')}</p>
          <button className="button button--secondary" type="button" onClick={() => window.location.reload()}>{t('adminOverview.retry')}</button>
        </div>
      </section>
    )
  }

  const sections = [
    { title: t('adminOverview.accounts'), items: [[t('adminOverview.totalUsers'), overview.accounts.total], [t('adminOverview.individual'), overview.accounts.individual], [t('adminOverview.organization'), overview.accounts.organization], [t('adminOverview.banned'), overview.accounts.banned]] as const },
    { title: t('adminOverview.invitations'), items: [[t('adminOverview.draft'), overview.invitations.draft], [t('adminOverview.scheduled'), overview.invitations.scheduled], [t('adminOverview.active'), overview.invitations.active], [t('adminOverview.paused'), overview.invitations.paused], [t('adminOverview.expired'), overview.invitations.expired], [t('adminOverview.deleted'), overview.invitations.deleted]] as const },
    { title: t('adminOverview.grants'), items: [[t('adminOverview.totalGrants'), overview.grants.total], [t('adminOverview.free'), overview.grants.free], [t('adminOverview.purchase'), overview.grants.individualPurchase], [t('adminOverview.subscription'), overview.grants.organizationSubscription], [t('adminOverview.revoked'), overview.grants.revoked]] as const },
    { title: t('adminOverview.plans'), items: [[t('adminOverview.totalPlans'), overview.plans.total], [t('adminOverview.enabled'), overview.plans.active], [t('adminOverview.disabled'), overview.plans.inactive]] as const },
    { title: t('adminOverview.payments'), items: [[t('adminOverview.pending'), overview.payments.pending], [t('adminOverview.unknown'), overview.payments.unknown], [t('adminOverview.succeeded'), overview.payments.succeeded], [t('adminOverview.failedPayment'), overview.payments.failed], [t('adminOverview.canceled'), overview.payments.canceled], [t('adminOverview.reversed'), overview.payments.reversed]] as const },
    { title: t('adminOverview.storage'), items: [[t('adminOverview.asset'), overview.storage.assets], [t('adminOverview.ready'), overview.storage.ready], [t('adminOverview.pendingUpload'), overview.storage.pendingUpload], [t('adminOverview.processing'), overview.storage.processing], [t('adminOverview.pendingDeletion'), overview.storage.pendingDeletion], [t('adminOverview.deleted'), overview.storage.deleted], [t('adminOverview.rejected'), overview.storage.rejected], [t('adminOverview.verifiedMedia'), formatBytes(overview.storage.verifiedBytes, locale)]] as const },
  ]

  return (
    <div className="admin-overview">
      <div className="admin-overview__heading">
        <div>
          <h2>{t('adminOverview.platformHeading')}</h2>
          <p>{t('adminOverview.privacy')}</p>
        </div>
        <p className="admin-overview__updated">{t('adminOverview.updated')}: <time dateTime={overview.generatedAtUtc}>{dateFormat.format(new Date(overview.generatedAtUtc))}</time></p>
      </div>

      <div className="admin-overview__grid">
        {sections.map(section => (
          <section className="admin-overview__card" aria-labelledby={`admin-${slug(section.title)}`} key={section.title}>
            <h3 id={`admin-${slug(section.title)}`}>{section.title}</h3>
            <dl>
              {section.items.map(([label, value]) => (
                <div className="admin-overview__metric" key={label}>
                  <dt>{label}</dt>
                  <dd>{typeof value === 'number' ? numberFormat.format(value) : value}</dd>
                </div>
              ))}
            </dl>
          </section>
        ))}
        <section className="admin-overview__card" aria-labelledby="admin-health">
          <h3 id="admin-health">{t('adminOverview.health')}</h3>
          <dl>
            <HealthMetric label="API" value={overview.health.api} />
            <HealthMetric label="Veritabanı" value={overview.health.database} />
          </dl>
        </section>
      </div>
      <p className="admin-overview__privacy">{t('adminOverview.privacyDetail')}</p>
    </div>
  )
}

function HealthMetric({ label, value }: { label: string; value: string }) {
  const { t } = useTranslation()
  const healthy = value.toLowerCase() === 'healthy'
  return <div className="admin-overview__metric"><dt>{label}</dt><dd><span className={`admin-overview__health${healthy ? ' is-healthy' : ' is-warning'}`}><span aria-hidden="true" />{healthy ? t('adminOverview.working') : value}</span></dd></div>
}

function formatBytes(bytes: number, locale: string): string {
  const numberFormat = new Intl.NumberFormat(locale)
  if (bytes < 1024) return `${numberFormat.format(bytes)} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit += 1
  }
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 1 }).format(value)} ${units[unit]}`
}

function slug(value: string): string {
  return value.toLocaleLowerCase('tr-TR').replaceAll(' ', '-')
}
