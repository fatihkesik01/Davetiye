import { useEffect, useRef, useState, type FormEvent } from 'react'

import {
  ApiRequestError, DavetiyeApiClient,
  type InvitationPublicationAction, type InvitationPublicationStatus, type PublicationActionRequest,
} from '../../api/generated/client'
import { invitationFieldLabel } from './invitationFields'
import { formatPublicationTime, publicationErrorMessage, publicationLocalTime, publicationStateLabels } from './publicationPresentation'
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

const actionLabels: Record<InvitationPublicationAction, string> = {
  publish: 'Yayınla', update: 'Güncelle', pause: 'Yayını durdur', resume: 'Yayına devam et',
  cancelSchedule: 'Planlamayı iptal et', reschedule: 'Yayın tarihini değiştir', publishNow: 'Hemen yayınla', reactivate: 'Yeniden yayınla',
  republish: 'Mevcut süreyle yeniden yayınla',
}

export function PublicationPanel({ api, status, loading, failed, hasUnsavedChanges, busy, onBusy, onRefresh, onChanged, onEditField }: PublicationPanelProps) {
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
    <h3 id="publication-heading">Yayın yönetimi</h3>
    {loading ? <p role="status">Güncel yayın durumu yükleniyor…</p> : <p role="alert">Yayın durumu alınamadı.</p>}
    {!loading ? <button type="button" className="button button--secondary" onClick={onRefresh}>Yayın durumunu tekrar yükle</button> : null}
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
      setErrorMessage(status.preflight.templateAvailable ? 'Yayın için gerekli alanları tamamlayın.' : 'Yayın için kullanılabilir bir şablon seçin.')
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
      setSuccessMessage(`${actionLabels[request.action]} işlemi tamamlandı. Güncel durum: ${publicationStateLabels[result.effectiveState]}.`)
    } catch (error) {
      if (error instanceof ApiRequestError && (error.problem?.missingRecommendedFields?.length ?? 0) > 0 && !request.proceedWithRecommendedWarnings) {
        setServerWarnings(error.problem!.missingRecommendedFields!)
        setConfirmation({ ...request })
        return
      }
      setConfirmation(null)
      setRequiredErrors(error instanceof ApiRequestError ? error.problem?.missingRequiredFields ?? [] : [])
      setErrorMessage(publicationErrorMessage(error))
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
    <h3 id="publication-heading">Yayın yönetimi</h3>
    <p className="publication-state">Durum: <strong>{publicationStateLabels[status.effectiveState]}</strong></p>
    {status.currentWindow ? <dl className="publication-window">
      <div><dt>Yayın başlangıcı</dt><dd>{formatPublicationTime(status.currentWindow.startsAtUtc, status.currentWindow.timeZoneId)}</dd></div>
      <div><dt>Yayın bitişi</dt><dd>{formatPublicationTime(status.currentWindow.endsAtUtc, status.currentWindow.timeZoneId)}</dd></div>
      <div><dt>Saat dilimi</dt><dd>{status.currentWindow.timeZoneId}</dd></div>
    </dl> : null}
    {loading ? <p role="status">Yayın durumu yenileniyor…</p> : null}
    {failed ? <div className="inline-alert" role="alert"><p>Güncel yayın durumu doğrulanamadı. İşlem yapmadan önce yenileyin.</p><button type="button" className="button button--secondary" onClick={onRefresh}>Yayın durumunu yenile</button></div> : null}
    {hasUnsavedChanges ? <p className="publication-notice" role="status">İşlem yapmadan önce değişikliklerinizin kaydedilmesini bekleyin. Kaydetme başarısızsa üstteki tekrar dene seçeneğini kullanın.</p> : null}
    {status.hasPendingChanges ? <p className="publication-notice">Kaydedilmiş değişiklikler henüz yayında değil. Yayındaki içerik yalnızca açıkça güncellediğinizde değişir.</p> : null}
    {status.effectiveState === 'Scheduled' ? <p>Planlama sırasında onayladığınız içerik, başlangıçta yayına açılır. Sonraki düzenlemeleri yayımlamak için Güncelle seçeneğini kullanın.</p> : null}
    {status.effectiveState === 'Expired' ? <p>Uygun yeni bir yayın hakkıyla hemen veya ileri tarihte yeniden yayınlayabilirsiniz. Aynı davetiye ve public bağlantınız korunur.</p> : null}
    {status.currentEntitlements ? <p>Güncel yayın hakkı: en fazla {status.currentEntitlements.maxPublishDays} gün, aynı anda {status.currentEntitlements.maxActiveInvitations} davetiye. Mevcut yayının bitişi paket değişikliğiyle kısaltılmaz.</p> : null}
    {allowed('republish') ? <p>Geri yüklenen davetiye taslaktır. Eski yayın aralığıyla yeniden açmak için aşağıda açıkça onaylayın. Önceki bitiş korunur; süre uzamaz ve yayın kotası yeniden kontrol edilir.</p> : null}

    {windowFormAction && allowed(windowFormAction) && !validConfirmation ? <form className="publication-form wizard-fields" onSubmit={submit}>
      <fieldset disabled={disabled} className="publication-fields">
        <legend>{windowFormAction === 'reactivate' ? 'Yeni yayın aralığı' : windowFormAction === 'reschedule' ? 'Planlanan yayın aralığı' : 'Yayın zamanı'}</legend>
        {windowFormAction !== 'reschedule' ? <>
          {choices.length === 0 ? <p className="inline-alert" role="status">Bu davetiye için uygun yeni yayın hakkı bulunmuyor.</p> : <div className="form-field">
            <label htmlFor="publication-grant">Yayın hakkı</label>
            <select id="publication-grant" value={selectedChoice?.grantId ?? ''} onChange={event => setGrantId(event.target.value)}>
              {choices.map(choice => <option key={choice.grantId ?? 'free'} value={choice.grantId ?? ''}>{choice.label}</option>)}
            </select>
            <p className="form-field__help">En fazla {selectedChoice?.maxPublishDays} gün · Aynı anda {selectedChoice?.maxActiveInvitations} davetiye{selectedChoice?.premiumTemplatesEnabled ? ' · Premium şablonlar dahil' : ''}</p>
          </div>}
          <fieldset className="publication-mode"><legend>Ne zaman yayınlansın?</legend>
            <label><input type="radio" name="publication-mode" checked={mode === 'Immediate'} onChange={() => setMode('Immediate')} /> Hemen</label>
            <label><input type="radio" name="publication-mode" checked={mode === 'Scheduled'} onChange={() => setMode('Scheduled')} /> İleri bir tarihte</label>
          </fieldset>
        </> : <p>Mevcut yayın hakkınızla yeniden planlanır. Tarihleri kendiniz seçin; başlangıç veya süre otomatik değiştirilmez.</p>}
        <div className="form-field">
          <label htmlFor="publication-zone">Yayın saat dilimi (IANA)</label>
          <input id="publication-zone" value={zone} required maxLength={100} onChange={event => setZone(event.target.value)} aria-describedby="publication-zone-help" />
          <p id="publication-zone-help" className="form-field__help">Örnek: Europe/Istanbul. Aşağıdaki yayın tarihleri bu dilime göre yorumlanır; etkinlik tarihini değiştirmez. Saat değişiminde geçersiz veya iki anlama gelen saatler kabul edilmez.</p>
        </div>
        {mode === 'Scheduled' || windowFormAction === 'reschedule' ? <div className="form-field">
          <label htmlFor="publication-start">Yayın başlangıcı</label>
          <input id="publication-start" type="datetime-local" value={starts} required onChange={event => setStarts(event.target.value)} />
        </div> : null}
        <div className="form-field">
          <label htmlFor="publication-end">Yayın bitişi</label>
          <input id="publication-end" type="datetime-local" value={ends} required onChange={event => setEnds(event.target.value)} aria-describedby="publication-end-help" />
          <p id="publication-end-help" className="form-field__help">Bitiş, başlangıçtan sonra ve seçilen yayın hakkının izin verdiği süre içinde olmalıdır.</p>
        </div>
      </fieldset>
      <div className="button-row">
        <button className="button button--primary" type="submit" disabled={disabled || (windowFormAction !== 'reschedule' && choices.length === 0)}>Yayın bilgilerini gözden geçir</button>
        {rescheduling ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => setRescheduling(false)}>Vazgeç</button> : null}
      </div>
    </form> : null}

    {!validConfirmation && !rescheduling ? <div className="button-row publication-actions">
      {allowed('update') ? <button type="button" className="button button--primary" disabled={disabled || !status.hasPendingChanges} onClick={() => prepare('update')}>Güncelle</button> : null}
      {allowed('pause') ? <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('pause')}>Yayını durdur</button> : null}
      {allowed('resume') ? <>
        <button type="button" className="button button--primary" disabled={disabled} onClick={() => prepare('resume', true)}>Değişiklikleri yayımla ve devam et</button>
        <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('resume')}>Mevcut yayınla devam et</button>
      </> : null}
      {allowed('republish') ? <>
        <button type="button" className="button button--primary" disabled={disabled} onClick={() => prepare('republish')}>Önceki yayınla yeniden aç</button>
        <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('republish', true)}>Son bilgileri yayımla ve yeniden aç</button>
      </> : null}
      {allowed('reschedule') ? <button type="button" className="button button--secondary" disabled={disabled} onClick={startRescheduling}>Yayın tarihini değiştir</button> : null}
      {allowed('publishNow') ? <button type="button" className="button button--primary" disabled={disabled} onClick={() => prepare('publishNow')}>Hemen yayınla</button> : null}
      {allowed('cancelSchedule') ? <button type="button" className="button button--secondary" disabled={disabled} onClick={() => prepare('cancelSchedule')}>Planlamayı iptal et</button> : null}
    </div> : null}

    {validConfirmation ? <section className="publication-confirmation" aria-labelledby="publication-confirm-heading">
      <h4 id="publication-confirm-heading" tabIndex={-1} ref={confirmationHeading}>İşlemi onaylayın</h4>
      <p>{confirmation.action === 'pause' ? 'Davetiye geçici olarak yayından kaldırılır. Yayın süresi durmaz ve yukarıdaki bitiş tarihi uzamaz.'
        : confirmation.action === 'cancelSchedule' ? 'Planlanan yayın iptal edilir ve davetiye taslağa döner. Başlamamış ücretsiz yayın hakkı tüketilmez; yeni bir hak da oluşturulmaz.'
        : confirmation.action === 'publishNow' ? 'Planlamada onayladığınız içerik şimdi yayına açılır. Sonraki değişiklikler otomatik yayımlanmaz. Onaylanmış bitiş tarihi korunur; yayın süresi uzamaz.'
        : confirmation.action === 'reschedule' ? 'Planlamada onayladığınız içerik korunur; yalnızca yayın aralığı değişir. Sonraki kaydedilmiş değişiklikler için ayrıca Güncelle seçeneğini kullanın.'
        : confirmation.action === 'resume' && !confirmation.publishWorkingContent ? 'Mevcut yayımlanmış içerikle devam edilir. Kaydettiğiniz sonraki değişiklikler yayına yansımaz. Bitiş tarihi korunur.'
        : confirmation.action === 'resume' ? 'Kaydettiğiniz içerik ve seçtiğiniz şablon yayımlanır, davetiye tekrar açılır. Bitiş tarihi korunur.'
        : confirmation.action === 'update' ? 'Kaydettiğiniz son bilgiler yayımlanmış içeriğin yerine geçer. Yayın başlangıcı ve bitişi değişmez.'
        : confirmation.action === 'republish' && !confirmation.publishWorkingContent ? 'Önceki yayımlanmış içerik ve aynı yayın aralığı yeniden kullanılır. Sonradan kaydettiğiniz bilgiler yayımlanmaz. Public bağlantınız ve yukarıdaki bitiş tarihi korunur; süre uzamaz. Yayın kotası yeniden kontrol edilir.'
        : confirmation.action === 'republish' ? 'Kaydettiğiniz son bilgiler ve seçtiğiniz şablon, aynı yayın aralığında yeniden yayımlanır. Public bağlantınız ve yukarıdaki bitiş tarihi korunur; süre uzamaz. Yayın kotası ve şablon uygunluğu yeniden kontrol edilir.'
        : 'Kaydettiğiniz bilgiler, seçtiğiniz yayın aralığıyla yayımlanır. Yayın hakkı ve uygunluk işlem sırasında yeniden kontrol edilir.'}</p>
      {confirmation.publication ? <dl className="publication-window">
        <div><dt>Başlangıç</dt><dd>{confirmation.publication.mode === 'Immediate' ? 'Hemen' : confirmation.publication.startsAtLocal?.replace('T', ' ')}</dd></div>
        <div><dt>Bitiş</dt><dd>{confirmation.publication.endsAtLocal.replace('T', ' ')}</dd></div>
        <div><dt>Saat dilimi</dt><dd>{confirmation.publication.timeZoneId}</dd></div>
      </dl> : null}
      {(requiresWorking(confirmation) || serverWarnings.length > 0) && confirmationWarnings.length > 0 ? <div className="validation-group validation-group--recommended">
        <h5>Önerilen alanlar eksik</h5><p>Bu alanları tamamlayabilir veya uyarıları kabul ederek devam edebilirsiniz.</p>
        <ul>{confirmationWarnings.map(field => <li key={field}><button className="text-button" type="button" onClick={() => onEditField(field)}>{invitationFieldLabel(field)}</button></li>)}</ul>
      </div> : null}
      <div className="button-row">
        <button type="button" className="button button--primary" disabled={disabled} onClick={() => void execute()}>
          {busy ? 'İşlem yapılıyor…' : (requiresWorking(confirmation) || serverWarnings.length > 0) && confirmationWarnings.length > 0 ? 'Uyarıları kabul et ve devam et' : actionLabels[confirmation.action]}
        </button>
        <button type="button" className="button button--secondary" disabled={busy} onClick={() => setConfirmation(null)}>Vazgeç</button>
      </div>
    </section> : null}
    {confirmation && !validConfirmation ? <p role="status">Bilgiler değişti. İşlemi güncel kayıtla yeniden gözden geçirmeniz gerekir.</p> : null}
    {errorMessage ? <section className="inline-alert" aria-labelledby="publication-error-heading" role="alert">
      <h4 id="publication-error-heading" ref={errorHeading} tabIndex={-1}>İşlem tamamlanamadı</h4><p>{errorMessage}</p>
      {requiredErrors.length > 0 ? <ul>{requiredErrors.map(field => <li key={field}><button className="text-button" type="button" onClick={() => onEditField(field)}>{invitationFieldLabel(field)}</button></li>)}</ul> : null}
      <button type="button" className="button button--secondary" onClick={onRefresh}>Güncel durumu yenile</button>
    </section> : null}
    {successMessage ? <p className="publication-success" role="status">{successMessage}</p> : null}
    <InvitationCheckoutPanel invitationId={status.invitationId} api={api} disabled={busy || loading || failed} />
  </section>
}
