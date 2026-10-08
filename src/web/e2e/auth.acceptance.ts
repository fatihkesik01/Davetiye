import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

const authRoutes = [
  ['/giris', 'Giriş yap'],
  ['/giris/kayit', 'Hesap oluştur'],
  ['/giris/e-posta-dogrula', 'E-postanı doğrula'],
  ['/giris/sifremi-unuttum', 'Şifremi unuttum'],
  ['/giris/sifre-sifirla', 'Yeni şifre belirle'],
] as const

for (const [path, heading] of authRoutes) {
  test(`${path} is responsive and has no automatic WCAG A/AA violations`, async ({ page }, testInfo) => {
    await page.route('**/api/v1/auth/capabilities', route => route.fulfill({
      contentType: 'application/json',
      json: { googleSignInEnabled: false },
    }))
    await page.goto(path)
    if (testInfo.project.name === 'chromium-200pct') {
      await page.evaluate(() => { document.documentElement.style.zoom = '2' })
    }

    await expect(page.getByRole('heading', { name: heading })).toBeVisible()
    const viewportMetrics = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }))
    expect(viewportMetrics.scrollWidth).toBeLessThanOrEqual(viewportMetrics.clientWidth)

    const accessibility = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze()
    expect(accessibility.violations).toEqual([])
  })
}

test('registration is keyboard reachable in logical order', async ({ page }) => {
  await page.route('**/api/v1/auth/capabilities', route => route.fulfill({
    contentType: 'application/json',
    json: { googleSignInEnabled: false },
  }))
  await page.goto('/giris/kayit')

  await expect(page.getByRole('heading', { name: 'Hesap oluştur' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByLabel('Görünen ad')).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('textbox', { name: 'E-posta (zorunlu)', exact: true })).toBeFocused()
})

test('account deletion email link opens its public confirmation page directly', async ({ page }) => {
  await page.goto('/hesap-silme/onayla')

  await expect(page.getByRole('heading', { name: 'Hesap silme talebi', exact: true })).toBeVisible()
  await expect(page.getByRole('alert')).toContainText(/eksik, geçersiz/)
  await expect(page.getByRole('button', { name: 'Hesabı silmeyi onayla', exact: true })).toHaveCount(0)
})

test('protected Creator content is not exposed to an unauthenticated browser session', async ({ page }) => {
  await page.route('**/api/v1/auth/session', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    json: { status: 401 },
  }))
  await page.goto('/panel')

  await expect(page.getByText('Creator paneli için korumalı kabuk hazır.')).toHaveCount(0)
  await expect(page.locator('#main-content').getByRole('link', { name: 'Giriş yap' }))
    .toHaveAttribute('href', '/giris?returnUrl=%2Fpanel')
})
