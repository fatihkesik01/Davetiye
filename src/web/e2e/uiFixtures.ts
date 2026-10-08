import type { Page, Route } from '@playwright/test'
import { publicationStatus } from './publicationFixtures'

/**
 * Shared, deterministic API fixtures for the palette x appearance sweeps. Every Creator, Admin, auth and
 * catalog screen is rendered with realistic (non-empty) data so badges, chips, tables and status messages
 * exist in the DOM when the colour checks run.
 */
export type Palette = 'kutlio' | 'sage' | 'rose' | 'ocean' | 'plum' | 'gold'
export type Appearance = 'system' | 'light' | 'dark'
export interface Preferences { locale: 'tr' | 'en'; colorTheme: Palette; appearance: Appearance; avatar: string | null }

export const STORAGE_KEY = 'kutlio:account-preferences'
export const palettes: readonly Palette[] = ['kutlio', 'sage', 'rose', 'ocean', 'plum', 'gold']
export const sessions = {
  anonymous: { authenticated: false, access: 'none' },
  creator: { authenticated: true, access: 'creator' },
  admin: { authenticated: true, access: 'mfa-complete-super-admin' },
  mfaSetup: { authenticated: true, access: 'mfa-setup-required-super-admin' },
} as const

export const invitationId = '11111111-1111-1111-1111-111111111111'
const secondId = '55555555-5555-4555-8555-555555555555'
const expected = { invitationRevision: 2, workingContentRevision: 3, publishedContentRevision: null, windowId: null, windowRevision: null }

