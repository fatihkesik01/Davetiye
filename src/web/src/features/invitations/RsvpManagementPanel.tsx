import { useEffect, useRef, useState, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'

import {
  ApiRequestError,
  type CreatorRsvpInputLimits,
  type DavetiyeApiClient,
  type InvitationRsvpConfiguration,
  type RsvpQuestion,
  type RsvpQuestionType,
  type SaveRsvpQuestionRequest,
} from '../../api/generated/client'
import { InternalLink } from '../../components/ui/InternalLink'

const questionTypes: Array<{ value: RsvpQuestionType; key: string }> = [
  { value: 'ShortText', key: 'shortText' }, { value: 'LongText', key: 'longText' },
  { value: 'SingleChoice', key: 'singleChoice' }, { value: 'MultipleChoice', key: 'multipleChoice' },
  { value: 'YesNo', key: 'yesNo' }, { value: 'Number', key: 'number' },
]
interface Props {
  api: DavetiyeApiClient
  invitationId: string
  effectiveState: string | null
  publicationReady: boolean
  templateSupportsRsvp: boolean
  onManagePublication: () => void
}

type QuestionDraft = {
  prompt: string
  type: RsvpQuestionType
  isRequired: boolean
  participantCount: boolean
  options: Array<{ id?: string; label: string }>
}

const emptyQuestion: QuestionDraft = {
  prompt: '', type: 'ShortText', isRequired: false, participantCount: false, options: [{ label: '' }],
}

export function RsvpManagementPanel({ api, invitationId, effectiveState, publicationReady, templateSupportsRsvp, onManagePublication }: Props) {
  const { t } = useTranslation()
  const [configuration, setConfiguration] = useState<InvitationRsvpConfiguration | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [draft, setDraft] = useState<QuestionDraft>(emptyQuestion)
  const [confirmAction, setConfirmAction] = useState<(() => Promise<void>) | null>(null)
  const [announcement, setAnnouncement] = useState('')
  const questionPrompt = useRef<HTMLInputElement>(null)

  const writable = publicationReady && (effectiveState === 'Draft' || effectiveState === 'Paused') &&
    Boolean(configuration && (configuration.effectiveState === 'Draft' || configuration.effectiveState === 'Paused'))
  const canMutate = writable && !busy

  useEffect(() => {
    const controller = new AbortController()
    void api.getInvitationRsvp(invitationId, controller.signal).then(result => {
      setConfiguration(result)
      setLoading(false)
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setFailed(true)
      setLoading(false)
    })
    return () => controller.abort()
  }, [api, invitationId, loadAttempt])

  useEffect(() => {
    if (editingId !== null) questionPrompt.current?.focus()
  }, [editingId])

  const refreshAfterConflict = async () => {
    try {
      setConfiguration(await api.getInvitationRsvp(invitationId))
      setMessage(t('creatorEditorUi.rsvpManagement.changedConflict'))
    } catch {
      setMessage(t('creatorEditorUi.rsvpManagement.refreshFailed'))
    }
  }

  const mutate = async (operation: (csrf: string, current: InvitationRsvpConfiguration) => Promise<InvitationRsvpConfiguration>) => {
    if (!canMutate || !configuration) return
    setBusy(true)
    setMessage(t('creatorEditorUi.rsvpManagement.saving'))
    try {
      const csrf = await api.getAntiforgeryToken()
      const updated = await operation(csrf, configuration)
      setConfiguration(updated)
      setMessage(t('creatorEditorUi.rsvpManagement.saved'))
      return updated
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) await refreshAfterConflict()
      else if (error instanceof ApiRequestError && error.status === 400) setMessage(t('creatorEditorUi.rsvpManagement.invalid'))
      else if (error instanceof ApiRequestError && error.status === 404) setMessage(t('creatorEditorUi.rsvpManagement.notFound'))
      else setMessage(t('creatorEditorUi.rsvpManagement.saveFailed'))
      return undefined
    } finally {
      setBusy(false)
    }
  }

  const toggle = async (enabled: boolean) => {
    const result = await mutate((csrf, current) => api.setInvitationRsvp(invitationId, {
      expectedRevision: current.revision, isEnabled: enabled,
    }, csrf))
    if (result && enabled) setAnnouncement(t('creatorEditorUi.rsvpManagement.opened'))
  }

  const beginAdd = () => {
    if (!canMutate || !configuration || configuration.questions.length >= configuration.inputLimits.maxActiveQuestionsPerInvitation) return
    setEditingId('new')
    setDraft(emptyQuestion)
  }

  const beginEdit = (question: RsvpQuestion) => {
    setEditingId(question.id)
    setDraft({
      prompt: question.prompt,
      type: question.type,
      isRequired: question.isRequired,
      participantCount: question.semanticRole === 'ParticipantCount',
      options: question.options.length ? question.options.map(option => ({ id: option.id, label: option.label })) : [{ label: '' }],
    })
  }

  const saveQuestion = async () => {
    if (!configuration) return
    const options = ['SingleChoice', 'MultipleChoice'].includes(draft.type)
      ? draft.options.map(option => option.label.trim()).filter(Boolean).map((label, sortOrder) => ({
        ...(draft.options[sortOrder]?.id ? { id: draft.options[sortOrder]!.id } : {}), label, sortOrder,
      }))
      : undefined
    const request: SaveRsvpQuestionRequest = {
      expectedRevision: configuration.revision,
      prompt: draft.prompt.trim(),
      type: draft.type,
      isRequired: draft.isRequired,
      semanticRole: draft.participantCount ? 'ParticipantCount' : null,
      ...(options ? { options } : {}),
    }
    const result = editingId === 'new'
      ? await mutate((csrf) => api.addInvitationRsvpQuestion(invitationId, request, csrf))
      : await mutate((csrf) => api.updateInvitationRsvpQuestion(invitationId, editingId!, request, csrf))
    if (result) setEditingId(null)
  }

  const deleteQuestion = (question: RsvpQuestion) => {
    const execute = async () => {
      await mutate((csrf, current) => api.deleteInvitationRsvpQuestion(invitationId, question.id, {
        expectedRevision: current.revision,
      }, csrf))
      setConfirmAction(null)
    }
    if (question.semanticRole === 'ParticipantCount') setConfirmAction(() => execute)
    else void execute()
  }

  const saveEdit = () => {
    const current = configuration?.questions.find(question => question.id === editingId)
    if (current?.semanticRole === 'ParticipantCount' && hasMeaningfulParticipantCountChanges(current, draft)) {
      setConfirmAction(() => async () => {
        await saveQuestion()
        setConfirmAction(null)
      })
    } else void saveQuestion()
  }

  const reorder = async (question: RsvpQuestion, offset: -1 | 1) => {
    if (!configuration) return
    const from = configuration.questions.findIndex(item => item.id === question.id)
    const to = from + offset
    if (to < 0 || to >= configuration.questions.length) return
    const questionIds = configuration.questions.map(item => item.id)
    ;[questionIds[from], questionIds[to]] = [questionIds[to]!, questionIds[from]!]
    const result = await mutate((csrf, current) => api.reorderInvitationRsvpQuestions(invitationId, {
      expectedRevision: current.revision, questionIds,
    }, csrf))
    if (result) {
      setAnnouncement(t('creatorEditorUi.rsvpManagement.moveAnnouncement', { prompt: question.prompt, direction: offset < 0 ? t('creatorEditorUi.rsvpManagement.moveUp').toLowerCase() : t('creatorEditorUi.rsvpManagement.moveDown').toLowerCase(), position: to + 1, total: result.questions.length }))
      requestAnimationFrame(() => document.getElementById(`rsvp-question-${question.id}-move-${offset < 0 ? 'up' : 'down'}`)?.focus())
    }
  }

  const titleId = `rsvp-panel-title-${invitationId}`
  if (!templateSupportsRsvp) return <section className="rsvp-panel" aria-labelledby={titleId}>
    <h3 id={titleId}>RSVP</h3><p>{t('creatorEditorUi.rsvpManagement.unavailable')}</p>
  </section>

  return <section className="rsvp-panel" aria-labelledby={titleId}>
    <div className="rsvp-panel__heading"><div><h3 id={titleId}>RSVP</h3><p>{t('creatorEditorUi.rsvpManagement.intro')}</p></div>
      {configuration ? <InternalLink className="button button--secondary" to={`/panel/davetiyeler/${invitationId}/rsvp-yanitlari`}>{t('creatorEditorUi.rsvpManagement.results')}</InternalLink> : null}
    </div>
    {loading ? <p role="status">{t('creatorEditorUi.rsvpManagement.loading')}</p> : null}
    {failed ? <div className="inline-alert" role="alert"><p>{t('creatorEditorUi.rsvpManagement.failed')}</p><button type="button" className="button button--secondary" onClick={() => { setLoading(true); setFailed(false); setLoadAttempt(value => value + 1) }}>{t('creatorEditorUi.rsvpManagement.retry')}</button></div> : null}
    {configuration ? <>
      {!writable ? <div className="rsvp-panel__notice" role="status">
        <p>{configuration.effectiveState === 'Active' || effectiveState === 'Active'
          ? t('creatorEditorUi.rsvpManagement.activeLocked')
          : (configuration.effectiveState === 'Draft' || configuration.effectiveState === 'Paused') &&
            (effectiveState === null || effectiveState === 'Draft' || effectiveState === 'Paused')
            ? t('creatorEditorUi.rsvpManagement.checking')
            : t('creatorEditorUi.rsvpManagement.locked')}</p>
        {(configuration.effectiveState === 'Active' || effectiveState === 'Active')
          ? <button type="button" className="button button--secondary" onClick={onManagePublication}>{t('creatorEditorUi.rsvpManagement.pause')}</button>
          : null}
      </div> : null}
      <label className="rsvp-panel__toggle"><input type="checkbox" checked={configuration.enabled} disabled={!canMutate} onChange={event => void toggle(event.target.checked)} /> {t('creatorEditorUi.rsvpManagement.toggle')}</label>
      {configuration.enabled ? <>
        <p className="rsvp-panel__privacy">{t('creatorEditorUi.rsvpManagement.private')}</p>
        <p>{t('creatorEditorUi.rsvpManagement.questionCount', { count: configuration.questions.length, max: configuration.inputLimits.maxActiveQuestionsPerInvitation })}</p>
        <ol className="rsvp-question-list" aria-label={t('creatorEditorUi.rsvpManagement.questionList')}>
          {configuration.questions.map((question, index) => <li className="rsvp-question-card" key={question.id}>
            <div className="rsvp-question-card__top"><div><strong>{question.prompt}</strong><p>{questionTypes.find(item => item.value === question.type) ? t(`creatorEditorUi.rsvpManagement.types.${questionTypes.find(item => item.value === question.type)!.key}`) : question.type}{question.isRequired ? ` · ${t('creatorEditorUi.rsvpManagement.required')}` : ` · ${t('creatorEditorUi.rsvpManagement.optional')}`}{question.semanticRole === 'ParticipantCount' ? ` · ${t('creatorEditorUi.rsvpManagement.participantCount')}` : ''}</p></div>
              <div className="rsvp-question-card__actions">
                <button id={`rsvp-question-${question.id}-move-up`} className="button button--secondary" type="button" disabled={!canMutate || index === 0} aria-label={t('creatorEditorUi.rsvpManagement.moveUpLabel', { prompt: question.prompt })} onClick={() => void reorder(question, -1)}>{t('creatorEditorUi.rsvpManagement.moveUp')}</button>
                <button id={`rsvp-question-${question.id}-move-down`} className="button button--secondary" type="button" disabled={!canMutate || index === configuration.questions.length - 1} aria-label={t('creatorEditorUi.rsvpManagement.moveDownLabel', { prompt: question.prompt })} onClick={() => void reorder(question, 1)}>{t('creatorEditorUi.rsvpManagement.moveDown')}</button>
                <button className="button button--secondary" type="button" disabled={!canMutate} aria-label={t('creatorEditorUi.rsvpManagement.editLabel', { prompt: question.prompt })} onClick={() => beginEdit(question)}>{t('creatorEditorUi.rsvpManagement.edit')}</button>
                <button className="button button--secondary" type="button" disabled={!canMutate} aria-label={t('creatorEditorUi.rsvpManagement.removeLabel', { prompt: question.prompt })} onClick={() => deleteQuestion(question)}>{t('creatorEditorUi.rsvpManagement.remove')}</button>
              </div>
            </div>
            {question.options.length ? <ul>{question.options.map(option => <li key={option.id}>{option.label}</li>)}</ul> : null}
          </li>)}
        </ol>
        {canMutate ? <button type="button" className="button button--secondary" disabled={configuration.questions.length >= configuration.inputLimits.maxActiveQuestionsPerInvitation} onClick={beginAdd}>{t('creatorEditorUi.rsvpManagement.addQuestion')}</button> : null}
        {configuration.questions.length >= configuration.inputLimits.maxActiveQuestionsPerInvitation ? <p role="status">{t('creatorEditorUi.rsvpManagement.maxQuestions', { max: configuration.inputLimits.maxActiveQuestionsPerInvitation })}</p> : null}
      </> : null}
    </> : null}
    {editingId && configuration ? <QuestionEditor draft={draft} setDraft={setDraft} busy={busy} promptRef={questionPrompt} limits={configuration.inputLimits}
      onSave={saveEdit} onCancel={() => setEditingId(null)} /> : null}
    {confirmAction ? <div className="rsvp-warning" role="alertdialog" aria-labelledby="rsvp-warning-title" aria-describedby="rsvp-warning-description">
      <h4 id="rsvp-warning-title">{t('creatorEditorUi.rsvpManagement.questionChangedTitle')}</h4>
      <p id="rsvp-warning-description">{t('creatorEditorUi.rsvpManagement.questionChangedBody')}</p>
      <div className="button-row"><button className="button button--primary" type="button" onClick={() => void confirmAction()}>{t('creatorEditorUi.rsvpManagement.confirm')}</button><button className="button button--secondary" type="button" onClick={() => setConfirmAction(null)}>{t('creatorEditorUi.rsvpManagement.cancel')}</button></div>
    </div> : null}
    <p role="status" aria-live="polite">{message}</p>
    <p className="visually-hidden" role="status" aria-live="polite">{announcement}</p>
  </section>
}

