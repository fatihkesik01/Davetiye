import { useEffect, useMemo, useState, type FormEvent } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'

import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminSystemSettingItem,
} from '../../api/generated/client'

const settingDefinitions = {
  deletedInvitationRetentionDays: true,
  abandonedMemoryRetentionDays: true,
} as const

type RetentionSettingKey = keyof typeof settingDefinitions

type RetentionSettingItem = AdminSystemSettingItem & { key: RetentionSettingKey }

const allowedKeys = Object.keys(settingDefinitions) as RetentionSettingKey[]

function isAllowedKey(value: string): value is RetentionSettingKey {
  return Object.prototype.hasOwnProperty.call(settingDefinitions, value)
}

function isAllowedItem(item: AdminSystemSettingItem): item is RetentionSettingItem {
  return isAllowedKey(item.key)
}

function orderedAllowedItems(items: AdminSystemSettingItem[]): RetentionSettingItem[] {
  const byKey = new Map(items.filter(isAllowedItem).map(item => [item.key, item]))
  return allowedKeys.flatMap(key => {
    const item = byKey.get(key)
    return item ? [item] : []
  })
}

export function AdminSettingsPage() {
  const { t } = useTranslation()
  const tRef = useLatestT()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [items, setItems] = useState<RetentionSettingItem[] | null>(null)
  const [drafts, setDrafts] = useState<Partial<Record<RetentionSettingKey, string>>>({})
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState(false)
  const [reloadVersion, setReloadVersion] = useState(0)
  const [savingKey, setSavingKey] = useState<RetentionSettingKey | null>(null)
  const [staleKeys, setStaleKeys] = useState<RetentionSettingKey[]>([])
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    void api.getAdminSettings(controller.signal)
      .then(result => {
        if (controller.signal.aborted) return
        const nextItems = orderedAllowedItems(result.items)
        setItems(nextItems)
        setDrafts(Object.fromEntries(nextItems.map(item => [item.key, String(item.value)])))
        setStaleKeys([])
        setError('')
        setLoadError(false)
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted) return
        setLoadError(true)
        if (reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)) {
          setError(tRef.current('adminUi.settings.authRequired'))
        }
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [api, reloadVersion, tRef])

  const retryLoad = () => {
    setLoading(true)
    setLoadError(false)
    setError('')
    setReloadVersion(value => value + 1)
  }

  const refreshAfterConflict = async (key: RetentionSettingKey) => {
    setLoading(true)
    try {
      const result = await api.getAdminSettings()
      const current = orderedAllowedItems(result.items)
      setItems(current)
      setDrafts(draftsNow => ({ ...draftsNow, [key]: String(current.find(item => item.key === key)?.value ?? '') }))
      setStaleKeys(keys => keys.filter(value => value !== key))
      setError('')
    } catch {
      setError(t('adminUi.settings.refreshError'))
    } finally {
      setLoading(false)
    }
  }

  const save = async (event: FormEvent<HTMLFormElement>, item: RetentionSettingItem) => {
    event.preventDefault()
    const rawValue = drafts[item.key] ?? ''
    const value = Number(rawValue)
    if (rawValue.trim() === '' || !Number.isInteger(value) || value < 0 || value > 365 || value < item.minimum || value > item.maximum) {
      setError(t('adminUi.settings.invalidDays'))
      return
    }
    if (value === item.value || savingKey) return
    const impact = t(`adminUi.settings.settingEffects.${item.key}`)
    if (!window.confirm(t('adminUi.settings.confirmSave', { name: item.displayName || t(`adminUi.settings.settingTitles.${item.key}`), value, impact }))) return

    setSavingKey(item.key)
    setError('')
    setMessage('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const updated = await api.updateAdminSetting(item.key, { expectedRevision: item.revision, value }, csrfToken)
      const updatedItem: RetentionSettingItem = { ...updated, key: item.key }
      setItems(current => current?.map(existing => existing.key === item.key ? updatedItem : existing) ?? [updatedItem])
      setDrafts(current => ({ ...current, [item.key]: String(updated.value) }))
      setMessage(t('adminUi.settings.saved', { name: item.displayName || t(`adminUi.settings.settingTitles.${item.key}`) }))
    } catch (reason) {
      if (reason instanceof ApiRequestError && reason.status === 409) {
        setStaleKeys(current => current.includes(item.key) ? current : [...current, item.key])
        setError(t('adminUi.settings.conflict', { name: item.displayName || t(`adminUi.settings.settingTitles.${item.key}`) }))
      } else if (reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)) {
        setError(t('adminUi.settings.saveAuthRequired'))
      } else {
        setError(t('adminUi.settings.saveError'))
      }
    } finally {
      setSavingKey(null)
    }
  }

  return <section className="admin-settings" aria-labelledby="admin-settings-title" aria-busy={loading || savingKey !== null}>
    <header className="admin-overview__heading">
      <div>
        <h2 id="admin-settings-title">{t('adminUi.settings.title')}</h2>
        <p>{t('adminUi.settings.description')}</p>
      </div>
    </header>

    {error ? <p className="admin-banned__message is-error" role="alert">{error}</p> : null}
    {message ? <p className="admin-banned__message" role="status">{message}</p> : null}
    {loading ? <p className="admin-operational__status" role="status">{t('adminUi.settings.loading')}</p> : null}
    {!loading && loadError ? <div className="admin-overview__error" role="alert">
      <h3>{t('adminUi.settings.loadFailed')}</h3>
      <p>{error || t('adminUi.settings.loadError')}</p>
      <button className="button button--secondary" type="button" onClick={retryLoad}>{t('adminUi.settings.retry')}</button>
    </div> : null}
    {!loading && !loadError && items?.length === 0 ? <div className="admin-operational__empty" role="status">
      <h3>{t('adminUi.settings.emptyTitle')}</h3>
      <p>{t('adminUi.settings.emptyBody')}</p>
    </div> : null}
    {!loading && !loadError && items?.length ? <div className="admin-settings__list">
      {items.map(item => {
        const draft = drafts[item.key] ?? String(item.value)
        const changed = Number(draft) !== item.value || draft.trim() === ''
        const stale = staleKeys.includes(item.key)
        return <form key={item.key} className="admin-settings__card" onSubmit={event => void save(event, item)} aria-labelledby={`admin-setting-title-${item.key}`}>
          <div className="admin-settings__heading">
            <div>
              <p className="eyebrow">{item.key}</p>
              <h3 id={`admin-setting-title-${item.key}`}>{item.displayName || t(`adminUi.settings.settingTitles.${item.key}`)}</h3>
            </div>
            <span className="admin-settings__revision">{t('adminUi.settings.revision', { revision: item.revision })}</span>
          </div>
          {item.description ? <p className="admin-settings__description">{item.description}</p> : null}
          <p className="admin-settings__effect">{t(`adminUi.settings.settingEffects.${item.key}`)}</p>
          <label className="admin-settings__field" htmlFor={`admin-setting-value-${item.key}`}>
            <span>{t('adminUi.settings.days')}</span>
            <input id={`admin-setting-value-${item.key}`} type="number" inputMode="numeric" min="0" max="365" step="1" value={draft}
              disabled={savingKey !== null}
              aria-describedby={`admin-setting-range-${item.key}`}
              aria-invalid={draft.trim() === '' || !Number.isInteger(Number(draft)) || Number(draft) < 0 || Number(draft) > 365 ? true : undefined}
              onChange={event => setDrafts(current => ({ ...current, [item.key]: event.target.value }))} />
            <small id={`admin-setting-range-${item.key}`}>{t('adminUi.settings.range', { minimum: item.minimum, maximum: item.maximum })}</small>
          </label>
          {stale ? <div className="admin-settings__conflict" role="group" aria-label={t('adminUi.settings.currentValue', { name: item.displayName || t(`adminUi.settings.settingTitles.${item.key}`) })}>
            <p>{t('adminUi.settings.unsavedPreserved')}</p>
            <button className="button button--secondary" type="button" onClick={() => void refreshAfterConflict(item.key)} disabled={loading}>{t('adminUi.settings.refresh')}</button>
          </div> : null}
          <div className="admin-settings__actions">
            <button className="button button--primary" type="submit" disabled={!changed || stale || savingKey !== null || draft.trim() === '' || !Number.isInteger(Number(draft)) || Number(draft) < 0 || Number(draft) > 365 || Number(draft) < item.minimum || Number(draft) > item.maximum}>
              {savingKey === item.key ? t('adminUi.settings.saving') : t('adminUi.settings.save')}
            </button>
          </div>
        </form>
      })}
    </div> : null}
  </section>
}