const templates = [
  { key: 'zamansiz-dugun', name: 'Zamansız Düğün', description: 'Örnek içerikle incelenebilen şablon.', category: 'Düğün', isPremium: false, rendererVersion: 1, previewImageUrl: '/template-previews/zamansiz-dugun.jpg', supportedModules: ['hero', 'venue', 'gallery'], requiredFields: ['headline', 'startsAt'], recommendedFields: ['venue.name'] },
  { key: 'romantik-nisan', name: 'Romantik Nişan', description: 'Nişan için sıcak bir şablon.', category: 'Nişan', isPremium: true, rendererVersion: 1, previewImageUrl: '/template-previews/romantik-nisan.jpg', supportedModules: ['hero', 'venue'], requiredFields: ['headline'], recommendedFields: [] },
  { key: 'gece-kina', name: 'Gece Kına', description: 'Kına gecesi için şablon.', category: 'Kına', isPremium: false, rendererVersion: 1, previewImageUrl: '/template-previews/gece-kina.jpg', supportedModules: ['hero'], requiredFields: ['headline'], recommendedFields: [] },
]
const basePlan = { description: null, currency: 'TRY', maxImages: 5, maxVideos: 0, maxActiveInvitations: 1 }
const publicPlans = [
  { ...basePlan, key: 'free', displayName: 'Ücretsiz', priceAmount: 0, billingPeriod: 'free', maxPublishDays: 1, maxRSVPResponses: 50, memoriesEnabled: false, giftRegistryEnabled: false, premiumTemplatesEnabled: false },
  { ...basePlan, key: 'premium', displayName: 'Premium', priceAmount: 1199, billingPeriod: 'one-time', maxPublishDays: 90, maxRSVPResponses: 1000, memoriesEnabled: true, giftRegistryEnabled: true, premiumTemplatesEnabled: true },
]
const draft = {
  id: invitationId, templateKey: 'zamansiz-dugun', rendererVersion: 1, createdAt: '2026-10-01T08:00:00Z', updatedAt: '2026-10-01T08:00:00Z',
  invitationRevision: 2, contentRevision: 3, contentSchemaVersion: 1,
  content: { eventType: 'dugun', headline: 'Elif & Deniz', hostNames: ['Elif', 'Deniz'], message: 'Bu güzel günümüzde sizi de aramızda görmek isteriz.', startsAt: '2026-11-20T15:00:00Z', timeZoneId: 'Europe/Istanbul', venue: { name: 'Bahçe Salon', address: 'Moda Cd. 12, Kadıköy' }, programItems: [] },
}
const entitlementKeys = ['maxPublishDays', 'maxActiveInvitations', 'maxImages', 'maxVideos', 'maxImageSizeMb', 'maxVideoSizeMb', 'maxVideoDurationSeconds', 'maxGuestImages', 'maxGuestVideos', 'maxGuestImageSizeMb', 'maxGuestVideoSizeMb', 'maxGuestVideoDurationSeconds', 'maxRSVPResponses', 'memoriesEnabled', 'giftRegistryEnabled', 'premiumTemplatesEnabled']
const boolKeys = ['memoriesEnabled', 'giftRegistryEnabled', 'premiumTemplatesEnabled']
const adminPlans = [
  { id: '11111111-1111-4111-8111-111111111111', key: 'free', displayName: 'Ücretsiz', description: null, priceAmount: 0, currency: 'TRY', billingKind: 'Free', revision: 1, entitlements: entitlementKeys.map(key => ({ key, numericValue: boolKeys.includes(key) ? null : 3, booleanValue: boolKeys.includes(key) ? false : null })) },
  { id: '11111111-1111-4111-8111-111111111112', key: 'premium', displayName: 'Premium', description: 'Tek seferlik', priceAmount: 1199, currency: 'TRY', billingKind: 'OneTime', revision: 2, entitlements: entitlementKeys.map(key => ({ key, numericValue: boolKeys.includes(key) ? null : 30, booleanValue: boolKeys.includes(key) ? true : null })) },
]
const overview = {
  generatedAtUtc: '2026-10-06T10:00:00Z',
  accounts: { total: 12, individual: 10, organization: 2, banned: 1 },
  invitations: { draft: 5, scheduled: 1, active: 3, paused: 1, expired: 2, deleted: 1 },
  plans: { total: 2, active: 2, inactive: 0 },
  grants: { total: 4, free: 2, individualPurchase: 1, organizationSubscription: 1, revoked: 0 },
  payments: { pending: 1, unknown: 1, succeeded: 3, failed: 1, canceled: 1, reversed: 1 },
  storage: { assets: 9, ready: 7, pendingUpload: 1, processing: 1, pendingDeletion: 0, deleted: 0, rejected: 0, verifiedBytes: 123456 },
  health: { api: 'Healthy', database: 'Warning' },
}
const rsvpQuestions = [
  { id: 'q1', prompt: 'Katılacak mısınız?', type: 'YesNo', isRequired: true, sortOrder: 1, semanticRole: null, options: [] },
  { id: 'q2', prompt: 'Kişi sayısı', type: 'Number', isRequired: true, sortOrder: 2, semanticRole: 'ParticipantCount', options: [] },
  { id: 'q3', prompt: 'Menü tercihi', type: 'SingleChoice', isRequired: false, sortOrder: 3, semanticRole: null, options: [{ id: 'o1', label: 'Et', sortOrder: 1 }, { id: 'o2', label: 'Vejetaryen', sortOrder: 2 }] },
]
const rsvpConfiguration = {
  invitationId, enabled: true, revision: 2, effectiveState: 'Active', questions: rsvpQuestions,
  inputLimits: { maxActiveQuestionsPerInvitation: 10, maxQuestionPromptCharacters: 200, maxOptionLabelCharacters: 80, maxDefinedOptionsPerChoiceQuestion: 8, maxShortTextAnswerCharacters: 200, maxLongTextAnswerCharacters: 2000, minimumParticipantCount: 1, maximumParticipantCount: 20, maxMultipleChoiceSelections: 4 },
}
const memoryLimits = { maxTextCharacters: 500, maxImagesPerMemory: 3, maxVideosPerMemory: 1 }

export function json(route: Route, value: unknown, status = 200) {
  return route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(value) })
}

export interface MockOptions {
  session?: unknown
  preferences?: Preferences
  subscription?: unknown
  authenticatedPreferences?: boolean
}

export async function seedPreferences(page: Page, preferences: Preferences) {
  await page.addInitScript(([key, value]) => { localStorage.setItem(key, value) }, [STORAGE_KEY, JSON.stringify(preferences)])
}

