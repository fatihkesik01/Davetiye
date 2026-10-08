import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'

import { ApiRequestError, normalizeRsvpNumberLexeme, type DavetiyeApiClient, type PublicRsvpAnswerInput, type PublicRsvpConfiguration, type PublicRsvpGuestAnswer, type PublicRsvpQuestion } from '../../api/generated/client'

interface Props {
  api: DavetiyeApiClient
  publicCode: string
  configuration: PublicRsvpConfiguration
}

type FormStatus = 'new' | 'loading-own' | 'ready' | 'editing' | 'saved' | 'submitting' | 'error'
type AnswerState = Record<string, string | boolean | string[]>
interface ExactDecimal { coefficient: bigint; scale: number }
const maxDecimalCoefficient = 79228162514264337593543950335n

export function PublicRsvpForm({ api, publicCode, configuration }: Props) {
  const { t } = useTranslation()
  const questions = useMemo(() => [...configuration.questions].sort((a, b) => a.sortOrder - b.sortOrder), [configuration.questions])
  const storageKey = `davetiye:rsvp-submission:${publicCode}`
  const [status, setStatus] = useState<FormStatus>(() => {
    try { return window.localStorage.getItem(storageKey) ? 'loading-own' : 'new' }
    catch { return 'new' }
  })
  const [answers, setAnswers] = useState<AnswerState>({})
  const [submissionId, setSubmissionId] = useState<string | null>(null)
  const [lookupAttempt, setLookupAttempt] = useState(0)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [formError, setFormError] = useState('')
  const [notice, setNotice] = useState('')
  const summaryRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (formError && Object.keys(errors).length) summaryRef.current?.focus()
  }, [errors, formError])

  useEffect(() => {
    let locator: string | null = null
    try { locator = window.localStorage.getItem(storageKey) } catch { /* Storage can be unavailable; new submissions still work. */ }
    if (!locator) return

    const controller = new AbortController()
    void api.getPublicRsvpSubmission(publicCode, locator, controller.signal).then(submission => {
      if (controller.signal.aborted) return
      setSubmissionId(submission.submissionId)
      setAnswers(answersFromSubmission(questions, submission.answers))
      setStatus('saved')
    }).catch(error => {
      if (controller.signal.aborted) return
      if (isNotFound(error)) {
        try { window.localStorage.removeItem(storageKey) } catch { /* A stale locator is harmless if storage cannot be cleared. */ }
        setSubmissionId(null)
        setAnswers({})
        setNotice(t('guest.previousCannotEdit'))
        setStatus('new')
      } else {
        setFormError(t('guest.previousFailed'))
        setStatus('error')
      }
    })
    return () => controller.abort()
  }, [api, lookupAttempt, publicCode, questions, storageKey, t])

  function changeAnswer(questionId: string, value: string | boolean | string[]) {
    setAnswers(current => ({ ...current, [questionId]: value }))
    setErrors(current => {
      if (!current[questionId]) return current
      const next = { ...current }
      delete next[questionId]
      return next
    })
  }

  async function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const validation = validateAnswers(questions, answers, configuration, t)
    setErrors(validation)
    setFormError('')
    if (Object.keys(validation).length) {
      setFormError(t('guest.validation'))
      return
    }

    setStatus('submitting')
    const payload = toAnswers(questions, answers)
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const result = submissionId
        ? await api.updatePublicRsvpSubmission(publicCode, submissionId, { answers: payload }, csrfToken)
        : await api.createPublicRsvpSubmission(publicCode, { answers: payload }, csrfToken)
      try { window.localStorage.setItem(storageKey, result.submissionId) } catch { /* The cookie remains the only authority; local storage is just a locator. */ }
      setSubmissionId(result.submissionId)
      setStatus('saved')
      setNotice(t('guest.saved'))
    } catch (error) {
      if (submissionId && isNotFound(error)) {
        try { window.localStorage.removeItem(storageKey) } catch { /* Best effort cleanup. */ }
        setSubmissionId(null)
        setAnswers({})
        setStatus('new')
        setNotice(t('guest.cannotUpdate'))
        return
      }
      setStatus(submissionId ? 'editing' : 'ready')
      setFormError(t('guest.submitFailed'))
    }
  }

  if (configuration.status !== 'available' || !questions.length) return null
  if (status === 'loading-own') return <section className="public-rsvp" aria-labelledby="public-rsvp-heading"><h2 id="public-rsvp-heading">{t('guest.rsvp')}</h2><p role="status">{t('guest.checkingAnswer')}</p></section>
  if (status === 'error') return <section className="public-rsvp" aria-labelledby="public-rsvp-heading"><h2 id="public-rsvp-heading">{t('guest.rsvp')}</h2><p role="alert">{formError}</p><button className="button button--secondary" type="button" onClick={() => { setFormError(''); setStatus('loading-own'); setLookupAttempt(value => value + 1) }}>{t('guest.retry')}</button></section>
  if (status === 'saved') return <section className="public-rsvp" aria-labelledby="public-rsvp-heading">
    <h2 id="public-rsvp-heading">{t('guest.rsvp')}</h2>
    <p role="status">{notice || t('guest.previous')}</p>
    <button className="button button--secondary" type="button" onClick={() => { setFormError(''); setNotice(''); setStatus('editing') }}>{t('guest.update')}</button>
  </section>

  const formEnabled = status !== 'submitting'
  return <section className="public-rsvp" aria-labelledby="public-rsvp-heading">
    <h2 id="public-rsvp-heading">{t('guest.rsvp')}</h2>
    {notice ? <p className="form-field__help" role="status">{notice}</p> : null}
    <form onSubmit={submit} noValidate>
      {formError ? <div id="public-rsvp-summary" ref={summaryRef} className="public-rsvp__summary" role="alert" tabIndex={-1}>
        <p>{formError}</p>
        {Object.keys(errors).length ? <ul>{questions.filter(question => errors[question.id]).map(question => <li key={question.id}><a href={`#rsvp-${question.id}`}>{question.prompt}: {errors[question.id]}</a></li>)}</ul> : null}
      </div> : null}
      {questions.map(question => <QuestionField key={question.id} question={question} value={answers[question.id]} error={errors[question.id]}
        configuration={configuration} disabled={!formEnabled} onChange={value => changeAnswer(question.id, value)} />)}
      <button className="button button--primary" type="submit" disabled={!formEnabled}>{status === 'submitting' ? t('guest.submitting') : submissionId ? t('guest.saveAnswer') : t('guest.sendAnswer')}</button>
    </form>
  </section>
}

