import { createContext, useContext } from 'react'

export type Locale = 'tr' | 'en'
export type ColorTheme = 'kutlio' | 'sage' | 'rose' | 'ocean' | 'plum'
export type Appearance = 'system' | 'light' | 'dark'
export interface AccountPreferences { locale: Locale; colorTheme: ColorTheme; appearance: Appearance }

export const DEFAULT_PREFERENCES: AccountPreferences = { locale: 'tr', colorTheme: 'kutlio', appearance: 'system' }
export const STORAGE_KEY = 'kutlio:account-preferences'
const validLocales = ['tr', 'en']
const validThemes = ['kutlio', 'sage', 'rose', 'ocean', 'plum']
const validAppearances = ['system', 'light', 'dark']

export function parsePreferences(value: unknown): AccountPreferences | null {
  if (!value || typeof value !== 'object') return null
  const candidate = value as Partial<AccountPreferences>
  return validLocales.includes(candidate.locale ?? '') && validThemes.includes(candidate.colorTheme ?? '') && validAppearances.includes(candidate.appearance ?? '')
    ? candidate as AccountPreferences
    : null
}

export function readLocal(): AccountPreferences {
  try { return parsePreferences(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null')) ?? DEFAULT_PREFERENCES }
  catch { return DEFAULT_PREFERENCES }
}

export function clearLocalAccountPreferences() {
  try { localStorage.removeItem(STORAGE_KEY) } catch { /* Browser storage is optional. */ }
}

export function applyPreferences(preferences: AccountPreferences) {
  document.documentElement.lang = preferences.locale
  document.documentElement.dataset.colorTheme = preferences.colorTheme
  document.documentElement.dataset.appearance = preferences.appearance
}

export interface PreferencesContextValue {
  preferences: AccountPreferences
  ready: boolean
  saveState: 'idle' | 'saving' | 'saved' | 'local' | 'error'
  hydrate: () => Promise<void>
  save: (next: AccountPreferences) => Promise<void>
  reset: () => void
}
export const PreferencesContext = createContext<PreferencesContextValue | null>(null)

export function useAccountPreferences() {
  const value = useContext(PreferencesContext)
  if (!value) throw new Error('AccountPreferencesProvider is missing')
  return value
}

export function useOptionalAccountPreferences() {
  return useContext(PreferencesContext)
}
