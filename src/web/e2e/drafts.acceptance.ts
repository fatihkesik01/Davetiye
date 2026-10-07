import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page, type Route } from '@playwright/test'
import { publicationStatus } from './publicationFixtures'

const invitationId = '11111111-1111-1111-1111-111111111111'

const template = {
  key: 'zamansiz-dugun',
  name: 'Zamansız Düğün',
  category: 'Düğün',
  isPremium: true,
  rendererVersion: 1,
  previewImageUrl: '/template-previews/zamansiz-dugun.jpg',
  supportedModules: ['hero', 'venue'],
  requiredFields: ['headline', 'startsAt'],
  recommendedFields: ['venue.name'],
}

const draft = {
  id: invitationId,
  templateKey: template.key,
  rendererVersion: 1,
  createdAt: '2026-10-01T08:00:00Z',
  updatedAt: '2026-10-01T08:00:00Z',
  invitationRevision: 2,
  contentRevision: 3,
  contentSchemaVersion: 1,
  content: {
    eventType: 'dugun',
    headline: 'Elif & Deniz',
    hostNames: ['Elif', 'Deniz'],
    message: 'Bu güzel günümüzde sizi de aramızda görmek isteriz.',
    startsAt: null,
    timeZoneId: null,
    venue: null,
    programItems: [],
  },
}

test('public catalog and demo are responsive, keyboard reachable and accessible', async ({ page }, testInfo) => {
  await page.route('**/*', route => {
    const path = new URL(route.request().url()).pathname
    return path === '/api/v1/templates' ? json(route, [template]) : route.continue()
  })
  await page.goto('/sablonlar')
  await applyZoom(page, testInfo.project.name)

  await expect(page.getByRole('heading', { name: 'Etkinliğinize yakışan görünümü seçin' })).toBeVisible()
  await expect(page.getByText('Premium', { exact: true })).toBeVisible()
  const demoLink = page.getByRole('link', { name: 'Demoyu incele' })
  await demoLink.focus()
  await page.keyboard.press('Enter')
  await expect(page).toHaveURL(/\/sablonlar\/zamansiz-dugun$/)
  await expect(page.locator('[data-renderer="zamansiz-dugun@1"]')).toBeVisible()

  await expectNoHorizontalPageOverflow(page)
  await expectNoAutomaticAxeViolations(page)
})

test('protected editor exposes the exact wizard, fail-closed validation and device preview', async ({ page }, testInfo) => {
  await mockCreatorEditorApi(page)
  await page.goto(`/panel/davetiyeler/${invitationId}/duzenle`)
  await applyZoom(page, testInfo.project.name)

  await expect(page.getByRole('heading', { name: 'Taslak editörü', level: 1 })).toBeVisible()
  await expect(page.getByRole('list', { name: 'Davetiye oluşturma adımları' }).getByRole('listitem')).toHaveCount(7)

  const previewStep = page.getByRole('button', { name: /Önizleme/ })
  await previewStep.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Önizleme', level: 2 })).toBeFocused()
  await expect(page.getByRole('heading', { name: 'Şablon doğrulanamadı' })).toBeVisible()
  await expect(page.getByText('Davetiye başlığı')).toHaveCount(0)
  await expect(page.getByText('Etkinlik tarihi ve saati')).toBeVisible()
  await expect(page.getByText('Mekân adı')).toBeVisible()

  const tablet = page.getByRole('button', { name: 'Tablet' })
  await tablet.focus()
  await page.keyboard.press('Enter')
  await expect(tablet).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByRole('link', { name: 'Yeni sekmede tam önizleme' }))
    .toHaveAttribute('href', `/panel/davetiyeler/${invitationId}/onizleme`)

  const publishStep = page.getByRole('button', { name: /Yayınla/ })
  await publishStep.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Yayınla', level: 2 })).toBeFocused()
  await expect(page.getByRole('heading', { name: 'Yayın yönetimi' })).toBeVisible()
  await expect(page.getByLabel('Yayın saat dilimi (IANA)')).toHaveValue('Europe/Istanbul')

  await expectNoHorizontalPageOverflow(page)
  await expectNoAutomaticAxeViolations(page)
})

