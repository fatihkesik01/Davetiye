import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'

import {
  DavetiyeApiClient,
  type InvitationDraftDetails,
  type InvitationDraftPage,
  type TemplateCatalogItem,
} from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { InternalLink } from '../../components/ui/InternalLink'
import { navigate } from '../../routes/navigation'
import { InvitationRenderer } from '../templates/rendering/InvitationRenderer'
import { normalizeDraftContent } from '../templates/rendering/model'
import { InvitationDraftEditor } from './InvitationDraftEditor'

export function InvitationDraftDashboard() {
  const { t, i18n } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [page, setPage] = useState<InvitationDraftPage | null>(null)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    void api.listInvitationDrafts(1, 50, controller.signal)
      .then(setPage)
      .catch(error => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFailed(true)
      })
    return () => controller.abort()
  }, [api, attempt])

  return <section className="draft-dashboard" aria-labelledby="drafts-heading">
    <div className="dashboard-heading">
      <div>
        <p className="eyebrow">{t('drafts.eyebrow')}</p>
        <h2 id="drafts-heading">{t('drafts.yours')}</h2>
        <p>{t('drafts.autosave')}</p>
      </div>
      <InternalLink className="button button--primary" to="/panel/davetiyeler/yeni">{t('drafts.create')}</InternalLink>
    </div>
    {failed ? <div className="inline-alert" role="alert">
      <p>{t('drafts.failed')}</p>
      <button className="button button--secondary" type="button" onClick={() => {
        setFailed(false)
        setPage(null)
        setAttempt(value => value + 1)
      }}>{t('drafts.retry')}</button>
    </div> : null}
    {!failed && !page ? <LoadingState label={t('drafts.loading')} /> : null}
    {page?.items.length === 0 ? <div className="catalog-empty">
      <h3>{t('drafts.emptyTitle')}</h3>
      <p>{t('drafts.emptyBody')}</p>
    </div> : null}
    {page && page.items.length > 0 ? <ul className="draft-list">
      {page.items.map(draft => <li key={draft.id}>
        <article className="draft-list__item">
          <div>
            <h3>{draft.headline?.trim() || `${t('drafts.unnamed')} · ${draft.id.slice(0, 8)}`}</h3>
            {draft.effectiveState ? <p>{t('common.status')}: {t(`drafts.state.${draft.effectiveState}`)}</p> : null}
            <p>{draft.templateKey ? t('drafts.selected') : t('drafts.waiting')} · {t('drafts.lastSaved')} {formatUpdatedAt(draft.updatedAt, i18n.language)}</p>
          </div>
          <InternalLink className="button button--secondary" to={`/panel/davetiyeler/${draft.id}/duzenle`}>
            {t('drafts.continue')}
          </InternalLink>
        </article>
      </li>)}
    </ul> : null}
  </section>
}

