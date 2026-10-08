import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'

import {
  ApiRequestError, DavetiyeApiClient,
  type InvitationPublicationAction, type InvitationPublicationStatus, type PublicationActionRequest,
} from '../../api/generated/client'
import { formatPublicationTime, publicationLocalTime } from './publicationPresentation'
import { InvitationCheckoutPanel } from '../payments/InvitationCheckoutPanel'

interface PublicationPanelProps {
  api: DavetiyeApiClient
  status: InvitationPublicationStatus | null
  loading: boolean
  failed: boolean
  hasUnsavedChanges: boolean
  busy: boolean
  onBusy: (value: boolean) => void
  onRefresh: () => void
  onChanged: (status: InvitationPublicationStatus) => Promise<void>
  onEditField: (field: string) => void
}

export function PublicationPanel({ api, status, loading, failed, hasUnsavedChanges, busy, onBusy, onRefresh, onChanged, onEditField }: PublicationPanelProps) {
  const { t, i18n } = useTranslation()
  const [rescheduling, setRescheduling] = useState(false)
  const [mode, setMode] = useState<'Immediate' | 'Scheduled'>('Immediate')
  const [zone, setZone] = useState(status?.timeZoneId ?? 'Europe/Istanbul')
  const [starts, setStarts] = useState('')
  const [ends, setEnds] = useState('')
  const [grantId, setGrantId] = useState('')
  const [confirmation, setConfirmation] = useState<PublicationActionRequest | null>(null)
  const [serverWarnings, setServerWarnings] = useState<string[]>([])
  const [requiredErrors, setRequiredErrors] = useState<string[]>([])
  const [errorMessage, setErrorMessage] = useState('')
  const [successMessage, setSuccessMessage] = useState('')
  const confirmationHeading = useRef<HTMLHeadingElement>(null)
  const errorHeading = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    if (confirmation) confirmationHeading.current?.focus()
  }, [confirmation])
  useEffect(() => {
    if (errorMessage) errorHeading.current?.focus()
  }, [errorMessage])

  if (!status) return <section className="publication-panel" aria-labelledby="publication-heading">
    <h3 id="publication-heading">{t('creatorFormsUi.publication.title')}</h3>
    {loading ? <p role="status">{t('creatorFormsUi.publication.loading')}</p> : <p role="alert">{t('creatorFormsUi.publication.loadFailed')}</p>}
    {!loading ? <button type="button" className="button button--secondary" onClick={onRefresh}>{t('creatorFormsUi.publication.reload')}</button> : null}
  </section>

  const expectedKey = JSON.stringify(status.expected)
  const validConfirmation = confirmation && JSON.stringify(confirmation.expected) === expectedKey && !hasUnsavedChanges && !loading && !failed
  const disabled = busy || loading || failed || hasUnsavedChanges
  const allowed = (action: InvitationPublicationAction) => status.allowedActions.includes(action)
  const windowFormAction = allowed('publish') ? 'publish' : allowed('reactivate') ? 'reactivate' : rescheduling ? 'reschedule' : null
  const choices = status.grantChoices
  const selectedChoice = choices.find(choice => (choice.grantId ?? '') === grantId) ?? choices[0]
  const requiresWorking = (request: PublicationActionRequest) => ['publish', 'update', 'reactivate'].includes(request.action) || (['resume', 'republish'].includes(request.action) && request.publishWorkingContent)
  const confirmationWarnings = serverWarnings.length > 0 ? serverWarnings : status.preflight.missingRecommendedFields
  const confirmationDescription = (request: PublicationActionRequest) => {
    const key = request.action === 'pause' ? 'pauseConfirm'
      : request.action === 'cancelSchedule' ? 'cancelConfirm'
        : request.action === 'publishNow' ? 'publishNowConfirm'
          : request.action === 'reschedule' ? 'rescheduleConfirm'
            : request.action === 'resume' && !request.publishWorkingContent ? 'resumeExistingConfirm'
              : request.action === 'resume' ? 'resumeLatestConfirm'
                : request.action === 'update' ? 'updateConfirm'
                  : request.action === 'republish' && !request.publishWorkingContent ? 'republishExistingConfirm'
                    : request.action === 'republish' ? 'republishLatestConfirm' : 'genericConfirm'
    return t(`creatorFormsUi.publication.${key}`)
  }

  function prepare(action: InvitationPublicationAction, publishWorkingContent = false) {
    if (disabled || !status) return
    setErrorMessage('')
    setSuccessMessage('')
    setServerWarnings([])
    setRequiredErrors([])
    const request: PublicationActionRequest = { action, expected: { ...status.expected }, publishWorkingContent, proceedWithRecommendedWarnings: false }
    if (['publish', 'reactivate', 'reschedule'].includes(action)) {
      request.publication = {
        mode: action === 'reschedule' ? 'Scheduled' : mode,
        startsAtLocal: action === 'reschedule' || mode === 'Scheduled' ? starts : null,
        endsAtLocal: ends,
        timeZoneId: zone.trim(),
        requestedGrantId: action === 'reschedule' ? status.currentWindow?.grantId ?? null : selectedChoice?.grantId ?? null,
      }
    }
    if (requiresWorking(request) && (!status.preflight.templateAvailable || status.preflight.missingRequiredFields.length > 0)) {
      setRequiredErrors(status.preflight.missingRequiredFields)
      setErrorMessage(status.preflight.templateAvailable ? t('creatorFormsUi.publication.requiredFields') : t('creatorFormsUi.publication.requiredTemplate'))
      return
    }
    setConfirmation(request)
  }

  async function execute() {
    if (!validConfirmation || !confirmation || disabled) return
    const request = { ...confirmation, proceedWithRecommendedWarnings: (requiresWorking(confirmation) && confirmationWarnings.length > 0) || serverWarnings.length > 0 }
    onBusy(true)
    setErrorMessage('')
    try {
      const token = await api.getAntiforgeryToken()
      const result = await api.executePublicationAction(status!.invitationId, request, token)
      setConfirmation(null)
      setRescheduling(false)
      setServerWarnings([])
      await onChanged(result)
      setSuccessMessage(t('creatorFormsUi.publication.success', { action: t(`creatorFormsUi.publication.action.${request.action}`), status: t(`creatorFormsUi.publication.state.${result.effectiveState}`) }))
    } catch (error) {
      if (error instanceof ApiRequestError && (error.problem?.missingRecommendedFields?.length ?? 0) > 0 && !request.proceedWithRecommendedWarnings) {
        setServerWarnings(error.problem!.missingRecommendedFields!)
        setConfirmation({ ...request })
        return
      }
      setConfirmation(null)
      setRequiredErrors(error instanceof ApiRequestError ? error.problem?.missingRequiredFields ?? [] : [])
      setErrorMessage(publicationErrorMessageForLocale(error, t))
      if (error instanceof ApiRequestError && error.status === 409) onRefresh()
    } finally {
      onBusy(false)
    }
  }

  function startRescheduling() {
    setMode('Scheduled')
    setZone(status!.currentWindow?.timeZoneId ?? status!.timeZoneId)
    setStarts(status!.currentWindow ? publicationLocalTime(status!.currentWindow.startsAtUtc, status!.currentWindow.timeZoneId) : '')
    setEnds(status!.currentWindow ? publicationLocalTime(status!.currentWindow.endsAtUtc, status!.currentWindow.timeZoneId) : '')
    setRescheduling(true)
    setConfirmation(null)
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (windowFormAction) prepare(windowFormAction)
  }

  return <section className="publication-panel" aria-labelledby="publication-heading" aria-busy={busy || loading}>
    <h3 id="publication-heading">{t('creatorFormsUi.publication.title')}</h3>
    <p className="publication-state">{t('creatorFormsUi.publication.status')}: <strong>{t(`creatorFormsUi.publication.state.${status.effectiveState}`)}</strong></p>
    {status.currentWindow ? <dl className="publication-window">
      <div><dt>{t('creatorFormsUi.publication.start')}</dt><dd>{formatPublicationTime(status.currentWindow.startsAtUtc, status.currentWindow.timeZoneId, i18n.language)}</dd></div>
      <div><dt>{t('creatorFormsUi.publication.end')}</dt><dd>{formatPublicationTime(status.currentWindow.endsAtUtc, status.currentWindow.timeZoneId, i18n.language)}</dd></div>
      <div><dt>{t('creatorFormsUi.publication.timeZone')}</dt><dd>{status.currentWindow.timeZoneId}</dd></div>
    </dl> : null}
    {loading ? <p role="status">{t('creatorFormsUi.publication.refreshing')}</p> : null}
    {failed ? <div className="inline-alert" role="alert"><p>{t('creatorFormsUi.publication.stale')}</p><button type="button" className="button button--secondary" onClick={onRefresh}>{t('creatorFormsUi.publication.refresh')}</button></div> : null}
    {hasUnsavedChanges ? <p className="publication-notice" role="status">{t('creatorFormsUi.publication.waitSave')}</p> : null}
    {status.hasPendingChanges ? <p className="publication-notice">{t('creatorFormsUi.publication.pendingChanges')}</p> : null}
    {status.effectiveState === 'Scheduled' ? <p>{t('creatorFormsUi.publication.scheduledInfo')}</p> : null}
    {status.effectiveState === 'Expired' ? <p>{t('creatorFormsUi.publication.expiredInfo')}</p> : null}
    {status.currentEntitlements ? <p>{t('creatorFormsUi.publication.entitlements', { days: status.currentEntitlements.maxPublishDays, count: status.currentEntitlements.maxActiveInvitations })}</p> : null}
    {allowed('republish') ? <p>{t('creatorFormsUi.publication.restoredDraft')}</p> : null}

    {windowFormAction && allowed(windowFormAction) && !validConfirmation ? <form className="publication-form wizard-fields" onSubmit={submit}>
      <fieldset disabled={disabled} className="publication-fields">
        <legend>{t(windowFormAction === 'reactivate' ? 'creatorFormsUi.publication.newWindow' : windowFormAction === 'reschedule' ? 'creatorFormsUi.publication.scheduledWindow' : 'creatorFormsUi.publication.publicationTime')}</legend>
        {windowFormAction !== 'reschedule' ? <>
          {choices.length === 0 ? <p className="inline-alert" role="status">{t('creatorFormsUi.publication.noGrant')}</p> : <div className="form-field">
            <label htmlFor="publication-grant">{t('creatorFormsUi.publication.grant')}</label>
            <select id="publication-grant" value={selectedChoice?.grantId ?? ''} onChange={event => setGrantId(event.target.value)}>
              {choices.map(choice => <option key={choice.grantId ?? 'free'} value={choice.grantId ?? ''}>{choice.label}</option>)}
            </select>
            <p className="form-field__help">{t('creatorFormsUi.publication.maxDays', { days: selectedChoice?.maxPublishDays })} · {t('creatorFormsUi.publication.concurrent', { count: selectedChoice?.maxActiveInvitations })}{selectedChoice?.premiumTemplatesEnabled ? ` · ${t('creatorFormsUi.publication.premiumIncluded')}` : ''}</p>
          </div>}
          <fieldset className="publication-mode"><legend>{t('creatorFormsUi.publication.mode')}</legend>
            <label><input type="radio" name="publication-mode" checked={mode === 'Immediate'} onChange={() => setMode('Immediate')} /> {t('creatorFormsUi.publication.immediate')}</label>
            <label><input type="radio" name="publication-mode" checked={mode === 'Scheduled'} onChange={() => setMode('Scheduled')} /> {t('creatorFormsUi.publication.later')}</label>
          </fieldset>
        </> : <p>{t('creatorFormsUi.publication.rescheduleInfo')}</p>}
        <div className="form-field">
          <label htmlFor="publication-zone">{t('creatorFormsUi.publication.zoneIana')}</label>
          <input id="publication-zone" value={zone} required maxLength={100} onChange={event => setZone(event.target.value)} aria-describedby="publication-zone-help" />
          <p id="publication-zone-help" className="form-field__help">{t('creatorFormsUi.publication.zoneHelp')}</p>
        </div>
        {mode === 'Scheduled' || windowFormAction === 'reschedule' ? <div className="form-field">
          <label htmlFor="publication-start">{t('creatorFormsUi.publication.start')}</label>
          <input id="publication-start" type="datetime-local" value={starts} required onChange={event => setStarts(event.target.value)} />
        </div> : null}
        <div className="form-field">
          <label htmlFor="publication-end">{t('creatorFormsUi.publication.end')}</label>
          <input id="publication-end" type="datetime-local" value={ends} required onChange={event => setEnds(event.target.value)} aria-describedby="publication-end-help" />
          <p id="publication-end-help" className="form-field__help">{t('creatorFormsUi.publication.endHelp')}</p>
        </div>
      </fieldset>
      <div className="button-row">
        <button className="button button--primary" type="submit" disabled={disabled || (windowFormAction !== 'reschedule' && choices.length === 0)}>{t('creatorFormsUi.publication.review')}</button>
        {rescheduling ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => setRescheduling(false)}>{t('creatorFormsUi.publication.cancel')}</button> : null}
      </div>
    </form> : null}

    {!validConfirmation && !rescheduling ? <div className="button-row publication-actions">
      {allowed('update') ? <button type="button" className="button button--primary" disabled={disabled || !status.hasPendingChanges} onClick={() => prepare('update')}>{t('creatorFormsUi.publication.action.update')}</button> : null}
      {allowed('pause') ? <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('pause')}>{t('creatorFormsUi.publication.action.pause')}</button> : null}
      {allowed('resume') ? <>
        <button type="button" className="button button--primary" disabled={disabled} onClick={() => prepare('resume', true)}>{t('creatorFormsUi.publication.resumePublish')}</button>
        <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('resume')}>{t('creatorFormsUi.publication.resumeExisting')}</button>
      </> : null}
      {allowed('republish') ? <>
        <button type="button" className="button button--primary" disabled={disabled} onClick={() => prepare('republish')}>{t('creatorFormsUi.publication.reopenPrevious')}</button>
        <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('republish', true)}>{t('creatorFormsUi.publication.reopenLatest')}</button>
      </> : null}
      {allowed('reschedule') ? <button type="button" className="button button--secondary" disabled={disabled} onClick={startRescheduling}>{t('creatorFormsUi.publication.changeDate')}</button> : null}
      {allowed('publishNow') ? <button type="button" className="button button--primary" disabled={disabled} onClick={() => prepare('publishNow')}>{t('creatorFormsUi.publication.publishNow')}</button> : null}
      {allowed('cancelSchedule') ? <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('cancelSchedule')}>{t('creatorFormsUi.publication.cancelSchedule')}</button> : null}
    </div> : null}

    {validConfirmation ? <section className="publication-confirmation" aria-labelledby="publication-confirm-heading">
      <h4 id="publication-confirm-heading" tabIndex={-1} ref={confirmationHeading}>{t('creatorFormsUi.publication.confirm')}</h4>
      <p>{confirmationDescription(confirmation)}</p>
      {confirmation.publication ? <dl className="publication-window">
        <div><dt>Başlangıç</dt><dd>{confirmation.publication.mode === 'Immediate' ? t('creatorFormsUi.publication.immediate') : confirmation.publication.startsAtLocal?.replace('T', ' ')}</dd></div>
        <div><dt>Bitiş</dt><dd>{confirmation.publication.endsAtLocal.replace('T', ' ')}</dd></div>
        <div><dt>{t('creatorFormsUi.publication.timeZone')}</dt><dd>{confirmation.publication.timeZoneId}</dd></div>
      </dl> : null}
      {(requiresWorking(confirmation) || serverWarnings.length > 0) && confirmationWarnings.length > 0 ? <div className="validation-group validation-group--recommended">
        <h5>Önerilen alanlar eksik</h5><p>Bu alanları tamamlayabilir veya uyarıları kabul ederek devam edebilirsiniz.</p>
        <ul>{confirmationWarnings.map(field => <li key={field}><button className="text-button" type="button" onClick={() => onEditField(field)}>{publicationFieldLabel(field, t)}</button></li>)}</ul>
      </div> : null}
      <div className="button-row">
        <button type="button" className="button button--primary" disabled={disabled} onClick={() => void execute()}>
          {busy ? t('creatorFormsUi.publication.processing') : (requiresWorking(confirmation) || serverWarnings.length > 0) && confirmationWarnings.length > 0 ? t('creatorFormsUi.publication.acceptWarnings') : t(`creatorFormsUi.publication.action.${confirmation.action}`)}
        </button>
        <button type="button" className="button button--secondary" disabled={busy} onClick={() => setConfirmation(null)}>{t('creatorFormsUi.publication.cancel')}</button>
      </div>
    </section> : null}
    {confirmation && !validConfirmation ? <p role="status">{t('creatorFormsUi.publication.changed')}</p> : null}
    {errorMessage ? <section className="inline-alert" aria-labelledby="publication-error-heading" role="alert">
      <h4 id="publication-error-heading" ref={errorHeading} tabIndex={-1}>{t('creatorFormsUi.publication.errorTitle')}</h4><p>{errorMessage}</p>
      {requiredErrors.length > 0 ? <ul>{requiredErrors.map(field => <li key={field}><button className="text-button" type="button" onClick={() => onEditField(field)}>{publicationFieldLabel(field, t)}</button></li>)}</ul> : null}
      <button type="button" className="button button--secondary" onClick={onRefresh}>{t('creatorFormsUi.publication.reloadCurrent')}</button>
    </section> : null}
    {successMessage ? <p className="publication-success" role="status">{successMessage}</p> : null}
    <InvitationCheckoutPanel invitationId={status.invitationId} api={api} disabled={busy || loading || failed} />
  </section>
}

