import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page, type Route } from '@playwright/test'
import type { InvitationDraftDetails, InvitationPublicationState, PublicationActionRequest } from '../src/api/generated/client'
import { publicationInvitationId as id, publicationStatus, publicationTemplate as template } from './publicationFixtures'

test('publishing separates recommended warnings, requires explicit confirmation and is accessible', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Draft')
  await openPublish(page, testInfo.project.name)
  await page.getByLabel('Yayın bitişi').fill('2026-10-03T11:00')
  await page.getByRole('button', { name: 'Yayın bilgilerini gözden geçir' }).click()
  await expect(page.getByRole('heading', { name: 'İşlemi onaylayın' })).toBeFocused()
  await expect(page.getByRole('heading', { name: 'Önerilen alanlar eksik' })).toBeVisible()
  expect(mock.requests).toHaveLength(0)
  await expectAccessibleAndResponsive(page)
  await page.screenshot({ path: testInfo.outputPath('publication-confirmation.png'), fullPage: true })
  await page.getByRole('button', { name: 'Uyarıları kabul et ve devam et' }).click()
  await expect(page.getByText(/Yayınla işlemi tamamlandı/)).toBeVisible()
  expect(mock.requests[0]).toMatchObject({ action: 'publish', proceedWithRecommendedWarnings: true,
    expected: { invitationRevision: 2, workingContentRevision: 3, publishedContentRevision: null, windowId: null, windowRevision: null },
    publication: { mode: 'Immediate', startsAtLocal: null, endsAtLocal: '2026-10-03T11:00', timeZoneId: 'Europe/Istanbul', requestedGrantId: null } })
})

test('Active template selection explains the pause flow and cannot submit a template change', async ({ page }, testInfo) => {
  await mockApi(page, 'Active')
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /Şablon/ }).click()
  await expect(page.getByText(/Yayındaki şablon doğrudan değiştirilemez/)).toBeVisible()
  await expect(page.getByRole('radio', { name: /Minimal Açılış/ })).toBeDisabled()
  await page.getByRole('button', { name: 'Yayın yönetimine git' }).click()
  await page.getByRole('button', { name: 'Yayını durdur' }).click()
  await expect(page.getByText(/Yayın süresi durmaz/)).toBeVisible()
  await expectAccessibleAndResponsive(page)
})

test('autosave pending blocks public update, then update uses every current revision', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Active', { holdAutosave: true })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /Temel bilgiler/ }).click()
  await page.getByLabel('Davetiye başlığı').fill('Kaydedilen yeni başlık')
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await expect(page.getByRole('button', { name: 'Güncelle', exact: true })).toBeDisabled()
  await expect(page.getByText(/Otoma[tı]ik kayıt yayındaki|Otomatik kayıt yayındaki/)).toBeVisible()
  await expect.poll(() => Boolean(mock.pendingSave)).toBe(true)
  await mock.releaseAutosave()
  await expect(page.getByRole('button', { name: 'Güncelle', exact: true })).toBeEnabled()
  expect(mock.requests).toHaveLength(0)
  await page.getByRole('button', { name: 'Güncelle', exact: true }).click()
  await page.getByRole('button', { name: 'Uyarıları kabul et ve devam et' }).click()
  await expect(page.getByText(/Güncelle işlemi tamamlandı/)).toBeVisible()
  expect(mock.requests[0]).toMatchObject({ action: 'update', expected: { invitationRevision: 2, workingContentRevision: 4, publishedContentRevision: 1, windowRevision: 1 } })
})

test('revision conflict requires reload and renewed confirmation without repeating a mutation', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Active', { conflictOnce: true })
  mock.status.hasPendingChanges = true
  await openPublish(page, testInfo.project.name)
  await page.getByRole('button', { name: 'Güncelle', exact: true }).click()
  await page.getByRole('button', { name: 'Uyarıları kabul et ve devam et' }).click()
  await expect(page.getByRole('heading', { name: 'İşlem tamamlanamadı' })).toBeFocused()
  await expect(page.getByRole('heading', { name: 'Taslak çakışması' })).toBeVisible()
  expect(mock.requests).toHaveLength(1)
  await page.getByRole('button', { name: 'Sunucudaki sürümü yükle' }).click()
  await expect(page.getByRole('button', { name: 'Güncelle', exact: true })).toBeEnabled()
  expect(mock.requests).toHaveLength(1)
  await page.getByRole('button', { name: 'Güncelle', exact: true }).click()
  expect(mock.requests).toHaveLength(1)
  await expectAccessibleAndResponsive(page)
})

