import AxeBuilder from '@axe-core/playwright'
import { expect, test, type Page } from '@playwright/test'

const code = 'c'.repeat(64)
const publicPath = `/api/v1/public/invitations/${code}`
const questionIds = {
  name: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1',
  attending: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2',
}
const activeInvitation = {
  status: 'active', templateKey: 'zamansiz-dugun', rendererVersion: 1, contentSchemaVersion: 1,
  content: {
    eventType: 'dugun', headline: 'Elif & Deniz', hostNames: ['Elif', 'Deniz'],
    message: 'Bu özel günü bizimle paylaşın.', startsAt: '2026-10-20T15:00:00Z',
    timeZoneId: 'Europe/Istanbul', venue: { name: 'Bahçe', address: 'İstanbul' }, programItems: [],
  },
}
const rsvpConfiguration = {
  status: 'available',
  questions: [
    { id: questionIds.name, prompt: 'Adınız', type: 'ShortText', isRequired: true, sortOrder: 0, options: [] },
    { id: questionIds.attending, prompt: 'Katılacak mısınız?', type: 'YesNo', isRequired: true, sortOrder: 1, options: [] },
  ],
  answerLimits: {
    maxShortTextAnswerCharacters: 200, maxLongTextAnswerCharacters: 2000,
    minimumParticipantCount: 0, maximumParticipantCount: 20, maxMultipleChoiceSelections: 10,
  },
}

interface RsvpState {
  created: unknown[]
  updated: unknown[]
  answer: { name: string; attending: boolean }
}

test('guest submits RSVP and updates that response after reopening the invitation in the same browser', async ({ page }, testInfo) => {
  const state: RsvpState = { created: [], updated: [], answer: { name: 'Deniz', attending: false } }
  await page.route('**/api/v1/**', async route => {
    const url = new URL(route.request().url())
    const method = route.request().method()
    if (url.pathname === publicPath) return json(route, activeInvitation)
    if (url.pathname === `${publicPath}/views`) return route.fulfill({ status: 204 })
    if (url.pathname === '/api/v1/templates') return json(route, [{
      key: activeInvitation.templateKey, name: 'Zamansız Düğün', category: 'Düğün', isPremium: false,
      rendererVersion: 1, previewImageUrl: null, supportedModules: ['hero', 'rsvp'], requiredFields: [], recommendedFields: [],
    }])
    if (url.pathname === `${publicPath}/rsvp`) return json(route, rsvpConfiguration)
    if (url.pathname === `${publicPath}/rsvp/submissions` && method === 'POST') {
      state.created.push(route.request().postDataJSON())
      const request = route.request().postDataJSON() as { answers: { questionId: string; textValue?: string; booleanValue?: boolean }[] }
      state.answer = {
        name: request.answers.find(answer => answer.questionId === questionIds.name)?.textValue ?? '',
        attending: request.answers.find(answer => answer.questionId === questionIds.attending)?.booleanValue ?? false,
      }
      return json(route, { submissionId: 'guest-submission-1', submittedAt: '2026-10-06T10:00:00Z' }, 201)
    }
    if (url.pathname === `${publicPath}/rsvp/submissions/guest-submission-1` && method === 'GET') {
      return json(route, {
        submissionId: 'guest-submission-1', updatedAt: '2026-10-06T10:00:00Z',
        answers: [
          { questionId: questionIds.name, textValue: state.answer.name, numberValue: null, booleanValue: null, selectedOptionIds: [] },
          { questionId: questionIds.attending, textValue: null, numberValue: null, booleanValue: state.answer.attending, selectedOptionIds: [] },
        ],
      })
    }
    if (url.pathname === `${publicPath}/rsvp/submissions/guest-submission-1` && method === 'PUT') {
      state.updated.push(route.request().postDataJSON())
      const request = route.request().postDataJSON() as { answers: { questionId: string; textValue?: string; booleanValue?: boolean }[] }
      state.answer = {
        name: request.answers.find(answer => answer.questionId === questionIds.name)?.textValue ?? '',
        attending: request.answers.find(answer => answer.questionId === questionIds.attending)?.booleanValue ?? false,
      }
      return json(route, { submissionId: 'guest-submission-1', updatedAt: '2026-10-06T10:01:00Z' })
    }
    if (url.pathname.includes('/antiforgery')) return json(route, { token: 'guest-csrf' })
    return route.fulfill({ status: 404, contentType: 'application/problem+json', body: '{}' })
  })

  await page.goto(`/davetiye/${code}`)
  await applyZoom(page, testInfo.project.name)
  const rsvp = page.getByRole('region', { name: 'Katılım yanıtı' })
  await expect(rsvp).toBeVisible()
  await rsvp.getByLabel('Adınız').fill('Deniz')
  await rsvp.getByLabel('Hayır').check()
  await rsvp.getByRole('button', { name: 'Yanıtı gönder' }).click()
  await expect(rsvp.getByRole('status')).toContainText('Yanıtınız kaydedildi')
  expect(state.created).toEqual([{
    answers: [
      { questionId: questionIds.name, textValue: 'Deniz' },
      { questionId: questionIds.attending, booleanValue: false },
    ],
  }])

  await page.reload()
  await applyZoom(page, testInfo.project.name)
  await expect(rsvp.getByRole('status')).toContainText('Daha önce yanıt verdiniz')
  await rsvp.getByRole('button', { name: 'Yanıtımı Güncelle' }).click()
  await expect(rsvp.getByLabel('Adınız')).toHaveValue('Deniz')
  await expect(rsvp.getByLabel('Hayır')).toBeChecked()
  await rsvp.getByLabel('Adınız').fill('Deniz Yılmaz')
  await rsvp.getByLabel('Evet').check()
  await rsvp.getByRole('button', { name: 'Yanıtımı kaydet' }).click()
  await expect(rsvp.getByRole('status')).toContainText('Yanıtınız kaydedildi')
  expect(state.updated).toEqual([{
    answers: [
      { questionId: questionIds.name, textValue: 'Deniz Yılmaz' },
      { questionId: questionIds.attending, booleanValue: true },
    ],
  }])

  await expectAccessibleAndResponsive(page)
})

function json(route: import('@playwright/test').Route, value: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
}

async function applyZoom(page: Page, project: string) {
  if (project === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })
}

async function expectAccessibleAndResponsive(page: Page) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  expect(result.violations).toEqual([])
}