const publicationFieldTranslationKeys: Record<string, string> = {
  eventType: 'eventType', headline: 'headline', hostNames: 'names', message: 'message', startsAt: 'startsAt',
  timeZoneId: 'timeZoneId', 'venue.name': 'venueName', 'venue.address': 'venueAddress', 'venue.mapUrl': 'mapUrl', programItems: 'program',
}

function publicationFieldLabel(field: string, t: (key: string) => string): string {
  const labelKey = publicationFieldTranslationKeys[field]
  return labelKey ? t(`creatorFormsUi.publication.fieldLabels.${labelKey}`) : t('creatorFormsUi.publication.fieldLabels.templateRequired')
}

function publicationErrorMessageForLocale(error: unknown, t: (key: string) => string): string {
  if (!(error instanceof ApiRequestError)) return t('creatorFormsUi.publication.errors.generic')
  const code = error.problem?.code ?? ''
  const localTimeErrors = [
    ...(error.problem?.errors?.startsAtLocal ?? []),
    ...(error.problem?.errors?.endsAtLocal ?? []),
  ].join(' ')
  if (code === 'InvalidRequest' && /ambiguous/i.test(localTimeErrors)) return t('creatorFormsUi.publication.errors.ambiguous')
  if (code === 'InvalidRequest' && /does not exist/i.test(localTimeErrors)) return t('creatorFormsUi.publication.errors.nonexistent')
  if (/ambiguous/i.test(code)) return t('creatorFormsUi.publication.errors.ambiguous')
  if (/nonexistent|invalid_local|invalidlocal|invalid_time|invalidtime|timezone/i.test(code)) return t('creatorFormsUi.publication.errors.invalidTime')
  if (/quota/i.test(code)) return t('creatorFormsUi.publication.errors.quota')
  if (/premium/i.test(code)) return t('creatorFormsUi.publication.errors.premium')
  if (/duration/i.test(code)) return t('creatorFormsUi.publication.errors.duration')
  if (/grant|entitlement/i.test(code)) return t('creatorFormsUi.publication.errors.entitlement')
  if (/required/i.test(code)) return t('creatorFormsUi.publication.errors.required')
  if (/template/i.test(code)) return t('creatorFormsUi.publication.errors.template')
  if (error.status === 404) return t('creatorFormsUi.publication.errors.notFound')
  if (error.status === 403) return t('creatorFormsUi.publication.errors.forbidden')
  if (error.status === 409) return t('creatorFormsUi.publication.errors.conflict')
  return t('creatorFormsUi.publication.errors.retry')
}
