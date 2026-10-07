import { useEffect, useMemo, useState, type FormEvent } from 'react'

import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminSystemSettingItem,
} from '../../api/generated/client'

const settingDefinitions = {
  deletedInvitationRetentionDays: {
    title: 'Silinen davetiyelerin saklama süresi',
    effect: 'Bu değişiklik, bundan sonra çöp kutusuna taşınan davetiyelere uygulanır. Mevcut davetiyelerin kalıcı silinme tarihleri değişmez.',
  },
  abandonedMemoryRetentionDays: {
    title: 'Tamamlanmamış anı kayıtlarının saklama süresi',
    effect: 'Süresi dolan Abandoned anı ve capability metadata kayıtları sınırlı partilerle temizlenir. MediaAsset, PendingUpload, sağlayıcı dosya baytları ve misafir kotası davetiye kalıcı silinene kadar korunur.',
  },
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
          setError('Bu ayarları görmek için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.')
        }
      })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [api, reloadVersion])

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
      setError('Güncel ayar alınamadı. Bağlantınızı kontrol edip yeniden deneyin.')
    } finally {
      setLoading(false)
    }
  }

  const save = async (event: FormEvent<HTMLFormElement>, item: RetentionSettingItem) => {
    event.preventDefault()
    const rawValue = drafts[item.key] ?? ''
    const value = Number(rawValue)
    if (rawValue.trim() === '' || !Number.isInteger(value) || value < 0 || value > 365 || value < item.minimum || value > item.maximum) {
      setError('Saklama süresi 0 ile 365 arasında tam gün olmalıdır.')
      return
    }
    if (value === item.value || savingKey) return
    const impact = settingDefinitions[item.key].effect
    if (!window.confirm(`${item.displayName} ayarını ${value} gün olarak kaydetmek istiyor musunuz? ${impact}`)) return

    setSavingKey(item.key)
    setError('')
    setMessage('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const updated = await api.updateAdminSetting(item.key, { expectedRevision: item.revision, value }, csrfToken)
      const updatedItem: RetentionSettingItem = { ...updated, key: item.key }
      setItems(current => current?.map(existing => existing.key === item.key ? updatedItem : existing) ?? [updatedItem])
      setDrafts(current => ({ ...current, [item.key]: String(updated.value) }))
      setMessage(`${item.displayName} kaydedildi. Yeni alımlarda ve ilgili yeni kayıt işlemlerinde geçerli olur.`)
    } catch (reason) {
      if (reason instanceof ApiRequestError && reason.status === 409) {
        setStaleKeys(current => current.includes(item.key) ? current : [...current, item.key])
        setError(`${item.displayName} siz düzenlerken başka bir yönetici tarafından değiştirildi. Değişikliğiniz korunuyor; kaydetmeden önce güncel değeri yükleyin.`)
      } else if (reason instanceof ApiRequestError && (reason.status === 401 || reason.status === 403)) {
        setError('Kaydetmek için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.')
      } else {
        setError('Ayar kaydedilemedi. Değeri kontrol edip yeniden deneyin.')
      }
    } finally {
      setSavingKey(null)
    }
  }

  return <section className="admin-settings" aria-labelledby="admin-settings-title" aria-busy={loading || savingKey !== null}>
    <header className="admin-overview__heading">
      <div>
        <h2 id="admin-settings-title">Sistem ayarları</h2>
        <p>Yalnızca davetiye ve tamamlanmamış anı saklama süreleri düzenlenebilir.</p>
      </div>
    </header>

    {error ? <p className="admin-banned__message is-error" role="alert">{error}</p> : null}
    {message ? <p className="admin-banned__message" role="status">{message}</p> : null}
    {loading ? <p className="admin-operational__status" role="status">Sistem ayarları yükleniyor…</p> : null}
    {!loading && loadError ? <div className="admin-overview__error" role="alert">
      <h3>Ayarlar yüklenemedi</h3>
      <p>{error || 'Sistem ayarları şu anda alınamadı. Biraz sonra yeniden deneyin.'}</p>
      <button className="button button--secondary" type="button" onClick={retryLoad}>Yeniden dene</button>
    </div> : null}
    {!loading && !loadError && items?.length === 0 ? <div className="admin-operational__empty" role="status">
      <h3>Düzenlenebilir saklama ayarı yok</h3>
      <p>Onaylı saklama ayarları API tarafından sağlandığında burada görünür.</p>
    </div> : null}
    {!loading && !loadError && items?.length ? <div className="admin-settings__list">
      {items.map(item => {
        const draft = drafts[item.key] ?? String(item.value)
        const changed = Number(draft) !== item.value || draft.trim() === ''
        const stale = staleKeys.includes(item.key)
        const definition = settingDefinitions[item.key]
        return <form key={item.key} className="admin-settings__card" onSubmit={event => void save(event, item)} aria-labelledby={`admin-setting-title-${item.key}`}>
          <div className="admin-settings__heading">
            <div>
              <p className="eyebrow">{item.key}</p>
              <h3 id={`admin-setting-title-${item.key}`}>{item.displayName || definition.title}</h3>
            </div>
            <span className="admin-settings__revision">Sürüm {item.revision}</span>
          </div>
          {item.description ? <p className="admin-settings__description">{item.description}</p> : null}
          <p className="admin-settings__effect">{definition.effect}</p>
          <label className="admin-settings__field" htmlFor={`admin-setting-value-${item.key}`}>
            <span>Süre (gün)</span>
            <input id={`admin-setting-value-${item.key}`} type="number" inputMode="numeric" min="0" max="365" step="1" value={draft}
              disabled={savingKey !== null}
              aria-describedby={`admin-setting-range-${item.key}`}
              aria-invalid={draft.trim() === '' || !Number.isInteger(Number(draft)) || Number(draft) < 0 || Number(draft) > 365 ? true : undefined}
              onChange={event => setDrafts(current => ({ ...current, [item.key]: event.target.value }))} />
            <small id={`admin-setting-range-${item.key}`}>0–365 tam gün. Sunucu sınırı: {item.minimum}–{item.maximum} gün.</small>
          </label>
          {stale ? <div className="admin-settings__conflict" role="group" aria-label={`${item.displayName} güncel değeri`}>
            <p>Kaydedilmemiş değeriniz korunuyor. Güncel sunucu değerini yüklediğinizde düzenlemeniz değiştirilir.</p>
            <button className="button button--secondary" type="button" onClick={() => void refreshAfterConflict(item.key)} disabled={loading}>Güncel değeri yükle</button>
          </div> : null}
          <div className="admin-settings__actions">
            <button className="button button--primary" type="submit" disabled={!changed || stale || savingKey !== null || draft.trim() === '' || !Number.isInteger(Number(draft)) || Number(draft) < 0 || Number(draft) > 365 || Number(draft) < item.minimum || Number(draft) > item.maximum}>
              {savingKey === item.key ? 'Kaydediliyor…' : 'Değişikliği kaydet'}
            </button>
          </div>
        </form>
      })}
    </div> : null}
  </section>
}
