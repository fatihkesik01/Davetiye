import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page, type Route } from '@playwright/test'
import { publicationInvitationId as id, publicationStatus, publicationTemplate } from './publicationFixtures'

const memoriesBase = `/api/v1/invitations/${id}/memories`
const inputLimits = { maxDisplayNameCharacters: 60, maxTextCharacters: 500, maxEmojiCharacters: 32, maxMemoriesPerInvitation: 200 }
const rsvpLimits = {
  maxActiveQuestionsPerInvitation: 10, maxQuestionPromptCharacters: 200, maxOptionLabelCharacters: 100, maxDefinedOptionsPerChoiceQuestion: 10,
  maxShortTextAnswerCharacters: 200, maxLongTextAnswerCharacters: 1000, minimumParticipantCount: 1, maximumParticipantCount: 20, maxMultipleChoiceSelections: 5,
}
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64')
const imageUrl = 'https://media.example.test/short-lived/image'
const videoUrl = 'https://customer-abc.cloudflarestream.com/token/iframe'

interface Memory {
  id: string; displayName: string | null; text: string | null; emoji: string | null; state: 'Published' | 'Hidden'; createdAt: string
  media: { assetId: string; kind: 'Image' | 'Video'; status: 'Pending' | 'Ready' | 'Rejected' | 'Deleted' }[]
}
interface State {
  config: { invitationId: string; isEnabled: boolean; visibility: 'CreatorOnly' | 'Public'; revision: number; effectiveState: string; inputLimits: typeof inputLimits }
  memories: Memory[]
  puts: unknown[]
  hidden: string[]
  deleted: string[]
  csrfRequests: number
  putStatus: number
  putRevisionJump: number
  // Booleans, not counters: React StrictMode (dev server) double-runs effects, so a counter would be consumed by an aborted request.
  listFailing: boolean
  deliveryFailing: boolean
  deliveryRequests: string[]
}

const published: Memory = { id: '10000000-0000-4000-8000-000000000001', displayName: 'Ayşe', text: 'Harika bir gün!', emoji: '🎉', state: 'Published', createdAt: '2026-10-05T10:00:00Z', media: [] }
const hiddenOne: Memory = { id: '10000000-0000-4000-8000-000000000002', displayName: null, text: 'Gizlenmiş not.', emoji: null, state: 'Hidden', createdAt: '2026-10-05T10:01:00Z', media: [] }

function newState(over: Partial<State> = {}): State {
  return {
    config: { invitationId: id, isEnabled: false, visibility: 'CreatorOnly', revision: 4, effectiveState: 'Draft', inputLimits },
    memories: [], puts: [], hidden: [], deleted: [], csrfRequests: 0, putStatus: 200, putRevisionJump: 2, listFailing: false, deliveryFailing: false, deliveryRequests: [],
    ...over,
  }
}

const json = (route: Route, body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })

