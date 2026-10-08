import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { installApi, seedPreferences, sessions, type Appearance, type Palette } from './uiFixtures'

const NARROW_LIMIT = 1120

async function setup(page: Page, session: keyof typeof sessions, palette: Palette = 'kutlio', appearance: Appearance = 'light') {
  const preferences = { locale: 'tr' as const, colorTheme: palette, appearance, avatar: null }
  await seedPreferences(page, preferences)
  await installApi(page, { session: sessions[session], preferences })
}

const isNarrow = (page: Page) => (page.viewportSize()?.width ?? 0) <= NARROW_LIMIT

async function expectNoHorizontalOverflow(page: Page) {
  const metrics = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
}

async function expectAccessible(page: Page) {
  const accessibility = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze()
  expect(accessibility.violations).toEqual([])
}

test.describe('narrow screens: disclosure menu', () => {
  test.beforeEach(({ page }) => { test.skip(!isNarrow(page), 'the menu button only exists on narrow screens') })

  test('an anonymous visitor opens the menu, finds the site and in-page links, and Escape returns focus to the button', async ({ page }) => {
    await setup(page, 'anonymous')
    await page.goto('/')
    const banner = page.getByRole('banner')
    const button = banner.getByRole('button', { name: 'Menü' })
    await expect(button).toHaveAttribute('aria-expanded', 'false')
    await expect(button).toHaveAttribute('aria-controls', 'site-menu')
    await expect(banner.getByRole('link', { name: 'Şablonlar' })).toBeHidden()
    // The sign-in action is not hidden inside the menu.
    await expect(banner.getByRole('link', { name: 'Giriş yap' })).toBeVisible()

    await button.click()
    await expect(button).toHaveAttribute('aria-expanded', 'true')
    const menu = page.locator('#site-menu')
    await expect(menu.getByRole('link')).toHaveText(['Şablonlar', 'Nasıl çalışır?', 'Özellikler', 'Paketler', 'SSS'])
    await expectNoHorizontalOverflow(page)

    await page.keyboard.press('Tab')
    await expect(banner.getByRole('link', { name: 'Kutlio ana sayfa' })).toBeFocused()
    await menu.getByRole('link', { name: 'SSS' }).focus()
    await page.keyboard.press('Escape')
    await expect(button).toHaveAttribute('aria-expanded', 'false')
    await expect(button).toBeFocused()
    await expect(menu.getByRole('link', { name: 'SSS' })).toBeHidden()
  })

  test('choosing a link closes the menu and navigates; a landing anchor closes it too', async ({ page }) => {
    await setup(page, 'anonymous')
    await page.goto('/')
    const button = page.getByRole('banner').getByRole('button', { name: 'Menü' })
    await button.click()
    await page.locator('#site-menu').getByRole('link', { name: 'Paketler' }).click()
    await expect(button).toHaveAttribute('aria-expanded', 'false')
    await expect(page).toHaveURL(/#plans$/)

    await button.click()
    await page.locator('#site-menu').getByRole('link', { name: 'Şablonlar' }).click()
    await expect(page).toHaveURL(/\/sablonlar$/)
    await expect(page.getByRole('banner').getByRole('button', { name: 'Menü' })).toHaveAttribute('aria-expanded', 'false')
  })

  test('browser back closes an open menu', async ({ page }) => {
    await setup(page, 'anonymous')
    await page.goto('/')
    await page.getByRole('banner').getByRole('button', { name: 'Menü' }).click()
    await page.locator('#site-menu').getByRole('link', { name: 'Şablonlar' }).click()
    await expect(page).toHaveURL(/\/sablonlar$/)
    await page.getByRole('banner').getByRole('button', { name: 'Menü' }).click()
    await page.goBack()
    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByRole('banner').getByRole('button', { name: 'Menü' })).toHaveAttribute('aria-expanded', 'false')
  })

  test('a signed-in Creator keeps the primary action and the account button in the bar while the zone links live in the menu', async ({ page }) => {
    await setup(page, 'creator')
    await page.goto('/panel/davetiyeler')
    const banner = page.getByRole('banner')
    await expect(banner.getByRole('button', { name: /Hesabım, tema ve dil/ })).toBeVisible()
    await banner.getByRole('button', { name: 'Menü' }).click()
    const menu = page.locator('#site-menu')
    await expect(menu.getByRole('link')).toHaveText(['Davetiyeler', 'Çöp kutusu', 'Plan ve ödeme', 'Hesap tercihleri'])
    await expect(menu.getByRole('link', { name: 'Davetiyeler' })).toHaveAttribute('aria-current', 'page')
    await expectNoHorizontalOverflow(page)
    await expectAccessible(page)
  })

  test('the Admin zone links, the active page and the account button work from the menu', async ({ page }) => {
    await setup(page, 'admin', 'gold', 'dark')
    await page.goto('/admin')
    const banner = page.getByRole('banner')
    await banner.getByRole('button', { name: 'Menü' }).click()
    const menu = page.locator('#site-menu')
    await expect(menu.getByRole('link')).toHaveCount(7)
    await expect(menu.getByRole('link', { name: 'Platform özeti' })).toHaveAttribute('aria-current', 'page')
    await expectAccessible(page)
    await expectNoHorizontalOverflow(page)
    await banner.getByRole('button', { name: 'Hesabım, tema ve dil' }).click()
    await expect(page.getByRole('dialog')).toBeVisible()
  })

  for (const [palette, appearance] of [['kutlio', 'light'], ['gold', 'dark'], ['plum', 'light'], ['ocean', 'dark']] as const) {
    test(`the open menu is accessible without overflow in ${palette} ${appearance}`, async ({ page }) => {
      await setup(page, 'anonymous', palette, appearance)
      await page.goto('/')
      await expect(page.locator('html')).toHaveAttribute('data-color-theme', palette)
      await page.getByRole('banner').getByRole('button', { name: 'Menü' }).click()
      await expectNoHorizontalOverflow(page)
      await expectAccessible(page)
    })
  }
})

