import { useEffect, useState } from 'react'
import { ApiRequestError, DavetiyeApiClient, type AdminOverview } from '../../api/generated/client'

const numberFormat = new Intl.NumberFormat('tr-TR')
const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function AdminOverviewPage() {
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
          ? 'Bu görünüm için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.'
          : 'Platform özeti şu anda alınamadı. Biraz sonra yeniden deneyin.')
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [api])

  if (loading) {
    return <section className="admin-overview" aria-busy="true" aria-live="polite"><p>Platform özeti yükleniyor…</p></section>
  }

  if (error || !overview) {
    return (
      <section className="admin-overview" aria-live="polite">
        <div className="admin-overview__error" role="alert">
          <h2>Özet yüklenemedi</h2>
          <p>{error ?? 'Beklenmeyen bir yanıt alındı.'}</p>
          <button className="button button--secondary" type="button" onClick={() => window.location.reload()}>Yeniden dene</button>
        </div>
      </section>
    )
  }

  const sections = [
    { title: 'Hesaplar', items: [['Toplam kullanıcı', overview.accounts.total], ['Bireysel', overview.accounts.individual], ['Organizasyon', overview.accounts.organization], ['Banlı hesap', overview.accounts.banned]] as const },
    { title: 'Davetiyeler', items: [['Taslak', overview.invitations.draft], ['Planlandı', overview.invitations.scheduled], ['Aktif', overview.invitations.active], ['Duraklatıldı', overview.invitations.paused], ['Süresi doldu', overview.invitations.expired], ['Silindi', overview.invitations.deleted]] as const },
    { title: 'Yayın hakları', items: [['Toplam hak', overview.grants.total], ['Ücretsiz', overview.grants.free], ['Tek seferlik satın alma', overview.grants.individualPurchase], ['Organizasyon aboneliği', overview.grants.organizationSubscription], ['İptal edilen', overview.grants.revoked]] as const },
    { title: 'Paketler', items: [['Toplam paket', overview.plans.total], ['Etkin', overview.plans.active], ['Devre dışı', overview.plans.inactive]] as const },
    { title: 'Ödemeler', items: [['Bekliyor', overview.payments.pending], ['Durumu bilinmiyor', overview.payments.unknown], ['Başarılı', overview.payments.succeeded], ['Başarısız', overview.payments.failed], ['İptal', overview.payments.canceled], ['Ters kayıt', overview.payments.reversed]] as const },
    { title: 'Depolama', items: [['Medya kaydı', overview.storage.assets], ['Hazır', overview.storage.ready], ['Yükleme bekliyor', overview.storage.pendingUpload], ['İşleniyor', overview.storage.processing], ['Silme bekliyor', overview.storage.pendingDeletion], ['Silindi', overview.storage.deleted], ['Reddedildi', overview.storage.rejected], ['DB’de doğrulanmış medya', formatBytes(overview.storage.verifiedBytes)]] as const },
  ]

  return (
    <div className="admin-overview">
      <div className="admin-overview__heading">
        <div>
          <h2>Platform özeti</h2>
          <p>Yalnızca toplu operasyon göstergeleri. Özel davetli içerikleri bu panelde gösterilmez.</p>
        </div>
        <p className="admin-overview__updated">Güncellendi: <time dateTime={overview.generatedAtUtc}>{dateFormat.format(new Date(overview.generatedAtUtc))}</time></p>
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
          <h3 id="admin-health">Sistem durumu</h3>
          <dl>
            <HealthMetric label="API" value={overview.health.api} />
            <HealthMetric label="Veritabanı" value={overview.health.database} />
          </dl>
        </section>
      </div>
      <p className="admin-overview__privacy">Depolama boyutu yalnızca veritabanında doğrulanmış medya baytlarını gösterir; Cloudflare kullanım veya faturalama miktarı değildir. Bu özet hesap veya davetli kimliği içermez ve yönetim işlemleri bu görünümden yapılmaz.</p>
    </div>
  )
}

function HealthMetric({ label, value }: { label: string; value: string }) {
  const healthy = value.toLowerCase() === 'healthy'
  return <div className="admin-overview__metric"><dt>{label}</dt><dd><span className={`admin-overview__health${healthy ? ' is-healthy' : ' is-warning'}`}><span aria-hidden="true" />{healthy ? 'Çalışıyor' : value}</span></dd></div>
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${numberFormat.format(bytes)} B`
  const units = ['KB', 'MB', 'GB', 'TB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit += 1
  }
  return `${new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 }).format(value)} ${units[unit]}`
}

function slug(value: string): string {
  return value.toLocaleLowerCase('tr-TR').replaceAll(' ', '-')
}
