import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

test('Super Admin can enroll by keyboard, save codes, re-authenticate with MFA, and reach Admin without layout or WCAG A/AA regressions', async ({ page }, testInfo) => {
  let access = 'mfa-setup-required-super-admin'
  await page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url())
    const path = url.pathname
    if (path === '/api/v1/account/preferences') return route.fulfill({ json: { locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: null } })
    if (path === '/api/v1/auth/session') {
      return route.fulfill({ json: { authenticated: true, access } })
    }
    if (path === '/api/v1/antiforgery/token') {
      return route.fulfill({ json: { token: 'e2e-csrf' } })
    }
    if (path === '/api/v1/admin/mfa/enroll') {
      return route.fulfill({ json: { sharedKey: 'JBSWY3DPEHPK3PXP', authenticatorUri: 'otpauth://totp/Davetiye:admin?secret=JBSWY3DPEHPK3PXP' } })
    }
    if (path === '/api/v1/admin/mfa/verify') {
      expect(route.request().headers()['x-csrf-token']).toBe('e2e-csrf')
      return route.fulfill({ json: { recoveryCodes: ['alpha-one', 'beta-two'] } })
    }
    if (path === '/api/v1/auth/capabilities') {
      return route.fulfill({ json: { googleSignInEnabled: false } })
    }
    if (path === '/api/v1/auth/login') {
      return route.fulfill({ json: { requiresTwoFactor: true } })
    }
    if (path === '/api/v1/admin/mfa/login/complete') {
      expect(route.request().postDataJSON()).toEqual({ code: '123456', isRecoveryCode: false })
      access = 'mfa-complete-super-admin'
      return route.fulfill({ status: 200 })
    }
    if (path === '/api/v1/admin/overview') {
      return route.fulfill({ json: {
        generatedAtUtc: '2026-10-06T10:00:00Z',
        accounts: { total: 0, individual: 0, organization: 0, banned: 0 },
        invitations: { draft: 0, scheduled: 0, active: 0, paused: 0, expired: 0, deleted: 0 },
        plans: { total: 0, active: 0, inactive: 0 },
        grants: { total: 0, free: 0, individualPurchase: 0, organizationSubscription: 0, revoked: 0 },
        payments: { pending: 0, unknown: 0, succeeded: 0, failed: 0, canceled: 0, reversed: 0 },
        storage: { assets: 0, ready: 0, pendingUpload: 0, processing: 0, pendingDeletion: 0, deleted: 0, rejected: 0, verifiedBytes: 0 },
        health: { api: 'Healthy', database: 'Healthy' },
      } })
    }
    throw new Error(`Unexpected API request: ${route.request().method()} ${path}`)
  })

  await page.goto('/admin')
  if (testInfo.project.name === 'chromium-200pct') {
    await page.evaluate(() => { document.documentElement.style.zoom = '2' })
  }
  await expect(page.getByRole('heading', { name: 'Yönetici hesabı için MFA kurulumu' })).toBeVisible()
  await expect(page.getByLabel('Authenticator kodu')).toBeVisible()

  // Use keyboard input and Enter to verify setup, then keyboard traversal to the recovery-code copy action.
  await page.getByLabel('Authenticator kodu').focus()
  await page.keyboard.type('123456')
  await page.keyboard.press('Enter')
  await expect(page.getByText('alpha-one')).toBeVisible()
  await expect(page.getByText('beta-two')).toBeVisible()
  const copyButton = page.getByRole('button', { name: 'Kodları kopyala' })
  for (let tab = 0; tab < 12; tab += 1) {
    if (await copyButton.evaluate(element => element === document.activeElement)) break
    await page.keyboard.press('Tab')
  }
  await expect(copyButton).toBeFocused()
  const downloadButton = page.getByRole('button', { name: 'Dosya olarak indir' })
  await page.keyboard.press('Tab')
  await expect(downloadButton).toBeFocused()

  const viewportMetrics = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }))
  expect(viewportMetrics.scrollWidth).toBeLessThanOrEqual(viewportMetrics.clientWidth)

  const accessibility = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(accessibility.violations).toEqual([])

  const download = page.waitForEvent('download')
  await page.keyboard.press('Enter')
  await download
  await expect(page.getByRole('button', { name: 'MFA ile yeniden giriş yap' })).toBeEnabled()
  await page.getByRole('button', { name: 'MFA ile yeniden giriş yap' }).click()
  await page.getByLabel('E-posta').fill('admin@example.test')
  await page.getByLabel('Şifre').fill('valid-password')
  await page.getByRole('button', { name: 'Giriş yap' }).click()
  await page.getByLabel('Authenticator doğrulama kodu').fill('123456')
  const loginAccessibility = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(loginAccessibility.violations).toEqual([])
  await page.getByRole('button', { name: 'Doğrula ve devam et' }).click()
  await expect(page.getByRole('heading', { name: 'Platform özeti' })).toBeVisible()
})