function QuestionField({ question, value, error, configuration, disabled, onChange }: {
  question: PublicRsvpQuestion
  value: AnswerState[string] | undefined
  error?: string
  configuration: PublicRsvpConfiguration
  disabled: boolean
  onChange: (value: string | boolean | string[]) => void
}) {
  const { t } = useTranslation()
  const inputId = `rsvp-${question.id}`
  const errorId = `${inputId}-error`
  const helpId = `${inputId}-help`
  const describedBy = [question.isRequired ? helpId : '', error ? errorId : ''].filter(Boolean).join(' ') || undefined
  const label = <>{question.prompt}{question.isRequired ? <span aria-hidden="true"> *</span> : null}</>
  const common = { id: inputId, disabled, 'aria-invalid': Boolean(error), 'aria-describedby': describedBy }
  const errorText = error ? <p className="form-field__error" id={errorId}>{error}</p> : null

  if (question.type === 'SingleChoice' || question.type === 'MultipleChoice' || question.type === 'YesNo') {
    const choices = question.type === 'YesNo' ? [{ id: 'yes', label: t('guest.yes') }, { id: 'no', label: t('guest.no') }] : question.options
    const selected = question.type === 'YesNo' ? value === true ? 'yes' : value === false ? 'no' : '' : value
    return <fieldset className="form-field public-rsvp__field" aria-describedby={describedBy}>
      <legend>{label}</legend>
      {question.isRequired ? <p className="form-field__help" id={helpId}>{t('guest.required')}</p> : null}
      <div className="form-field__radio-options">
        {choices.map(option => {
          const checked = question.type === 'MultipleChoice' ? Array.isArray(selected) && selected.includes(option.id) : selected === option.id
          const choiceId = `${inputId}-${option.id}`
          return <label key={option.id} className="form-field__radio-option" htmlFor={choiceId}>
            <input id={choiceId} type={question.type === 'MultipleChoice' ? 'checkbox' : 'radio'} name={inputId} value={option.id} checked={checked}
              disabled={disabled || question.type === 'MultipleChoice' && !checked && Array.isArray(selected) && selected.length >= configuration.answerLimits.maxMultipleChoiceSelections}
              aria-invalid={Boolean(error)} aria-describedby={describedBy}
              onChange={event => {
                if (question.type === 'MultipleChoice') {
                  const current = Array.isArray(selected) ? selected : []
                  onChange(event.target.checked ? [...current, option.id] : current.filter(item => item !== option.id))
                } else if (question.type === 'YesNo') onChange(option.id === 'yes')
                else onChange(option.id)
              }} />
            <span>{option.label}</span>
          </label>
        })}
      </div>
      {question.type === 'MultipleChoice' ? <p className="form-field__help">{t('guest.maxOptions', { count: configuration.answerLimits.maxMultipleChoiceSelections })}</p> : null}
      {errorText}
    </fieldset>
  }

  return <div className="form-field public-rsvp__field">
    <label htmlFor={inputId}>{label}</label>
    {question.isRequired ? <p className="form-field__help" id={helpId}>{t('guest.required')}</p> : null}
    {question.type === 'LongText'
      ? <textarea {...common} maxLength={configuration.answerLimits.maxLongTextAnswerCharacters} rows={4} value={typeof value === 'string' ? value : ''} onChange={event => onChange(event.target.value)} />
      : question.type === 'Number'
        ? <input {...common} type="number" step={question.minimumNumberValue != null || question.maximumNumberValue != null ? 1 : 'any'} min={question.minimumNumberValue ?? undefined} max={question.maximumNumberValue ?? undefined} value={typeof value === 'string' ? value : ''} onChange={event => onChange(event.target.value)} />
        : <input {...common} type="text" maxLength={configuration.answerLimits.maxShortTextAnswerCharacters} value={typeof value === 'string' ? value : ''} onChange={event => onChange(event.target.value)} />}
    {question.type === 'ShortText' ? <p className="form-field__help">{t('guest.maxChars', { count: configuration.answerLimits.maxShortTextAnswerCharacters })}</p> : null}
    {question.type === 'LongText' ? <p className="form-field__help">{t('guest.maxChars', { count: configuration.answerLimits.maxLongTextAnswerCharacters })}</p> : null}
    {question.type === 'Number' && (question.minimumNumberValue != null || question.maximumNumberValue != null)
      ? <p className="form-field__help">{question.minimumNumberValue != null ? t('guest.minimum', { value: question.minimumNumberValue }) : ''}{question.minimumNumberValue != null && question.maximumNumberValue != null ? ' · ' : ''}{question.maximumNumberValue != null ? t('guest.maximum', { value: question.maximumNumberValue }) : ''} · {t('guest.integer')}</p>
      : null}
    {errorText}
  </div>
}

