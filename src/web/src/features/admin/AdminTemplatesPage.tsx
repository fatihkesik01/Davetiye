import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminTemplateItem,
  type UpdateAdminTemplateRequest,
} from '../../api/generated/client'

type TemplateDraft = Pick<UpdateAdminTemplateRequest, 'name' | 'description' | 'isActive'>

const toDraft = (template: AdminTemplateItem): TemplateDraft => ({
  name: template.name,
  description: template.description ?? '',
  isActive: template.isActive,
})

export function AdminTemplatesPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [templates, setTemplates] = useState<AdminTemplateItem[] | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [draft, setDraft] = useState<TemplateDraft | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState(false)
  const [reloadVersion, setReloadVersion] = useState(0)
  const [saving, setSaving] = useState(false)
  const [stale, setStale] = useState(false)
  const [message, setMessage] = useState('')
  const [actionError, setActionError] = useState('')
  const staleRef = useRef(false)
  const selectedIdRef = useRef<string | null>(null)
  const selected = templates?.find(template => template.id === selectedId) ?? null

  useEffect(() => {
    const controller = new AbortController()
    void api.getAdminTemplates(controller.signal)
      .then(items => {
        if (controller.signal.aborted) return
        setTemplates(items)
        const currentId = selectedIdRef.current
        const next = items.find(item => item.id === currentId) ?? items[0] ?? null
        selectedIdRef.current = next?.id ?? null
        setSelectedId(next?.id ?? null)
        if (next && (staleRef.current || !currentId)) setDraft(toDraft(next))
        staleRef.current = false
        setStale(false)
        setActionError('')
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        setLoadError(true)
        if (error instanceof ApiRequestError && (error.status === 401 || error.status === 403)) {
          setActionError('Bu görünüm için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.')
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [api, reloadVersion])

  const changed = Boolean(selected && draft && (
    draft.name.trim() !== selected.name ||
    (draft.description?.trim() || null) !== selected.description ||
    draft.isActive !== selected.isActive
  ))

  function selectTemplate(template: AdminTemplateItem) {
    selectedIdRef.current = template.id
    setSelectedId(template.id)
    setDraft(toDraft(template))
    setStale(false)
    staleRef.current = false
    setMessage('')
    setActionError('')
  }

  function refreshAfterConflict() {
    setLoading(true)
    setLoadError(false)
    setReloadVersion(value => value + 1)
  }

  function retryLoad() {
    setLoading(true)
    setLoadError(false)
    setActionError('')
    setReloadVersion(value => value + 1)
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || !draft || !changed || saving) return
    const name = draft.name.trim()
    const description = draft.description?.trim() || null
    const visibility = draft.isActive ? 'katalogda görünür' : 'katalogda gizli'
    if (!window.confirm(`“${name || selected.name}” şablonunun adını, açıklamasını ve görünürlüğünü kaydetmek istiyor musunuz? Yeni durum: ${visibility}.`)) return

    setSaving(true)
    setActionError('')
    setMessage('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const updated = await api.updateAdminTemplate(selected.id, {
        expectedRevision: selected.revision,
        name,
        description,
        isActive: draft.isActive,
      }, csrfToken)
      setTemplates(current => current?.map(item => item.id === updated.id ? updated : item) ?? [updated])
      setDraft(toDraft(updated))
      staleRef.current = false
      setMessage(`${updated.name} şablonu kaydedildi.`)
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        staleRef.current = true
        setStale(true)
        setActionError('Şablon siz düzenlerken başka bir yönetici tarafından değiştirildi. Kaydetmeden önce güncel sürümü yenileyin.')
      } else if (error instanceof ApiRequestError && (error.status === 401 || error.status === 403)) {
        setActionError('Kaydetmek için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.')
      } else {
        setActionError('Şablon kaydedilemedi. Bağlantınızı kontrol edip yeniden deneyin.')
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="admin-templates" aria-labelledby="admin-templates-title" aria-busy={loading || saving}>
      <header className="admin-overview__heading">
        <div>
          <h2 id="admin-templates-title">Şablonlar</h2>
          <p>Şablon adını, isteğe bağlı açıklamasını ve katalog görünürlüğünü yönetin. Tasarım ve renderer bilgileri kod tarafından yönetilir.</p>
        </div>
      </header>

      {actionError ? <p className="admin-banned__message is-error" role="alert">{actionError}</p> : null}
      {message ? <p className="admin-banned__message" role="status">{message}</p> : null}
      {loading ? <p className="admin-operational__status" role="status">Şablonlar yükleniyor…</p> : null}
      {!loading && loadError ? (
        <div className="admin-overview__error" role="alert">
          <h3>Şablonlar yüklenemedi</h3>
          <p>{actionError || 'Şablon listesi şu anda alınamadı. Biraz sonra yeniden deneyin.'}</p>
          <button className="button button--secondary" type="button" onClick={retryLoad}>Yeniden dene</button>
        </div>
      ) : null}
      {!loading && !loadError && templates?.length === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>Yönetilecek şablon yok</h3>
          <p>Şablon tanımları eklendiğinde burada görünür.</p>
        </div>
      ) : null}
      {!loading && !loadError && templates && templates.length > 0 ? (
        <div className="admin-templates__layout">
          <nav className="admin-templates__list" aria-label="Düzenlenecek şablon">
            {templates.map(template => (
              <button key={template.id} type="button"
                className={`admin-templates__choice${template.id === selectedId ? ' is-selected' : ''}`}
                aria-current={template.id === selectedId ? 'true' : undefined}
                onClick={() => selectTemplate(template)}>
                <span className="admin-templates__choice-name">{template.name}</span>
                <span className="admin-templates__choice-meta">{template.key} · {template.isActive ? 'Görünür' : 'Gizli'}</span>
              </button>
            ))}
          </nav>
          {selected && draft ? (
            <form className="admin-templates__editor" onSubmit={event => void save(event)} aria-labelledby="admin-template-editor-title">
              <div className="admin-templates__editor-heading">
                <div>
                  <p className="eyebrow">{selected.key}</p>
                  <h3 id="admin-template-editor-title">Şablon bilgileri</h3>
                </div>
                <span className="admin-templates__revision">Sürüm {selected.revision}</span>
              </div>
              <label className="admin-templates__field" htmlFor="admin-template-name">
                <span>Ad</span>
                <input id="admin-template-name" required maxLength={200} value={draft.name}
                  onChange={event => setDraft(current => current ? { ...current, name: event.target.value } : current)} />
              </label>
              <label className="admin-templates__field" htmlFor="admin-template-description">
                <span>Açıklama <span className="admin-templates__optional">(isteğe bağlı)</span></span>
                <textarea id="admin-template-description" rows={4} maxLength={2000} value={draft.description ?? ''}
                  onChange={event => setDraft(current => current ? { ...current, description: event.target.value } : current)} />
                <span className="admin-templates__hint">En çok 2.000 karakter.</span>
              </label>
              <label className="admin-templates__visibility" htmlFor="admin-template-active">
                <input id="admin-template-active" type="checkbox" checked={draft.isActive}
                  onChange={event => setDraft(current => current ? { ...current, isActive: event.target.checked } : current)} />
                <span><strong>Katalogda göster</strong><small>Gizlenen şablon yeni seçimlerden kaldırılır; mevcut taslak ve yayınlar etkilenmez.</small></span>
              </label>
              {stale ? <div className="admin-templates__conflict" role="group" aria-label="Güncel şablon sürümü">
                <p>Sunucudaki güncel değerleri yükleyerek bu değişiklikleri yenileyin.</p>
                <button className="button button--secondary" type="button" onClick={refreshAfterConflict} disabled={loading}>Güncel sürümü yükle</button>
              </div> : null}
              <div className="admin-templates__actions">
                <button className="button button--primary" type="submit" disabled={!changed || !draft.name.trim() || saving || stale}>
                  {saving ? 'Kaydediliyor…' : 'Değişiklikleri kaydet'}
                </button>
              </div>
            </form>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