test('Creator media controls and verified library actions remain accessible at 320px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 })
  await mockCreatorEditorApi(page, { mediaReady: true })
  page.on('dialog', dialog => dialog.accept())
  await page.goto(`/panel/davetiyeler/${invitationId}/duzenle`)

  await page.getByRole('button', { name: /Opsiyonel bölümler/ }).click()
  await expect(page.getByRole('heading', { name: 'Kapak ve galeri medyası' })).toBeVisible()
  await expect(page.getByLabel('Kullanım alanı')).toBeVisible()
  await expect(page.getByLabel('Fotoğraf veya video ekle')).toBeEnabled()
  await expect(page.getByText('Dosya sunucu tarafından doğrulandı ve hazır.')).toBeVisible()
  await page.getByRole('button', { name: 'Kapağa ekle' }).click()
  await expect(page.getByText('Kapak · Galeri')).toBeVisible()
  await page.getByRole('button', { name: 'Kaldır' }).click()
  await expect(page.getByText('Kaldırılıyor')).toBeVisible()

  const metrics = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
  const accessibility = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(accessibility.violations).toEqual([])
})

test('Creator can enter the single draft flow from an empty dashboard', async ({ page }) => {
  await mockCreatorEditorApi(page)
  await page.goto('/panel')

  await expect(page.getByRole('heading', { name: 'Davetiyeleriniz' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Henüz taslağınız yok' })).toBeVisible()
  await page.getByRole('link', { name: 'Yeni davetiye oluştur' }).click()
  await expect(page).toHaveURL(/\/panel\/davetiyeler\/yeni$/)
  await page.getByRole('button', { name: 'Taslağı oluştur' }).click()

  await expect(page).toHaveURL(new RegExp(`/panel/davetiyeler/${invitationId}/duzenle$`))
  await expect(page.getByRole('heading', { name: 'Etkinlik türü', level: 2 })).toBeVisible()
})

test('autosave conflict pauses writes and offers both explicit resolution paths', async ({ page }) => {
  await mockCreatorEditorApi(page, { autosaveConflict: true })
  await page.goto(`/panel/davetiyeler/${invitationId}/duzenle`)

  await page.getByRole('button', { name: /Temel bilgiler/ }).click()
  await page.getByLabel('Davetiye başlığı').fill('Başka bir başlık')
  await expect(page.getByRole('heading', { name: 'Taslak çakışması' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Benim değişikliklerimi yeniden kaydet' })).toBeVisible()
  await page.getByRole('button', { name: 'Sunucudaki sürümü yükle' }).click()

  await expect(page.getByRole('heading', { name: 'Taslak çakışması' })).toHaveCount(0)
  await expect(page.getByLabel('Davetiye başlığı')).toHaveValue('Elif & Deniz')
})

test('full draft preview never renders outside the Creator guard', async ({ page }) => {
  await page.route('**/*', route => {
    const path = new URL(route.request().url()).pathname
    return path === '/api/v1/auth/session'
      ? json(route, { authenticated: false, access: 'none' })
      : route.continue()
  })

  await page.goto(`/panel/davetiyeler/${invitationId}/onizleme`)

  await expect(page.getByRole('heading', { name: 'Oturum gerekli' })).toBeVisible()
  await expect(page.getByText(/Korumalı taslak önizlemesi/i)).toHaveCount(0)
  await expect(page.locator('.invitation-renderer')).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Giriş yap' })).toHaveAttribute(
    'href',
    `/giris?returnUrl=${encodeURIComponent(`/panel/davetiyeler/${invitationId}/onizleme`)}`,
  )
})

async function mockCreatorEditorApi(page: Page, options: { autosaveConflict?: boolean; mediaReady?: boolean } = {}) {
  let mediaState = 'Ready'
  let placements = [{ role: 'Gallery', sortOrder: 0 }]
  await page.route('**/*', route => {
    const request = route.request()
    const path = new URL(request.url()).pathname

    if (!path.startsWith('/api/v1/')) return route.continue()

    if (path === '/api/v1/auth/session') {
      return json(route, { authenticated: true, access: 'creator' })
    }
    if (path === '/api/v1/templates') return json(route, [options.mediaReady ? { ...template, supportedModules: ['hero', 'gallery'] } : template])
    if (path === '/api/v1/antiforgery/token') return json(route, { token: 'browser-test-csrf' })
    if (path === '/api/v1/invitations' && request.method() === 'GET') {
      return json(route, { items: [], page: 1, pageSize: 50, totalCount: 0 })
    }
    if (path === '/api/v1/invitations' && request.method() === 'POST') return json(route, draft)
    if (path === `/api/v1/invitations/${invitationId}/validation`) {
      return json(route, {
        invitationId,
        templateKey: template.key,
        rendererVersion: 1,
        invitationRevision: 2,
        contentRevision: 3,
        templateSelected: true,
        templateAvailable: false,
        requiredFields: [
          { field: 'headline', isRecognized: true, isPresent: true },
          { field: 'startsAt', isRecognized: true, isPresent: false },
        ],
        recommendedFields: [
          { field: 'venue.name', isRecognized: true, isPresent: false },
        ],
      })
    }
    if (path === `/api/v1/invitations/${invitationId}/publication`) return json(route, publicationStatus(options.mediaReady ? 'Active' : 'Draft', { invitationRevision: 2, workingContentRevision: 3 }))
    if (path === `/api/v1/creator/invitations/${invitationId}/media` && request.method() === 'GET') {
      return json(route, { assets: options.mediaReady ? [{
        assetId: '44444444-4444-4444-8444-444444444444', kind: 'Image', state: mediaState,
        byteLength: 1024, durationSeconds: null, requestedPresentationRole: 'Gallery', placements,
      }] : [] })
    }
    if (path === `/api/v1/creator/invitations/${invitationId}/media/44444444-4444-4444-8444-444444444444/placement` && request.method() === 'PUT') {
      const placement = request.postDataJSON() as { role: string; sortOrder: number }
      placements = [...placements.filter(item => item.role !== placement.role), placement]
      return route.fulfill({ status: 204 })
    }
    if (path === `/api/v1/creator/invitations/${invitationId}/media/44444444-4444-4444-8444-444444444444` && request.method() === 'DELETE') {
      mediaState = 'PendingDeletion'
      return route.fulfill({ status: 202 })
    }
    if (path === `/api/v1/invitations/${invitationId}` && request.method() === 'GET') {
      return json(route, draft)
    }
    if (path === `/api/v1/invitations/${invitationId}/draft` && request.method() === 'PUT') {
      if (options.autosaveConflict) {
        return route.fulfill({
          status: 409,
          contentType: 'application/problem+json',
          body: JSON.stringify({
            status: 409,
            code: 'invitation_concurrency_conflict',
            currentInvitationRevision: 2,
            currentContentRevision: 4,
          }),
        })
      }
      return json(route, { ...draft, contentRevision: 4, updatedAt: '2026-10-01T08:05:00Z' })
    }
    if (path === `/api/v1/invitations/${invitationId}/template` && request.method() === 'PUT') {
      return json(route, { ...draft, invitationRevision: 3 })
    }

    return route.fulfill({ status: 404, contentType: 'application/problem+json', body: '{}' })
  })
}

async function applyZoom(page: Page, projectName: string) {
  if (projectName === 'chromium-200pct') {
    await page.evaluate(() => { document.documentElement.style.zoom = '2' })
  }
}

async function expectNoHorizontalPageOverflow(page: Page) {
  const metrics = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }))
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth)
}

async function expectNoAutomaticAxeViolations(page: Page) {
  const accessibility = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()
  expect(accessibility.violations).toEqual([])
}

function json(route: Route, value: unknown) {
  return route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(value),
  })
}
