import type { InvitationPublicationState, InvitationPublicationStatus, PublicationExpected } from '../src/api/generated/client'

export const publicationInvitationId = '11111111-1111-1111-1111-111111111111'
export const publicationTemplate = {
  key: 'minimal-acilis', name: 'Minimal Açılış', category: 'Açılış', isPremium: false, rendererVersion: 1,
  previewImageUrl: null, supportedModules: ['hero', 'venue'], requiredFields: ['headline'], recommendedFields: ['venue.name'],
}

export function publicationStatus(state: InvitationPublicationState, expected?: Partial<PublicationExpected>): InvitationPublicationStatus {
  const published = state !== 'Draft'
  const scheduled = state === 'Scheduled'
  return {
    invitationId: publicationInvitationId, publicCode: 'a'.repeat(64), storedState: state, effectiveState: state,
    serverNowUtc: '2026-10-02T09:00:00Z', timeZoneId: 'Europe/Istanbul',
    expected: { invitationRevision: 2, workingContentRevision: 3, publishedContentRevision: published ? 1 : null,
      windowId: published ? '22222222-2222-2222-2222-222222222222' : null, windowRevision: published ? 1 : null, ...expected },
    currentWindow: published ? { id: '22222222-2222-2222-2222-222222222222', grantId: '33333333-3333-3333-3333-333333333333',
      startsAtUtc: scheduled ? '2026-10-03T09:00:00Z' : '2026-10-01T09:00:00Z', endsAtUtc: '2026-10-04T09:00:00Z', timeZoneId: 'Europe/Istanbul', revision: 1 } : null,
    published: published ? { revision: 1, sourceWorkingRevision: 3, templateKey: publicationTemplate.key, rendererVersion: 1 } : null,
    hasPendingChanges: false,
    allowedActions: state === 'Draft' ? ['publish'] : state === 'Active' ? ['update', 'pause'] : state === 'Scheduled' ? ['update', 'cancelSchedule', 'reschedule', 'publishNow'] : state === 'Paused' ? ['update', 'resume'] : ['reactivate'],
    preflight: { templateAvailable: true, isPremiumTemplate: false, missingRequiredFields: [], missingRecommendedFields: ['venue.name'] },
    grantChoices: [{ grantId: null, label: 'Ücretsiz yayın hakkı', kind: 'free', maxPublishDays: 1, maxActiveInvitations: 1, premiumTemplatesEnabled: false }],
    trashRetentionDays: 3,
    currentEntitlements: published ? { maxPublishDays: 30, maxActiveInvitations: 1, premiumTemplatesEnabled: false } : null,
  }
}
