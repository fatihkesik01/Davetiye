import { afterEach, describe, expect, it } from 'vitest'
import { AVATAR_KEYS, DEFAULT_PREFERENCES, parsePreferences, readLocal, STORAGE_KEY } from './preferencesContext'

const base = { locale: 'tr', colorTheme: 'kutlio', appearance: 'system' }

describe('parsePreferences avatar handling', () => {
  afterEach(() => localStorage.clear())

  it('accepts every allow-listed avatar key and null', () => {
    expect(AVATAR_KEYS).toHaveLength(12)
    for (const avatar of AVATAR_KEYS) expect(parsePreferences({ ...base, avatar })?.avatar).toBe(avatar)
    expect(parsePreferences({ ...base, avatar: null })?.avatar).toBeNull()
  })

  it('treats a missing avatar (older API) as none', () => {
    expect(parsePreferences(base)).toEqual({ ...base, avatar: null })
  })

  it('rejects unknown, padded, wrong-case and non-string avatars', () => {
    for (const avatar of ['unknown', ' sunny', 'sunny ', 'Sunny', 'SUNNY', '', 5, {}, []]) expect(parsePreferences({ ...base, avatar })).toBeNull()
  })

  it('rejects tampered localStorage and falls back to the defaults', () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...base, avatar: '<img src=x>' }))
    expect(readLocal()).toEqual(DEFAULT_PREFERENCES)
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...base, avatar: 'peach' }))
    expect(readLocal().avatar).toBe('peach')
    localStorage.setItem(STORAGE_KEY, JSON.stringify(base))
    expect(readLocal().avatar).toBeNull()
  })

  it('does not pass unknown extra fields through', () => {
    expect(Object.keys(parsePreferences({ ...base, avatar: null, extra: 'x' }) ?? {}).sort()).toEqual(['appearance', 'avatar', 'colorTheme', 'locale'])
  })
})
