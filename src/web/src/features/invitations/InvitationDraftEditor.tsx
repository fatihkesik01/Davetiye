import {
  type ChangeEvent,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'

import {
  ApiRequestError,
  DavetiyeApiClient,
  type InvitationDraftDetails,
  type InvitationDraftValidationReport,
  type InvitationPublicationStatus,
  type TemplateCatalogItem,
} from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { InternalLink } from '../../components/ui/InternalLink'
import { DevicePreview } from './DevicePreview'
import { DraftValidationSummary } from './DraftValidationSummary'
import { PublicationPanel } from './PublicationPanel'
import { TrashInvitationControl } from './TrashInvitationControl'
import { CreatorSharePanel } from './CreatorSharePanel'
import { OptionalContentEditor } from './OptionalContentEditor'
import { CreatorMediaPanel } from './CreatorMediaPanel'
import { MemoriesManagementPanel } from './MemoriesManagementPanel'
import { GiftRegistryManagementPanel } from './GiftRegistryManagementPanel'
import { RsvpManagementPanel } from './RsvpManagementPanel'
import { invitationFields } from './invitationFields'
import { publicationStateLabels } from './publicationPresentation'
import {
  draftRecoveryKey,
  emptyEditableDraft,
  eventTypes,
  parseRecoverySnapshot,
  toDraftContent,
  toEditableDraft,
  type DraftRecoverySnapshot,
  type EditableDraftContent,
} from './draftModel'

type SaveState = 'idle' | 'unsaved' | 'saving' | 'saved' | 'offline' | 'error' | 'conflict'

const steps = [
  'Etkinlik türü',
  'Temel bilgiler',
  'Şablon',
  'Tarih / Mekân',
  'Opsiyonel bölümler',
  'Önizleme',
  'Yayınla',
] as const

export function InvitationDraftEditor({ invitationId }: { invitationId: string }) {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [draft, setDraft] = useState<InvitationDraftDetails | null>(null)
  const [content, setContent] = useState<EditableDraftContent>(emptyEditableDraft)
  const [templates, setTemplates] = useState<TemplateCatalogItem[]>([])
  const [step, setStep] = useState(0)
  const [saveState, setSaveState] = useState<SaveState>('idle')
  const [message, setMessage] = useState('')
  const [recovery, setRecovery] = useState<DraftRecoverySnapshot | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [saveAttempt, setSaveAttempt] = useState(0)
  const [savedContentSnapshot, setSavedContentSnapshot] = useState('')
  const [validation, setValidation] = useState<InvitationDraftValidationReport | null>(null)
  const [validationLoading, setValidationLoading] = useState(false)
  const [validationFailed, setValidationFailed] = useState(false)
  const [validationAttempt, setValidationAttempt] = useState(0)
  const [publication, setPublication] = useState<InvitationPublicationStatus | null>(null)
  const [publicationLoading, setPublicationLoading] = useState(true)
  const [publicationFailed, setPublicationFailed] = useState(false)
  const [publicationAttempt, setPublicationAttempt] = useState(0)
  const [publicationBusy, setPublicationBusy] = useState(false)
  const stepHeading = useRef<HTMLHeadingElement>(null)
  const requestedFieldFocus = useRef<string | undefined>(undefined)
  const currentContent = useRef(content)
  const currentSaveState = useRef(saveState)
  const lastSavedContent = useRef('')

  useEffect(() => {
    currentContent.current = content
  }, [content])

  useEffect(() => {
    currentSaveState.current = saveState
  }, [saveState])

  useEffect(() => {
    const controller = new AbortController()
    void Promise.all([
      api.getInvitationDraft(invitationId, controller.signal),
      api.listTemplates(controller.signal),
    ]).then(([loadedDraft, loadedTemplates]) => {
      const editable = toEditableDraft(loadedDraft.content)
      const serialized = JSON.stringify(toDraftContent(editable))
      const localRecovery = parseRecoverySnapshot(sessionStorage.getItem(draftRecoveryKey(invitationId)))
      setDraft(loadedDraft)
      setContent(editable)
      setTemplates(loadedTemplates)
      lastSavedContent.current = serialized
      setSavedContentSnapshot(serialized)
      setSaveState('saved')
      if (localRecovery && JSON.stringify(toDraftContent(localRecovery.content)) !== serialized) {
        setRecovery(localRecovery)
      }
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setLoadFailed(true)
      setMessage(draftErrorMessage(error))
    })
    return () => controller.abort()
  }, [api, invitationId])

  useEffect(() => {
    const requested = requestedFieldFocus.current
    if (requested) document.getElementById(requested)?.focus()
    else stepHeading.current?.focus()
    requestedFieldFocus.current = undefined
  }, [step])

  const invitationRevision = draft?.invitationRevision
  const contentRevision = draft?.contentRevision
  useEffect(() => {
    if (invitationRevision === undefined || contentRevision === undefined) return
    const controller = new AbortController()
    queueMicrotask(() => {
      if (controller.signal.aborted) return
      setPublicationLoading(true)
      setPublicationFailed(false)
    })
    void api.getInvitationPublication(invitationId, controller.signal).then(status => {
      setPublication(status)
      setPublicationLoading(false)
      if (status.expected.invitationRevision !== invitationRevision || status.expected.workingContentRevision !== contentRevision) {
        setSaveState('conflict')
        setMessage('Sunucudaki davetiye siz düzenlerken değişti. Güncel sürümü yükleyip yeniden gözden geçirin.')
      }
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setPublicationLoading(false)
      setPublicationFailed(true)
    })
    return () => controller.abort()
  }, [api, invitationId, invitationRevision, contentRevision, publicationAttempt])

  useEffect(() => {
    if (!publication || publicationBusy) return
    const serverNow = new Date(publication.serverNowUtc).valueOf()
    const boundaries = publication.currentWindow
      ? [publication.currentWindow.startsAtUtc, publication.currentWindow.endsAtUtc].map(value => new Date(value).valueOf() - serverNow).filter(value => value > 0)
      : []
    const delay = Math.max(1000, Math.min(60_000, ...boundaries.map(value => value + 100)))
    const timeout = window.setTimeout(() => setPublicationAttempt(value => value + 1), delay)
    const refresh = () => setPublicationAttempt(value => value + 1)
    window.addEventListener('focus', refresh)
    return () => {
      window.clearTimeout(timeout)
      window.removeEventListener('focus', refresh)
    }
  }, [publication, publicationBusy])

  useEffect(() => {
    if (!draft || currentSaveState.current === 'conflict') return
    const typedContent = toDraftContent(content)
    const serialized = JSON.stringify(typedContent)
    if (serialized === lastSavedContent.current) return

    const recoverySnapshot: DraftRecoverySnapshot = {
      content,
      baseContentRevision: draft.contentRevision,
    }
    sessionStorage.setItem(draftRecoveryKey(invitationId), JSON.stringify(recoverySnapshot))
    setSaveState('unsaved')
    setMessage('Değişiklikler kaydedilmeyi bekliyor.')
    const controller = new AbortController()
    const timeout = window.setTimeout(() => {
      setSaveState('saving')
      setMessage('Kaydediliyor…')
      void api.getAntiforgeryToken(controller.signal)
        .then(csrfToken => api.autosaveInvitationDraft(invitationId, {
          contentSchemaVersion: 1,
          content: typedContent,
          expectedContentRevision: draft.contentRevision,
        }, csrfToken, controller.signal))
        .then(savedDraft => {
          lastSavedContent.current = serialized
          setSavedContentSnapshot(serialized)
          setDraft(savedDraft)
          if (JSON.stringify(toDraftContent(currentContent.current)) === serialized) {
            sessionStorage.removeItem(draftRecoveryKey(invitationId))
            setSaveState('saved')
            setMessage('Kaydedildi.')
          }
        })
        .catch(error => {
          if (error instanceof DOMException && error.name === 'AbortError') return
          if (error instanceof ApiRequestError && error.status === 409) {
            setSaveState('conflict')
            setMessage('Sunucudaki taslak siz düzenlerken değişti. Hangi sürümle devam edeceğinizi seçin.')
            return
          }
          setSaveState(navigator.onLine ? 'error' : 'offline')
          setMessage(navigator.onLine
            ? 'Kaydetme başarısız. Değişiklikler bu sekmede geçici olarak korunuyor.'
            : 'Bağlantı yok. Değişiklikler bu sekmede geçici olarak korunuyor.')
        })
    }, 800)

    return () => {
      window.clearTimeout(timeout)
      controller.abort()
    }
  }, [api, content, draft, invitationId, saveAttempt])

  useEffect(() => {
    if (step < 5 || !draft) return
    const controller = new AbortController()
    queueMicrotask(() => {
      if (controller.signal.aborted) return
      setValidationLoading(true)
      setValidationFailed(false)
    })
    void api.getInvitationDraftValidation(invitationId, controller.signal)
      .then(report => {
        setValidation(report)
        setValidationLoading(false)
      })
      .catch(error => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setValidationLoading(false)
        setValidationFailed(true)
      })
    return () => controller.abort()
  }, [
    api,
    draft,
    invitationId,
    step,
    validationAttempt,
  ])

  useEffect(() => {
    const hasUnsavedChanges = ['unsaved', 'saving', 'offline', 'error', 'conflict'].includes(saveState)
    if (!hasUnsavedChanges) return
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [saveState])

  if (loadFailed) return <section className="inline-alert" role="alert"><p>{message}</p><InternalLink to="/panel/davetiyeler">Taslaklara dön</InternalLink></section>
  if (!draft) return <LoadingState label="Taslak editörü yükleniyor" />

  const update = <Key extends keyof EditableDraftContent>(key: Key, value: EditableDraftContent[Key]) => {
    setContent(previous => ({ ...previous, [key]: value }))
  }

  const hasUnsavedChanges = JSON.stringify(toDraftContent(content)) !== savedContentSnapshot || ['unsaved', 'saving', 'offline', 'error', 'conflict'].includes(saveState)
  const publicationPanel = <><PublicationPanel
    api={api}
    status={publication}
    loading={publicationLoading}
    failed={publicationFailed}
    hasUnsavedChanges={hasUnsavedChanges}
    busy={publicationBusy}
    onBusy={setPublicationBusy}
    onRefresh={() => setPublicationAttempt(value => value + 1)}
    onChanged={async status => {
      const updated = await api.getInvitationDraft(invitationId)
      setDraft(updated)
      setPublication(status)
    }}
    onEditField={field => {
      const target = invitationFields[field]
      requestedFieldFocus.current = target?.id
      setStep(target?.step ?? 2)
    }}
  /><CreatorSharePanel status={publication} /><TrashInvitationControl
    api={api}
    status={publication}
    disabled={hasUnsavedChanges || publicationLoading || publicationFailed}
    busy={publicationBusy}
    onBusy={setPublicationBusy}
    onRefresh={() => setPublicationAttempt(value => value + 1)}
  /></>

  const selectedTemplate = templates.find(template => template.key === draft.templateKey)
  const creatorMediaPanel = <CreatorMediaPanel
    api={api}
    invitationId={invitationId}
    hasPublicationGrant={Boolean(publication?.currentWindow?.grantId)}
    supportedModules={selectedTemplate?.supportedModules ?? []}
  />

  const selectTemplate = async (templateKey: string) => {
    if (draft.templateKey === templateKey) return
    setSaveState('saving')
    setMessage('Şablon seçimi kaydediliyor…')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const updated = await api.selectInvitationTemplate(invitationId, {
        templateKey,
        expectedInvitationRevision: draft.invitationRevision,
      }, csrfToken)
      setDraft(updated)
      setSaveState('saved')
      setMessage('Şablon seçimi kaydedildi.')
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        setSaveState('conflict')
        setMessage('Sunucudaki şablon seçimi değişti. Taslağı yenileyip tekrar deneyin.')
      } else {
        setSaveState('error')
        setMessage(draftErrorMessage(error))
      }
    }
  }

  const reloadServerVersion = async () => {
    setMessage('Sunucudaki sürüm yükleniyor…')
    try {
      const loaded = await api.getInvitationDraft(invitationId)
      const editable = toEditableDraft(loaded.content)
      setDraft(loaded)
      setContent(editable)
      lastSavedContent.current = JSON.stringify(toDraftContent(editable))
      setSavedContentSnapshot(lastSavedContent.current)
      sessionStorage.removeItem(draftRecoveryKey(invitationId))
      setSaveState('saved')
      setMessage('Sunucudaki sürüm yüklendi.')
    } catch (error) {
      setSaveState('error')
      setMessage(draftErrorMessage(error))
    }
  }

  const retryOwnVersion = async () => {
    setMessage('Güncel revision alınıyor…')
    try {
      const loaded = await api.getInvitationDraft(invitationId)
      setDraft(loaded)
      setSaveState('unsaved')
      setMessage('Değişiklikleriniz yeniden kaydedilecek.')
    } catch (error) {
      setSaveState('error')
      setMessage(draftErrorMessage(error))
    }
  }

  const restoreRecovery = () => {
    if (!recovery) return
    setContent(recovery.content)
    setRecovery(null)
    setSaveState('unsaved')
    setMessage('Bu sekmede korunan değişiklikler geri yüklendi ve kaydedilecek.')
  }

  const discardRecovery = () => {
    sessionStorage.removeItem(draftRecoveryKey(invitationId))
    setRecovery(null)
  }

  return <div className="draft-editor">
    <nav className="editor-topbar" aria-label="Editör üst menüsü">
      <InternalLink to="/panel/davetiyeler">← Taslaklar</InternalLink>
      <div className="autosave-status-group">
        <p className={`autosave-status autosave-status--${saveState}`} role={saveState === 'error' || saveState === 'conflict' ? 'alert' : 'status'} aria-live="polite">
          <span aria-hidden="true" />{message || 'Taslak hazır.'}
        </p>
        {saveState === 'error' || saveState === 'offline'
          ? <button className="text-button" type="button" onClick={() => setSaveAttempt(value => value + 1)}>Kaydetmeyi tekrar dene</button>
          : null}
      </div>
    </nav>

    {publication ? <div className="publication-editor-status">
      <p>Durum: <strong>{publicationStateLabels[publication.effectiveState]}</strong></p>
      {publication.hasPendingChanges || (publication.published && hasUnsavedChanges) ? <p role="status">Değişiklikler henüz yayında değil. Otomatik kayıt yayındaki davetiyeyi değiştirmez.</p> : null}
    </div> : null}

    {recovery ? <section className="recovery-banner" aria-labelledby="recovery-heading">
      <h2 id="recovery-heading">Kaydedilmemiş değişiklik bulundu</h2>
      <p>Bu sekmede geçici olarak korunan bilgileri geri yüklemek ister misiniz?</p>
      <div className="button-row">
        <button className="button button--primary" type="button" onClick={restoreRecovery}>Değişiklikleri geri yükle</button>
        <button className="button button--secondary" type="button" onClick={discardRecovery}>Sunucudaki sürümle devam et</button>
      </div>
    </section> : null}

    {saveState === 'conflict' ? <section className="conflict-banner" aria-labelledby="conflict-heading">
      <h2 id="conflict-heading">Taslak çakışması</h2>
      <p>Başka bir sekmede veya cihazda daha yeni bir kayıt var. Seçiminiz yapılana kadar otomatik kayıt durduruldu.</p>
      <div className="button-row">
        <button className="button button--primary" type="button" onClick={() => void reloadServerVersion()}>Sunucudaki sürümü yükle</button>
        <button className="button button--secondary" type="button" onClick={() => void retryOwnVersion()}>Benim değişikliklerimi yeniden kaydet</button>
      </div>
    </section> : null}

    <ol className="wizard-steps" aria-label="Davetiye oluşturma adımları">
      {steps.map((label, index) => <li key={label} aria-current={index === step ? 'step' : undefined}>
        <button type="button" disabled={publicationBusy} onClick={() => setStep(index)}>
          <span>{index + 1}</span>{label}
        </button>
      </li>)}
    </ol>

    <section className="wizard-panel" aria-labelledby="wizard-step-heading">
      <p className="wizard-panel__counter">Adım {step + 1} / {steps.length}</p>
      <h2 id="wizard-step-heading" ref={stepHeading} tabIndex={-1}>{steps[step]}</h2>
      <fieldset className="wizard-content" disabled={publicationBusy}>
      <WizardStep
        step={step}
        content={content}
        update={update}
        templates={templates}
        draft={draft}
        selectTemplate={selectTemplate}
        validation={validation}
        validationLoading={validationLoading}
        validationFailed={validationFailed}
        hasUnsavedChanges={hasUnsavedChanges}
        retryValidation={() => setValidationAttempt(value => value + 1)}
        templateLocked={!publication || publicationLoading || publicationFailed || publication.effectiveState === 'Active'}
        templateActive={publication?.effectiveState === 'Active'}
        publicationPanel={publicationPanel}
        mediaPanel={creatorMediaPanel}
        rsvpPanel={<RsvpManagementPanel
          key={invitationId}
          api={api}
          invitationId={invitationId}
          effectiveState={publication?.effectiveState ?? null}
          publicationReady={!publicationLoading && !publicationFailed && Boolean(publication)}
          templateSupportsRsvp={selectedTemplate?.supportedModules.includes('rsvp') ?? false}
          onManagePublication={() => setStep(6)}
        />}
        memoriesPanel={<MemoriesManagementPanel key={`${invitationId}-memories`} api={api} invitationId={invitationId} />}
        giftsPanel={<GiftRegistryManagementPanel key={`${invitationId}-gifts`} api={api} invitationId={invitationId} />}
        onManagePublication={() => setStep(6)}
      />
      </fieldset>
      <div className="wizard-actions">
        {step > 0 ? <button disabled={publicationBusy} className="button button--secondary" type="button" onClick={() => setStep(value => value - 1)}>Geri</button> : <span />}
        {step === 4 ? <button disabled={publicationBusy} className="button button--secondary" type="button" onClick={() => {
          update('programTitle', '')
          update('programDescription', '')
          update('programStartsAtLocal', '')
          update('additionalProgramItems', [])
          setStep(5)
        }}>Şimdilik geç</button> : null}
        {step < steps.length - 1 ? <button disabled={publicationBusy} className="button button--primary" type="button" onClick={() => setStep(value => value + 1)}>Devam et</button> : null}
      </div>
    </section>
  </div>
}

