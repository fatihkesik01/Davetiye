import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import type { InvitationDraftDetails, InvitationPublicationStatus, InvitationTrashItem, PublicationActionRequest } from '../src/api/generated/client'
import { publicationInvitationId as id, publicationStatus, publicationTemplate as template } from './publicationFixtures'

test('delete confirms configured retention and restore returns Draft before explicit same-window republish', async ({ page }, testInfo) => {
  const mock = await mockTrashApi(page, { retentionDays: 7 })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Davetiyeyi silmek istiyor musunuz?' })).toBeFocused()
  await expect(page.getByText(/Güncel geri yükleme süresi 7 gündür/)).toBeVisible()
  await expect(page.getByText(/Public erişim hemen kapanır;/)).toBeVisible()
  expect(mock.trashRequests).toHaveLength(0)
  await expectAccessibleAndResponsive(page)
  await page.getByRole('button', { name: 'Onayla ve çöp kutusuna taşı' }).click()
  await expect(page).toHaveURL(/\/panel\/cop-kutusu$/)
  await expect(page.getByRole('heading', { name: 'Silinen davetiyeleriniz' })).toBeVisible()
  expect(mock.trashRequests[0].expected).toEqual(publicationStatus('Active').expected)
  expect(mock.trashRequests[0].expectedRetentionDays).toBe(7)
  await expect(page.getByText(/Geri yükleme için son tarih:/)).toBeVisible()
  await expect(page.getByText(/Son kontrol sırasında yaklaşık 7 gün/)).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('trash-list.png'), fullPage: true })
  await page.getByRole('button', { name: /^Geri yükle\s*:/ }).click()
  await expect(page.getByRole('heading', { name: 'Taslağı geri yükleyin' })).toBeFocused()
  await expect(page.getByText(/taslak olarak geri yüklenir. Public erişim açılmaz/)).toBeVisible()
  expect(mock.restoreRequests).toHaveLength(0)
  await expectAccessibleAndResponsive(page)
  await page.getByRole('button', { name: 'Taslak olarak geri yükle' }).click()
  await expect(page).toHaveURL(new RegExp(`/panel/davetiyeler/${id}/duzenle$`))
  expect(mock.publicationRequests).toHaveLength(0)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await expect(page.getByLabel('Yayın başlangıcı')).toHaveCount(0)
  await expect(page.getByLabel('Yayın bitişi')).toHaveCount(0)
  await expect(page.getByText(/Eski yayın aralığıyla yeniden açmak/)).toBeVisible()
  await page.getByRole('button', { name: 'Önceki yayınla yeniden aç' }).click()
  await expect(page.getByText(/Önceki yayımlanmış içerik ve aynı yayın aralığı yeniden kullanılır/)).toBeVisible()
  await page.getByRole('button', { name: 'Mevcut süreyle yeniden yayınla', exact: true }).click()
  await expect(page.getByText(/Mevcut süreyle yeniden yayınla işlemi tamamlandı/)).toBeVisible()
  expect(mock.publicationRequests[0]).toMatchObject({ action: 'republish', publishWorkingContent: false, proceedWithRecommendedWarnings: false })
  expect(mock.publicationRequests[0]).not.toHaveProperty('publication')
})

test('deadline expiry rejects restore without retrying and disables it after the server clock refresh', async ({ page }, testInfo) => {
  const mock = await mockTrashApi(page, { expiredRestore: true })
  mock.deleted = true
  await page.goto('/panel/cop-kutusu')
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^Geri yükle\s*:/ }).click()
  await page.getByRole('button', { name: 'Taslak olarak geri yükle' }).click()
  await expect(page.getByText('Geri yükleme süresi dolduğu için davetiye geri yüklenemiyor.')).toBeVisible()
  await expect(page.getByText('Geri yükleme süresi doldu.', { exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: /^Geri yükle\s*:/ })).toBeDisabled()
  expect(mock.restoreRequests).toHaveLength(1)
  await expectAccessibleAndResponsive(page)
})

