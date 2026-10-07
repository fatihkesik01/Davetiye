import { useEffect, useState, type FormEvent } from 'react'
import { ApiRequestError, DavetiyeApiClient, type AdminBannedAccountListItem, type AdminPage } from '../../api/generated/client'

const pageSize = 50
const numberFormat = new Intl.NumberFormat('tr-TR')
const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' })

export function AdminBannedAccountsPage() {
  const [client] = useState(() => new DavetiyeApiClient())
  const api = client
  const [page, setPage] = useState(1)
  const [emailDraft, setEmailDraft] = useState('')
  const [emailPrefix, setEmailPrefix] = useState('')
  const [result, setResult] = useState<AdminPage<AdminBannedAccountListItem> | null>(null)
  const [loadedKey, setLoadedKey] = useState('')
  const [error, setError] = useState<{ key: string; message: string } | null>(null)
  const [reloadVersion, setReloadVersion] = useState(0)
  const [pendingId, setPendingId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState('')
  const requestKey = `${page}:${emailPrefix}:${reloadVersion}`
  const loading = loadedKey !== requestKey
  const visibleError = error?.key === requestKey ? error.message : null

  useEffect(() => {
    const controller = new AbortController()
    void api.getAntiforgeryToken(controller.signal)
      .then(csrfToken => {
        if (controller.signal.aborted) return null
        return api.searchAdminBannedAccounts(page, pageSize, emailPrefix || undefined, csrfToken, controller.signal)
      })
      .then(value => {
        if (value === null) return
        setResult(value)
        setError(null)
        setLoadedKey(requestKey)
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return
        setError({
          key: requestKey,
          message: reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)
            ? 'Bu görünüm için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.'
            : 'Banlı hesaplar şu anda alınamadı. Biraz sonra yeniden deneyin.',
        })
        setLoadedKey(requestKey)
      })
    return () => controller.abort()
  }, [api, page, emailPrefix, reloadVersion, requestKey])

  const totalCount = result?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const items = result?.items ?? []

  async function unban(account: AdminBannedAccountListItem) {
    if (!window.confirm(`${account.displayName || account.email || account.accountId} hesabının banını kaldırmak istiyor musunuz? Hesabın erişimi geri gelir.`)) return
    setPendingId(account.accountId)
    setActionError(null)
    setNotice('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      await api.unbanAccount(account.accountId, csrfToken)
      setNotice(`${account.email || account.displayName || 'Hesap'} hesabının banı kaldırıldı.`)
      if (items.length === 1 && page > 1) setPage(current => current - 1)
      else setReloadVersion(current => current + 1)
    } catch (reason) {
      setActionError(reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)
        ? 'İşlem için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.'
        : 'Ban kaldırılamadı. Hesap durumu değişmiş olabilir; listeyi yenileyip tekrar deneyin.')
    } finally {
      setPendingId(null)
    }
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const nextPrefix = emailDraft.trim()
    setPage(1)
    setNotice('')
    if (nextPrefix === emailPrefix) setReloadVersion(current => current + 1)
    else setEmailPrefix(nextPrefix)
  }

  return (
    <section className="admin-banned" aria-labelledby="admin-banned-title" aria-busy={loading}>
      <header className="admin-overview__heading">
        <div>
          <h2 id="admin-banned-title">Banlı hesaplar</h2>
          <p>Banlı hesap özetlerini e-posta başlangıcına göre arayın. Dahili notlar burada gösterilmez.</p>
        </div>
        <p className="admin-operational__count">Toplam: {numberFormat.format(totalCount)}</p>
      </header>

      <form className="admin-banned__search" onSubmit={submitSearch} role="search">
        <label htmlFor="admin-banned-email">E-posta başlangıcı</label>
        <div className="admin-banned__search-controls">
          <input id="admin-banned-email" type="search" autoComplete="email" maxLength={254} value={emailDraft}
            onChange={event => setEmailDraft(event.target.value)} placeholder="ornek@eposta.com" />
          <button className="button button--primary" type="submit" disabled={loading}>Ara</button>
        </div>
      </form>

      {actionError ? <p className="admin-banned__message is-error" role="alert">{actionError}</p> : null}
      {notice ? <p className="admin-banned__message" role="status">{notice}</p> : null}
      {loading ? <p className="admin-operational__status" role="status">Banlı hesaplar yükleniyor…</p> : null}
      {!loading && visibleError ? (
        <div className="admin-overview__error" role="alert">
          <h3>Banlı hesaplar yüklenemedi</h3>
          <p>{visibleError}</p>
          <button className="button button--secondary" type="button" onClick={() => setReloadVersion(value => value + 1)}>Yeniden dene</button>
        </div>
      ) : null}
      {!loading && !visibleError && items.length === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>{emailPrefix ? 'Eşleşen banlı hesap yok' : 'Banlı hesap yok'}</h3>
          <p>{emailPrefix ? 'Farklı bir e-posta başlangıcı deneyin.' : 'Banlı hesaplar bu listede görünür.'}</p>
        </div>
      ) : null}
      {!loading && !visibleError && items.length > 0 ? <BannedAccountsTable items={items} pendingId={pendingId} onUnban={account => void unban(account)} /> : null}
      {!loading && !visibleError && totalCount > 0 ? (
        <nav className="admin-operational__pagination" aria-label="Banlı hesaplar sayfaları">
          <button className="button button--secondary" type="button" disabled={page <= 1 || pendingId !== null} onClick={() => setPage(value => Math.max(1, value - 1))}>Önceki</button>
          <span aria-live="polite">Sayfa {numberFormat.format(page)} / {numberFormat.format(totalPages)}</span>
          <button className="button button--secondary" type="button" disabled={page >= totalPages || pendingId !== null} onClick={() => setPage(value => Math.min(totalPages, value + 1))}>Sonraki</button>
        </nav>
      ) : null}
    </section>
  )
}