interface WizardStepProps {
  step: number
  content: EditableDraftContent
  update: <Key extends keyof EditableDraftContent>(key: Key, value: EditableDraftContent[Key]) => void
  templates: TemplateCatalogItem[]
  draft: InvitationDraftDetails
  selectTemplate: (templateKey: string) => Promise<void>
  validation: InvitationDraftValidationReport | null
  validationLoading: boolean
  validationFailed: boolean
  hasUnsavedChanges: boolean
  retryValidation: () => void
  templateLocked: boolean
  templateActive: boolean
  publicationPanel: React.ReactNode
  mediaPanel: React.ReactNode
  rsvpPanel: React.ReactNode
  memoriesPanel: React.ReactNode
  giftsPanel: React.ReactNode
  onManagePublication: () => void
}

function WizardStep({
  step,
  content,
  update,
  templates,
  draft,
  selectTemplate,
  validation,
  validationLoading,
  validationFailed,
  hasUnsavedChanges,
  retryValidation,
  templateLocked,
  templateActive,
  publicationPanel,
  mediaPanel,
  rsvpPanel,
  memoriesPanel,
  giftsPanel,
  onManagePublication,
}: WizardStepProps) {
  if (step === 0) return <fieldset className="choice-grid">
    <legend>Etkinliğinizi en iyi anlatan türü seçin</legend>
    {eventTypes.map(option => <label key={option.value} className="choice-card">
      <input type="radio" name="event-type" value={option.value} checked={content.eventType === option.value} onChange={() => update('eventType', option.value)} />
      <span>{option.label}</span>
    </label>)}
  </fieldset>

  if (step === 1) return <div className="wizard-fields">
    <EditorField id="draft-headline" label="Davetiye başlığı" value={content.headline} onChange={value => update('headline', value)} maxLength={200} help="Örnek: Zeynep & Kerem evleniyor" />
    <EditorField id="draft-hosts" label="İsimler" value={content.hostNamesText} onChange={value => update('hostNamesText', value)} maxLength={500} help="Birden fazla ismi virgülle ayırabilirsiniz." />
    <EditorTextArea id="draft-message" label="Kısa davet mesajı" value={content.message} onChange={value => update('message', value)} maxLength={4000} />
  </div>

  if (step === 2) return <fieldset className="template-choice-grid">
    <legend>Bir şablon seçin</legend>
    <p className="fieldset-help">Premium şablonları taslakta seçebilir ve önizleyebilirsiniz.</p>
    {templateLocked ? <div className="fieldset-help publication-notice">
      <p>{templateActive ? 'Yayındaki şablon doğrudan değiştirilemez. Önce yayını durdurun, şablonu değiştirip önizleyin, ardından değişiklikleri yayımlayarak devam edin.' : 'Şablon seçmeden önce güncel yayın durumu doğrulanmalıdır.'}</p>
      <button type="button" className="button button--secondary" onClick={onManagePublication}>Yayın yönetimine git</button>
    </div> : null}
    {templates.length === 0 ? <p className="inline-alert" role="status">Şu anda seçilebilecek aktif şablon bulunmuyor.</p> : null}
    {templates.map(template => <label key={template.key} className="template-choice-card">
      <input
        type="radio"
        name="template"
        value={template.key}
        checked={draft.templateKey === template.key}
        disabled={templateLocked || hasUnsavedChanges}
        onChange={() => void selectTemplate(template.key)}
      />
      {template.previewImageUrl ? <img src={template.previewImageUrl} alt="" width="960" height="640" /> : null}
      <span className="template-choice-card__copy">
        <strong>{template.name}</strong>
        <small>{template.category} · {template.isPremium ? 'Premium' : 'Ücretsiz'}</small>
      </span>
    </label>)}
  </fieldset>

  if (step === 3) return <div className="wizard-fields">
    <EditorDateTime id="draft-starts-at" label="Etkinlik tarihi ve saati" value={content.startsAtLocal} onChange={value => update('startsAtLocal', value)} />
    <EditorField id="draft-venue" label="Mekân adı" value={content.venueName} onChange={value => update('venueName', value)} maxLength={200} />
    <EditorTextArea id="draft-address" label="Adres" value={content.venueAddress} onChange={value => update('venueAddress', value)} maxLength={500} />
    <EditorField id="draft-map-url" label="Harita bağlantısı" value={content.mapUrl} onChange={value => update('mapUrl', value)} maxLength={2048} inputMode="url" help="İsteğe bağlı; http veya https bağlantısı kullanın." />
  </div>

  if (step === 4) return <div className="wizard-fields">
    <p>İsterseniz davetiyenize ilk program satırını ekleyin. Bu adımı şimdi geçebilirsiniz.</p>
    <EditorField id="draft-program-title" label="Program başlığı" value={content.programTitle} onChange={value => update('programTitle', value)} maxLength={200} help="Örnek: Karşılama, Nikâh veya Kutlama" />
    <EditorDateTime id="draft-program-start" label="Program saati" value={content.programStartsAtLocal} onChange={value => update('programStartsAtLocal', value)} />
    <EditorTextArea id="draft-program-description" label="Program açıklaması" value={content.programDescription} onChange={value => update('programDescription', value)} maxLength={1000} />
    <OptionalContentEditor content={content} update={update} />
    {rsvpPanel}
    {memoriesPanel}
    {giftsPanel}
    {mediaPanel}
  </div>

  if (step === 5) return <div className="preview-step">
    <DevicePreview
      invitationId={draft.id}
      templateKey={draft.templateKey}
      rendererVersion={draft.rendererVersion}
      content={toDraftContent(content)}
    />
    <DraftValidationSummary
      report={validation}
      loading={validationLoading}
      failed={validationFailed}
      hasUnsavedChanges={hasUnsavedChanges}
      onRetry={retryValidation}
    />
  </div>

  return <div className="publish-step">{publicationPanel}</div>
}

