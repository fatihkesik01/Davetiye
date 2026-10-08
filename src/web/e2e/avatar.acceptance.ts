import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

type Preferences = { locale: 'tr' | 'en'; colorTheme: 'kutlio' | 'sage' | 'rose' | 'ocean' | 'plum'; appearance: 'system' | 'light' | 'dark'; avatar: string | null }
const creator = { authenticated: true, access: 'creator' }
const consents = {
  serviceNotice: { acknowledged: true, acknowledgedAt: '2026-10-01T10:00:00Z', noticeVersion: 'v1', textStatus: 'draft' },
  marketing: { optedIn: false, updatedAt: null, version: 'v1' },
  history: [],
}
const SHOT_DIR = process.env.AVATAR_SHOTS_DIR

async function mockApi(page: Page, initial: Preferences = { locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: null }, session: unknown = creator) {
  const state = { preferences: initial, puts: [] as Preferences[], requests: [] as string[], unexpected: [] as string[] }
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    state.requests.push(`${request.method()} ${path}`)
    if (path === '/api/v1/auth/session') return route.fulfill({ json: session })
    if (path === '/api/v1/templates') return route.fulfill({ json: [] })
    if (path === '/api/v1/public/plans') return route.fulfill({ json: [] })
    if (path === '/api/v1/auth/capabilities') return route.fulfill({ json: { googleSignInEnabled: false } })
    if (path === '/api/v1/antiforgery/token') return route.fulfill({ json: { token: 'csrf' } })
    if (path === '/api/v1/account/consents') return route.fulfill({ json: consents })
    if (path === '/api/v1/account/preferences') {
      if (request.method() === 'PUT') {
        state.preferences = request.postDataJSON() as Preferences
        state.puts.push(state.preferences)
      }
      return route.fulfill({ json: state.preferences })
    }
    state.unexpected.push(`${request.method()} ${path}`)
    return route.fulfill({ status: 404, contentType: 'application/problem+json', json: { status: 404 } })
  })
  return state
}

async function expectNoHorizontalOverflow(page: Page) {
  const metrics = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
}

async function expectAccessible(page: Page) {
  const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze()
  expect(accessibility.violations).toEqual([])
}

