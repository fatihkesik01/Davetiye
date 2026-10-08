import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AccountPreferencesProvider } from '../../features/preferences/preferences'
import { STORAGE_KEY } from '../../features/preferences/preferencesContext'
import { SiteHeader } from './SiteHeader'

const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const serverPreferences = { locale: 'en', colorTheme: 'ocean', appearance: 'dark' }

function stubApi({ session, preferences = json(serverPreferences) }: { session: unknown; preferences?: Response }) {
  const fetch = vi.fn((input: RequestInfo | URL) => {
    const url = String(input)
    if (url === '/api/v1/auth/session') return session instanceof Error ? Promise.reject(session) : Promise.resolve(json(session))
    if (url === '/api/v1/account/preferences') return Promise.resolve(preferences.clone())
    return Promise.reject(new Error(`Unexpected request: ${url}`))
  })
  vi.stubGlobal('fetch', fetch)
  return fetch
}

const sectionLinks = [{ href: '#how-it-works', label: 'Nasıl çalışır?' }, { href: '#faq', label: 'SSS' }]
const renderHeader = (zone: 'public' | 'creator' | 'admin' = 'public', withSections = false) => render(<AccountPreferencesProvider>
  <SiteHeader zone={zone} {...(withSections ? { sectionLinks, sectionLabel: 'Sayfa bölümleri' } : {})} />
</AccountPreferencesProvider>)
const requestedUrls = (fetch: ReturnType<typeof stubApi>) => fetch.mock.calls.map(([url]) => String(url))

