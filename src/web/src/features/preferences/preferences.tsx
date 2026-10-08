import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiRequestError, DavetiyeApiClient } from '../../api/generated/client'
import {
  type AccountPreferences, applyPreferences, clearLocalAccountPreferences, DEFAULT_PREFERENCES, parsePreferences,
  PreferencesContext, type PreferencesContextValue, readLocal, STORAGE_KEY, useAccountPreferences,
} from './preferencesContext'

export function AccountPreferencesProvider({ children }: { children: ReactNode }) {
  const { i18n } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [preferences, setPreferences] = useState(readLocal)
  const [ready, setReady] = useState(false)
  const [saveState, setSaveState] = useState<PreferencesContextValue['saveState']>('idle')
  const requestVersion = useRef(0)
  const saveQueue = useRef<Promise<void>>(Promise.resolve())
  const hydrationRequest = useRef<Promise<void> | null>(null)
  const hydrated = useRef(false)

  useEffect(() => { applyPreferences(preferences); void i18n.changeLanguage(preferences.locale) }, [i18n, preferences])

  const hydrate = useCallback(async () => {
    if (hydrated.current) return
    if (hydrationRequest.current) return hydrationRequest.current
    const version = requestVersion.current
    const request = (async () => {
      let expired = false
      try {
        const serverPreferences = parsePreferences(await api.getAccountPreferences())
        if (!serverPreferences) throw new Error('Invalid preferences response')
        if (version !== requestVersion.current) return
        setPreferences(serverPreferences)
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(serverPreferences)) } catch { /* Server preference still applies without local storage. */ }
      } catch (error) {
        if (version !== requestVersion.current) return
        if (error instanceof ApiRequestError && error.status === 401) {
          // The session ended: never keep the previous user's preference on a shared browser.
          expired = true
          clearLocalAccountPreferences()
          setPreferences(DEFAULT_PREFERENCES)
        } else setPreferences(readLocal())
      } finally {
        if (version === requestVersion.current) { hydrated.current = !expired; setReady(true) }
      }
    })()
    hydrationRequest.current = request
    try { await request } finally { if (hydrationRequest.current === request) hydrationRequest.current = null }
  }, [api])

  const save = useCallback(async (next: AccountPreferences) => {
    const version = ++requestVersion.current
    setPreferences(next)
    setSaveState('saving')
    let localSaved = false
    try { localStorage.setItem(STORAGE_KEY, JSON.stringify(next)); localSaved = true } catch { /* Browser storage is optional. */ }
    const pendingSave = saveQueue.current.then(async () => {
      const token = await api.getAntiforgeryToken()
      const saved = parsePreferences(await api.updateAccountPreferences(next, token))
      if (version === requestVersion.current && saved) {
        setPreferences(saved)
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(saved)) } catch { /* Browser storage is optional. */ }
      }
      if (version === requestVersion.current) setSaveState('saved')
    })
    saveQueue.current = pendingSave.catch(() => {})
    try { await pendingSave }
    catch { if (version === requestVersion.current) setSaveState(localSaved ? 'local' : 'error') }
    setReady(true)
  }, [api])

  const reset = useCallback(() => {
    ++requestVersion.current
    hydrationRequest.current = null
    hydrated.current = false
    setPreferences(DEFAULT_PREFERENCES)
    setReady(false)
    setSaveState('idle')
    clearLocalAccountPreferences()
  }, [])

  const value = useMemo(() => ({ preferences, ready, saveState, hydrate, save, reset }), [preferences, ready, saveState, hydrate, save, reset])
  return <PreferencesContext.Provider value={value}>{children}</PreferencesContext.Provider>
}

export function AccountPreferenceControls() {
  const { t } = useTranslation()
  const { preferences, ready, save, saveState } = useAccountPreferences()
  const update = (change: Partial<AccountPreferences>) => { void save({ ...preferences, ...change }) }
  return <div className="preference-controls" aria-busy={!ready}>
    <fieldset disabled={!ready}>
      <legend>{t('preferences.language')}</legend>
      <div className="preference-controls__choices">
        <button type="button" aria-pressed={preferences.locale === 'tr'} onClick={() => update({ locale: 'tr' })}>Türkçe</button>
        <button type="button" aria-pressed={preferences.locale === 'en'} onClick={() => update({ locale: 'en' })}>English</button>
      </div>
    </fieldset>
    <fieldset disabled={!ready}>
      <legend>{t('preferences.colorTheme')}</legend>
      <div className="preference-controls__themes">
        {(['kutlio', 'sage', 'rose', 'ocean', 'plum'] as const).map(theme => <button key={theme} type="button" className={`preference-theme preference-theme--${theme}`} aria-pressed={preferences.colorTheme === theme} onClick={() => update({ colorTheme: theme })}>
          <span aria-hidden="true" />{t(`preferences.themes.${theme}`)}
        </button>)}
      </div>
    </fieldset>
    <fieldset disabled={!ready}>
      <legend>{t('preferences.appearance')}</legend>
      <div className="preference-controls__choices">
        {(['system', 'light', 'dark'] as const).map(appearance => <button key={appearance} type="button" aria-pressed={preferences.appearance === appearance} onClick={() => update({ appearance })}>{t(`preferences.appearances.${appearance}`)}</button>)}
      </div>
    </fieldset>
    <p className="preference-controls__status" role="status" aria-live="polite">{!ready ? t('common.loading') : saveState === 'saving' ? t('preferences.saving') : saveState === 'saved' ? t('preferences.saved') : saveState === 'local' ? t('preferences.local') : saveState === 'error' ? t('error.title') : ''}</p>
  </div>
}
