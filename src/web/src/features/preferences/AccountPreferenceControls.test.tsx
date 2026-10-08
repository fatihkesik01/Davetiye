import type { ReactNode } from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AccountPreferenceControls, AccountPreferencesProvider } from './preferences'
import { useAccountPreferences } from './preferencesContext'

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })

function stubApi(server: Record<string, unknown>) {
  const puts: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/v1/antiforgery/token') return Promise.resolve(json({ token: 't' }))
    if (url === '/api/v1/account/preferences') {
      if (init?.method === 'PUT') { const body = JSON.parse(String(init.body)) as Record<string, unknown>; puts.push(body); return Promise.resolve(json(body)) }
      return Promise.resolve(json(server))
    }
    return Promise.reject(new Error(`Unexpected request: ${url}`))
  }))
  return puts
}

function Hydrate({ children }: { children: ReactNode }) {
  const { hydrate } = useAccountPreferences()
  return <><button onClick={() => void hydrate()}>hydrate</button>{children}</>
}

async function renderControls(variant: 'page' | 'drawer' = 'page') {
  render(<AccountPreferencesProvider><Hydrate><AccountPreferenceControls variant={variant} /></Hydrate></AccountPreferencesProvider>)
  fireEvent.click(screen.getByText('hydrate'))
  await waitFor(() => expect(screen.getAllByRole('radio')[0]?.matches(':disabled')).toBe(false))
}

describe('AccountPreferenceControls', () => {
  afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); localStorage.clear(); document.documentElement.lang = 'tr' })

  it('offers language, colour theme and appearance as three named radio groups', async () => {
    stubApi({ locale: 'tr', colorTheme: 'sage', appearance: 'dark', avatar: null })
    await renderControls('drawer')
    const language = screen.getByRole('radiogroup', { name: 'Dil' })
    expect(within(language).getAllByRole('radio').map(radio => (radio as HTMLInputElement).value)).toEqual(['tr', 'en'])
    expect(within(language).getByRole('radio', { name: 'Türkçe' })).toBeTruthy()
    expect(within(language).getByRole('radio', { name: 'English' })).toBeTruthy()

    const themes = screen.getByRole('radiogroup', { name: 'Renk teması' })
    expect(within(themes).getAllByRole('radio').map(radio => (radio as HTMLInputElement).value)).toEqual(['kutlio', 'sage', 'rose', 'ocean', 'plum'])
    expect(themes.querySelectorAll('.preference-swatch[data-swatch]')).toHaveLength(5)
    expect(themes.querySelector('[style]')).toBeNull()

    const appearance = screen.getByRole('radiogroup', { name: 'Görünüm' })
    expect(within(appearance).getAllByRole('radio').map(radio => (radio as HTMLInputElement).value)).toEqual(['light', 'dark', 'system'])
    expect(within(appearance).getAllByRole('radio').map(radio => radio.closest('label')?.textContent)).toEqual(['Açık', 'Koyu', 'Cihaz ayarı'])
    expect(appearance.querySelectorAll('svg[aria-hidden="true"]').length).toBeGreaterThanOrEqual(3)
    await waitFor(() => expect((within(themes).getByRole('radio', { name: 'Adaçayı' }) as HTMLInputElement).checked).toBe(true))
    expect((within(appearance).getByRole('radio', { name: 'Koyu' }) as HTMLInputElement).checked).toBe(true)
    expect((within(language).getByRole('radio', { name: 'Türkçe' }) as HTMLInputElement).checked).toBe(true)
  })

  it('names the appearance group differently on the settings page so it does not repeat the section heading', async () => {
    stubApi({ locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: null })
    await renderControls('page')
    expect(screen.getByRole('radiogroup', { name: 'Açık / koyu' })).toBeTruthy()
  })

  it('saves every choice with all four fields and keeps the other values', async () => {
    const puts = stubApi({ locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: 'berry' })
    await renderControls()
    fireEvent.click(screen.getByRole('radio', { name: 'Okyanus' }))
    await waitFor(() => expect(puts).toHaveLength(1))
    expect(puts[0]).toEqual({ locale: 'tr', colorTheme: 'ocean', appearance: 'light', avatar: 'berry' })
    fireEvent.click(screen.getByRole('radio', { name: 'Cihaz ayarı' }))
    await waitFor(() => expect(puts).toHaveLength(2))
    expect(puts[1]).toEqual({ locale: 'tr', colorTheme: 'ocean', appearance: 'system', avatar: 'berry' })
    fireEvent.click(screen.getByRole('radio', { name: 'English' }))
    await waitFor(() => expect(puts).toHaveLength(3))
    expect(puts[2]).toEqual({ locale: 'en', colorTheme: 'ocean', appearance: 'system', avatar: 'berry' })
    await waitFor(() => expect(screen.getByRole('radiogroup', { name: 'Language' })).toBeTruthy())
    expect(screen.getByRole('radio', { name: 'Device setting' })).toBeTruthy()
  })

  it('keeps the groups disabled until the saved preferences have loaded', () => {
    stubApi({ locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: null })
    render(<AccountPreferencesProvider><AccountPreferenceControls /></AccountPreferencesProvider>)
    for (const radio of screen.getAllByRole('radio')) expect(radio.matches(':disabled')).toBe(true)
  })
})