export function NewInvitationPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const templateKey = new URLSearchParams(window.location.search).get('template')
  const [template, setTemplate] = useState<TemplateCatalogItem | null>(null)
  const [templateStatus, setTemplateStatus] = useState<'idle' | 'loading' | 'resolved' | 'missing' | 'error'>(
    templateKey ? 'loading' : 'idle',
  )
  const [templateAttempt, setTemplateAttempt] = useState(0)
  const [creating, setCreating] = useState(false)
  const [message, setMessage] = useState('')

  useEffect(() => {
    if (!templateKey) return
    const controller = new AbortController()
    queueMicrotask(() => {
      if (!controller.signal.aborted) setTemplateStatus('loading')
    })
    void api.listTemplates(controller.signal)
      .then(templates => {
        const selected = templates.find(item => item.key === templateKey) ?? null
        setTemplate(selected)
        setTemplateStatus(selected ? 'resolved' : 'missing')
      })
      .catch(error => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setTemplate(null)
        setTemplateStatus('error')
      })
    return () => controller.abort()
  }, [api, templateAttempt, templateKey])

  const create = async () => {
    if (templateKey && (templateStatus === 'loading' || templateStatus === 'error')) return
    setCreating(true)
    setMessage('Taslağınız hazırlanıyor…')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const draft = await api.createInvitationDraft({
        contentSchemaVersion: 1,
        content: {},
        templateKey: template?.key ?? null,
      }, csrfToken)
      navigate(`/panel/davetiyeler/${draft.id}/duzenle`)
    } catch {
      setCreating(false)
      setMessage('Taslak oluşturulamadı. Bağlantınızı kontrol edip yeniden deneyin.')
    }
  }

  return <section className="new-draft-card" aria-labelledby="new-draft-heading">
    <p className="eyebrow">Yeni davetiye</p>
    <h2 id="new-draft-heading">Birkaç adımda taslağınızı hazırlayın</h2>
    <p>Her adımı tamamlamak zorunda değilsiniz. Taslağınız oluşturulduktan sonra anlamlı değişiklikler otomatik kaydedilir.</p>
    {templateKey && templateStatus === 'loading' ? <p className="selection-note" role="status">
      Seçtiğiniz şablon doğrulanıyor…
    </p> : null}
    {templateKey && templateStatus === 'resolved' && template ? <p className="selection-note">
      <strong>{template.name}</strong> şablonuyla başlayacaksınız{template.isPremium ? '. Premium şablonlar taslakta serbestçe denenebilir.' : '.'}
    </p> : null}
    {templateKey && templateStatus === 'missing' ? <p className="selection-note">
      Seçtiğiniz şablon aktif katalogda bulunamadı; şablonsuz başlayabilirsiniz.
    </p> : null}
    {templateKey && templateStatus === 'error' ? <div className="inline-alert" role="alert">
      <p>Seçtiğiniz şablon doğrulanamadı. Seçiminizi kaybetmemek için tekrar deneyin.</p>
      <button className="button button--secondary" type="button" onClick={() => setTemplateAttempt(value => value + 1)}>
        Tekrar dene
      </button>
    </div> : null}
    <div className="button-row">
      <button
        className="button button--primary"
        type="button"
        onClick={() => void create()}
        disabled={creating || templateStatus === 'loading' || templateStatus === 'error'}
      >Taslağı oluştur</button>
      <InternalLink className="button button--secondary" to="/panel/davetiyeler">Vazgeç</InternalLink>
    </div>
    {message ? <p className={`form-status ${creating ? '' : 'form-status--error'}`} role={creating ? 'status' : 'alert'} aria-live="polite">{message}</p> : null}
  </section>
}

export function InvitationEditorPage({ invitationId }: { invitationId: string }) {
  return <InvitationDraftEditor invitationId={invitationId} />
}

export function FullDraftPreviewPage({ invitationId }: { invitationId: string }) {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [draft, setDraft] = useState<InvitationDraftDetails | null>(null)
  const [notAvailable, setNotAvailable] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    void api.getInvitationDraft(invitationId, controller.signal)
      .then(setDraft)
      .catch(error => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setNotAvailable(true)
      })
    return () => controller.abort()
  }, [api, invitationId])

  if (notAvailable) return <p className="inline-alert" role="alert">Bu taslak bulunamadı veya erişim izniniz yok.</p>
  if (!draft) return <LoadingState label="Önizleme yükleniyor" />
  if (!draft.templateKey || !draft.rendererVersion) return <section className="preview-empty" role="status"><h2>Önizleme hazır değil</h2><p>Önce editörde bir şablon seçin.</p></section>

  return <section className="full-draft-preview" aria-label="Tam taslak önizlemesi">
    <p className="draft-preview-banner" role="status">Korumalı taslak önizlemesi · Public bir davetiye değildir.</p>
    <InvitationRenderer
      templateKey={draft.templateKey}
      rendererVersion={draft.rendererVersion}
      model={normalizeDraftContent(draft.content)}
      previewContext="creator"
    />
  </section>
}

function formatUpdatedAt(value: string, locale: string): string {
  try {
    return new Intl.DateTimeFormat(locale === 'en' ? 'en-US' : 'tr-TR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
  } catch {
    return 'bilinmiyor'
  }
}