test('missing retention disables deletion and zero retention explicitly warns there is no restore period', async ({ page }, testInfo) => {
  const mock = await mockTrashApi(page, { retentionDays: null })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await expect(page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true })).toBeDisabled()
  await expect(page.getByText(/Güncel geri yükleme süresi doğrulanamadı/)).toBeVisible()
  mock.status.trashRetentionDays = 0
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true }).click()
  await expect(page.getByText(/Güncel ayarda geri yükleme süresi yoktur/)).toBeVisible()
  await expect(page.getByText(/ve geri yüklenemez/)).toBeVisible()
  expect(mock.trashRequests).toHaveLength(0)
  await expectAccessibleAndResponsive(page)
})

test('restored Draft can explicitly publish Working with warning acknowledgement without extending the window', async ({ page }, testInfo) => {
  const mock = await mockTrashApi(page, { restored: true })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await page.getByRole('button', { name: 'Son bilgileri yayımla ve yeniden aç' }).click()
  await expect(page.getByText(/Kaydettiğiniz son bilgiler ve seçtiğiniz şablon, aynı yayın aralığında yeniden yayımlanır/)).toBeVisible()
  await page.getByRole('button', { name: 'Uyarıları kabul et ve devam et' }).click()
  await expect(page.getByText(/Mevcut süreyle yeniden yayınla işlemi tamamlandı/)).toBeVisible()
  expect(mock.publicationRequests[0]).toMatchObject({ action: 'republish', publishWorkingContent: true, proceedWithRecommendedWarnings: true })
  expect(mock.status.currentWindow?.endsAtUtc).toBe('2026-10-04T09:00:00Z')
})

test('changed retention requires fresh confirmation before deletion', async ({ page }, testInfo) => {
  const mock = await mockTrashApi(page, { retentionDays: 3 })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true }).click()
  await expect(page.getByText(/Güncel geri yükleme süresi 3 gündür/)).toBeVisible()
  mock.status.trashRetentionDays = 0
  await page.getByRole('button', { name: 'Onayla ve çöp kutusuna taşı' }).click()
  await expect(page.getByText(/Davetiye veya silme bilgileri değişti/)).toBeVisible()
  expect(mock.deleted).toBe(false)
  expect(mock.trashRequests).toHaveLength(1)
  expect(mock.trashRequests[0].expectedRetentionDays).toBe(3)
  await page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true }).click()
  await expect(page.getByText(/Güncel ayarda geri yükleme süresi yoktur/)).toBeVisible()
  expect(mock.trashRequests).toHaveLength(1)
  await page.getByRole('button', { name: 'Onayla ve çöp kutusuna taşı' }).click()
  await expect(page).toHaveURL(/\/panel\/cop-kutusu$/)
  expect(mock.trashRequests[1].expectedRetentionDays).toBe(0)
})

test('prestart Scheduled deletion explains cancellation and released unused rights', async ({ page }, testInfo) => {
  const mock = await mockTrashApi(page)
  mock.status = publicationStatus('Scheduled')
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await page.getByRole('button', { name: 'Çöp kutusuna taşı', exact: true }).click()
  await expect(page.getByText(/Henüz başlamamış yayın planı iptal edilir ve kullanılmamış yayın hakkı serbest bırakılır/)).toBeVisible()
  await expect(page.getByText(/eski planlama açılmaz/)).toBeVisible()
  expect(mock.trashRequests).toHaveLength(0)
  await expectAccessibleAndResponsive(page)
})

test('Trash stays behind the Creator guard for anonymous browsers', async ({ page }) => {
  const apiPaths: string[] = []
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    apiPaths.push(path)
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ authenticated: false, access: 'none' }) })
  })
  await page.goto('/panel/cop-kutusu')
  await expect(page.getByRole('heading', { name: 'Oturum gerekli' })).toBeVisible()
  expect(apiPaths.every(path => path === '/api/v1/auth/session')).toBe(true)
  await expect(page.getByRole('heading', { name: 'Silinen davetiyeleriniz' })).toHaveCount(0)
})

