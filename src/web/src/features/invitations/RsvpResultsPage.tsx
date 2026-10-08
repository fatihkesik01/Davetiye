import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

import {
  ApiRequestError,
  DavetiyeApiClient,
  type CreatorRsvpSubmissionDetail,
  type CreatorRsvpSubmissionPage,
  type InvitationRsvpConfiguration,
} from '../../api/generated/client'
import { LoadingState } from '../../components/feedback/LoadingState'
import { InternalLink } from '../../components/ui/InternalLink'

const pageSize = 25

export function RsvpResultsPage({ invitationId, api: injectedApi }: { invitationId: string; api?: DavetiyeApiClient }) {
  const { t, i18n } = useTranslation()
  const api = useMemo(() => injectedApi ?? new DavetiyeApiClient(), [injectedApi])
  const [page, setPage] = useState<CreatorRsvpSubmissionPage | null>(null)
  const [configuration, setConfiguration] = useState<InvitationRsvpConfiguration | null>(null)
  const [pageNumber, setPageNumber] = useState(1)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const [expanded, setExpanded] = useState<string | null>(null)
  const [details, setDetails] = useState<Record<string, CreatorRsvpSubmissionDetail>>({})
  const [detailLoading, setDetailLoading] = useState<string | null>(null)
  const [detailFailed, setDetailFailed] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<{ id: string; label: string; trigger: HTMLButtonElement } | null>(null)
  const [deleting, setDeleting] = useState<string | null>(null)
  const [message, setMessage] = useState('')
  const [actionError, setActionError] = useState('')
  const cancelDeleteRef = useRef<HTMLButtonElement>(null)
  const confirmDeleteRef = useRef<HTMLButtonElement>(null)
  const pageTitleRef = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    const controller = new AbortController()
    queueMicrotask(() => {
      if (!controller.signal.aborted) {
        setLoading(true)
        setFailed(false)
      }
    })
    void api.getCreatorRsvpSubmissions(invitationId, pageNumber, pageSize, controller.signal).then(result => {
      if (controller.signal.aborted) return
      setPage(result)
      setLoading(false)
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      if (controller.signal.aborted) return
      setFailed(true)
      setLoading(false)
    })
    void api.getInvitationRsvp(invitationId, controller.signal).then(config => {
      if (!controller.signal.aborted) setConfiguration(config)
    }).catch(() => {
      if (!controller.signal.aborted) setConfiguration(null)
    })
    return () => controller.abort()
  }, [api, invitationId, pageNumber, attempt])

  useEffect(() => {
    if (!deleteTarget) return
    cancelDeleteRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !deleting) {
        const trigger = deleteTarget.trigger
        setDeleteTarget(null)
        requestAnimationFrame(() => trigger.focus())
      }
      if (event.key === 'Tab') {
        const first = cancelDeleteRef.current
        const last = confirmDeleteRef.current
        if (!first || !last) return
        if (event.shiftKey && document.activeElement === first) {
          event.preventDefault()
          last.focus()
        } else if (!event.shiftKey && document.activeElement === last) {
          event.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [deleteTarget, deleting])

  const closeDelete = () => {
    const trigger = deleteTarget?.trigger
    setDeleteTarget(null)
    requestAnimationFrame(() => trigger?.focus())
  }

  const loadDetails = async (submissionId: string) => {
    if (details[submissionId]) return
    setDetailLoading(submissionId)
    setDetailFailed(null)
    try {
      const result = await api.getCreatorRsvpSubmission(invitationId, submissionId)
      setDetails(current => ({ ...current, [submissionId]: result }))
    } catch {
      setDetailFailed(submissionId)
    } finally {
      setDetailLoading(null)
    }
  }

  const toggleDetails = async (submissionId: string) => {
    if (expanded === submissionId) {
      setExpanded(null)
      return
    }
    setExpanded(submissionId)
    await loadDetails(submissionId)
  }

  const retryDetails = () => {
    if (expanded) void loadDetails(expanded)
  }

  const removeSubmission = async () => {
    if (!deleteTarget || deleting) return
    const target = deleteTarget
    setDeleting(target.id)
    setMessage(t('creatorEditorUi.rsvpResults.deleting'))
    setActionError('')
    try {
      const csrf = await api.getAntiforgeryToken()
      await api.deleteCreatorRsvpSubmission(invitationId, target.id, csrf)
      const current = page
      const nextCount = Math.max(0, (current?.totalCount ?? 1) - 1)
      const lastPage = Math.max(1, Math.ceil(nextCount / pageSize))
      setDetails(value => {
        const next = { ...value }
        delete next[target.id]
        return next
      })
      setExpanded(null)
      setPageNumber(value => Math.min(value, lastPage))
      setPage(null)
      setMessage(t('creatorEditorUi.rsvpResults.deleted'))
      setDeleteTarget(null)
      requestAnimationFrame(() => pageTitleRef.current?.focus())
      setAttempt(value => value + 1)
    } catch (error) {
      const errorMessage = error instanceof ApiRequestError && error.status === 404
        ? t('creatorEditorUi.rsvpResults.missing')
        : t('creatorEditorUi.rsvpResults.deleteFailed')
      setMessage(errorMessage)
      setActionError(errorMessage)
    } finally {
      setDeleting(null)
    }
  }

  const titleId = `rsvp-results-title-${invitationId}`
  const maxPage = Math.max(1, Math.ceil((page?.totalCount ?? 0) / pageSize))
  return <section className="rsvp-results" aria-labelledby={titleId} aria-busy={loading || undefined}>
    <div className="rsvp-results__heading">
      <div>
        <p className="eyebrow">{t('creatorEditorUi.rsvpResults.privateEyebrow')}</p>
        <h2 id={titleId} ref={pageTitleRef} tabIndex={-1}>{t('creatorEditorUi.rsvpResults.title')}</h2>
        <p>{t('creatorEditorUi.rsvpResults.privacy')}</p>
      </div>
      <InternalLink className="button button--secondary" to={`/panel/davetiyeler/${invitationId}/duzenle`}>{t('creatorEditorUi.rsvpResults.back')}</InternalLink>
    </div>

    {loading ? <LoadingState label={t('creatorEditorUi.rsvpResults.loading')} /> : null}
    {failed ? <div className="inline-alert" role="alert">
      <p>{t('creatorEditorUi.rsvpResults.loadFailed')}</p>
      <button className="button button--secondary" type="button" onClick={() => { setLoading(true); setFailed(false); setAttempt(value => value + 1) }}>{t('creatorEditorUi.rsvpResults.retry')}</button>
    </div> : null}
    {!loading && page ? <>
      <section className="rsvp-results__summary" aria-labelledby="rsvp-summary-heading">
        <h3 id="rsvp-summary-heading">{t('creatorEditorUi.rsvpResults.summary')}</h3>
        <div className="rsvp-results__summary-grid">
          <div className="rsvp-results__metric"><span>{t('creatorEditorUi.rsvpResults.responseCount')}</span><strong>{page.summary.responseCount}</strong></div>
          {page.summary.participantCountAvailable && page.summary.totalParticipants !== null
            ? <div className="rsvp-results__metric"><span>{t('creatorEditorUi.rsvpResults.totalParticipants')}</span><strong>{page.summary.totalParticipants}</strong><small>{t('creatorEditorUi.rsvpResults.participantCountHelp')}</small></div>
            : <p className="rsvp-results__unavailable">{t('creatorEditorUi.rsvpResults.participantCountUnavailable')}</p>}
        </div>
        {page.summary.questions.length ? <div className="rsvp-results__question-summaries">
          {page.summary.questions.map(question => <article className="rsvp-results__question-summary" key={`${question.questionId}:${question.prompt}:${question.type}`}>
            <h4>{question.prompt}</h4>
            <p className="rsvp-results__muted">{t('creatorEditorUi.rsvpResults.answerFormat')}: {questionTypeLabel(question.type, t)}</p>
            <p>{t('creatorEditorUi.rsvpResults.answers', { count: question.answeredCount })}</p>
            {question.valueCounts.length ? <ul aria-label={t('creatorEditorUi.rsvpResults.distributionAria', { prompt: question.prompt })}>
              {question.valueCounts.map(item => <li key={`${item.value}:${item.label ?? ''}`}><span>{getAggregateLabel(question.type, item.value, item.label, configuration, question.questionId, t)}</span><strong>{item.count}</strong></li>)}
            </ul> : <p className="rsvp-results__muted">{t('creatorEditorUi.rsvpResults.noDistribution')}</p>}
          </article>)}
        </div> : <p>{t('creatorEditorUi.rsvpResults.noQuestions')}</p>}
      </section>

      <section aria-labelledby="rsvp-submissions-heading">
        <div className="rsvp-results__list-heading"><div><h3 id="rsvp-submissions-heading">{t('creatorEditorUi.rsvpResults.submissions')}</h3><p>{t('creatorEditorUi.rsvpResults.records', { count: page.totalCount })}</p></div></div>
        {page.totalCount === 0 ? <div className="catalog-empty"><h4>{t('creatorEditorUi.rsvpResults.emptyTitle')}</h4><p>{t('creatorEditorUi.rsvpResults.emptyBody')}</p></div> : <ol className="rsvp-submission-list">
          {page.submissions.map((submission, index) => {
            const ordinal = (page.page - 1) * page.pageSize + index + 1
            const opened = expanded === submission.submissionId
            const detailsForSubmission = details[submission.submissionId]
            const deleteLabel = t('creatorEditorUi.rsvpResults.responseNumber', { ordinal, date: formatDate(submission.submittedAt, i18n.language) })
            return <li key={submission.submissionId}>
              <article className="rsvp-submission-card">
                <div className="rsvp-submission-card__heading">
                  <div><h4>{t('creatorEditorUi.rsvpResults.responseNumberShort', { ordinal })}</h4><p>{t('creatorEditorUi.rsvpResults.submitted')}: {formatDate(submission.submittedAt, i18n.language)}</p><p>{t('creatorEditorUi.rsvpResults.lastUpdated')}: {formatDate(submission.updatedAt, i18n.language)}</p></div>
                  <div className="rsvp-submission-card__actions">
                    <button className="button button--secondary" type="button" aria-expanded={opened} aria-controls={`rsvp-submission-detail-${submission.submissionId}`} onClick={() => void toggleDetails(submission.submissionId)}>
                      {opened ? t('creatorEditorUi.rsvpResults.hideResponse') : t('creatorEditorUi.rsvpResults.showResponse')}
                    </button>
                    <button className="button button--danger" type="button" disabled={deleting === submission.submissionId} aria-label={t('creatorEditorUi.rsvpResults.deleteAria', { label: deleteLabel })} onClick={event => setDeleteTarget({ id: submission.submissionId, label: deleteLabel, trigger: event.currentTarget })}>{t('creatorEditorUi.rsvpResults.deleteShort')}</button>
                  </div>
                </div>
                {opened ? <div id={`rsvp-submission-detail-${submission.submissionId}`} className="rsvp-submission-card__details">
                  {detailLoading === submission.submissionId ? <p role="status">{t('creatorEditorUi.rsvpResults.detailsLoading')}</p> : null}
                  {detailFailed === submission.submissionId ? <div className="inline-alert" role="alert"><p>{t('creatorEditorUi.rsvpResults.detailsFailed')}</p><button className="button button--secondary" type="button" onClick={retryDetails}>{t('creatorEditorUi.rsvpResults.retry')}</button></div> : null}
                  {detailsForSubmission && detailsForSubmission.answers.length ? <dl>{detailsForSubmission.answers.map(answer => <div key={answer.questionId}>
                    <dt>{answer.prompt}</dt><dd>{formatAnswer(answer, t)}</dd>
                  </div>)}</dl> : null}
                  {detailsForSubmission?.answers.length === 0 ? <p>{t('creatorEditorUi.rsvpResults.noDetails')}</p> : null}
                </div> : null}
              </article>
            </li>
          })}
        </ol>}
        {page.totalCount > 0 ? <nav className="rsvp-results__pagination" aria-label={t('creatorEditorUi.rsvpResults.pagesAria')}>
          <button className="button button--secondary" type="button" disabled={pageNumber <= 1 || loading} onClick={() => setPageNumber(value => Math.max(1, value - 1))}>{t('creatorEditorUi.rsvpResults.previous')}</button>
          <span>{t('creatorEditorUi.rsvpResults.page', { current: page.page, total: maxPage })}</span>
          <button className="button button--secondary" type="button" disabled={pageNumber >= maxPage || loading} onClick={() => setPageNumber(value => Math.min(maxPage, value + 1))}>{t('creatorEditorUi.rsvpResults.next')}</button>
        </nav> : null}
      </section>
    </> : null}

    {actionError ? <div className="inline-alert" role="alert"><p>{actionError}</p><button className="button button--secondary" type="button" onClick={() => { setActionError(''); setLoading(true); setAttempt(value => value + 1) }}>{t('creatorEditorUi.rsvpResults.refreshList')}</button></div> : null}
    <p className="visually-hidden" role="status" aria-live="polite">{message}</p>
    {deleteTarget ? <div className="rsvp-delete-confirmation" role="alertdialog" aria-modal="true" aria-labelledby="rsvp-delete-title" aria-describedby="rsvp-delete-description">
      <div className="rsvp-delete-confirmation__card">
        <h3 id="rsvp-delete-title" tabIndex={-1}>{t('creatorEditorUi.rsvpResults.confirmDeleteTitle')}</h3>
        <p><strong>{deleteTarget.label}</strong></p>
        <p id="rsvp-delete-description">{t('creatorEditorUi.rsvpResults.confirmDeleteBody')}</p>
        <div className="button-row">
          <button className="button button--secondary" type="button" ref={cancelDeleteRef} disabled={deleting !== null} onClick={closeDelete}>{t('creatorEditorUi.rsvpResults.cancel')}</button>
          <button className="button button--danger" type="button" ref={confirmDeleteRef} disabled={deleting !== null} onClick={() => void removeSubmission()}>{deleting ? t('creatorEditorUi.rsvpResults.deleting') : t('creatorEditorUi.rsvpResults.delete')}</button>
        </div>
      </div>
    </div> : null}
  </section>
}

function formatAnswer(answer: CreatorRsvpSubmissionDetail['answers'][number], t: TFunction): string {
  if (answer.textValue !== null) return answer.textValue || '—'
  if (answer.numberValue !== null) return answer.numberValue
  if (answer.booleanValue !== null) return answer.booleanValue ? t('creatorEditorUi.rsvpResults.yes') : t('creatorEditorUi.rsvpResults.no')
  if (answer.selectedOptions.length) return answer.selectedOptions.map(option => option.label).join(', ')
  return t('creatorEditorUi.rsvpResults.notAnswered')
}

function questionTypeLabel(type: string, t: TFunction): string {
  switch (type) {
    case 'ShortText': return t('creatorEditorUi.rsvpResults.types.shortText')
    case 'LongText': return t('creatorEditorUi.rsvpResults.types.longText')
    case 'SingleChoice': return t('creatorEditorUi.rsvpResults.types.singleChoice')
    case 'MultipleChoice': return t('creatorEditorUi.rsvpResults.types.multipleChoice')
    case 'YesNo': return t('creatorEditorUi.rsvpResults.types.yesNo')
    case 'Number': return t('creatorEditorUi.rsvpResults.types.number')
    default: return type
  }
}

function getAggregateLabel(type: string, value: string, label: string | null, configuration: InvitationRsvpConfiguration | null, questionId: string, t: TFunction): string {
  if (type === 'YesNo') return value === 'true' ? t('creatorEditorUi.rsvpResults.yes') : value === 'false' ? t('creatorEditorUi.rsvpResults.no') : t('creatorEditorUi.rsvpResults.otherAnswer')
  if (type === 'SingleChoice' || type === 'MultipleChoice') {
    if (label !== null) return label
    const option = configuration?.questions.find(question => question.id === questionId)?.options.find(item => item.id === value)
    return option?.label ?? t('creatorEditorUi.rsvpResults.unavailableOption')
  }
  return value
}

function formatDate(value: string, language: string): string {
  try { return new Intl.DateTimeFormat(language === 'en' ? 'en-US' : 'tr-TR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
  catch { return language === 'en' ? 'Date unavailable' : 'Tarih bilgisi kullanılamıyor' }
}