// The editor mounts the media, RSVP and memories panels; every request is answered with a real shape.
async function mockApi(page: Page, state: State) {
  await page.route(imageUrl, route => route.fulfill({ status: 200, contentType: 'image/png', body: png }))
  await page.route(videoUrl, route => route.fulfill({ status: 200, contentType: 'text/html', body: '<!doctype html><title>video</title><p>video</p>' }))
  await page.route('**/api/v1/**', route => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const method = route.request().method()
    if (path === '/api/v1/auth/session') return json(route, { authenticated: true, access: 'creator' })
    if (path === '/api/v1/templates') return json(route, [publicationTemplate])
    if (path === '/api/v1/antiforgery/token') { state.csrfRequests++; return json(route, { token: 'csrf-token' }) }
    if (path === `/api/v1/creator/invitations/${id}/media`) return json(route, { assets: [] })
    if (path === `/api/v1/invitations/${id}/gifts` || path === `/api/v1/invitations/${id}/gifts/reservations`) return json(route, [])
    if (path === `/api/v1/invitations/${id}/rsvp`) {
      return json(route, { invitationId: id, enabled: false, revision: 1, effectiveState: 'Draft', questions: [], inputLimits: rsvpLimits })
    }
    if (path === `${memoriesBase}/configuration`) {
      if (method === 'PUT') {
        const body = route.request().postDataJSON()
        state.puts.push(body)
        if (state.putStatus !== 200) return route.fulfill({ status: state.putStatus })
        state.config = { ...state.config, isEnabled: body.isEnabled, visibility: body.visibility, revision: body.expectedRevision + state.putRevisionJump }
        return json(route, state.config)
      }
      return json(route, state.config)
    }
    if (path === memoriesBase && method === 'GET') {
      if (state.listFailing) return route.fulfill({ status: 500 })
      return json(route, { page: 1, pageSize: 25, totalCount: state.memories.length, items: state.memories })
    }
    const hide = path.match(new RegExp(`^${memoriesBase}/([^/]+)/hide$`))
    if (hide && method === 'PUT') {
      expect(route.request().headers()['x-csrf-token']).toBe('csrf-token')
      state.hidden.push(hide[1])
      state.memories = state.memories.map(memory => memory.id === hide[1] ? { ...memory, state: 'Hidden' } : memory)
      return route.fulfill({ status: 204 })
    }
    const delivery = path.match(new RegExp(`^${memoriesBase}/([^/]+)/media/([^/]+)/delivery$`))
    if (delivery && method === 'POST') {
      expect(route.request().headers()['x-csrf-token']).toBe('csrf-token')
      state.deliveryRequests.push(delivery[2])
      if (state.deliveryFailing) return route.fulfill({ status: 503 })
      const video = delivery[2].startsWith('video')
      return json(route, { mediaKind: video ? 'video' : 'image', deliveryUrl: video ? videoUrl : imageUrl, expiresAt: '2099-10-05T12:00:00Z' })
    }
    const remove = path.match(new RegExp(`^${memoriesBase}/([^/]+)$`))
    if (remove && method === 'DELETE') {
      expect(route.request().headers()['x-csrf-token']).toBe('csrf-token')
      state.deleted.push(remove[1])
      state.memories = state.memories.filter(memory => memory.id !== remove[1])
      return route.fulfill({ status: 204 })
    }
    if (path.endsWith('/publication')) return json(route, publicationStatus('Draft'))
    if (path.endsWith('/validation')) return json(route, { invitationId: id, invitationRevision: 2, contentRevision: 3, requiredFields: [], recommendedFields: [] })
    return json(route, {
      id, templateKey: publicationTemplate.key, rendererVersion: 1, createdAt: '2026-10-01T00:00:00Z', invitationRevision: 2, contentRevision: 3, contentSchemaVersion: 1,
      content: { headline: 'Anı yönetimi', hostNames: ['Deniz'], timeZoneId: 'Europe/Istanbul', programItems: [] },
    })
  })
}

async function openPanel(page: Page, projectName: string) {
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  if (projectName === 'chromium-200pct') await page.addStyleTag({ content: 'html { zoom: 2 }' })
  await page.getByRole('button', { name: /^5\s*Opsiyonel bölümler$/ }).click()
  const panel = page.getByRole('region', { name: 'Anılar', exact: true })
  await expect(panel).toBeVisible()
  return panel
}

async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}

test('memories configuration is off by default and save adopts the returned revision', async ({ page }, testInfo) => {
  const state = newState()
  await mockApi(page, state)
  const panel = await openPanel(page, testInfo.project.name)

  const toggle = panel.getByLabel('Anılar bölümünü aç')
  await expect(toggle).not.toBeChecked()
  await expect(panel.getByRole('radio', { name: /Sadece Creator/ })).toBeChecked()
  await expect(panel.getByRole('button', { name: 'Kaydet' })).toBeDisabled()
  await expect(panel.getByText('Anı bulunmuyor.')).toBeVisible()
  await expectAccessibleAndResponsive(page)

  await toggle.check()
  await panel.getByRole('radio', { name: /Public/ }).check()
  await panel.getByRole('button', { name: 'Kaydet' }).click()
  await expect(panel.getByText('Anılar ayarları kaydedildi.')).toBeVisible()
  expect(state.puts).toEqual([{ expectedRevision: 4, isEnabled: true, visibility: 'Public' }])
  expect(state.config.revision).toBe(6)
  await expect(panel.getByRole('button', { name: 'Kaydet' })).toBeDisabled()

  // The next save must use the adopted revision (4 -> 6), not a locally incremented one (5).
  await panel.getByRole('radio', { name: /Sadece Creator/ }).check()
  await panel.getByRole('button', { name: 'Kaydet' }).click()
  await expect.poll(() => state.puts.length).toBe(2)
  expect(state.puts[1]).toEqual({ expectedRevision: 6, isEnabled: true, visibility: 'CreatorOnly' })
  await expectAccessibleAndResponsive(page)
})

test('a stale revision reloads the current configuration with a clear message', async ({ page }, testInfo) => {
  const state = newState({ putStatus: 409 })
  await mockApi(page, state)
  const panel = await openPanel(page, testInfo.project.name)

  await panel.getByLabel('Anılar bölümünü aç').check()
  state.config = { ...state.config, isEnabled: true, visibility: 'Public', revision: 9 }
  await panel.getByRole('button', { name: 'Kaydet' }).click()
  await expect(panel.getByText(/başka bir sekmede değişti/)).toBeVisible()
  await expect(panel.getByRole('radio', { name: /Public/ })).toBeChecked()
  await expect(panel.getByLabel('Anılar bölümünü aç')).toBeChecked()
  await expect(panel.getByRole('button', { name: 'Kaydet' })).toBeDisabled()
  await expectAccessibleAndResponsive(page)
})

