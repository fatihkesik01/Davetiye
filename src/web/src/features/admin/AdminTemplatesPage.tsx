import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation()
  const tRef = useLatestT()
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
          setActionError(tRef.current('adminUi.templates.authRequired'))
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [api, reloadVersion, tRef])

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
    const visibility = t(draft.isActive ? 'adminUi.templates.visible' : 'adminUi.templates.hidden')
    if (!window.confirm(t('adminUi.templates.confirmSave', { name: name || selected.name, visibility }))) return

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
      setMessage(t('adminUi.templates.saved', { name: updated.name }))
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        staleRef.current = true
        setStale(true)
        setActionError(t('adminUi.templates.stale'))
      } else if (error instanceof ApiRequestError && (error.status === 401 || error.status === 403)) {
        setActionError(t('adminUi.templates.saveAuthRequired'))
      } else {
        setActionError(t('adminUi.templates.saveError'))
      }
    } finally {
      setSaving(false)
    }
  }

  return (
    <section className="admin-templates" aria-labelledby="admin-templates-title" aria-busy={loading || saving}>
      <header className="admin-overview__heading">
        <div>
          <h2 id="admin-templates-title">{t('adminUi.templates.title')}</h2>
          <p>{t('adminUi.templates.description')}</p>
        </div>
      </header>

      {actionError ? <p className="admin-banned__message is-error" role="alert">{actionError}</p> : null}
      {message ? <p className="admin-banned__message" role="status">{message}</p> : null}
      {loading ? <p className="admin-operational__status" role="status">{t('adminUi.templates.loading')}</p> : null}
      {!loading && loadError ? (
        <div className="admin-overview__error" role="alert">
          <h3>{t('adminUi.templates.loadFailed')}</h3>
          <p>{actionError || t('adminUi.templates.loadError')}</p>
          <button className="button button--secondary" type="button" onClick={retryLoad}>{t('adminUi.templates.retry')}</button>
        </div>
      ) : null}
      {!loading && !loadError && templates?.length === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>{t('adminUi.templates.emptyTitle')}</h3>
          <p>{t('adminUi.templates.emptyBody')}</p>
        </div>
      ) : null}
      {!loading && !loadError && templates && templates.length > 0 ? (
        <div className="admin-templates__layout">
          <nav className="admin-templates__list" aria-label={t('adminUi.templates.listLabel')}>
            {templates.map(template => (
              <button key={template.id} type="button"
                className={`admin-templates__choice${template.id === selectedId ? ' is-selected' : ''}`}
                aria-current={template.id === selectedId ? 'true' : undefined}
                onClick={() => selectTemplate(template)}>
                <span className="admin-templates__choice-name">{template.name}</span>
                <span className="admin-templates__choice-meta">{template.key} · {t(template.isActive ? 'adminUi.templates.visible' : 'adminUi.templates.hidden')}</span>
              </button>
            ))}
          </nav>
          {selected && draft ? (
            <form className="admin-templates__editor" onSubmit={event => void save(event)} aria-labelledby="admin-template-editor-title">
              <div className="admin-templates__editor-heading">
                <div>
                  <p className="eyebrow">{selected.key}</p>
                  <h3 id="admin-template-editor-title">{t('adminUi.templates.details')}</h3>
                </div>
                <span className="admin-templates__revision">{t('adminUi.templates.revision', { revision: selected.revision })}</span>
              </div>
              <label className="admin-templates__field" htmlFor="admin-template-name">
                <span>{t('adminUi.templates.name')}</span>
                <input id="admin-template-name" required maxLength={200} value={draft.name}
                  onChange={event => setDraft(current => current ? { ...current, name: event.target.value } : current)} />
              </label>
              <label className="admin-templates__field" htmlFor="admin-template-description">
                <span>{t('adminUi.templates.descriptionLabel')} <span className="admin-templates__optional">{t('adminUi.templates.optional')}</span></span>
                <textarea id="admin-template-description" rows={4} maxLength={2000} value={draft.description ?? ''}
                  onChange={event => setDraft(current => current ? { ...current, description: event.target.value } : current)} />
                <span className="admin-templates__hint">{t('adminUi.templates.maxChars')}</span>
              </label>
              <label className="admin-templates__visibility" htmlFor="admin-template-active">
                <input id="admin-template-active" type="checkbox" checked={draft.isActive}
                  onChange={event => setDraft(current => current ? { ...current, isActive: event.target.checked } : current)} />
                <span><strong>{t('adminUi.templates.showInCatalog')}</strong><small>{t('adminUi.templates.hiddenHint')}</small></span>
              </label>
              {stale ? <div className="admin-templates__conflict" role="group" aria-label={t('adminUi.templates.currentRevision')}>
                <p>{t('adminUi.templates.refreshConflict')}</p>
                <button className="button button--secondary" type="button" onClick={refreshAfterConflict} disabled={loading}>{t('adminUi.templates.refresh')}</button>
              </div> : null}
              <div className="admin-templates__actions">
                <button className="button button--primary" type="submit" disabled={!changed || !draft.name.trim() || saving || stale}>
                  {saving ? t('adminUi.templates.saving') : t('adminUi.templates.saveChanges')}
                </button>
              </div>
            </form>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
