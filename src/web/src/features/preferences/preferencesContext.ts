import { createContext, useContext } from 'react'
import type { AvatarKey } from '../../api/generated/client'

export type Locale = 'tr' | 'en'
export type ColorTheme = 'kutlio' | 'sage' | 'rose' | 'ocean' | 'plum'
export type Appearance = 'system' | 'light' | 'dark'
export type { AvatarKey }
export interface AccountPreferences { locale: Locale; colorTheme: ColorTheme; appearance: Appearance; avatar: AvatarKey | null }

export const AVATAR_KEYS: readonly AvatarKey[] = ['sunny', 'mint', 'berry', 'sky', 'coral', 'lilac', 'amber', 'forest', 'night', 'rose', 'slate', 'peach']

export function isAvatarKey(value: unknown): value is AvatarKey {
  return typeof value === 'string' && (AVATAR_KEYS as readonly string[]).includes(value)
}

/** Light is the default; "system" and "dark" stay valid saved choices that are always respected. */
export const DEFAULT_PREFERENCES: AccountPreferences = { locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: null }
export const STORAGE_KEY = 'kutlio:account-preferences'
const validLocales = ['tr', 'en']
const validThemes = ['kutlio', 'sage', 'rose', 'ocean', 'plum']
const validAppearances = ['system', 'light', 'dark']

/**
 * Validates server and localStorage data against the allow-lists. A missing `avatar` (older API or an
 * older stored copy) is treated as "none"; any other non-allow-listed value rejects the whole object.
 */
export function parsePreferences(value: unknown): AccountPreferences | null {
  if (!value || typeof value !== 'object') return null
  const candidate = value as Record<string, unknown>
  const { locale, colorTheme, appearance, avatar } = candidate
  if (typeof locale !== 'string' || !validLocales.includes(locale)) return null
  if (typeof colorTheme !== 'string' || !validThemes.includes(colorTheme)) return null
  if (typeof appearance !== 'string' || !validAppearances.includes(appearance)) return null
  if (avatar !== undefined && avatar !== null && !isAvatarKey(avatar)) return null
  return { locale, colorTheme, appearance, avatar: (avatar ?? null) as AvatarKey | null } as AccountPreferences
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