test('Creator lists published and hidden memories, hides with antiforgery and deletes after confirmation', async ({ page }, testInfo) => {
  const state = newState({ memories: [{ ...published }, { ...hiddenOne }] })
  await mockApi(page, state)
  const panel = await openPanel(page, testInfo.project.name)

  const list = panel.getByRole('list', { name: 'Creator tarafından yönetilen anılar' })
  await expect(list.getByRole('listitem')).toHaveCount(2)
  await expect(list.getByText('Yayında')).toHaveCount(1)
  await expect(list.getByText('Gizli')).toHaveCount(1)
  await expect(panel.getByRole('button', { name: 'Misafir anısını gizle' })).toHaveCount(0)
  await expectAccessibleAndResponsive(page)

  await panel.getByRole('button', { name: 'Ayşe anısını gizle' }).click()
  await expect(panel.getByText('Ayşe anısı gizlendi.')).toBeVisible()
  await expect(list.getByText('Gizli')).toHaveCount(2)
  await expect(list.getByText('Yayında')).toHaveCount(0)
  await expect(panel.getByRole('button', { name: 'Ayşe anısını gizle' })).toHaveCount(0)
  expect(state.hidden).toEqual([published.id])
  expect(state.csrfRequests).toBeGreaterThan(0)

  await panel.getByRole('button', { name: 'Ayşe anısını kalıcı olarak sil' }).click()
  const confirm = panel.getByRole('group', { name: 'Anı kalıcı olarak silinsin mi?' })
  await expect(confirm).toBeVisible()
  expect(state.deleted).toEqual([])
  await expectAccessibleAndResponsive(page)
  await confirm.getByRole('button', { name: 'Vazgeç' }).click()
  await expect(confirm).toHaveCount(0)
  expect(state.deleted).toEqual([])

  await panel.getByRole('button', { name: 'Ayşe anısını kalıcı olarak sil' }).click()
  await panel.getByRole('button', { name: 'Kalıcı olarak sil', exact: true }).click()
  await expect(panel.getByText('Ayşe anısı kalıcı olarak silindi.')).toBeVisible()
  expect(state.deleted).toEqual([published.id])
  await expect(list.getByRole('listitem')).toHaveCount(1)
  await expectAccessibleAndResponsive(page)
})

test('Ready media previews load on demand, failures are neutral and retry works', async ({ page }, testInfo) => {
  const withMedia: Memory = {
    ...published,
    media: [{ assetId: 'image-ready', kind: 'Image', status: 'Ready' }, { assetId: 'video-ready', kind: 'Video', status: 'Ready' }, { assetId: 'image-pending', kind: 'Image', status: 'Pending' }],
  }
  const state = newState({ memories: [withMedia], deliveryFailing: true })
  await mockApi(page, state)
  const panel = await openPanel(page, testInfo.project.name)

  // Previews are lazy: nothing is requested until a Ready item scrolls near the viewport.
  const previews = panel.locator('.memories-moderation__preview')
  await expect(previews).toHaveCount(2)
  expect(state.deliveryRequests).toEqual([])
  await previews.first().scrollIntoViewIfNeeded()
  await previews.last().scrollIntoViewIfNeeded()
  const retries = panel.getByRole('button', { name: 'Önizlemeyi tekrar dene' })
  await expect(retries).toHaveCount(2)
  await expect(panel.getByText('Önizleme şu anda kullanılamıyor.')).toHaveCount(2)
  await expect(panel.locator('.memories-moderation__media')).not.toContainText(/503|hata kodu|https:/)
  await expectAccessibleAndResponsive(page)

  state.deliveryFailing = false
  await retries.first().click()
  await retries.first().click()
  const image = panel.getByRole('img', { name: 'Fotoğraf anı önizlemesi' })
  await expect(image).toHaveAttribute('src', imageUrl)
  const frame = panel.locator('iframe[title="Video anı önizlemesi"]')
  await expect(frame).toHaveAttribute('src', videoUrl)
  await expect(frame).toHaveAttribute('sandbox', 'allow-scripts allow-same-origin allow-presentation')
  expect(state.deliveryRequests).not.toContain('image-pending')
  await expectAccessibleAndResponsive(page)
})

test('Creator moderation shows an error with retry, then the empty state', async ({ page }, testInfo) => {
  const state = newState({ listFailing: true })
  await mockApi(page, state)
  const panel = await openPanel(page, testInfo.project.name)

  await expect(panel.getByText('Anılar yüklenemedi.')).toBeVisible()
  await expectAccessibleAndResponsive(page)
  state.listFailing = false
  await panel.getByRole('button', { name: 'Tekrar dene' }).click()
  await expect(panel.getByText('Anı bulunmuyor.')).toBeVisible()
  await expectAccessibleAndResponsive(page)
})
