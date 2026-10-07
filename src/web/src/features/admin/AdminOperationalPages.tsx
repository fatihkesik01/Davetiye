import { useEffect, useState } from 'react'
import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminAuditListItem,
  type AdminPaymentListItem,
} from '../../api/generated/client'

const numberFormat = new Intl.NumberFormat('tr-TR')
const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })
const pageSize = 50

type OperationalPage = 'payments' | 'audit'

export function AdminOperationalPage({ kind }: { kind: OperationalPage }) {
  return <AdminOperationalContent key={kind} kind={kind} />
}

function AdminOperationalContent({ kind }: { kind: OperationalPage }) {
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
            ? 'Bu görünüm için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.'
            : 'Kayıtlar şu anda alınamadı. Biraz sonra yeniden deneyin.',
        })
        setLoadedRequestKey(requestKey)
      })

    return () => controller.abort()
  }, [api, kind, page, reloadVersion, requestKey])

  const title = kind === 'payments' ? 'Ödemeler' : 'Denetim kayıtları'
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))

  return (
    <section className="admin-operational" aria-labelledby="admin-operational-title" aria-busy={loading}>
      <header className="admin-overview__heading">
        <div>
          <h2 id="admin-operational-title">{title}</h2>
          <p>{kind === 'payments'
            ? 'En son güncellenen ödeme kayıtları. Hesap ve davetiye bilgileri bu listede yer almaz.'
            : 'En yeni yönetim olayları. Kayıtlar eklenmeye devam eder ve şu an için otomatik silinmez.'}</p>
        </div>
        <p className="admin-operational__count">Toplam: {numberFormat.format(totalCount)}</p>
      </header>

      {loading ? <p className="admin-operational__status" role="status">{title} yükleniyor…</p> : null}
      {!loading && visibleError ? (
        <div className="admin-overview__error" role="alert">
          <h3>{title} yüklenemedi</h3>
          <p>{visibleError}</p>
          <button className="button button--secondary" type="button" onClick={() => setReloadVersion(current => current + 1)}>Yeniden dene</button>
        </div>
      ) : null}

      {!loading && !visibleError && totalCount === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>Henüz kayıt yok</h3>
          <p>{kind === 'payments' ? 'Ödeme kaydı oluştuğunda burada görünür.' : 'Yönetim olayı oluştuğunda burada görünür.'}</p>
        </div>
      ) : null}

      {!loading && !visibleError && totalCount > 0 ? (
        <div className="admin-operational__table-wrap" tabIndex={0} aria-label={`${title} tablosu. Dar ekranda yatay kaydırılabilir.`}>
          {kind === 'payments' ? <PaymentsTable items={payments} /> : <AuditTable items={audit} />}
        </div>
      ) : null}

      {!loading && !visibleError && totalCount > 0 ? (
        <nav className="admin-operational__pagination" aria-label={`${title} sayfaları`}>
          <button className="button button--secondary" type="button" disabled={page <= 1} onClick={() => setPage(current => Math.max(1, current - 1))}>Önceki</button>
          <span aria-live="polite">Sayfa {numberFormat.format(page)} / {numberFormat.format(totalPages)}</span>
          <button className="button button--secondary" type="button" disabled={page >= totalPages} onClick={() => setPage(current => current + 1)}>Sonraki</button>
        </nav>
      ) : null}
    </section>
  )
}

function PaymentsTable({ items }: { items: AdminPaymentListItem[] }) {
  return (
    <table className="admin-operational__table">
      <caption className="visually-hidden">Ödeme kayıtları</caption>
      <thead><tr><th scope="col">Referans</th><th scope="col">Durum</th><th scope="col">Paket</th><th scope="col">Tutar</th><th scope="col">Oluşturulma</th><th scope="col">Son güncelleme</th><th scope="col">Ters kayıt</th></tr></thead>
      <tbody>{items.map(payment => (
        <tr key={payment.id}>
          <td data-label="Referans"><span className="admin-operational__reference">{payment.reference}</span></td>
          <td data-label="Durum">{paymentStatus(payment.status)}</td>
          <td data-label="Paket">{planLabel(payment.planKey)}</td>
          <td data-label="Tutar">{formatMoney(payment.amount, payment.currency)}</td>
          <td data-label="Oluşturulma">{formatDate(payment.createdAtUtc)}</td>
          <td data-label="Son güncelleme">{formatDate(payment.updatedAtUtc)}</td>
          <td data-label="Ters kayıt">{payment.reversalKind ? `${reversalLabel(payment.reversalKind)}${payment.reversedAtUtc ? ` · ${formatDate(payment.reversedAtUtc)}` : ''}` : '—'}</td>
        </tr>
      ))}</tbody>
    </table>
  )
}

function AuditTable({ items }: { items: AdminAuditListItem[] }) {
  return (
    <table className="admin-operational__table admin-operational__table--audit">
      <caption className="visually-hidden">Denetim kayıtları</caption>
      <thead><tr><th scope="col">Olay</th><th scope="col">Tarih</th><th scope="col">İşlemi yapan</th><th scope="col">İlgili kayıt</th></tr></thead>
      <tbody>{items.map(record => (
        <tr key={record.id}>
          <td data-label="Olay">{record.eventType}</td>
          <td data-label="Tarih">{formatDate(record.occurredAtUtc)}</td>
          <td data-label="İşlemi yapan"><span className="admin-operational__identifier">{record.actorId}</span></td>
          <td data-label="İlgili kayıt"><span className="admin-operational__identifier">{record.subjectId}</span></td>
        </tr>
      ))}</tbody>
    </table>
  )
}

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
}

function formatMoney(amount: number, currency: string): string {
  try {
    return new Intl.NumberFormat('tr-TR', { style: 'currency', currency }).format(amount)
  } catch {
    return `${numberFormat.format(amount)} ${currency}`
  }
}

function paymentStatus(status: string): string {
  const labels: Record<string, string> = {
    Pending: 'Bekliyor', Unknown: 'Belirsiz', Succeeded: 'Başarılı', Failed: 'Başarısız', Canceled: 'İptal edildi', Reversed: 'Ters kayıt',
  }
  return labels[status] ?? 'Diğer'
}

function reversalLabel(kind: string): string {
  const labels: Record<string, string> = { FullRefund: 'Tam iade', FinalLostChargeback: 'Sonuçlanmış kart itirazı' }
  return labels[kind] ?? 'Ters kayıt'
}

function planLabel(planKey: string): string {
  const labels: Record<string, string> = { standard: 'Standard', premium: 'Premium' }
  return labels[planKey] ?? 'Diğer paket'
}
