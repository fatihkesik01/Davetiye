import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'
import { publicationInvitationId as id, publicationStatus, publicationTemplate } from './publicationFixtures'

const code = 'a'.repeat(64)

test('Creator copy fallback, native share and local QR contain only the public URL', async ({ page }, testInfo) => {
  await page.addInitScript(() => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: () => Promise.reject(new Error('denied')) } })
    Object.defineProperty(navigator, 'share', { configurable: true, value: async (data: ShareData) => { document.body.dataset.sharedUrl = data.url } })
  })
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    const response = path === '/api/v1/public/capabilities' ? { canonicalBaseUrl: 'https://davet.example', mapEmbedEnabled: false, mapEmbedOrigin: 'https://www.google.com', mapEmbedPath: '/maps/embed/v1/place' }
      : path.endsWith('/statistics') ? {
        totalPageViews: 42, rsvpResponseCount: 8, participantCountTotal: 14,
        memoryCount: 2, readyMediaCount: 6, activeGiftReservationCount: 1,
      }
        : path === '/api/v1/auth/session' ? { authenticated: true, access: 'creator' }
      : path === '/api/v1/templates' ? [publicationTemplate]
        : path.endsWith('/publication') ? publicationStatus('Active')
          : path.endsWith('/validation') ? { invitationId: id, requiredFields: [], recommendedFields: [] }
            : { id, templateKey: publicationTemplate.key, rendererVersion: 1, createdAt: '2026-10-01T00:00:00Z', invitationRevision: 2, contentRevision: 3, contentSchemaVersion: 1, content: { headline: 'Paylaşım davetiyesi', hostNames: ['Deniz'], programItems: [] } }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(response) })
  })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await zoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  const url = `https://davet.example/davetiye/${code}`
  await expect(page.getByLabel('Davetiye bağlantısı')).toHaveValue(url)
  await expect(page.getByRole('heading', { name: 'Davetiye istatistikleri' })).toBeVisible()
  await expect(page.getByText('42')).toBeVisible()
  await expect(page.getByText('RSVP yanıtı sayısı')).toBeVisible()
  await page.getByRole('button', { name: 'Linki kopyala' }).click()
  await expect(page.getByLabel('Davetiye bağlantısı')).toBeFocused()
  await expect(page.getByText(/Bağlantıyı seçtik/)).toBeVisible()
  await expect(page.getByRole('link', { name: "WhatsApp'ta paylaş" })).toHaveAttribute('href', `https://wa.me/?text=${encodeURIComponent(`Davetiyemiz: ${url}`)}`)
  await page.getByRole('button', { name: 'Cihazla paylaş' }).click()
  await expect(page.locator('body')).toHaveAttribute('data-shared-url', url)
  await page.getByRole('button', { name: 'QR kodunu göster' }).click()
  await expect(page.getByRole('img', { name: 'Davetiye bağlantısını açan QR kodu' })).toHaveAttribute('src', /^data:image\/png;base64,/)
  const download = page.waitForEvent('download')
  await page.getByRole('link', { name: 'QR kodunu indir' }).click()
  expect((await download).suggestedFilename()).toBe('davetiye-qr.png')
  await accessible(page)
  await page.screenshot({ path: testInfo.outputPath('creator-share.png'), fullPage: true })
})

