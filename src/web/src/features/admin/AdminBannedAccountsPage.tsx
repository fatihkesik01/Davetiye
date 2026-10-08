import { useEffect, useState, type FormEvent } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'
import { ApiRequestError, DavetiyeApiClient, type AdminBannedAccountListItem, type AdminPage } from '../../api/generated/client'

const pageSize = 50
export function AdminBannedAccountsPage() {
  const { t, i18n } = useTranslation()
  const tRef = useLatestT()
  const translate = (key: string) => t(key)
  const locale = i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR'
  const numberFormat = new Intl.NumberFormat(locale)
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
            ? tRef.current('adminUi.banned.authRequired')
            : tRef.current('adminUi.banned.loadError'),
        })
        setLoadedKey(requestKey)
      })
    return () => controller.abort()
  }, [api, page, emailPrefix, reloadVersion, requestKey, tRef])

  const totalCount = result?.totalCount ?? 0
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  const items = result?.items ?? []

  async function unban(account: AdminBannedAccountListItem) {
    const accountName = account.displayName || account.email || account.accountId
    if (!window.confirm(t('adminUi.banned.confirmUnban', { name: accountName }))) return
    setPendingId(account.accountId)
    setActionError(null)
    setNotice('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      await api.unbanAccount(account.accountId, csrfToken)
      setNotice(t('adminUi.banned.unbanSuccess', { name: account.email || account.displayName || t('adminUi.banned.unnamedAccount') }))
      if (items.length === 1 && page > 1) setPage(current => current - 1)
      else setReloadVersion(current => current + 1)
    } catch (reason) {
      setActionError(reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)
        ? t('adminUi.banned.actionAuthRequired')
        : t('adminUi.banned.unbanError'))
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
          <h2 id="admin-banned-title">{t('adminUi.banned.title')}</h2>
          <p>{t('adminUi.banned.description')}</p>
        </div>
        <p className="admin-operational__count">{t('adminUi.banned.total', { count: numberFormat.format(totalCount) })}</p>
      </header>

      <form className="admin-banned__search" onSubmit={submitSearch} role="search">
        <label htmlFor="admin-banned-email">{t('adminUi.banned.emailPrefix')}</label>
        <div className="admin-banned__search-controls">
          <input id="admin-banned-email" type="search" autoComplete="email" maxLength={254} value={emailDraft}
            onChange={event => setEmailDraft(event.target.value)} placeholder="name@example.com" />
          <button className="button button--primary" type="submit" disabled={loading}>{t('adminUi.banned.search')}</button>
        </div>
      </form>

      {actionError ? <p className="admin-banned__message is-error" role="alert">{actionError}</p> : null}
      {notice ? <p className="admin-banned__message" role="status">{notice}</p> : null}
      {loading ? <p className="admin-operational__status" role="status">{t('adminUi.banned.loading')}</p> : null}
      {!loading && visibleError ? (
        <div className="admin-overview__error" role="alert">
          <h3>{t('adminUi.banned.loadFailed')}</h3>
          <p>{visibleError}</p>
          <button className="button button--secondary" type="button" onClick={() => setReloadVersion(value => value + 1)}>{t('adminUi.banned.retry')}</button>
        </div>
      ) : null}
      {!loading && !visibleError && items.length === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>{t(emailPrefix ? 'adminUi.banned.emptyMatchTitle' : 'adminUi.banned.emptyTitle')}</h3>
          <p>{t(emailPrefix ? 'adminUi.banned.tryDifferentPrefix' : 'adminUi.banned.emptyBody')}</p>
        </div>
      ) : null}
      {!loading && !visibleError && items.length > 0 ? <BannedAccountsTable items={items} pendingId={pendingId} onUnban={account => void unban(account)} translate={translate} /> : null}
      {!loading && !visibleError && totalCount > 0 ? (
        <nav className="admin-operational__pagination" aria-label={t('adminUi.banned.pages')}>
          <button className="button button--secondary" type="button" disabled={page <= 1 || pendingId !== null} onClick={() => setPage(value => Math.max(1, value - 1))}>{t('adminUi.banned.previous')}</button>
          <span aria-live="polite">{t('adminUi.banned.page', { current: numberFormat.format(page), total: numberFormat.format(totalPages) })}</span>
          <button className="button button--secondary" type="button" disabled={page >= totalPages || pendingId !== null} onClick={() => setPage(value => Math.min(totalPages, value + 1))}>{t('adminUi.banned.next')}</button>
        </nav>
      ) : null}
    </section>
  )
}

function BannedAccountsTable({ items, pendingId, onUnban, translate }: {
  items: AdminBannedAccountListItem[]
  pendingId: string | null
  onUnban: (account: AdminBannedAccountListItem) => void
  translate: (key: string) => string
}) {
  const { t, i18n } = useTranslation()
  const locale = i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR'
  const dateFormat = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' })
  return (
    <div className="admin-banned__table-wrap">
      <table className="admin-operational__table admin-banned__table">
          <caption className="visually-hidden">{t('adminUi.banned.caption')}</caption>
        <thead><tr><th scope="col">{t('adminUi.banned.account')}</th><th scope="col">{t('adminUi.banned.email')}</th><th scope="col">{t('adminUi.banned.type')}</th><th scope="col">{t('adminUi.banned.created')}</th><th scope="col">{t('adminUi.banned.bannedAt')}</th><th scope="col">{t('adminUi.banned.reason')}</th><th scope="col">{t('adminUi.banned.action')}</th></tr></thead>
        <tbody>{items.map(account => (
          <tr key={account.accountId}>
            <td data-label={t('adminUi.banned.account')}>{account.displayName || t('adminUi.banned.unnamedAccount')}</td>
            <td data-label={t('adminUi.banned.email')}><span className="admin-operational__reference">{account.email || t('adminUi.banned.noEmail')}</span></td>
            <td data-label={t('adminUi.banned.type')}>{accountTypeLabel(account.accountType, translate)}</td>
            <td data-label={t('adminUi.banned.created')}>{formatDate(account.createdAtUtc, dateFormat)}</td>
            <td data-label={t('adminUi.banned.bannedAt')}>{formatDate(account.bannedAtUtc, dateFormat)}</td>
            <td data-label={t('adminUi.banned.reason')}>{account.reason || t('adminUi.banned.unspecified')}</td>
            <td data-label={t('adminUi.banned.action')}><button className="button button--secondary" type="button" disabled={pendingId !== null}
              aria-label={t('adminUi.banned.unbanAria', { name: account.email || account.displayName || account.accountId })} onClick={() => onUnban(account)}>
              {pendingId === account.accountId ? t('adminUi.banned.unbanning') : t('adminUi.banned.unban')}
            </button></td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  )
}

function accountTypeLabel(value: AdminBannedAccountListItem['accountType'], t: (key: string) => string): string {
  return value === 'individual' ? t('adminUi.banned.individual') : t('adminUi.banned.organization')
}

function formatDate(value: string, dateFormat: Intl.DateTimeFormat): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '—' : dateFormat.format(date)
}