test.describe('wide screens: inline navigation and sticky bar', () => {
  test.beforeEach(({ page }) => { test.skip(isNarrow(page), 'inline navigation only exists on wide screens') })

  test('the menu button is hidden and the links are inline, with the current page marked', async ({ page }) => {
    await setup(page, 'creator')
    await page.goto('/panel/davetiyeler')
    const banner = page.getByRole('banner')
    await expect(banner.getByRole('button', { name: 'Menü' })).toBeHidden()
    const current = banner.getByRole('link', { name: 'Davetiyeler' })
    await expect(current).toBeVisible()
    await expect(current).toHaveAttribute('aria-current', 'page')
    await expect(banner.getByRole('link', { name: 'Çöp kutusu' })).not.toHaveAttribute('aria-current', 'page')
    // The active page has a tinted pill and an underline bar; other links stay transparent.
    expect(await current.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe('rgba(0, 0, 0, 0)')
    expect(await current.evaluate(element => getComputedStyle(element, '::after').height)).not.toBe('auto')
    expect(await banner.getByRole('link', { name: 'Çöp kutusu' }).evaluate(element => getComputedStyle(element).backgroundColor)).toBe('rgba(0, 0, 0, 0)')
    for (const link of await banner.getByRole('navigation').getByRole('link').all()) {
      expect((await link.boundingBox())?.height ?? 0).toBeGreaterThanOrEqual(44)
    }
  })

  test('the landing bar stays pinned at the top and gains its shadow only after scrolling', async ({ page }) => {
    await setup(page, 'anonymous')
    await page.goto('/')
    const header = page.locator('header.site-header')
    await expect(header).toHaveAttribute('data-scrolled', 'false')
    expect((await header.evaluate(element => getComputedStyle(element).boxShadow))).toBe('none')
    await page.evaluate(() => window.scrollTo(0, 1200))
    await expect(header).toHaveAttribute('data-scrolled', 'true')
    expect((await header.evaluate(element => getComputedStyle(element).boxShadow))).not.toBe('none')
    expect((await header.boundingBox())?.y).toBe(0)
    await page.evaluate(() => window.scrollTo(0, 0))
    await expect(header).toHaveAttribute('data-scrolled', 'false')
  })

  test('in-page anchors land below the sticky bar', async ({ page }) => {
    await setup(page, 'anonymous')
    await page.goto('/')
    await page.getByRole('banner').getByRole('link', { name: 'SSS' }).click()
    await expect(page).toHaveURL(/#faq$/)
    await expect.poll(async () => page.locator('#faq').evaluate(element => element.getBoundingClientRect().top)).toBeGreaterThanOrEqual((await page.locator('header.site-header').boundingBox())?.height ?? 0)
  })
})