test('V2 public modules and calendar use Published UTC with no sharing or third-party load', async ({ page }, testInfo) => {
  const external: string[] = []
  page.on('request', request => { if (new URL(request.url()).origin !== 'http://127.0.0.1:4173') external.push(request.url()) })
  let views = 0
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/views')) { views++; expect(route.request().headers()['x-invitation-render']).toBe('1'); return route.fulfill({ status: 204 }) }
    if (path.endsWith('/capabilities')) return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ canonicalBaseUrl: 'https://davet.example', mapEmbedEnabled: false, mapEmbedOrigin: 'https://www.google.com', mapEmbedPath: '/maps/embed/v1/place' }) })
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'active', templateKey: 'zamansiz-dugun', rendererVersion: 2, contentSchemaVersion: 1, content: {
    headline: 'Published özel etkinlik', hostNames: ['Elif', 'Deniz'], startsAt: '2020-01-01T12:00:00+03:00', timeZoneId: 'Europe/Istanbul', venue: { name: 'Bahçe', address: 'Uzun özel adres İstanbul' },
    contacts: [{ name: 'Deniz', phone: '+90 555 123 45 67', role: 'İletişim kişisi' }], announcement: 'Published duyuru', faqs: [{ question: 'Otopark var mı?', answer: 'Evet, ücretsiz.' }], transportStops: [{ name: 'Kadıköy', address: 'İskele', departureTime: '17:30' }],
  } }) }) })
  await page.goto(`/davetiye/${code}`)
  await zoom(page, testInfo.project.name)
  await expect(page.getByText('Published duyuru')).toBeVisible()
  await expect(page.getByText('0 gün · 0 saat · 0 dakika · 0 saniye')).toBeVisible()
  await expect(page.getByRole('link', { name: /Ara\s*:\s*Deniz/ })).toHaveAttribute('href', 'tel:+905551234567')
  await page.getByText('Otopark var mı?').click()
  await expect(page.getByText('Evet, ücretsiz.')).toBeVisible()
  await expect(page.getByText('Kalkış: 17:30 (Europe/Istanbul)')).toBeVisible()
  await expect(page.getByRole('button', { name: /paylaş/i })).toHaveCount(0)
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Takvime ekle' }).click()
  expect((await download).suggestedFilename()).toBe('davetiye.ics')
  expect(external).toEqual([])
  await expect.poll(() => views).toBe(1)
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(page.getByText('Published duyuru')).toBeVisible()
  expect(views).toBe(1)
  await accessible(page)
  await page.screenshot({ path: testInfo.outputPath('public-v2.png'), fullPage: true })
})

test('Google embed requires consent and disappears when publication access closes', async ({ page }, testInfo) => {
  let unavailable = false
  const external: string[] = []
  await page.route('https://www.google.com/**', route => {
    expect(route.request().headers().referer).toBe('http://127.0.0.1:4173/')
    external.push(route.request().url())
    return route.fulfill({ status: 200, contentType: 'text/html', body: '<html lang="tr"><title>Harita</title><body>Harita</body></html>' })
  })
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/views')) return route.fulfill({ status: 204 })
    const response = path.endsWith('/capabilities') ? { canonicalBaseUrl: 'https://davet.example', mapEmbedEnabled: true, mapEmbedOrigin: 'https://www.google.com', mapEmbedPath: '/maps/embed/v1/place', mapEmbedKey: 'restricted-test-key' }
      : unavailable ? { status: 'unavailable' } : { status: 'active', templateKey: 'minimal-acilis', rendererVersion: 2, contentSchemaVersion: 1, content: { headline: 'Harita etkinliği', timeZoneId: 'Europe/Istanbul', venue: { name: 'Bahçe & Salon', address: 'İstanbul' } } }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(response) })
  })
  await page.goto(`/davetiye/${code}`)
  await zoom(page, testInfo.project.name)
  await expect(page.getByRole('button', { name: 'Google haritasını yükle' })).toBeVisible()
  expect(external).toEqual([])
  await expect(page.locator('iframe')).toHaveCount(0)
  await accessible(page)
  await page.getByRole('button', { name: 'Google haritasını yükle' }).click()
  const map = page.getByTitle('Etkinlik mekânı Google haritası')
  await expect(map).toHaveAttribute('referrerpolicy', 'strict-origin-when-cross-origin')
  await expect(map).toHaveAttribute('src', /https:\/\/www.google.com\/maps\/embed\/v1\/place\?key=restricted-test-key&q=/)
  await expect.poll(() => external.length).toBe(1)
  await page.getByRole('button', { name: 'Haritayı kapat' }).click()
  await expect(page.locator('iframe')).toHaveCount(0)
  unavailable = true
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(page.getByRole('heading', { name: 'Bu davetiye şu anda yayında değil' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Google haritasını yükle' })).toHaveCount(0)
})

test('optional Creator modules autosave and appear in the same V2 preview', async ({ page }, testInfo) => {
  let content: Record<string, unknown> = { headline: 'V2 editör etkinliği', hostNames: ['Deniz'], timeZoneId: 'Europe/Istanbul', programItems: [] }
  let revision = 3
  const payloads: Record<string, unknown>[] = []
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/draft') && route.request().method() === 'PUT') { content = route.request().postDataJSON().content; revision++; payloads.push(content) }
    // The V2 editor also mounts media, RSVP, memories and gifts panels; answer their requests with real shapes.
    if (path.endsWith('/memories/configuration')) return route.fulfill({ status: 404 })
    const response = path === '/api/v1/auth/session' ? { authenticated: true, access: 'creator' }
      : path === '/api/v1/templates' ? [{ ...publicationTemplate, rendererVersion: 2 }]
        : path === `/api/v1/creator/invitations/${id}/media` ? { assets: [] }
          : path === `/api/v1/invitations/${id}/gifts` || path === `/api/v1/invitations/${id}/gifts/reservations` ? []
          : path.endsWith('/publication') ? publicationStatus('Draft', { workingContentRevision: revision })
          : path.endsWith('/validation') ? { invitationId: id, invitationRevision: 2, contentRevision: revision, requiredFields: [], recommendedFields: [] }
            : { id, templateKey: publicationTemplate.key, rendererVersion: 2, createdAt: '2026-10-01T00:00:00Z', invitationRevision: 2, contentRevision: revision, contentSchemaVersion: 1, content }
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(response) })
  })
  await page.goto(`/panel/davetiyeler/${id}/duzenle`)
  await zoom(page, testInfo.project.name)
  await page.getByRole('button', { name: /^5\s*Opsiyonel bölümler$/ }).click()
  await page.getByLabel('Duyuru metni (isteğe bağlı)').fill('Duyuru metnimiz')
  await page.getByRole('button', { name: 'İletişim kişisi ekle' }).click()
  const contact = page.getByRole('group', { name: 'İletişim kişisi 1', exact: true })
  await contact.getByLabel('İsim', { exact: true }).fill('Deniz')
  await contact.getByLabel('Telefon', { exact: true }).fill('+905551234567')
  await page.getByRole('button', { name: 'Soru ekle' }).click()
  const faq = page.getByRole('group', { name: 'Soru 1', exact: true })
  await faq.getByLabel('Soru', { exact: true }).fill('Çocuklar gelebilir mi?')
  await faq.getByLabel('Yanıt', { exact: true }).fill('Elbette.')
  await page.getByRole('button', { name: 'Kalkış noktası ekle' }).click()
  const stop = page.getByRole('group', { name: 'Kalkış noktası 1', exact: true })
  await stop.getByLabel('Nokta adı').fill('İskele')
  await stop.getByLabel('Kalkış saati (isteğe bağlı)').fill('17:30')
  await expect.poll(() => payloads.at(-1)?.transportStops).toEqual([{ name: 'İskele', departureTime: '17:30' }])
  await accessible(page)
  await page.getByRole('button', { name: /^6\s*Önizleme$/ }).click()
  await expect(page.getByText('Duyuru metnimiz', { exact: true })).toBeVisible()
  await expect(page.getByText('Çocuklar gelebilir mi?', { exact: true })).toBeVisible()
  await expect(page.getByText('Kalkış: 17:30 (Europe/Istanbul)')).toBeVisible()
  await page.getByRole('button', { name: /^7\s*Yayınla$/ }).click()
  await expect(page.getByRole('heading', { name: 'Davetiyenizi paylaşın' })).toHaveCount(0)
})