async function mockTrashApi(page: Page, options: { retentionDays?: number | null; expiredRestore?: boolean; restored?: boolean } = {}) {
  const draft: InvitationDraftDetails = { id, templateKey: template.key, rendererVersion: 1, createdAt: '2026-10-01T08:00:00Z', updatedAt: '2026-10-01T08:00:00Z', invitationRevision: 2, contentRevision: 3, contentSchemaVersion: 1, content: { headline: 'Silinecek özel başlık ve korunacak taslak', eventType: 'acilis-genel', startsAt: '2026-10-20T09:00:00Z', hostNames: ['Deniz'], venue: null, programItems: [] } }
  const model = { status: publicationStatus('Active'), deleted: false, serverNowUtc: '2026-10-02T09:00:00Z',
    trashRequests: [] as { expected: unknown; expectedRetentionDays: number }[], restoreRequests: [] as { expected: unknown }[], publicationRequests: [] as PublicationActionRequest[] }
  model.status.trashRetentionDays = options.retentionDays === undefined ? 3 : options.retentionDays
  if (options.restored) model.status = restoredStatus(model.status)
  const item = (): InvitationTrashItem => ({ invitationId: id, headline: draft.content.headline ?? null,
    deletedAtUtc: '2026-10-02T09:00:00Z', purgeAfterUtc: options.expiredRestore ? '2026-10-02T09:01:00Z' : '2026-10-09T09:00:00Z', expected: { ...model.status.expected } })
  await page.route('**/api/v1/**', async route => {
    const path = new URL(route.request().url()).pathname
    if (path === '/api/v1/auth/session') return json(route, { authenticated: true, access: 'creator' })
    if (path === '/api/v1/templates') return json(route, [template])
    if (path === '/api/v1/antiforgery/token') return json(route, { token: 'test-csrf' })
    if (path === '/api/v1/invitations/trash') return json(route, { items: model.deleted ? [item()] : [], page: 1, pageSize: 20, totalCount: model.deleted ? 1 : 0, serverNowUtc: model.serverNowUtc })
    if (path === `/api/v1/invitations/${id}`) return json(route, { ...draft, invitationRevision: model.status.expected.invitationRevision })
    if (path === `/api/v1/invitations/${id}/publication`) return json(route, model.status)
    if (path === `/api/v1/invitations/${id}/validation`) return json(route, { invitationId: id, invitationRevision: draft.invitationRevision, contentRevision: draft.contentRevision, templateKey: template.key, rendererVersion: 1, templateSelected: true, templateAvailable: true, requiredFields: [], recommendedFields: [] })
    if (path === `/api/v1/invitations/${id}/trash`) {
      expect(route.request().headers()['x-csrf-token']).toBe('test-csrf')
      model.trashRequests.push(route.request().postDataJSON())
      if (model.trashRequests.at(-1)?.expectedRetentionDays !== model.status.trashRetentionDays)
        return route.fulfill({ status: 409, contentType: 'application/problem+json', body: JSON.stringify({ status: 409, code: 'RetentionChanged' }) })
      model.deleted = true
      model.status.expected.invitationRevision++
      return json(route, item())
    }
    if (path === `/api/v1/invitations/${id}/restore`) {
      model.restoreRequests.push(route.request().postDataJSON())
      if (options.expiredRestore) {
        model.serverNowUtc = '2026-10-02T09:01:00Z'
        return route.fulfill({ status: 409, contentType: 'application/problem+json', body: JSON.stringify({ status: 409, code: 'RestoreExpired' }) })
      }
      model.deleted = false
      model.status = restoredStatus(model.status)
      model.status.expected.invitationRevision++
      return json(route, model.status)
    }
    if (path === `/api/v1/invitations/${id}/publication/actions`) {
      model.publicationRequests.push(route.request().postDataJSON())
      const previous = model.status
      model.status = publicationStatus('Active', { invitationRevision: previous.expected.invitationRevision + 1 })
      model.status.currentWindow = previous.currentWindow
      return json(route, model.status)
    }
    return route.fulfill({ status: 404, body: '{}' })
  })
  return model
}

function restoredStatus(status: InvitationPublicationStatus): InvitationPublicationStatus {
  return { ...status, effectiveState: 'Draft', storedState: 'Draft', allowedActions: ['republish'], hasPendingChanges: true }
}
async function applyZoom(page: Page, project: string) {
  if (project === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}
async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}
function json(route: import('@playwright/test').Route, value: unknown) {
  return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(value) })
}