test('Scheduled publish-now keeps the accepted snapshot even when Working is incomplete', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Scheduled')
  mock.status.hasPendingChanges = true
  mock.status.preflight.missingRequiredFields = ['headline']
  await openPublish(page, testInfo.project.name)
  await page.getByRole('button', { name: 'Hemen yayınla', exact: true }).click()
  await expect(page.getByText(/Planlamada onayladığınız içerik şimdi yayına açılır/)).toBeVisible()
  await page.getByRole('button', { name: 'Hemen yayınla', exact: true }).click()
  await expect(page.getByText(/Hemen yayınla işlemi tamamlandı/)).toBeVisible()
  expect(mock.requests[0]).toMatchObject({ action: 'publishNow', publishWorkingContent: false, proceedWithRecommendedWarnings: false })
  expect(mock.requests[0]).not.toHaveProperty('publication')
})

test('scheduled cancel and reschedule preserve explicit dates and use the current window grant', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Scheduled')
  await openPublish(page, testInfo.project.name)
  await page.getByRole('button', { name: 'Yayın tarihini değiştir', exact: true }).click()
  await page.getByLabel('Yayın başlangıcı').fill('2026-10-03T14:00')
  await page.getByLabel('Yayın bitişi').fill('2026-10-04T14:00')
  await page.getByRole('button', { name: 'Yayın bilgilerini gözden geçir' }).click()
  await page.getByRole('button', { name: 'Yayın tarihini değiştir', exact: true }).click()
  await expect(page.getByText(/Yayın tarihini değiştir işlemi tamamlandı/)).toBeVisible()
  expect(mock.requests[0].publication).toMatchObject({ mode: 'Scheduled', startsAtLocal: '2026-10-03T14:00', endsAtLocal: '2026-10-04T14:00', requestedGrantId: '33333333-3333-3333-3333-333333333333' })
  await page.getByRole('button', { name: 'Planlamayı iptal et', exact: true }).click()
  await expect(page.getByText(/Başlamamış ücretsiz yayın hakkı tüketilmez/)).toBeVisible()
  await page.getByRole('button', { name: 'Planlamayı iptal et', exact: true }).click()
  await expect(page.getByText(/Planlamayı iptal et işlemi tamamlandı/)).toBeVisible()
})

test('Expired reactivation supports future local time and rejects DST ambiguity without rewriting it', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Expired', { ambiguousTime: true })
  await openPublish(page, testInfo.project.name)
  await expect(page.getByText(/Aynı davetiye ve public bağlantınız korunur/)).toBeVisible()
  await page.getByRole('radio', { name: 'İleri bir tarihte' }).check()
  await page.getByLabel('Yayın saat dilimi (IANA)').fill('America/New_York')
  await page.getByLabel('Yayın başlangıcı').fill('2026-11-01T01:30')
  await page.getByLabel('Yayın bitişi').fill('2026-11-01T23:30')
  await page.getByRole('button', { name: 'Yayın bilgilerini gözden geçir' }).click()
  await page.getByRole('button', { name: 'Uyarıları kabul et ve devam et' }).click()
  await expect(page.getByText(/Bu saat, seçtiğiniz saat diliminde iki kez yaşanıyor/)).toBeVisible()
  await expect(page.getByLabel('Yayın başlangıcı')).toHaveValue('2026-11-01T01:30')
  expect(mock.requests[0].publication).toMatchObject({ mode: 'Scheduled', startsAtLocal: '2026-11-01T01:30', timeZoneId: 'America/New_York' })
  await expectAccessibleAndResponsive(page)
})

