import { type ReactNode, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiRequestError, DavetiyeApiClient } from '../../api/generated/client'
import { ChoiceGroup } from '../../components/ui/ChoiceGroup'
import { AvatarPicker } from './AvatarPicker'
import { appearanceIcons } from './preferenceIcons'
import { PreferenceStatus } from './PreferenceStatus'
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
      const response: unknown = await api.updateAccountPreferences(next, token)
      // An older API ignores the avatar field and omits it from the response: keep the chosen value instead of dropping it.
      const saved = parsePreferences(response && typeof response === 'object' && !('avatar' in response) ? { ...response, avatar: next.avatar } : response)
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

const themes = ['kutlio', 'sage', 'rose', 'ocean', 'plum', 'gold'] as const
const appearances = ['light', 'dark', 'system'] as const
const locales = ['tr', 'en'] as const

interface AccountPreferenceControlsProps {
  /** Adds the compact avatar picker (account drawer). The settings page renders its own profile-picture card. */
  showAvatar?: boolean
  /** `drawer`: small group labels and tight spacing; `page`: the roomier settings-card layout. */
  variant?: 'drawer' | 'page'
  /** The drawer shows the save status in its footer so it stays visible while the body scrolls. */
  showStatus?: boolean
}

export function AccountPreferenceControls({ showAvatar = false, variant = 'page', showStatus = true }: AccountPreferenceControlsProps) {
  const { t } = useTranslation()
  const { preferences, ready, save } = useAccountPreferences()
  const update = (change: Partial<AccountPreferences>) => { void save({ ...preferences, ...change }) }
  return <div className={`preference-controls preference-controls--${variant}`} aria-busy={!ready}>
    <ChoiceGroup variant="segmented" legend={t('preferences.language')} disabled={!ready} value={preferences.locale} onChange={locale => update({ locale })}
      options={locales.map(locale => ({ value: locale, label: t(`preferences.languages.${locale}`), lang: locale, badge: locale.toUpperCase() }))} />
    <ChoiceGroup variant="swatches" legend={t('preferences.colorTheme')} disabled={!ready} value={preferences.colorTheme} onChange={colorTheme => update({ colorTheme })}
      options={themes.map(theme => ({ value: theme, label: t(`preferences.themes.${theme}`), swatch: <span className="preference-swatch" data-swatch={theme} aria-hidden="true" /> }))} />
    <ChoiceGroup variant="segmented" legend={variant === 'drawer' ? t('preferences.appearance') : t('preferences.appearanceMode')} disabled={!ready} value={preferences.appearance} onChange={appearance => update({ appearance })}
      options={appearances.map(appearance => ({ value: appearance, label: t(`preferences.appearances.${appearance}`), icon: appearanceIcons[appearance] }))} />
    {showAvatar ? <AvatarPicker compact /> : null}
    {showStatus ? <PreferenceStatus /> : null}
  </div>
}