interface EditorFieldProps {
  id: string
  label: string
  value: string
  onChange: (value: string) => void
  maxLength: number
  help?: string
  inputMode?: 'text' | 'url'
}

function EditorField({ id, label, value, onChange, maxLength, help, inputMode = 'text' }: EditorFieldProps) {
  const helpId = help ? `${id}-help` : undefined
  return <div className="form-field">
    <label htmlFor={id}>{label}</label>
    {help ? <p id={helpId} className="form-field__help">{help}</p> : null}
    <input id={id} name={id} type={inputMode === 'url' ? 'url' : 'text'} inputMode={inputMode === 'url' ? 'url' : 'text'} value={value} maxLength={maxLength} aria-describedby={helpId} onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)} />
  </div>
}

function EditorTextArea({ id, label, value, onChange, maxLength }: Omit<EditorFieldProps, 'help' | 'inputMode'>) {
  return <div className="form-field">
    <label htmlFor={id}>{label}</label>
    <textarea id={id} name={id} value={value} maxLength={maxLength} rows={4} onChange={(event: ChangeEvent<HTMLTextAreaElement>) => onChange(event.target.value)} />
  </div>
}

function EditorDateTime({ id, label, value, onChange }: Omit<EditorFieldProps, 'maxLength' | 'help' | 'inputMode'>) {
  return <div className="form-field">
    <label htmlFor={id}>{label}</label>
    <input id={id} name={id} type="datetime-local" value={value} onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)} />
    <p className="form-field__help">Tarih ve saat bu cihazın yerel saatine göre kaydedilir. Bu aşamada ayrıca bir saat dilimi seçilmez.</p>
  </div>
}

function draftErrorMessage(error: unknown): string {
  if (error instanceof ApiRequestError && error.status === 404) return 'Bu taslak bulunamadı veya erişim izniniz yok.'
  if (error instanceof ApiRequestError && error.status === 400) return 'Bazı bilgiler kaydedilemedi. Alanları kontrol edip yeniden deneyin.'
  return 'İşlem tamamlanamadı. Bağlantınızı kontrol edip yeniden deneyin.'
}