/** Installs the realistic API fixture. Unknown endpoints answer 404 so a page shows its own error state. */
export async function installApi(page: Page, { session = sessions.anonymous, preferences = { locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: 'berry' }, subscription }: MockOptions = {}) {
  await page.route('**/api/v1/**', route => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    const method = request.method()
    if (path === '/api/v1/auth/session') return json(route, session)
    if (path === '/api/v1/account/preferences') return json(route, method === 'PUT' ? request.postDataJSON() : preferences)
    if (path === '/api/v1/account/consents') return json(route, {
      serviceNotice: { acknowledged: true, acknowledgedAt: '2026-10-01T10:00:00Z', noticeVersion: 'v1', textStatus: 'draft' },
      marketing: { optedIn: true, updatedAt: '2026-10-02T10:00:00Z', version: 'v1' },
      history: [{ kind: 'marketing', granted: true, version: 'v1', recordedAt: '2026-10-02T10:00:00Z' }],
    })
    if (path === '/api/v1/antiforgery/token') return json(route, { token: 'csrf' })
    if (path === '/api/v1/auth/capabilities') return json(route, { googleSignInEnabled: true })
    if (path === '/api/v1/templates') return json(route, templates)
    if (path === '/api/v1/public/plans') return json(route, publicPlans)
    if (path === '/api/v1/payments/plans') return json(route, [{ key: 'standard', displayName: 'Standart', amount: 499, currency: 'TRY', billingPeriod: 'one-time' }, { key: 'premium', displayName: 'Premium', amount: 1199, currency: 'TRY', billingPeriod: 'one-time' }])
    if (path === '/api/v1/payments/organization-subscription') return json(route, subscription ?? { subscriptionId: 's1', status: 'Active', paidThroughAtUtc: '2026-12-01T00:00:00Z', cancelAtPeriodEnd: false, cancelRequestedAtUtc: null, planDisplayName: 'Kurumsal', priceAmount: 999, currency: 'TRY', billingPeriod: 'monthly' })
    if (path === '/api/v1/invitations/trash') return json(route, { items: [{ invitationId, headline: 'Elif & Deniz', deletedAtUtc: '2026-10-05T10:00:00Z', purgeAfterUtc: '2026-10-08T10:00:00Z', expected }, { invitationId: secondId, headline: null, deletedAtUtc: '2026-10-04T10:00:00Z', purgeAfterUtc: '2026-10-07T10:00:00Z', expected }], page: 1, pageSize: 20, totalCount: 2, serverNowUtc: '2026-10-06T10:00:00Z' })
    if (path === '/api/v1/invitations' && method === 'GET') return json(route, { items: [
      { id: invitationId, templateKey: 'zamansiz-dugun', rendererVersion: 1, createdAt: '2026-10-01T08:00:00Z', updatedAt: '2026-10-02T08:00:00Z', invitationRevision: 2, contentRevision: 3, headline: 'Elif & Deniz', effectiveState: 'Active' },
      { id: secondId, templateKey: null, rendererVersion: null, createdAt: '2026-10-01T08:00:00Z', updatedAt: '2026-10-01T08:00:00Z', invitationRevision: 1, contentRevision: 1, headline: null, effectiveState: 'Draft' },
    ], page: 1, pageSize: 50, totalCount: 2 })
    if (path === '/api/v1/invitations' && method === 'POST') return json(route, draft)
    if (path === `/api/v1/invitations/${invitationId}` && method === 'GET') return json(route, draft)
    if (path === `/api/v1/invitations/${invitationId}/validation`) return json(route, {
      invitationId, templateKey: 'zamansiz-dugun', rendererVersion: 1, invitationRevision: 2, contentRevision: 3, templateSelected: true, templateAvailable: true,
      requiredFields: [{ field: 'headline', isRecognized: true, isPresent: true }, { field: 'startsAt', isRecognized: true, isPresent: false }],
      recommendedFields: [{ field: 'venue.name', isRecognized: true, isPresent: false }],
    })
    if (path === `/api/v1/invitations/${invitationId}/publication`) return json(route, publicationStatus('Active', { invitationRevision: 2, workingContentRevision: 3 }))
    if (path === `/api/v1/invitations/${invitationId}/statistics`) return json(route, { totalPageViews: 120, rsvpResponseCount: 7, participantCountTotal: 15, memoryCount: 2, readyMediaCount: 3, activeGiftReservationCount: 1 })
    if (path === `/api/v1/invitations/${invitationId}/rsvp`) return json(route, rsvpConfiguration)
    if (path === `/api/v1/invitations/${invitationId}/rsvp/submissions`) return json(route, {
      invitationId, page: 1, pageSize: 25, totalCount: 2,
      summary: { responseCount: 2, totalParticipants: 5, participantCountAvailable: true, questions: [
        { questionId: 'q1', prompt: 'Katılacak mısınız?', type: 'YesNo', answeredCount: 2, valueCounts: [{ value: 'true', label: 'Evet', count: 2 }] },
        { questionId: 'q3', prompt: 'Menü tercihi', type: 'SingleChoice', answeredCount: 2, valueCounts: [{ value: 'o1', label: 'Et', count: 1 }, { value: 'o2', label: 'Vejetaryen', count: 1 }] },
      ] },
      submissions: [{ submissionId: 'a1', submittedAt: '2026-10-03T10:00:00Z', updatedAt: '2026-10-03T10:00:00Z' }, { submissionId: 'a2', submittedAt: '2026-10-04T10:00:00Z', updatedAt: '2026-10-04T10:00:00Z' }],
    })
    if (path === `/api/v1/invitations/${invitationId}/memories/configuration`) return json(route, { invitationId, isEnabled: true, visibility: 'Public', revision: 1, effectiveState: 'Active', inputLimits: memoryLimits })
    if (path === `/api/v1/invitations/${invitationId}/memories`) return json(route, { page: 1, pageSize: 25, totalCount: 2, items: [
      { id: 'm1', displayName: 'Ayşe', text: 'Çok güzel bir geceydi.', emoji: '🎉', state: 'Published', createdAt: '2026-10-03T10:00:00Z', media: [] },
      { id: 'm2', displayName: null, text: 'Tebrikler!', emoji: null, state: 'Hidden', createdAt: '2026-10-04T10:00:00Z', media: [] },
    ] })
    if (path === `/api/v1/invitations/${invitationId}/gifts`) return json(route, [{ id: 'g1', name: 'Kahve makinesi', requestedQuantity: 2, reservedQuantity: 1, remainingQuantity: 1, ordinal: 1, revision: 1 }])
    if (path === `/api/v1/invitations/${invitationId}/gifts/reservations`) return json(route, [])
    if (path === `/api/v1/creator/invitations/${invitationId}/media`) return json(route, { assets: [{ assetId: '44444444-4444-4444-8444-444444444444', kind: 'Image', state: 'Ready', byteLength: 1024, durationSeconds: null, requestedPresentationRole: 'Gallery', placements: [{ role: 'Gallery', sortOrder: 0 }] }] })
    if (path === '/api/v1/admin/mfa/enroll') return json(route, { sharedKey: 'JBSWY3DPEHPK3PXP', authenticatorUri: 'otpauth://totp/Davetiye:admin?secret=JBSWY3DPEHPK3PXP' })
    if (path === '/api/v1/admin/overview') return json(route, overview)
    if (path === '/api/v1/admin/accounts/banned/search') return json(route, { items: [{ accountId: 'acc-1', displayName: 'Örnek Hesap', accountType: 'individual', email: 'ornek@example.com', createdAtUtc: '2026-09-01T10:00:00Z', bannedAtUtc: '2026-10-01T10:00:00Z', reason: 'Kötüye kullanım' }], page: 1, pageSize: 50, totalCount: 1 })
    if (path === '/api/v1/admin/plans') return json(route, adminPlans)
    if (path === '/api/v1/admin/settings') return json(route, { items: [
      { key: 'deletedInvitationRetentionDays', displayName: 'Silinen davetiyelerin saklama süresi', description: 'Çöp kutusu saklama süresi.', value: 30, minimum: 0, maximum: 365, revision: 1 },
      { key: 'abandonedMemoryRetentionDays', displayName: 'Tamamlanmamış anı kayıtlarının saklama süresi', description: 'Terk edilmiş kayıtların saklama süresi.', value: 7, minimum: 0, maximum: 365, revision: 1 },
    ] })
    if (path === '/api/v1/admin/templates') return json(route, [{ id: '22222222-2222-4222-8222-222222222222', key: 'minimal-acilis', name: 'Minimal Açılış', description: 'Sade açılış şablonu', isActive: true, revision: 1 }, { id: '22222222-2222-4222-8222-222222222223', key: 'gece-kina', name: 'Gece Kına', description: null, isActive: false, revision: 3 }])
    if (path === '/api/v1/admin/payments') return json(route, { items: [{ id: 'p1', reference: 'KUT-0001', status: 'Succeeded', planKey: 'premium', amount: 1199, currency: 'TRY', createdAtUtc: '2026-10-01T10:00:00Z', updatedAtUtc: '2026-10-01T10:05:00Z', reversalKind: null, reversedAtUtc: null }, { id: 'p2', reference: 'KUT-0002', status: 'Failed', planKey: 'standard', amount: 499, currency: 'TRY', createdAtUtc: '2026-10-02T10:00:00Z', updatedAtUtc: '2026-10-02T10:05:00Z', reversalKind: 'Refund', reversedAtUtc: '2026-10-03T10:00:00Z' }], page: 1, pageSize: 50, totalCount: 2 })
    if (path === '/api/v1/admin/audit') return json(route, { items: [{ id: 'au1', actorId: 'a'.repeat(8), subjectId: 'b'.repeat(8), occurredAtUtc: '2026-10-03T10:00:00Z', eventType: 'PlanUpdated' }], page: 1, pageSize: 50, totalCount: 1 })
    return route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ status: 404 }) })
  })
}