function BannedAccountsTable({ items, pendingId, onUnban }: {
  items: AdminBannedAccountListItem[]
  pendingId: string | null
  onUnban: (account: AdminBannedAccountListItem) => void
}) {
  return (
    <div className="admin-banned__table-wrap">
      <table className="admin-operational__table admin-banned__table">
        <caption className="visually-hidden">Banlı hesaplar</caption>
        <thead><tr><th scope="col">Hesap</th><th scope="col">E-posta</th><th scope="col">Tür</th><th scope="col">Kayıt tarihi</th><th scope="col">Ban tarihi</th><th scope="col">Sebep</th><th scope="col">İşlem</th></tr></thead>
        <tbody>{items.map(account => (
          <tr key={account.accountId}>
            <td data-label="Hesap">{account.displayName || 'İsimsiz hesap'}</td>
            <td data-label="E-posta"><span className="admin-operational__reference">{account.email || 'E-posta yok'}</span></td>
            <td data-label="Tür">{accountTypeLabel(account.accountType)}</td>
            <td data-label="Kayıt tarihi">{formatDate(account.createdAtUtc)}</td>
            <td data-label="Ban tarihi">{formatDate(account.bannedAtUtc)}</td>
            <td data-label="Sebep">{account.reason || 'Belirtilmedi'}</td>
            <td data-label="İşlem"><button className="button button--secondary" type="button" disabled={pendingId !== null}
              aria-label={`${account.email || account.displayName || account.accountId} hesabının banını kaldır`} onClick={() => onUnban(account)}>
              {pendingId === account.accountId ? 'Ban kaldırılıyor…' : 'Banı kaldır'}
            </button></td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

function accountTypeLabel(value: AdminBannedAccountListItem['accountType']): string {
  return value === 'individual' ? 'Bireysel' : 'Organizasyon'
}

function formatDate(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
}
