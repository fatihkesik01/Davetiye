import type { ReactNode } from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { SiteHeader } from '../../components/layout/SiteHeader'
import { Avatar } from './avatars'
import { AvatarPicker } from './AvatarPicker'
import { AccountPreferencesProvider } from './preferences'
import { STORAGE_KEY, useAccountPreferences } from './preferencesContext'

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const base = { locale: 'tr', colorTheme: 'kutlio', appearance: 'system' }

function stubApi({ server = { ...base, avatar: null } as Record<string, unknown>, session = { authenticated: true, access: 'creator' } as unknown, echoAvatar = true } = {}) {
  const puts: Record<string, unknown>[] = []
  const fetch = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/v1/auth/session') return Promise.resolve(json(session))
    if (url === '/api/v1/antiforgery/token') return Promise.resolve(json({ token: 't' }))
    if (url === '/api/v1/account/preferences') {
      if (init?.method === 'PUT') {
        const body = JSON.parse(String(init.body)) as Record<string, unknown>
        puts.push(body)
        if (!echoAvatar) return Promise.resolve(json({ locale: body.locale, colorTheme: body.colorTheme, appearance: body.appearance }))
        return Promise.resolve(json(body))
      }
      return Promise.resolve(json(server))
    }
    return Promise.reject(new Error(`Unexpected request: ${url}`))
  })
  vi.stubGlobal('fetch', fetch)
  return { puts }
}

function Hydrate({ children }: { children: ReactNode }) {
  const { hydrate, preferences } = useAccountPreferences()
  return <><button onClick={() => void hydrate()}>hydrate</button><span data-testid="avatar">{String(preferences.avatar)}</span>{children}</>
}
const renderPicker = async () => {
  render(<AccountPreferencesProvider><Hydrate><AvatarPicker /></Hydrate></AccountPreferencesProvider>)
  fireEvent.click(screen.getByText('hydrate'))
  await waitFor(() => expect((screen.getAllByRole('radio')[0] as HTMLInputElement).disabled).toBe(false))
}
const cleanup = () => { vi.restoreAllMocks(); vi.unstubAllGlobals(); localStorage.clear(); document.documentElement.lang = 'tr' }

describe('Avatar', () => {
  it('renders a decorative svg for a key and the generic icon for null', () => {
    const { container, rerender } = render(<Avatar avatarKey="sunny" size={40} />)
    const svg = container.querySelector('svg[data-avatar="sunny"]')
    expect(svg?.getAttribute('aria-hidden')).toBe('true')
    expect(svg?.getAttribute('width')).toBe('40')
    expect(container.querySelector('[style]')).toBeNull()
    rerender(<Avatar avatarKey={null} />)
    expect(container.querySelector('[data-avatar="none"] svg')).not.toBeNull()
    rerender(<Avatar avatarKey="mint" label="Nane" />)
    expect(screen.getByRole('img', { name: 'Nane' })).toBeTruthy()
  })
})

describe('AvatarPicker', () => {
  afterEach(cleanup)

  it('renders twelve named options in one radio group plus a remove action and exposes the selected state', async () => {
    stubApi({ server: { ...base, avatar: 'berry' } })
    await renderPicker()
    const group = screen.getByRole('group', { name: 'Profil resmi seçin' })
    const radios = within(group).getAllByRole('radio') as HTMLInputElement[]
    expect(radios).toHaveLength(12)
    expect(new Set(radios.map(radio => radio.name)).size).toBe(1)
    await waitFor(() => expect(radios.filter(radio => radio.checked).map(radio => radio.value)).toEqual(['berry']))
    expect(within(group).getByRole('radio', { name: 'Böğürtlen' })).toBeTruthy()
    expect(within(group).getByRole('button', { name: 'Avatarı kaldır' })).toBeTruthy()
  })

  it('saves the chosen key with all four fields, and null on remove', async () => {
    const { puts } = stubApi()
    await renderPicker()
    expect((screen.getByRole('button', { name: 'Avatarı kaldır' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Nane' }))
    await waitFor(() => expect(puts).toHaveLength(1))
    expect(puts[0]).toEqual({ ...base, avatar: 'mint' })
    await waitFor(() => expect(screen.getByTestId('avatar').textContent).toBe('mint'))
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}').avatar).toBe('mint')
    fireEvent.click(await screen.findByRole('button', { name: 'Avatarı kaldır' }))
    await waitFor(() => expect(puts).toHaveLength(2))
    expect(puts[1]).toEqual({ ...base, avatar: null })
    await waitFor(() => expect(screen.getByTestId('avatar').textContent).toBe('null'))
  })

  it('keeps the chosen avatar when an older API ignores the field', async () => {
    const { puts } = stubApi({ echoAvatar: false })
    await renderPicker()
    fireEvent.click(screen.getByRole('radio', { name: 'Gece' }))
    await waitFor(() => expect(puts).toHaveLength(1))
    await waitFor(() => expect(screen.getByTestId('avatar').textContent).toBe('night'))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(screen.getByTestId('avatar').textContent).toBe('night')
  })
})

describe('SiteHeader avatar', () => {
  afterEach(cleanup)
  const header = (zone: 'creator' | 'admin' = 'creator') => render(<AccountPreferencesProvider><SiteHeader zone={zone} /></AccountPreferencesProvider>)

  it('shows the hydrated avatar on the account button', async () => {
    stubApi({ server: { ...base, avatar: 'forest' } })
    const view = header()
    expect(view.container.querySelector('.site-account [data-avatar="none"]')).not.toBeNull()
    await waitFor(() => expect(view.container.querySelector('.site-account [data-avatar="forest"]')).not.toBeNull())
  })

  it('shows the generic icon when no avatar is chosen', async () => {
    const { puts } = stubApi()
    const view = header()
    await waitFor(() => expect(vi.mocked(fetch).mock.calls.some(([url]) => String(url) === '/api/v1/account/preferences')).toBe(true))
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(view.container.querySelector('.site-account [data-avatar="none"]')).not.toBeNull()
    expect(puts).toHaveLength(0)
  })

  it('does not show a stored avatar before hydration and clears it on a 401', async () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...base, avatar: 'rose' }))
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => String(input) === '/api/v1/auth/session'
      ? Promise.resolve(json({ authenticated: true, access: 'creator' })) : Promise.resolve(json({ status: 401 }, 401))))
    const view = header()
    expect(view.container.querySelector('.site-account [data-avatar="rose"]')).toBeNull()
    await waitFor(() => expect(localStorage.getItem(STORAGE_KEY)).toBeNull())
    expect(view.container.querySelector('.site-account [data-avatar="rose"], .preferences-drawer__avatar[data-avatar="rose"]')).toBeNull()
  })

  it('offers the picker in the drawer for a Super Admin as well', async () => {
    stubApi({ session: { authenticated: true, access: 'mfa-complete-super-admin' } })
    HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', '') }
    header('admin')
    fireEvent.click(screen.getByRole('button', { name: 'Hesabım, tema ve dil' }))
    const drawer = document.querySelector('dialog.preferences-drawer') as HTMLDialogElement
    await waitFor(() => expect(within(drawer).getAllByRole('radio', { hidden: true })).toHaveLength(12))
  })
})