function validateAnswers(questions: PublicRsvpQuestion[], answers: AnswerState, configuration: PublicRsvpConfiguration, t: TFunction) {
  const errors: Record<string, string> = {}
  for (const question of questions) {
    const value = answers[question.id]
    if (question.isRequired && isEmpty(value)) {
      errors[question.id] = t('guest.required')
      continue
    }
    if (isEmpty(value)) continue
    if (question.type === 'ShortText' && String(value).length > configuration.answerLimits.maxShortTextAnswerCharacters) errors[question.id] = t('guest.maxChars', { count: configuration.answerLimits.maxShortTextAnswerCharacters })
    if (question.type === 'LongText' && String(value).length > configuration.answerLimits.maxLongTextAnswerCharacters) errors[question.id] = t('guest.maxChars', { count: configuration.answerLimits.maxLongTextAnswerCharacters })
    if (question.type === 'Number') {
      const parsed = parseDotNetDecimal(String(value))
      const minimum = question.minimumNumberValue ?? null
      const maximum = question.maximumNumberValue ?? null
      if (parsed === null) errors[question.id] = t('guest.outOfRange')
      else if ((minimum !== null || maximum !== null) && parsed.scale !== 0) errors[question.id] = t('guest.integer')
      else if (minimum !== null && compareDecimalToInteger(parsed, minimum) < 0 || maximum !== null && compareDecimalToInteger(parsed, maximum) > 0) {
        errors[question.id] = t('guest.range', { minimum: minimum ?? '…', maximum: maximum ?? '…' })
      }
    }
    if (question.type === 'MultipleChoice' && Array.isArray(value) && value.length > configuration.answerLimits.maxMultipleChoiceSelections) errors[question.id] = t('guest.maxOptions', { count: configuration.answerLimits.maxMultipleChoiceSelections })
  }
  return errors
}

