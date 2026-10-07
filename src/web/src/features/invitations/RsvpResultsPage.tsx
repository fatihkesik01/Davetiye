import { useEffect, useMemo, useRef, useState } from 'react'

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
    setMessage('Yanıt siliniyor…')
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
      setMessage('Yanıt kalıcı olarak silindi. Toplamlar güncellendi.')
      setDeleteTarget(null)
      requestAnimationFrame(() => pageTitleRef.current?.focus())
      setAttempt(value => value + 1)
    } catch (error) {
      const errorMessage = error instanceof ApiRequestError && error.status === 404
        ? 'Bu yanıt artık bulunamıyor. Listeyi yenileyin.'
        : 'Yanıt silinemedi. Bağlantınızı kontrol edip yeniden deneyin.'
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
        <p className="eyebrow">Davetli yanıtları · Özel</p>
        <h2 id={titleId} ref={pageTitleRef} tabIndex={-1}>RSVP yanıtları</h2>
        <p>Bu yanıtları yalnızca davetiye sahibi görebilir.</p>
      </div>
      <InternalLink className="button button--secondary" to={`/panel/davetiyeler/${invitationId}/duzenle`}>Davetiyeye dön</InternalLink>
    </div>

    {loading ? <LoadingState label="RSVP yanıtları yükleniyor" /> : null}
    {failed ? <div className="inline-alert" role="alert">
      <p>RSVP yanıtları yüklenemedi veya bu davetiyeye erişilemiyor.</p>
      <button className="button button--secondary" type="button" onClick={() => { setLoading(true); setFailed(false); setAttempt(value => value + 1) }}>Tekrar dene</button>
    </div> : null}
    {!loading && page ? <>
      <section className="rsvp-results__summary" aria-labelledby="rsvp-summary-heading">
        <h3 id="rsvp-summary-heading">Genel görünüm</h3>
        <div className="rsvp-results__summary-grid">
          <div className="rsvp-results__metric"><span>Yanıt sayısı</span><strong>{page.summary.responseCount}</strong></div>
          {page.summary.participantCountAvailable && page.summary.totalParticipants !== null
            ? <div className="rsvp-results__metric"><span>Toplam katılımcı</span><strong>{page.summary.totalParticipants}</strong><small>Katılımcı sayısı sorusuna verilen yanıtların toplamı.</small></div>
            : <p className="rsvp-results__unavailable">Etkin bir katılımcı sayısı sorusu bulunmadığı için toplam hesaplanamıyor. Soru değiştirilir veya kaldırılırsa eski yanıtlar katılımcı toplamına dahil edilmeyebilir.</p>}
        </div>
        {page.summary.questions.length ? <div className="rsvp-results__question-summaries">
          {page.summary.questions.map(question => <article className="rsvp-results__question-summary" key={`${question.questionId}:${question.prompt}:${question.type}`}>
            <h4>{question.prompt}</h4>
            <p className="rsvp-results__muted">Yanıt biçimi: {questionTypeLabel(question.type)}</p>
            <p>{question.answeredCount} yanıt</p>
            {question.valueCounts.length ? <ul aria-label={`${question.prompt} yanıt dağılımı`}>
              {question.valueCounts.map(item => <li key={`${item.value}:${item.label ?? ''}`}><span>{getAggregateLabel(question.type, item.value, item.label, configuration, question.questionId)}</span><strong>{item.count}</strong></li>)}
            </ul> : <p className="rsvp-results__muted">Bu soru için seçenek dağılımı gösterilmiyor.</p>}
          </article>)}
        </div> : <p>Etkin RSVP sorusu bulunmuyor; soru dağılımları gösterilemiyor.</p>}
      </section>

      <section aria-labelledby="rsvp-submissions-heading">
        <div className="rsvp-results__list-heading"><div><h3 id="rsvp-submissions-heading">Gönderilen yanıtlar</h3><p>{page.totalCount} kayıt</p></div></div>
        {page.totalCount === 0 ? <div className="catalog-empty"><h4>Henüz yanıt yok</h4><p>Davetliler yanıt gönderdikçe burada listelenir.</p></div> : <ol className="rsvp-submission-list">
          {page.submissions.map((submission, index) => {
            const ordinal = (page.page - 1) * page.pageSize + index + 1
            const opened = expanded === submission.submissionId
            const detailsForSubmission = details[submission.submissionId]
            const deleteLabel = `Yanıt ${ordinal} · ${formatDate(submission.submittedAt)}`
            return <li key={submission.submissionId}>
              <article className="rsvp-submission-card">
                <div className="rsvp-submission-card__heading">
                  <div><h4>Yanıt {ordinal}</h4><p>Gönderildi: {formatDate(submission.submittedAt)}</p><p>Son güncelleme: {formatDate(submission.updatedAt)}</p></div>
                  <div className="rsvp-submission-card__actions">
                    <button className="button button--secondary" type="button" aria-expanded={opened} aria-controls={`rsvp-submission-detail-${submission.submissionId}`} onClick={() => void toggleDetails(submission.submissionId)}>
                      {opened ? 'Yanıtı gizle' : 'Yanıtı görüntüle'}
                    </button>
                    <button className="button button--danger" type="button" disabled={deleting === submission.submissionId} aria-label={`${deleteLabel} kaydını kalıcı olarak sil`} onClick={event => setDeleteTarget({ id: submission.submissionId, label: deleteLabel, trigger: event.currentTarget })}>Kalıcı sil</button>
                  </div>
                </div>
                {opened ? <div id={`rsvp-submission-detail-${submission.submissionId}`} className="rsvp-submission-card__details">
                  {detailLoading === submission.submissionId ? <p role="status">Yanıt ayrıntıları yükleniyor…</p> : null}
                  {detailFailed === submission.submissionId ? <div className="inline-alert" role="alert"><p>Yanıt ayrıntıları yüklenemedi.</p><button className="button button--secondary" type="button" onClick={retryDetails}>Tekrar dene</button></div> : null}
                  {detailsForSubmission && detailsForSubmission.answers.length ? <dl>{detailsForSubmission.answers.map(answer => <div key={answer.questionId}>
                    <dt>{answer.prompt}</dt><dd>{formatAnswer(answer)}</dd>
                  </div>)}</dl> : null}
                  {detailsForSubmission?.answers.length === 0 ? <p>Bu kayıtta görüntülenecek yanıt ayrıntısı yok.</p> : null}
                </div> : null}
              </article>
            </li>
          })}
        </ol>}
        {page.totalCount > 0 ? <nav className="rsvp-results__pagination" aria-label="Yanıt sayfaları">
          <button className="button button--secondary" type="button" disabled={pageNumber <= 1 || loading} onClick={() => setPageNumber(value => Math.max(1, value - 1))}>Önceki</button>
          <span>Sayfa {page.page} / {maxPage}</span>
          <button className="button button--secondary" type="button" disabled={pageNumber >= maxPage || loading} onClick={() => setPageNumber(value => Math.min(maxPage, value + 1))}>Sonraki</button>
        </nav> : null}
      </section>
    </> : null}

    {actionError ? <div className="inline-alert" role="alert"><p>{actionError}</p><button className="button button--secondary" type="button" onClick={() => { setActionError(''); setLoading(true); setAttempt(value => value + 1) }}>Listeyi yenile</button></div> : null}
    <p className="visually-hidden" role="status" aria-live="polite">{message}</p>
    {deleteTarget ? <div className="rsvp-delete-confirmation" role="alertdialog" aria-modal="true" aria-labelledby="rsvp-delete-title" aria-describedby="rsvp-delete-description">
      <div className="rsvp-delete-confirmation__card">
        <h3 id="rsvp-delete-title" tabIndex={-1}>Yanıt kalıcı olarak silinsin mi?</h3>
        <p><strong>{deleteTarget.label}</strong></p>
        <p id="rsvp-delete-description">Bu yanıt ve ona ait yanıt güncelleme yetkisi kalıcı olarak silinir. İşlem geri alınamaz; yanıt ve katılımcı toplamları değişebilir.</p>
        <div className="button-row">
          <button className="button button--secondary" type="button" ref={cancelDeleteRef} disabled={deleting !== null} onClick={closeDelete}>Vazgeç</button>
          <button className="button button--danger" type="button" ref={confirmDeleteRef} disabled={deleting !== null} onClick={() => void removeSubmission()}>{deleting ? 'Siliniyor…' : 'Kalıcı olarak sil'}</button>
        </div>
      </div>
    </div> : null}
  </section>
}

