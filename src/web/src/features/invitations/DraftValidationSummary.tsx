import type {
  DraftValidationFieldResult,
  InvitationDraftValidationReport,
} from '../../api/generated/client'
import { invitationFieldLabel } from './invitationFields'

interface DraftValidationSummaryProps {
  report: InvitationDraftValidationReport | null
  loading: boolean
  failed: boolean
  hasUnsavedChanges: boolean
  onRetry: () => void
}

export function DraftValidationSummary({
  report,
  loading,
  failed,
  hasUnsavedChanges,
  onRetry,
}: DraftValidationSummaryProps) {
  if (loading && !report) {
    return <section className="validation-summary" aria-labelledby="validation-heading" aria-busy="true">
      <h3 id="validation-heading">Taslak kontrolü</h3>
      <p role="status">Kaydedilmiş taslak kontrol ediliyor…</p>
    </section>
  }

  if (failed && !report) {
    return <section className="validation-summary validation-summary--blocked" aria-labelledby="validation-heading">
      <h3 id="validation-heading">Taslak kontrolü alınamadı</h3>
      <p role="alert">Eksik alan raporu şu anda yüklenemedi. Bu durum taslağınızı kaydetmenizi engellemez.</p>
      <button className="button button--secondary" type="button" onClick={onRetry}>Tekrar dene</button>
    </section>
  }

  if (!report) return null

  const requiredMissing = missingFields(report.requiredFields)
  const recommendedMissing = missingFields(report.recommendedFields)
  const templateProblem = !report.templateSelected || !report.templateAvailable
  const readyForFuturePreflight = !templateProblem && requiredMissing.length === 0

  return <section
    className={`validation-summary ${readyForFuturePreflight ? 'validation-summary--ready' : 'validation-summary--blocked'}`}
    aria-labelledby="validation-heading"
  >
    <h3 id="validation-heading">Taslak kontrolü</h3>
    {hasUnsavedChanges ? <p className="validation-summary__notice" role="status">
      Bu rapor son kaydedilen sürümü gösterir. Bekleyen değişiklikler kaydedildiğinde otomatik yenilenir.
    </p> : null}
    {loading ? <p className="validation-summary__notice" role="status">Rapor yenileniyor…</p> : null}
    {failed ? <p className="validation-summary__notice" role="alert">
      Rapor yenilenemedi; aşağıdaki sonuç son başarılı kontroldendir.
      {' '}<button className="text-button" type="button" onClick={onRetry}>Tekrar dene</button>
    </p> : null}

    {!report.templateSelected ? <div className="validation-group validation-group--required">
      <h4>Şablon seçimi gerekli</h4>
      <p>Yayın kontrolünün tamamlanabilmesi için önce aktif katalogdan bir şablon seçin.</p>
    </div> : null}
    {report.templateSelected && !report.templateAvailable ? <div className="validation-group validation-group--required">
      <h4>Şablon doğrulanamadı</h4>
      <p>Seçili şablon veya renderer sürümü artık aktif katalogda bulunmuyor. Bu rapor tamamlanmış sayılmaz.</p>
    </div> : null}

    <ValidationGroup
      title="Gerekli alanlar"
      emptyMessage="Kaydedilmiş sürümde zorunlu alan eksiği görünmüyor."
      fields={requiredMissing}
      kind="required"
    />
    <ValidationGroup
      title="Önerilen alanlar"
      emptyMessage="Kaydedilmiş sürümde önerilen alan eksiği görünmüyor."
      fields={recommendedMissing}
      kind="recommended"
    />
    <p className="validation-summary__footnote">
      Bu rapor kaydedilmiş bilgilerinizi kontrol eder. Yayınla adımında yayın hakkınız ve seçtiğiniz tarihler ayrıca doğrulanır.
    </p>
  </section>
}

function ValidationGroup({
  title,
  emptyMessage,
  fields,
  kind,
}: {
  title: string
  emptyMessage: string
  fields: DraftValidationFieldResult[]
  kind: 'required' | 'recommended'
}) {
  return <div className={`validation-group validation-group--${kind}`}>
    <h4>{title}</h4>
    {fields.length === 0
      ? <p>{emptyMessage}</p>
      : <ul>{fields.map(result => <li key={result.field}>
        {invitationFieldLabel(result.field)}
        {!result.isRecognized ? ' (şablon alanı tanınmıyor)' : ''}
      </li>)}</ul>}
  </div>
}

function missingFields(fields: DraftValidationFieldResult[]): DraftValidationFieldResult[] {
  return fields.filter(field => !field.isRecognized || !field.isPresent)
}