function toAnswers(questions: PublicRsvpQuestion[], values: AnswerState): PublicRsvpAnswerInput[] {
  return questions.filter(question => !isEmpty(values[question.id])).map(question => {
    const value = values[question.id]!
    if (question.type === 'Number') return { questionId: question.id, numberValue: String(value) }
    if (question.type === 'YesNo') return { questionId: question.id, booleanValue: value as boolean }
    if (question.type === 'SingleChoice') return { questionId: question.id, selectedOptionIds: [value as string] }
    if (question.type === 'MultipleChoice') return { questionId: question.id, selectedOptionIds: value as string[] }
    return { questionId: question.id, textValue: value as string }
  })
}

function answersFromSubmission(questions: PublicRsvpQuestion[], answers: PublicRsvpGuestAnswer[]): AnswerState {
  const knownIds = new Set(questions.map(question => question.id))
  return Object.fromEntries(answers.filter(answer => knownIds.has(answer.questionId)).map(answer => {
    const question = questions.find(item => item.id === answer.questionId)!
    if (question.type === 'Number') return [answer.questionId, answer.numberValue ?? '']
    if (question.type === 'YesNo') return [answer.questionId, answer.booleanValue ?? '']
    if (question.type === 'MultipleChoice') return [answer.questionId, answer.selectedOptionIds ?? []]
    if (question.type === 'SingleChoice') return [answer.questionId, answer.selectedOptionIds?.[0] ?? '']
    return [answer.questionId, answer.textValue ?? '']
  }))
}

function isEmpty(value: AnswerState[string] | undefined): boolean {
  return value === undefined || value === '' || (Array.isArray(value) && value.length === 0)
}

function parseDotNetDecimal(value: string): ExactDecimal | null {
  const normalized = normalizeRsvpNumberLexeme(value)
  if (normalized === null) return null
  const match = /^(-?)([0-9]+)(?:[.]([0-9]+))?(?:e([+-]?[0-9]+))?$/.exec(normalized)
  if (!match) return null

  const exponentText = match[4] ?? '0'
  const exponentDigits = exponentText.replace(/^[+-]?0*/, '') || '0'
  if (exponentDigits.length > 4) return null
  const exponent = Number(exponentText)
  if (!Number.isSafeInteger(exponent) || Math.abs(exponent) > 1000) return null

  let digits = `${match[2]}${match[3] ?? ''}`.replace(/^0+/, '')
  if (!digits) return { coefficient: 0n, scale: 0 }
  let scale = (match[3]?.length ?? 0) - exponent
  if (scale < 0) {
    if (digits.length - scale > 29) return null
    digits += '0'.repeat(-scale)
    scale = 0
  }
  const trailingZeros = digits.length - digits.replace(/0+$/, '').length
  const removableZeros = Math.min(scale, trailingZeros)
  if (removableZeros > 0) {
    digits = digits.slice(0, -removableZeros)
    scale -= removableZeros
  }
  if (scale > 28 || digits.length > 29) return null
  let coefficient = BigInt(digits)
  if (coefficient > maxDecimalCoefficient) return null
  if (match[1] === '-') coefficient = -coefficient
  return { coefficient, scale }
}

function compareDecimalToInteger(value: ExactDecimal, integer: number): number {
  const scale = Math.max(value.scale, 0)
  const left = value.coefficient * 10n ** BigInt(scale - value.scale)
  const right = BigInt(integer) * 10n ** BigInt(scale)
  return left < right ? -1 : left > right ? 1 : 0
}

function isNotFound(error: unknown): boolean {
  return error instanceof ApiRequestError && error.status === 404
}