export interface Scenario {
  name: string
  path: string
  session: keyof typeof sessions
  /** Optional interaction that reveals more UI (open drawer, step through the editor, ...). */
  reveal?: (page: Page) => Promise<void>
  subscription?: unknown
}

async function openDrawer(page: Page) {
  await page.getByRole('banner').getByRole('button', { name: /Hesabım|My Account/ }).click()
  await page.getByRole('dialog').getByRole('radio', { name: 'Güneşli' }).waitFor()
}
async function wizardStep(page: Page, index: number) {
  await page.locator('.wizard-steps button').nth(index).click()
}

export const scenarios: Scenario[] = [
  { name: 'landing anonymous', path: '/', session: 'anonymous' },
  { name: 'landing signed in', path: '/', session: 'creator' },
  { name: 'landing drawer', path: '/', session: 'creator', reveal: openDrawer },
  { name: 'catalog', path: '/sablonlar', session: 'anonymous' },
  { name: 'catalog demo', path: '/sablonlar/zamansiz-dugun', session: 'anonymous' },
  { name: 'login', path: '/giris', session: 'anonymous' },
  { name: 'register', path: '/giris/kayit', session: 'anonymous' },
  { name: 'forgot password', path: '/giris/sifremi-unuttum', session: 'anonymous' },
  { name: 'reset password', path: '/giris/sifre-sifirla', session: 'anonymous' },
  { name: 'confirm email', path: '/giris/e-posta-dogrula', session: 'anonymous' },
  { name: 'privacy', path: '/gizlilik', session: 'anonymous' },
  { name: 'terms', path: '/kullanim-kosullari', session: 'anonymous' },
  { name: 'not found', path: '/yok-boyle-bir-sayfa', session: 'anonymous' },
  { name: 'creator dashboard', path: '/panel/davetiyeler', session: 'creator' },
  { name: 'creator new invitation', path: '/panel/davetiyeler/yeni', session: 'creator' },
  { name: 'creator trash', path: '/panel/cop-kutusu', session: 'creator' },
  { name: 'creator billing active', path: '/panel/plan-odeme', session: 'creator' },
  { name: 'creator billing canceled', path: '/panel/plan-odeme', session: 'creator', subscription: { subscriptionId: 's1', status: 'Canceled', paidThroughAtUtc: '2026-12-01T00:00:00Z', cancelAtPeriodEnd: true, cancelRequestedAtUtc: '2026-10-01T00:00:00Z', planDisplayName: 'Kurumsal', priceAmount: 999, currency: 'TRY', billingPeriod: 'monthly' } },
  { name: 'creator billing expired', path: '/panel/plan-odeme', session: 'creator', subscription: { subscriptionId: 's1', status: 'Expired', paidThroughAtUtc: '2026-09-01T00:00:00Z', cancelAtPeriodEnd: false, cancelRequestedAtUtc: null, planDisplayName: 'Kurumsal', priceAmount: 999, currency: 'TRY', billingPeriod: 'monthly' } },
  { name: 'creator account settings', path: '/panel/hesap', session: 'creator' },
  { name: 'creator account drawer', path: '/panel/hesap', session: 'creator', reveal: openDrawer },
  { name: 'creator rsvp results', path: `/panel/davetiyeler/${invitationId}/rsvp-yanitlari`, session: 'creator' },
  { name: 'creator draft preview', path: `/panel/davetiyeler/${invitationId}/onizleme`, session: 'creator' },
  ...[0, 1, 2, 3, 4, 5, 6].map((index): Scenario => ({ name: `creator editor step ${index + 1}`, path: `/panel/davetiyeler/${invitationId}/duzenle`, session: 'creator', reveal: page => wizardStep(page, index) })),
  { name: 'admin overview', path: '/admin', session: 'admin' },
  { name: 'admin accounts', path: '/admin/accounts', session: 'admin' },
  { name: 'admin plans', path: '/admin/plans', session: 'admin' },
  { name: 'admin settings', path: '/admin/settings', session: 'admin' },
  { name: 'admin templates', path: '/admin/templates', session: 'admin' },
  { name: 'admin payments', path: '/admin/payments', session: 'admin' },
  { name: 'admin audit', path: '/admin/audit', session: 'admin' },
  { name: 'admin drawer', path: '/admin', session: 'admin', reveal: openDrawer },
  { name: 'admin mfa setup', path: '/admin/mfa/setup', session: 'mfaSetup' },
]