test('existing Creator session reaches count admission while public content stays anonymous', async ({ page, context }) => {
  await context.addCookies([{ name: 'creator-session', value: 'creator-fixture', url: 'http://127.0.0.1:4173', httpOnly: true }])
  let totalPageViews = 0
  let renderRequests = 0
  await page.route('**/api/v1/**', route => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/views')) {
      renderRequests++
      const cookie = route.request().headers().cookie ?? ''
      expect(cookie).toContain('creator-session=creator-fixture')
      if (!cookie.includes('creator-session=creator-fixture')) totalPageViews++
      return route.fulfill({ status: 204 })
    }
    expect(route.request().headers().cookie).toBeUndefined()
    if (path === '/api/v1/templates' || path.endsWith('/memories/configuration')) return route.fulfill(path === '/api/v1/templates' ? { status: 200, contentType: 'application/json', body: '[]' } : { status: 404 })
    return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ status: 'active', templateKey: 'minimal-acilis', rendererVersion: 1, contentSchemaVersion: 1, content: { headline: 'Creator ziyareti', timeZoneId: 'Europe/Istanbul' } }) })
  })
  await page.goto(`/davetiye/${code}`)
  await expect(page.getByRole('heading', { name: 'Creator ziyareti' })).toBeVisible()
  await expect.poll(() => renderRequests).toBe(1)
  expect(totalPageViews).toBe(0)
  await page.evaluate(() => window.dispatchEvent(new Event('focus')))
  await expect(page.getByRole('heading', { name: 'Creator ziyareti' })).toBeVisible()
  expect(renderRequests).toBe(1)
  expect(totalPageViews).toBe(0)
})

async function zoom(page: Page, name: string) { if (name.includes('200pct')) await page.addStyleTag({ content: 'html { zoom: 2 }' }) }
async function accessible(page: Page) {
  expect((await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze()).violations).toEqual([])
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
}