async function applyZoom(page: Page, projectName: string) {
  if (projectName === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}

async function shot(page: Page, name: string, testInfo: { project: { name: string } }) {
  if (SHOT_DIR) await page.screenshot({ path: `${SHOT_DIR}/${name}-${testInfo.project.name}.png`, fullPage: false })
}

async function openDrawer(page: Page) {
  await page.getByRole('banner').getByRole('button', { name: 'Hesabım, tema ve dil' }).click()
  const drawer = page.getByRole('dialog')
  await expect(drawer.getByRole('radio', { name: 'Güneşli' })).toBeEnabled()
  return drawer
}

test('choosing an avatar in the drawer shows it in the header, saves all four fields and survives a reload', async ({ page }, testInfo) => {
  const api = await mockApi(page)
  await page.goto('/')
  await expect(page.getByRole('banner').getByRole('button', { name: 'Hesabım, tema ve dil' })).toBeVisible()
  await expect(page.locator('.site-account [data-avatar="none"]')).toHaveCount(1)
  const drawer = await openDrawer(page)
  await drawer.getByRole('radio', { name: 'Mercan' }).check()
  await expect(drawer.getByRole('status')).toHaveText('Tercihleriniz kaydedildi.')
  expect(api.puts).toEqual([{ locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: 'coral' }])
  await expect(page.locator('.site-account [data-avatar="coral"]')).toHaveCount(1)
  await expect(drawer.locator('.preferences-drawer__avatar[data-avatar="coral"]')).toHaveCount(1)
  await expect(drawer.getByRole('radio', { name: 'Mercan' })).toBeChecked()
  await applyZoom(page, testInfo.project.name)
  await expectNoHorizontalOverflow(page)
  await shot(page, 'drawer-avatar-chosen', testInfo)
  await drawer.getByRole('button', { name: 'Kapat' }).click()
  await shot(page, 'header-avatar-landing', testInfo)

  await page.reload()
  await expect(page.locator('.site-account [data-avatar="coral"]')).toHaveCount(1)

  const again = await openDrawer(page)
  await again.getByRole('button', { name: 'Avatarı kaldır' }).click()
  await expect(again.getByRole('status')).toHaveText('Tercihleriniz kaydedildi.')
  expect(api.puts.at(-1)).toEqual({ locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: null })
  await expect(page.locator('.site-account [data-avatar="none"]')).toHaveCount(1)
  expect(api.unexpected).toEqual([])
})

test('the drawer picker can be operated with the keyboard only', async ({ page }) => {
  const api = await mockApi(page, { locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: 'sunny' })
  await page.goto('/')
  const drawer = await openDrawer(page)
  await drawer.getByRole('radio', { name: 'Güneşli' }).focus()
  await expect(drawer.getByRole('radio', { name: 'Güneşli' })).toBeFocused()
  await page.keyboard.press('ArrowRight')
  await expect(drawer.getByRole('radio', { name: 'Nane' })).toBeFocused()
  await expect(drawer.getByRole('radio', { name: 'Nane' })).toBeChecked()
  await expect.poll(() => api.puts.at(-1)?.avatar).toBe('mint')
  // Tab leaves the group to the remove action, which is operated with the keyboard too.
  await page.keyboard.press('Tab')
  await expect(drawer.getByRole('button', { name: 'Avatarı kaldır' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect.poll(() => api.puts.at(-1)?.avatar).toBeNull()
  await expect(page.locator('.site-account [data-avatar="none"]')).toHaveCount(1)
})

test('the Creator settings page has a profile picture section that saves and shows the avatar in the header', async ({ page }, testInfo) => {
  const api = await mockApi(page)
  await page.goto('/panel/hesap')
  const section = page.getByRole('region', { name: 'Profil resmi' })
  await expect(section.getByRole('radio', { name: 'Gece' })).toBeEnabled()
  await expect(section.getByRole('radio')).toHaveCount(12)
  await section.getByRole('radio', { name: 'Gece' }).check()
  await expect(section.getByRole('status')).toHaveText('Tercihleriniz kaydedildi.')
  expect(api.puts).toEqual([{ locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: 'night' }])
  await expect(page.locator('.site-account [data-avatar="night"]')).toHaveCount(1)
  await expect(section.getByRole('radio', { name: 'Gece' })).toBeChecked()
  await applyZoom(page, testInfo.project.name)
  await expectNoHorizontalOverflow(page)
  await section.scrollIntoViewIfNeeded()
  await shot(page, 'settings-picker', testInfo)
  await section.getByRole('button', { name: 'Avatarı kaldır' }).click()
  await expect.poll(() => api.puts.at(-1)?.avatar).toBeNull()
  expect(api.unexpected).toEqual([])
})

const matrix: { name: string; preferences: Preferences }[] = [
  { name: 'kutlio light', preferences: { locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: 'berry' } },
  { name: 'kutlio dark', preferences: { locale: 'tr', colorTheme: 'kutlio', appearance: 'dark', avatar: 'night' } },
  { name: 'ocean light', preferences: { locale: 'en', colorTheme: 'ocean', appearance: 'light', avatar: null } },
  { name: 'plum dark', preferences: { locale: 'en', colorTheme: 'plum', appearance: 'dark', avatar: 'peach' } },
]
for (const { name, preferences } of matrix) {
  test(`the picker is accessible with no overflow on the settings page and in the drawer (${name})`, async ({ page }, testInfo) => {
    await page.addInitScript(([key, value]) => { localStorage.setItem(key, value) }, ['kutlio:account-preferences', JSON.stringify(preferences)])
    const api = await mockApi(page, preferences)
    await page.goto('/panel/hesap')
    const radios = page.getByRole('region', { name: preferences.locale === 'en' ? 'Profile picture' : 'Profil resmi' }).getByRole('radio')
    await expect(radios).toHaveCount(12)
    await expect(radios.first()).toBeEnabled()
    await applyZoom(page, testInfo.project.name)
    await expectNoHorizontalOverflow(page)
    await expectAccessible(page)
    await shot(page, `settings-${name.replace(' ', '-')}`, testInfo)

    await page.getByRole('banner').getByRole('button', { name: preferences.locale === 'en' ? 'My account, theme and language' : 'Hesabım, tema ve dil' }).click()
    const drawer = page.getByRole('dialog')
    await expect(drawer.getByRole('radio')).toHaveCount(12)
    await expectNoHorizontalOverflow(page)
    await expectAccessible(page)
    await shot(page, `drawer-${name.replace(' ', '-')}`, testInfo)
    expect(api.unexpected).toEqual([])
  })
}

test('the public invitation route shows no avatar and never asks for account preferences', async ({ page }) => {
  const api = await mockApi(page, { locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: 'sunny' })
  await page.goto(`/davetiye/${'a'.repeat(64)}`)
  await page.waitForLoadState('networkidle')
  await expect(page.locator('[data-avatar], .site-account, .avatar')).toHaveCount(0)
  expect(api.requests.filter(request => request.includes('/account/preferences'))).toEqual([])
})

test('an anonymous visitor sees the unchanged header without any avatar', async ({ page }) => {
  const api = await mockApi(page, { locale: 'tr', colorTheme: 'kutlio', appearance: 'system', avatar: 'sunny' }, { authenticated: false, access: 'none' })
  await page.goto('/')
  await expect(page.getByRole('banner').getByRole('link', { name: 'Giriş yap' })).toBeVisible()
  await expect(page.locator('[data-avatar], .site-account')).toHaveCount(0)
  expect(api.requests.filter(request => request.includes('/account/preferences'))).toEqual([])
})

// Font metrics differ between platforms (Linux CI fonts are wider than Windows). Widen all text deterministically.
for (const spacing of ['0.08em', '0.16em']) {
  test(`settings page and drawer do not overflow with wider font metrics (${spacing})`, async ({ page }, testInfo) => {
    await mockApi(page, { locale: 'en', colorTheme: 'plum', appearance: 'dark', avatar: 'peach' })
    await page.goto('/panel/hesap')
    await applyZoom(page, testInfo.project.name)
    await page.addStyleTag({ content: `* { letter-spacing: ${spacing} !important; }` })
    await expect(page.getByRole('region', { name: 'Profile picture' }).getByRole('radio').first()).toBeEnabled()
    await expectNoHorizontalOverflow(page)
    await page.getByRole('banner').getByRole('button', { name: 'My account, theme and language' }).click()
    const drawer = page.getByRole('dialog')
    await expect(drawer.getByRole('radio')).toHaveCount(12)
    await expectNoHorizontalOverflow(page)
    const drawerBox = await drawer.evaluate(element => ({ client: element.clientWidth, scroll: element.scrollWidth }))
    expect(drawerBox.scroll).toBeLessThanOrEqual(drawerBox.client)
  })
}