function formatAnswer(answer: CreatorRsvpSubmissionDetail['answers'][number]): string {
  if (answer.textValue !== null) return answer.textValue || '—'
  if (answer.numberValue !== null) return answer.numberValue
  if (answer.booleanValue !== null) return answer.booleanValue ? 'Evet' : 'Hayır'
  if (answer.selectedOptions.length) return answer.selectedOptions.map(option => option.label).join(', ')
  return 'Yanıt verilmedi'
}

function questionTypeLabel(type: string): string {
  switch (type) {
    case 'ShortText': return 'Kısa metin'
    case 'LongText': return 'Uzun metin'
    case 'SingleChoice': return 'Tek seçim'
    case 'MultipleChoice': return 'Çoklu seçim'
    case 'YesNo': return 'Evet / Hayır'
    case 'Number': return 'Sayı'
    default: return type
  }
}

function getAggregateLabel(type: string, value: string, label: string | null, configuration: InvitationRsvpConfiguration | null, questionId: string): string {
  if (type === 'YesNo') return value === 'true' ? 'Evet' : value === 'false' ? 'Hayır' : 'Diğer yanıt'
  if (type === 'SingleChoice' || type === 'MultipleChoice') {
    if (label !== null) return label
    const option = configuration?.questions.find(question => question.id === questionId)?.options.find(item => item.id === value)
    return option?.label ?? 'Önceki veya kullanılamayan seçenek'
  }
  return value
}

function formatDate(value: string): string {
  try { return new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
  catch { return 'Tarih bilgisi kullanılamıyor' }
}
