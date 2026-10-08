import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import {
  ApiRequestError,
  type CreatorMemoryConfiguration,
  type DavetiyeApiClient,
  type MemoryVisibility,
} from '../../api/generated/client'
import { CreatorMemoriesModeration } from './CreatorMemoriesModeration'

interface Props {
  api: DavetiyeApiClient
  invitationId: string
}

export function MemoriesManagementPanel({ api, invitationId }: Props) {
  const { t } = useTranslation()
  const [configuration, setConfiguration] = useState<CreatorMemoryConfiguration | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [isEnabled, setIsEnabled] = useState(false)
  const [visibility, setVisibility] = useState<MemoryVisibility>('CreatorOnly')

  useEffect(() => {
    const controller = new AbortController()
    void Promise.resolve().then(() => api.getInvitationMemoriesConfiguration(invitationId, controller.signal)).then(result => {
      if (controller.signal.aborted) return
      adopt(result)
      setLoading(false)
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setFailed(true)
      setLoading(false)
    })
    return () => controller.abort()
  }, [api, invitationId, loadAttempt])

  function adopt(result: CreatorMemoryConfiguration) {
    setConfiguration(result)
    setIsEnabled(result.isEnabled)
    setVisibility(result.visibility === 'Public' ? 'Public' : 'CreatorOnly')
  }

  const dirty = Boolean(configuration) && (isEnabled !== configuration!.isEnabled || visibility !== configuration!.visibility)

  const save = async () => {
    if (!configuration || busy || !dirty) return
    setBusy(true)
    setMessage(t('creatorFormsUi.memories.saving'))
    try {
      const csrf = await api.getAntiforgeryToken()
      // The revision in the response is authoritative: it can advance by more than one.
      const updated = await api.setInvitationMemoriesConfiguration(invitationId, {
        expectedRevision: configuration.revision, isEnabled, visibility,
      }, csrf)
      adopt(updated)
      setMessage(t('creatorFormsUi.memories.saved'))
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        try {
          adopt(await api.getInvitationMemoriesConfiguration(invitationId))
          setMessage(t('creatorFormsUi.memories.conflict'))
        } catch {
          setMessage(t('creatorFormsUi.memories.reloadFailed'))
        }
      } else if (error instanceof ApiRequestError && error.status === 400) setMessage(t('creatorFormsUi.memories.invalid'))
      else if (error instanceof ApiRequestError && error.status === 404) setMessage(t('creatorFormsUi.memories.inaccessible'))
      else if (error instanceof ApiRequestError && error.status === 429) setMessage(t('creatorFormsUi.memories.rateLimited'))
      else setMessage(t('creatorFormsUi.memories.failedSave'))
    } finally {
      setBusy(false)
    }
  }

  const titleId = `memories-panel-title-${invitationId}`
  const limits = configuration?.inputLimits

  return <section className="rsvp-panel memories-panel" aria-labelledby={titleId}>
    <div className="rsvp-panel__heading"><div><h3 id={titleId}>{t('creatorFormsUi.memories.title')}</h3><p>{t('creatorFormsUi.memories.intro')}</p></div></div>
    {loading ? <p role="status">{t('creatorFormsUi.memories.loading')}</p> : null}
    {failed ? <div className="inline-alert" role="alert"><p>{t('creatorFormsUi.memories.failed')}</p>
      <button type="button" className="button button--secondary" onClick={() => { setLoading(true); setFailed(false); setLoadAttempt(value => value + 1) }}>{t('creatorFormsUi.memories.retry')}</button></div> : null}
    {configuration ? <>
      <label className="rsvp-panel__toggle"><input type="checkbox" checked={isEnabled} disabled={busy} onChange={event => setIsEnabled(event.target.checked)} /> {t('creatorFormsUi.memories.toggle')}</label>
      <fieldset className="memories-panel__visibility">
        <legend>{t('creatorFormsUi.memories.visibility')}</legend>
        <label className="memories-panel__option">
          <input type="radio" name={`memories-visibility-${invitationId}`} value="CreatorOnly" checked={visibility === 'CreatorOnly'} disabled={busy} onChange={() => setVisibility('CreatorOnly')} />
          <span><strong>{t('creatorFormsUi.memories.creatorOnly')}</strong><span className="memories-panel__hint">{t('creatorFormsUi.memories.creatorOnlyHelp')}</span></span>
        </label>
        <label className="memories-panel__option">
          <input type="radio" name={`memories-visibility-${invitationId}`} value="Public" checked={visibility === 'Public'} disabled={busy} onChange={() => setVisibility('Public')} />
          <span><strong>{t('creatorFormsUi.memories.public')}</strong><span className="memories-panel__hint">{t('creatorFormsUi.memories.publicHelp')}</span></span>
        </label>
        <p className="rsvp-panel__privacy">{t('creatorFormsUi.memories.visibilityHelp')}</p>
      </fieldset>
      <p className="rsvp-panel__privacy">{t('creatorFormsUi.memories.availability')}</p>
      {limits ? <p className="rsvp-panel__privacy">{t('creatorFormsUi.memories.limits', { nameMax: limits.maxDisplayNameCharacters, textMax: limits.maxTextCharacters, memoryMax: limits.maxMemoriesPerInvitation })}</p> : null}
      <div className="button-row"><button type="button" className="button button--primary" disabled={busy || !dirty} onClick={() => void save()}>{t('common.save')}</button></div>
      <CreatorMemoriesModeration api={api} invitationId={invitationId} />
    </> : null}
    <p role="status" aria-live="polite">{message}</p>
  </section>
}
