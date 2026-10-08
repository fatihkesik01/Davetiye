import {
  type ChangeEvent,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import { useTranslation } from 'react-i18next'

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

export function InvitationDraftEditor({ invitationId }: { invitationId: string }) {
  const { t } = useTranslation()
  const steps = [
    t('creatorFormsUi.editor.steps.eventType'), t('creatorFormsUi.editor.steps.basics'), t('creatorFormsUi.editor.steps.template'),
    t('creatorFormsUi.editor.steps.dateVenue'), t('creatorFormsUi.editor.steps.optional'), t('creatorFormsUi.editor.steps.preview'), t('creatorFormsUi.editor.steps.publish'),
  ]
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
      setMessage(draftErrorMessage(error, t))
    })
    return () => controller.abort()
  }, [api, invitationId, t])

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
        setMessage(t('creatorFormsUi.editor.conflictPublication'))
      }
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setPublicationLoading(false)
      setPublicationFailed(true)
    })
    return () => controller.abort()
  }, [api, invitationId, invitationRevision, contentRevision, publicationAttempt, t])

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
    setMessage(t('creatorFormsUi.editor.unsaved'))
    const controller = new AbortController()
    const timeout = window.setTimeout(() => {
      setSaveState('saving')
      setMessage(t('creatorFormsUi.editor.saving'))
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
            setMessage(t('creatorFormsUi.editor.saved'))
          }
        })
        .catch(error => {
          if (error instanceof DOMException && error.name === 'AbortError') return
          if (error instanceof ApiRequestError && error.status === 409) {
            setSaveState('conflict')
            setMessage(t('creatorFormsUi.editor.saveConflict'))
            return
          }
          setSaveState(navigator.onLine ? 'error' : 'offline')
          setMessage(navigator.onLine
            ? t('creatorFormsUi.editor.saveFailed')
            : t('creatorFormsUi.editor.offline'))
        })
    }, 800)

    return () => {
      window.clearTimeout(timeout)
      controller.abort()
    }
  }, [api, content, draft, invitationId, saveAttempt, t])

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

  if (loadFailed) return <section className="inline-alert" role="alert"><p>{message}</p><InternalLink to="/panel/davetiyeler">{t('creatorFormsUi.editor.loadFailedBack')}</InternalLink></section>
  if (!draft) return <LoadingState label={t('creatorFormsUi.editor.loading')} />

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
    setMessage(t('creatorFormsUi.editor.templateSaving'))
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const updated = await api.selectInvitationTemplate(invitationId, {
        templateKey,
        expectedInvitationRevision: draft.invitationRevision,
      }, csrfToken)
      setDraft(updated)
      setSaveState('saved')
      setMessage(t('creatorFormsUi.editor.templateSaved'))
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        setSaveState('conflict')
        setMessage(t('creatorFormsUi.editor.templateConflict'))
      } else {
        setSaveState('error')
        setMessage(draftErrorMessage(error, t))
      }
    }
  }

  const reloadServerVersion = async () => {
    setMessage(t('creatorFormsUi.editor.serverLoading'))
    try {
      const loaded = await api.getInvitationDraft(invitationId)
      const editable = toEditableDraft(loaded.content)
      setDraft(loaded)
      setContent(editable)
      lastSavedContent.current = JSON.stringify(toDraftContent(editable))
      setSavedContentSnapshot(lastSavedContent.current)
      sessionStorage.removeItem(draftRecoveryKey(invitationId))
      setSaveState('saved')
      setMessage(t('creatorFormsUi.editor.serverLoaded'))
    } catch (error) {
      setSaveState('error')
      setMessage(draftErrorMessage(error, t))
    }
  }

  const retryOwnVersion = async () => {
    setMessage(t('creatorFormsUi.editor.revisionLoading'))
    try {
      const loaded = await api.getInvitationDraft(invitationId)
      setDraft(loaded)
      setSaveState('unsaved')
      setMessage(t('creatorFormsUi.editor.retryOwn'))
    } catch (error) {
      setSaveState('error')
      setMessage(draftErrorMessage(error, t))
    }
  }

  const restoreRecovery = () => {
    if (!recovery) return
    setContent(recovery.content)
    setRecovery(null)
    setSaveState('unsaved')
    setMessage(t('creatorFormsUi.editor.recoveryRestored'))
  }

  const discardRecovery = () => {
    sessionStorage.removeItem(draftRecoveryKey(invitationId))
    setRecovery(null)
  }

  return <div className="draft-editor">
    <nav className="editor-topbar" aria-label={t('creatorFormsUi.editor.topNav')}>
      <InternalLink to="/panel/davetiyeler">{t('creatorFormsUi.editor.backDrafts')}</InternalLink>
      <div className="autosave-status-group">
        <p className={`autosave-status autosave-status--${saveState}`} role={saveState === 'error' || saveState === 'conflict' ? 'alert' : 'status'} aria-live="polite">
          <span aria-hidden="true" />{message || t('creatorFormsUi.editor.ready')}
        </p>
        {saveState === 'error' || saveState === 'offline'
          ? <button className="text-button" type="button" onClick={() => setSaveAttempt(value => value + 1)}>{t('creatorFormsUi.editor.retrySave')}</button>
          : null}
      </div>
    </nav>

    {publication ? <div className="publication-editor-status">
      <p>{t('creatorFormsUi.publication.status')}: <strong>{t(`creatorFormsUi.publication.state.${publication.effectiveState}`)}</strong></p>
      {publication.hasPendingChanges || (publication.published && hasUnsavedChanges) ? <p role="status">{t('creatorFormsUi.editor.unpublished')}</p> : null}
    </div> : null}

    {recovery ? <section className="recovery-banner" aria-labelledby="recovery-heading">
      <h2 id="recovery-heading">{t('creatorFormsUi.editor.recoveryTitle')}</h2>
      <p>{t('creatorFormsUi.editor.recoveryBody')}</p>
      <div className="button-row">
        <button className="button button--primary" type="button" onClick={restoreRecovery}>{t('creatorFormsUi.editor.restore')}</button>
        <button className="button button--secondary" type="button" onClick={discardRecovery}>{t('creatorFormsUi.editor.useServer')}</button>
      </div>
    </section> : null}

    {saveState === 'conflict' ? <section className="conflict-banner" aria-labelledby="conflict-heading">
      <h2 id="conflict-heading">{t('creatorFormsUi.editor.conflictTitle')}</h2>
      <p>{t('creatorFormsUi.editor.conflictBody')}</p>
      <div className="button-row">
        <button className="button button--primary" type="button" onClick={() => void reloadServerVersion()}>{t('creatorFormsUi.editor.loadServer')}</button>
        <button className="button button--secondary" type="button" onClick={() => void retryOwnVersion()}>{t('creatorFormsUi.editor.retryMine')}</button>
      </div>
    </section> : null}

    <ol className="wizard-steps" aria-label={t('creatorFormsUi.editor.stepsLabel')}>
      {steps.map((label, index) => <li key={label} aria-current={index === step ? 'step' : undefined}>
        <button type="button" disabled={publicationBusy} onClick={() => setStep(index)}>
          <span>{index + 1}</span>{label}
        </button>
      </li>)}
    </ol>

    <section className="wizard-panel" aria-labelledby="wizard-step-heading">
      <p className="wizard-panel__counter">{t('creatorFormsUi.editor.stepCounter', { current: step + 1, total: steps.length })}</p>
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
        {step > 0 ? <button disabled={publicationBusy} className="button button--secondary" type="button" onClick={() => setStep(value => value - 1)}>{t('creatorFormsUi.editor.back')}</button> : <span />}
        {step === 4 ? <button disabled={publicationBusy} className="button button--secondary" type="button" onClick={() => {
          update('programTitle', '')
          update('programDescription', '')
          update('programStartsAtLocal', '')
          update('additionalProgramItems', [])
          setStep(5)
        }}>{t('creatorFormsUi.editor.skip')}</button> : null}
        {step < steps.length - 1 ? <button disabled={publicationBusy} className="button button--primary" type="button" onClick={() => setStep(value => value + 1)}>{t('creatorFormsUi.editor.continue')}</button> : null}
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
  const { t } = useTranslation()
  if (step === 0) return <fieldset className="choice-grid">
    <legend>{t('creatorFormsUi.editor.eventPrompt')}</legend>
    {eventTypes.map(option => <label key={option.value} className="choice-card">
      <input type="radio" name="event-type" value={option.value} checked={content.eventType === option.value} onChange={() => update('eventType', option.value)} />
      <span>{t(`creatorFormsUi.editor.eventType.${({ dugun: 'dugun', nisan: 'nisan', kina: 'kina', sunnet: 'sunnet', 'dogum-gunu': 'dogum', 'baby-shower': 'baby', mezuniyet: 'mezuniyet', 'acilis-genel': 'general' } as Record<string, string>)[option.value] ?? 'general'}`)}</span>
    </label>)}
  </fieldset>

  if (step === 1) return <div className="wizard-fields">
    <EditorField id="draft-headline" label={t('creatorFormsUi.editor.headline')} value={content.headline} onChange={value => update('headline', value)} maxLength={200} help={t('creatorFormsUi.editor.headlineHelp')} />
    <EditorField id="draft-hosts" label={t('creatorFormsUi.editor.names')} value={content.hostNamesText} onChange={value => update('hostNamesText', value)} maxLength={500} help={t('creatorFormsUi.editor.namesHelp')} />
    <EditorTextArea id="draft-message" label={t('creatorFormsUi.editor.message')} value={content.message} onChange={value => update('message', value)} maxLength={4000} />
  </div>

  if (step === 2) return <fieldset className="template-choice-grid">
    <legend>{t('creatorFormsUi.editor.chooseTemplate')}</legend>
    <p className="fieldset-help">{t('creatorFormsUi.editor.premiumDraft')}</p>
    {templateLocked ? <div className="fieldset-help publication-notice">
      <p>{templateActive ? t('creatorFormsUi.editor.activeTemplateLocked') : t('creatorFormsUi.editor.verifyPublication')}</p>
      <button type="button" className="button button--secondary" onClick={onManagePublication}>{t('creatorFormsUi.editor.managePublication')}</button>
    </div> : null}
    {templates.length === 0 ? <p className="inline-alert" role="status">{t('creatorFormsUi.editor.noTemplates')}</p> : null}
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
        <small>{template.category} · {template.isPremium ? t('creatorFormsUi.editor.premium') : t('creatorFormsUi.editor.free')}</small>
      </span>
    </label>)}
  </fieldset>

  if (step === 3) return <div className="wizard-fields">
    <EditorDateTime id="draft-starts-at" label={t('creatorFormsUi.editor.eventDate')} value={content.startsAtLocal} onChange={value => update('startsAtLocal', value)} />
    <EditorField id="draft-venue" label={t('creatorFormsUi.editor.venue')} value={content.venueName} onChange={value => update('venueName', value)} maxLength={200} />
    <EditorTextArea id="draft-address" label={t('creatorFormsUi.editor.address')} value={content.venueAddress} onChange={value => update('venueAddress', value)} maxLength={500} />
    <EditorField id="draft-map-url" label={t('creatorFormsUi.editor.map')} value={content.mapUrl} onChange={value => update('mapUrl', value)} maxLength={2048} inputMode="url" help={t('creatorFormsUi.editor.optionalUrl')} />
  </div>

  if (step === 4) return <div className="wizard-fields">
    <p>{t('creatorFormsUi.editor.optionalIntro')}</p>
    <EditorField id="draft-program-title" label={t('creatorFormsUi.editor.programTitle')} value={content.programTitle} onChange={value => update('programTitle', value)} maxLength={200} help={t('creatorFormsUi.editor.programTitleHelp')} />
    <EditorDateTime id="draft-program-start" label={t('creatorFormsUi.editor.programTime')} value={content.programStartsAtLocal} onChange={value => update('programStartsAtLocal', value)} />
    <EditorTextArea id="draft-program-description" label={t('creatorFormsUi.editor.programDescription')} value={content.programDescription} onChange={value => update('programDescription', value)} maxLength={1000} />
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
  const { t } = useTranslation()
  return <div className="form-field">
    <label htmlFor={id}>{label}</label>
    <input id={id} name={id} type="datetime-local" value={value} onChange={(event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)} />
    <p className="form-field__help">{t('creatorFormsUi.editor.localTimeHelp')}</p>
  </div>
}

function draftErrorMessage(error: unknown, t: (key: string) => string): string {
  if (error instanceof ApiRequestError && error.status === 404) return t('creatorFormsUi.editor.errorNotFound')
  if (error instanceof ApiRequestError && error.status === 400) return t('creatorFormsUi.editor.errorInvalid')
  return t('creatorFormsUi.editor.errorGeneric')
}