test('Paused resume can explicitly keep Published or publish changed Working', async ({ page }, testInfo) => {
  const mock = await mockApi(page, 'Paused')
  mock.status.hasPendingChanges = true
  await openPublish(page, testInfo.project.name)
  await page.getByRole('button', { name: 'Mevcut yayınla devam et' }).click()
  await expect(page.getByText(/Kaydettiğiniz sonraki değişiklikler yayına yansımaz/)).toBeVisible()
  await page.getByRole('button', { name: 'Vazgeç', exact: true }).click()
  await page.getByRole('button', { name: 'Değişiklikleri yayımla ve devam et' }).click()
  await page.getByRole('button', { name: 'Uyarıları kabul et ve devam et' }).click()
  await expect(page.getByText(/Yayına devam et işlemi tamamlandı/)).toBeVisible()
  expect(mock.requests[0]).toMatchObject({ action: 'resume', publishWorkingContent: true, proceedWithRecommendedWarnings: true })
})

async function mockApi(page: Page, initial: InvitationPublicationState, options: { holdAutosave?: boolean; conflictOnce?: boolean; ambiguousTime?: boolean } = {}) {
  const draft: InvitationDraftDetails = { id, templateKey: template.key, rendererVersion: 1, createdAt: '2026-10-01T08:00:00Z', updatedAt: '2026-10-01T08:00:00Z', invitationRevision: 2, contentRevision: 3, contentSchemaVersion: 1, content: { headline: 'Buluşmamıza davetlisiniz', eventType: 'acilis-genel', startsAt: '2026-10-20T09:00:00Z', hostNames: ['Deniz'], venue: null, programItems: [] } }
  const model = { status: publicationStatus(initial), requests: [] as PublicationActionRequest[], pendingSave: undefined as Route | undefined,
    releaseAutosave: async () => {
      if (!model.pendingSave) return
      draft.content = model.pendingSave.request().postDataJSON().content
      draft.contentRevision++
      model.status.expected.workingContentRevision = draft.contentRevision
      model.status.hasPendingChanges = true
      await json(model.pendingSave, draft)
      model.pendingSave = undefined
    } }
  await page.route('**/api/v1/**', async route => {
    const request = route.request()
    const path = new URL(request.url()).pathname
    if (path === '/api/v1/auth/session') return json(route, { authenticated: true, access: 'creator' })
    if (path === '/api/v1/templates') return json(route, [template])
    if (path === '/api/v1/antiforgery/token') return json(route, { token: 'test-csrf' })
    if (path === `/api/v1/invitations/${id}`) return json(route, draft)
    if (path === `/api/v1/invitations/${id}/publication`) return json(route, model.status)
    if (path === `/api/v1/invitations/${id}/validation`) return json(route, { invitationId: id, invitationRevision: draft.invitationRevision, contentRevision: draft.contentRevision, templateKey: template.key, rendererVersion: 1, templateSelected: true, templateAvailable: true, requiredFields: [], recommendedFields: [] })
    if (path === `/api/v1/invitations/${id}/draft`) {
      model.pendingSave = route
      if (!options.holdAutosave) await model.releaseAutosave()
      return
    }
    if (path === `/api/v1/invitations/${id}/publication/actions`) {
      const command = request.postDataJSON() as PublicationActionRequest
      model.requests.push(command)
      if (options.conflictOnce && model.requests.length === 1) {
        draft.invitationRevision++
        model.status.expected.invitationRevision = draft.invitationRevision
        return problem(route, 409, 'publication_conflict')
      }
      if (options.ambiguousTime) return problem(route, 400, 'InvalidRequest', { startsAtLocal: ['This local time is ambiguous in the selected time zone.'] })
      const next = command.action === 'pause' ? 'Paused' : command.action === 'cancelSchedule' ? 'Draft' : command.action === 'reschedule' || command.publication?.mode === 'Scheduled' ? 'Scheduled' : 'Active'
      draft.invitationRevision++
      model.status = publicationStatus(next, { invitationRevision: draft.invitationRevision, workingContentRevision: draft.contentRevision })
      return json(route, model.status)
    }
    return route.fulfill({ status: 404, body: '{}' })
  })
  return model
}

async function openPublish(page: Page, project: string) {
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await applyZoom(page, project)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await expect(page.getByRole('heading', { name: 'Yayın yönetimi' })).toBeVisible()
}
async function applyZoom(page: Page, project: string) {
  if (project === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}
async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}
function json(route: Route, body: unknown) {
  return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}
function problem(route: Route, status: number, code: string, errors?: Record<string, string[]>) {
  return route.fulfill({ status, contentType: 'application/problem+json', body: JSON.stringify({ status, code, errors }) })
}

