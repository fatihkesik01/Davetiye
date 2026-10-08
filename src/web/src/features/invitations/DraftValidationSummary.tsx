import { useTranslation } from 'react-i18next'
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
  const { t } = useTranslation()
  if (loading && !report) {
    return <section className="validation-summary" aria-labelledby="validation-heading" aria-busy="true">
      <h3 id="validation-heading">{t('creatorEditorUi.draftValidation.title')}</h3>
      <p role="status">{t('creatorEditorUi.draftValidation.checking')}</p>
    </section>
  }

  if (failed && !report) {
    return <section className="validation-summary validation-summary--blocked" aria-labelledby="validation-heading">
      <h3 id="validation-heading">{t('creatorEditorUi.draftValidation.unavailable')}</h3>
      <p role="alert">{t('creatorEditorUi.draftValidation.loadFailure')}</p>
      <button className="button button--secondary" type="button" onClick={onRetry}>{t('creatorEditorUi.draftValidation.retry')}</button>
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
    <h3 id="validation-heading">{t('creatorEditorUi.draftValidation.title')}</h3>
    {hasUnsavedChanges ? <p className="validation-summary__notice" role="status">
      {t('creatorEditorUi.draftValidation.stale')}
    </p> : null}
    {loading ? <p className="validation-summary__notice" role="status">{t('creatorEditorUi.draftValidation.refreshing')}</p> : null}
    {failed ? <p className="validation-summary__notice" role="alert">
      {t('creatorEditorUi.draftValidation.refreshFailure')}
      {' '}<button className="text-button" type="button" onClick={onRetry}>{t('creatorEditorUi.draftValidation.retry')}</button>
    </p> : null}

    {!report.templateSelected ? <div className="validation-group validation-group--required">
      <h4>{t('creatorEditorUi.draftValidation.templateRequired')}</h4>
      <p>{t('creatorEditorUi.draftValidation.selectTemplate')}</p>
    </div> : null}
    {report.templateSelected && !report.templateAvailable ? <div className="validation-group validation-group--required">
      <h4>{t('creatorEditorUi.draftValidation.templateInvalid')}</h4>
      <p>{t('creatorEditorUi.draftValidation.templateUnavailable')}</p>
    </div> : null}

    <ValidationGroup
      title={t('creatorEditorUi.draftValidation.required')}
      emptyMessage={t('creatorEditorUi.draftValidation.requiredEmpty')}
      fields={requiredMissing}
      kind="required"
    />
    <ValidationGroup
      title={t('creatorEditorUi.draftValidation.recommended')}
      emptyMessage={t('creatorEditorUi.draftValidation.recommendedEmpty')}
      fields={recommendedMissing}
      kind="recommended"
    />
    <p className="validation-summary__footnote">
      {t('creatorEditorUi.draftValidation.footnote')}
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
  const { t } = useTranslation()
  return <div className={`validation-group validation-group--${kind}`}>
    <h4>{title}</h4>
    {fields.length === 0
      ? <p>{emptyMessage}</p>
      : <ul>{fields.map(result => <li key={result.field}>
        {invitationFieldLabel(result.field)}
        {!result.isRecognized ? t('creatorEditorUi.draftValidation.unrecognized') : ''}
      </li>)}</ul>}
  </div>
}

function missingFields(fields: DraftValidationFieldResult[]): DraftValidationFieldResult[] {
  return fields.filter(field => !field.isRecognized || !field.isPresent)
}