interface QuestionEditorProps {
  draft: QuestionDraft
  setDraft: (draft: QuestionDraft) => void
  busy: boolean
  promptRef: RefObject<HTMLInputElement | null>
  limits: CreatorRsvpInputLimits
  onSave: () => void
  onCancel: () => void
}

function QuestionEditor({ draft, setDraft, busy, promptRef, limits, onSave, onCancel }: QuestionEditorProps) {
  const { t } = useTranslation()
  const choices = draft.type === 'SingleChoice' || draft.type === 'MultipleChoice'
  const answerLimit = draft.participantCount && draft.type === 'Number'
    ? t('creatorEditorUi.rsvpManagement.answerIntegerLimit', { min: limits.minimumParticipantCount, max: limits.maximumParticipantCount })
    : draft.type === 'ShortText'
      ? t('creatorEditorUi.rsvpManagement.shortAnswerLimit', { max: limits.maxShortTextAnswerCharacters })
      : draft.type === 'LongText'
        ? t('creatorEditorUi.rsvpManagement.longAnswerLimit', { max: limits.maxLongTextAnswerCharacters })
        : draft.type === 'MultipleChoice'
          ? t('creatorEditorUi.rsvpManagement.multipleChoiceLimit', { max: limits.maxMultipleChoiceSelections })
          : null
  return <fieldset className="rsvp-question-editor" aria-label={t('creatorEditorUi.rsvpManagement.editorLabel')}>
    <legend>{draft.prompt ? t('creatorEditorUi.rsvpManagement.editQuestion') : t('creatorEditorUi.rsvpManagement.newQuestion')}</legend>
    <label>{t('creatorEditorUi.rsvpManagement.prompt')}<input ref={promptRef} maxLength={limits.maxQuestionPromptCharacters} aria-describedby="rsvp-prompt-limit" value={draft.prompt} onChange={event => setDraft({ ...draft, prompt: event.target.value })} /></label>
    <p id="rsvp-prompt-limit">{t('creatorEditorUi.rsvpManagement.promptLimit', { max: limits.maxQuestionPromptCharacters })}</p>
    <label>{t('creatorEditorUi.rsvpManagement.type')}<select aria-describedby={answerLimit ? 'rsvp-answer-limit' : undefined} value={draft.type} onChange={event => setDraft({ ...draft, type: event.target.value as RsvpQuestionType, participantCount: event.target.value === 'Number' ? draft.participantCount : false })}>
      {questionTypes.map(type => <option key={type.value} value={type.value}>{t(`creatorEditorUi.rsvpManagement.types.${type.key}`)}</option>)}
    </select></label>
    {answerLimit ? <p id="rsvp-answer-limit">{answerLimit}</p> : null}
    {choices ? <fieldset><legend>{t('creatorEditorUi.rsvpManagement.options')}</legend><p>{t('creatorEditorUi.rsvpManagement.optionsHelp', { labelMax: limits.maxOptionLabelCharacters, optionMax: limits.maxDefinedOptionsPerChoiceQuestion })}</p>{draft.options.map((option, index) => <div className="rsvp-option-row" key={index}>
      <label htmlFor={`rsvp-option-${index}`}>{t('creatorEditorUi.rsvpManagement.option', { index: index + 1 })}<input id={`rsvp-option-${index}`} aria-describedby="rsvp-option-limit" maxLength={limits.maxOptionLabelCharacters} value={option.label} onChange={event => setDraft({ ...draft, options: draft.options.map((item, position) => position === index ? { ...item, label: event.target.value } : item) })} /></label>
      {draft.options.length > 1 ? <button type="button" className="button button--secondary" onClick={() => setDraft({ ...draft, options: draft.options.filter((_, position) => position !== index) })}>{t('creatorEditorUi.rsvpManagement.removeOption')}<span className="visually-hidden">: {index + 1}</span></button> : null}
    </div>)}<p id="rsvp-option-limit">{t('creatorEditorUi.rsvpManagement.optionLimit', { max: limits.maxOptionLabelCharacters })}</p><button type="button" className="button button--secondary" disabled={draft.options.length >= limits.maxDefinedOptionsPerChoiceQuestion} onClick={() => setDraft({ ...draft, options: [...draft.options, { label: '' }] })}>{t('creatorEditorUi.rsvpManagement.addOption')}</button>{draft.options.length >= limits.maxDefinedOptionsPerChoiceQuestion ? <p role="status">{t('creatorEditorUi.rsvpManagement.maxOptions', { max: limits.maxDefinedOptionsPerChoiceQuestion })}</p> : null}</fieldset> : null}
    <label className="rsvp-inline-check"><input type="checkbox" checked={draft.isRequired} onChange={event => setDraft({ ...draft, isRequired: event.target.checked })} /> {t('creatorEditorUi.rsvpManagement.required')}</label>
    {draft.type === 'Number' ? <label className="rsvp-inline-check"><input type="checkbox" checked={draft.participantCount} onChange={event => setDraft({ ...draft, participantCount: event.target.checked })} /> {t('creatorEditorUi.rsvpManagement.useParticipantCount')}</label> : null}
    <div className="button-row"><button type="button" className="button button--primary" disabled={busy || !draft.prompt.trim() || draft.prompt.length > limits.maxQuestionPromptCharacters || (choices && (draft.options.length > limits.maxDefinedOptionsPerChoiceQuestion || draft.options.some(option => !option.label.trim() || option.label.length > limits.maxOptionLabelCharacters)))} onClick={onSave}>{t('creatorEditorUi.rsvpManagement.saveQuestion')}</button><button type="button" className="button button--secondary" disabled={busy} onClick={onCancel}>{t('creatorEditorUi.rsvpManagement.cancel')}</button></div>
  </fieldset>
}

function hasMeaningfulParticipantCountChanges(question: RsvpQuestion, draft: QuestionDraft): boolean {
  if (draft.prompt.trim() !== question.prompt || draft.type !== question.type || draft.isRequired !== question.isRequired ||
    !draft.participantCount || draft.type !== 'Number') return true

  const retainedOptions = draft.options.filter(option => option.label.trim())
  return retainedOptions.length !== question.options.length || retainedOptions.some((option, index) => {
    const current = question.options[index]
    return !current || option.id !== current.id || option.label.trim() !== current.label || index !== current.sortOrder
  })
}