describe('SiteHeader', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    localStorage.clear()
    document.documentElement.removeAttribute('data-color-theme')
    document.documentElement.removeAttribute('data-appearance')
    document.documentElement.lang = 'tr'
  })

  it('renders the anonymous bar immediately with a sign-in link and no preference or account controls', async () => {
    const fetch = stubApi({ session: { authenticated: false, access: 'none' } })
    renderHeader()
    expect(screen.getByRole('link', { name: 'Kutlio ana sayfa' }).getAttribute('href')).toBe('/')
    expect(screen.getByRole('link', { name: 'Giriş yap' }).getAttribute('href')).toBe('/giris')
    expect(within(screen.getByRole('navigation', { name: 'Ana gezinme' })).getByRole('link', { name: 'Şablonlar' }).getAttribute('href')).toBe('/sablonlar')
    await waitFor(() => expect(requestedUrls(fetch)).toContain('/api/v1/auth/session'))
    expect(screen.queryByRole('button', { name: /Hesabım/ })).toBeNull()
    expect(requestedUrls(fetch)).not.toContain('/api/v1/account/preferences')
  })

  it('treats a failed session request as anonymous', async () => {
    const fetch = stubApi({ session: new Error('offline') })
    renderHeader()
    await waitFor(() => expect(requestedUrls(fetch)).toContain('/api/v1/auth/session'))
    expect(screen.getByRole('link', { name: 'Giriş yap' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Panele git' })).toBeNull()
  })

  it('upgrades to the Creator links, the account button and server preferences without any personal data', async () => {
    const fetch = stubApi({ session: { authenticated: true, access: 'creator', email: 'ada@example.com', displayName: 'Ada Lovelace' } })
    const view = renderHeader()
    expect(screen.getByRole('link', { name: 'Giriş yap' })).toBeTruthy()
    const panel = await screen.findByRole('link', { name: 'Panele git' })
    expect(panel.getAttribute('href')).toBe('/panel/davetiyeler')
    expect(screen.queryByRole('link', { name: 'Giriş yap' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Hesabım, tema ve dil' })).toBeTruthy()
    await waitFor(() => expect(document.documentElement.dataset.colorTheme).toBe('ocean'))
    expect(document.documentElement.dataset.appearance).toBe('dark')
    expect(document.documentElement.lang).toBe('en')
    expect(requestedUrls(fetch).filter(url => url === '/api/v1/auth/session')).toHaveLength(1)
    expect(view.container.textContent).not.toContain('ada@example.com')
    expect(view.container.textContent).not.toContain('Ada')
  })

  it('opens the shared preferences drawer from the account button and links to account settings for Creators', async () => {
    stubApi({ session: { authenticated: true, access: 'creator' }, preferences: json({ locale: 'tr', colorTheme: 'sage', appearance: 'light' }) })
    // jsdom does not implement modal dialogs.
    HTMLDialogElement.prototype.showModal = function showModal(this: HTMLDialogElement) { this.setAttribute('open', '') }
    renderHeader()
    fireEvent.click(await screen.findByRole('button', { name: 'Hesabım, tema ve dil' }))
    const drawer = document.querySelector('dialog.preferences-drawer') as HTMLDialogElement
    expect(drawer).not.toBeNull()
    expect(within(drawer).getByText('Tema')).toBeTruthy()
    for (const group of ['Dil', 'Tema', 'Görünüm']) expect(within(drawer).getByRole('radiogroup', { name: group, hidden: true })).toBeTruthy()
    expect(within(drawer).getByRole('group', { name: 'Profil resmi', hidden: true })).toBeTruthy()
    const heading = drawer.querySelector('.preferences-drawer__heading') as HTMLElement
    expect(within(heading).getByRole('heading', { name: 'Hesabım', hidden: true })).toBeTruthy()
    expect(within(heading).getByRole('link', { name: 'Hesap tercihleri', hidden: true })).toBeTruthy()
    expect(within(heading).getByRole('button', { name: 'Güvenli çıkış yap', hidden: true })).toBeTruthy()
    const accountLink = within(heading).getByRole('link', { name: 'Hesap tercihleri', hidden: true })
    const logout = within(heading).getByRole('button', { name: 'Güvenli çıkış yap', hidden: true })
    for (const [control, label] of [[accountLink, 'Hesap tercihleri'], [logout, 'Güvenli çıkış yap']] as const) {
      expect(control.textContent).toBe('')
      expect(control.getAttribute('data-tooltip')).toBe(label)
      expect(control.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true')
    }
    const footer = drawer.querySelector('.preferences-drawer__footer') as HTMLElement
    expect(within(footer).queryByRole('link', { name: 'Hesap tercihleri', hidden: true })).toBeNull()
    expect(within(footer).queryByRole('button', { name: 'Güvenli çıkış yap', hidden: true })).toBeNull()
    expect(drawer.querySelector('.preferences-drawer__body')?.contains(heading)).toBe(false)
    expect(within(drawer).getByRole('link', { name: 'Hesap tercihleri', hidden: true }).getAttribute('href')).toBe('/panel/hesap')
    expect(within(drawer).getByRole('button', { name: 'Güvenli çıkış yap', hidden: true })).toBeTruthy()
  })

  it('shows the administration link for a Super Admin with completed MFA and no Creator account link', async () => {
    stubApi({ session: { authenticated: true, access: 'mfa-complete-super-admin' } })
    renderHeader()
    expect((await screen.findByRole('link', { name: 'Yönetim paneli' })).getAttribute('href')).toBe('/admin')
    expect(screen.queryByRole('link', { name: 'Panele git' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'Hesap tercihleri', hidden: true })).toBeNull()
  })

  it('points an admin who still has to finish MFA enrollment at the setup page', async () => {
    stubApi({ session: { authenticated: true, access: 'mfa-setup-required-super-admin' } })
    renderHeader()
    expect((await screen.findByRole('link', { name: 'Yönetici güvenliğini tamamla' })).getAttribute('href')).toBe('/admin/mfa/setup')
  })

  it('uses the same brand and controls in the Creator and Admin zones with their own navigation', async () => {
    stubApi({ session: { authenticated: true, access: 'creator' } })
    const creator = renderHeader('creator')
    expect(screen.getByRole('link', { name: 'Kutlio ana sayfa' })).toBeTruthy()
    expect(within(screen.getByRole('navigation', { name: 'Davetiye sahibi menüsü' })).getAllByRole('link')).toHaveLength(4)
    expect(screen.getByRole('button', { name: 'Hesabım, tema ve dil' })).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Panele git' })).toBeNull()
    creator.unmount()

    renderHeader('admin')
    expect(screen.getByRole('link', { name: 'Kutlio ana sayfa' })).toBeTruthy()
    expect(within(screen.getByRole('navigation', { name: 'Yönetim bölümleri' })).getAllByRole('link')).toHaveLength(7)
    expect(screen.getByRole('button', { name: 'Hesabım, tema ve dil' })).toBeTruthy()
  })

  it('clears the previous user preference from this browser when the preferences request returns 401', async () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ locale: 'en', colorTheme: 'plum', appearance: 'dark' }))
    stubApi({ session: { authenticated: true, access: 'creator' }, preferences: json({ status: 401 }, 401) })
    renderHeader()
    await waitFor(() => expect(localStorage.getItem(STORAGE_KEY)).toBeNull())
    await waitFor(() => expect(document.documentElement.dataset.colorTheme).toBe('kutlio'))
    expect(document.documentElement.dataset.appearance).toBe('light')
    expect(document.documentElement.lang).toBe('tr')
  })

  it('keeps the local preference when the preferences request fails for a non-authentication reason', async () => {
    const stored = { locale: 'en', colorTheme: 'plum', appearance: 'dark' }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
    stubApi({ session: { authenticated: true, access: 'creator' }, preferences: json({ status: 503 }, 503) })
    renderHeader()
    await waitFor(() => expect(document.documentElement.dataset.colorTheme).toBe('plum'))
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null')).toEqual(stored)
  })

  describe('navigation menu and sticky behaviour', () => {
    const menuButton = () => screen.getByRole('button', { name: 'Menü' })
    const menu = () => document.getElementById('site-menu') as HTMLElement

    afterEach(() => { window.history.pushState({}, '', '/'); Object.defineProperty(window, 'scrollY', { value: 0, configurable: true }) })

    it('exposes a disclosure button that controls the navigation panel and toggles aria-expanded', async () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      renderHeader()
      expect(menuButton().getAttribute('aria-controls')).toBe('site-menu')
      expect(menuButton().getAttribute('aria-expanded')).toBe('false')
      expect(menu().dataset.open).toBe('false')
      fireEvent.click(menuButton())
      expect(menuButton().getAttribute('aria-expanded')).toBe('true')
      expect(menu().dataset.open).toBe('true')
      expect(within(menu()).getByRole('link', { name: 'Şablonlar' })).toBeTruthy()
      fireEvent.click(menuButton())
      expect(menuButton().getAttribute('aria-expanded')).toBe('false')
      await waitFor(() => expect(screen.getByRole('link', { name: 'Giriş yap' })).toBeTruthy())
    })

    it('puts the menu button before the brand and the links in the tab order, with the sign-in action staying outside the panel', () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      renderHeader()
      const order = Array.from(document.querySelectorAll('.site-header__inner > *'))
      expect(order[0]).toBe(menuButton())
      expect(order[1]).toBe(screen.getByRole('link', { name: 'Kutlio ana sayfa' }))
      expect(order[2]).toBe(menu())
      expect(menu().contains(screen.getByRole('link', { name: 'Giriş yap' }))).toBe(false)
    })

    it('closes on Escape and returns focus to the menu button', () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      renderHeader()
      fireEvent.click(menuButton())
      const link = within(menu()).getByRole('link', { name: 'Şablonlar' })
      link.focus()
      fireEvent.keyDown(link, { key: 'Escape' })
      expect(menuButton().getAttribute('aria-expanded')).toBe('false')
      expect(document.activeElement).toBe(menuButton())
    })

    it('closes when a menu link is chosen and when the route changes', () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      renderHeader()
      fireEvent.click(menuButton())
      fireEvent.click(within(menu()).getByRole('link', { name: 'Şablonlar' }))
      expect(menuButton().getAttribute('aria-expanded')).toBe('false')
      expect(window.location.pathname).toBe('/sablonlar')

      fireEvent.click(menuButton())
      expect(menuButton().getAttribute('aria-expanded')).toBe('true')
      act(() => { window.history.pushState({}, '', '/gizlilik'); window.dispatchEvent(new PopStateEvent('popstate')) })
      expect(menuButton().getAttribute('aria-expanded')).toBe('false')
    })

    it('closes when something outside the bar is pressed', () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      renderHeader()
      fireEvent.click(menuButton())
      fireEvent.pointerDown(document.body)
      expect(menuButton().getAttribute('aria-expanded')).toBe('false')
    })

    it('includes the landing in-page section anchors in the same panel, after the site links', () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      renderHeader('public', true)
      const links = within(menu()).getAllByRole('link').map(link => [link.textContent, link.getAttribute('href')])
      expect(links).toEqual([['Şablonlar', '/sablonlar'], ['Nasıl çalışır?', '#how-it-works'], ['SSS', '#faq']])
      expect(within(menu()).getByRole('navigation', { name: 'Sayfa bölümleri' })).toBeTruthy()
    })

    it('keeps the account button and the primary action outside the collapsible panel for a signed-in Creator', async () => {
      stubApi({ session: { authenticated: true, access: 'creator' } })
      renderHeader()
      const panel = await screen.findByRole('link', { name: 'Panele git' })
      const account = screen.getByRole('button', { name: 'Hesabım, tema ve dil' })
      expect(menu().contains(panel)).toBe(false)
      expect(menu().contains(account)).toBe(false)
      expect(menuButton()).toBeTruthy()
    })

    it('lists the Creator and Admin zone links inside the menu', () => {
      stubApi({ session: { authenticated: true, access: 'creator' } })
      const creator = renderHeader('creator')
      expect(within(menu()).getAllByRole('link')).toHaveLength(4)
      creator.unmount()
      renderHeader('admin')
      expect(within(menu()).getAllByRole('link')).toHaveLength(7)
    })

    it('marks only the current page link with aria-current', () => {
      stubApi({ session: { authenticated: true, access: 'creator' } })
      window.history.pushState({}, '', '/panel/cop-kutusu')
      renderHeader('creator')
      const current = within(menu()).getAllByRole('link').filter(link => link.getAttribute('aria-current') === 'page')
      expect(current.map(link => link.textContent)).toEqual(['Silinenler'])
    })

    it('adds the shadow state only after the page scrolls, via a data attribute', () => {
      stubApi({ session: { authenticated: false, access: 'none' } })
      const { container } = renderHeader()
      const header = container.querySelector('header.site-header') as HTMLElement
      expect(header.dataset.scrolled).toBe('false')
      act(() => { Object.defineProperty(window, 'scrollY', { value: 120, configurable: true }); window.dispatchEvent(new Event('scroll')) })
      expect(header.dataset.scrolled).toBe('true')
      act(() => { Object.defineProperty(window, 'scrollY', { value: 0, configurable: true }); window.dispatchEvent(new Event('scroll')) })
      expect(header.dataset.scrolled).toBe('false')
      expect(header.getAttribute('style')).toBeNull()
    })
  })
})
