import { readFile, writeFile } from 'node:fs/promises'

const scriptDirectory = new URL('.', import.meta.url)
const specUrl = new URL('../openapi/davetiye.v1.json', scriptDirectory)
const templateUrl = new URL('./api-client.template.ts', scriptDirectory)
const outputUrl = new URL('../src/api/generated/client.ts', scriptDirectory)

async function downloadOpenApiDocument() {
  const endpoint = process.env.DAVETIYE_OPENAPI_URL
  if (!endpoint) {
    throw new Error('DAVETIYE_OPENAPI_URL must point to the running API OpenAPI document.')
  }

  const response = await fetch(endpoint)
  if (!response.ok) {
    throw new Error(`Could not download OpenAPI document: ${response.status}.`)
  }

  return `${JSON.stringify(await response.json(), null, 2)}\n`
}

if (process.argv.includes('--sync')) {
  await writeFile(specUrl, await downloadOpenApiDocument())
}

const specText = await readFile(specUrl, 'utf8')
if (process.argv.includes('--verify')) {
  const downloaded = await downloadOpenApiDocument()
  if (specText !== downloaded) {
    throw new Error('OpenAPI snapshot is stale. Run npm run api:sync against the API under test.')
  }
}

const spec = JSON.parse(specText)

// The Phase 1 M7A + Phase 2 M1/M3/M4 client contract this hand-authored template
// (scripts/api-client.template.ts) depends on. Extending the template to cover a new endpoint means
// adding its path/schema requirement here too, so a live backend that regresses the contract fails
// `api:sync`/`api:verify` loudly instead of silently shipping a client that no longer matches reality.
const requiredGetPaths = [
  '/api/v1/system/info',
  '/api/v1/account/consents',
  '/api/v1/account/preferences',
  '/api/v1/auth/session',
  '/api/v1/auth/capabilities',
  '/api/v1/antiforgery/token',
  '/api/v1/templates',
  '/api/v1/invitations',
  '/api/v1/invitations/{invitationId}',
  '/api/v1/invitations/{invitationId}/validation',
  '/api/v1/invitations/{invitationId}/publication',
  '/api/v1/invitations/{invitationId}/rsvp',
  '/api/v1/invitations/{invitationId}/rsvp/submissions',
  '/api/v1/invitations/{invitationId}/rsvp/submissions/{submissionId}',
  '/api/v1/public/invitations/{publicCode}',
  '/api/v1/public/invitations/{publicCode}/gifts',
  '/api/v1/public/invitations/{publicCode}/gifts/reservations',
  '/api/v1/public/invitations/{publicCode}/rsvp',
  '/api/v1/invitations/{invitationId}/memories/configuration',
  '/api/v1/invitations/{invitationId}/gifts',
  '/api/v1/invitations/{invitationId}/gifts/reservations',
  '/api/v1/invitations/{invitationId}/memories',
  '/api/v1/public/invitations/{publicCode}/memories',
  '/api/v1/public/invitations/{publicCode}/memories/configuration',
  '/api/v1/public/invitations/{publicCode}/memories/{memoryId}/status',
  '/api/v1/public/invitations/{publicCode}/rsvp/submissions/{submissionId}',
  '/api/v1/invitations/trash',
  '/api/v1/public/capabilities',
  '/api/v1/invitations/{invitationId}/statistics',
  '/api/v1/creator/invitations/{invitationId}/media',
  '/api/v1/payments/plans',
  '/api/v1/public/plans',
  '/api/v1/payments/organization-subscription',
  '/api/v1/admin/overview',
  '/api/v1/admin/plans',
  '/api/v1/admin/settings',
  '/api/v1/admin/templates',
  '/api/v1/admin/payments',
  '/api/v1/admin/audit',
]

const requiredPostPaths = [
  '/api/v1/account/deletion-requests',
  '/api/v1/account/deletion-requests/confirm',
  '/api/v1/account/consents/service-notice',
  '/api/v1/auth/google/challenge',
  '/api/v1/auth/register',
  '/api/v1/auth/confirm-email',
  '/api/v1/auth/login',
  '/api/v1/auth/logout',
  '/api/v1/auth/request-password-reset',
  '/api/v1/auth/reset-password',
  '/api/v1/invitations',
  '/api/v1/invitations/{invitationId}/publication/actions',
  '/api/v1/invitations/{invitationId}/checkout',
  '/api/v1/admin/accounts/{accountId}/ban',
  '/api/v1/admin/accounts/{accountId}/unban',
  '/api/v1/admin/accounts/banned/search',
  '/api/v1/admin/mfa/enroll',
  '/api/v1/admin/mfa/verify',
  '/api/v1/admin/mfa/login/complete',
  '/api/v1/payments/organization-subscription/{subscriptionId}/cancel',
  '/api/v1/invitations/{invitationId}/rsvp/questions',
  '/api/v1/invitations/{invitationId}/gifts',
  '/api/v1/invitations/{invitationId}/trash',
  '/api/v1/invitations/{invitationId}/restore',
  '/api/v1/public/invitations/{publicCode}/views',
  '/api/v1/public/invitations/{publicCode}/rsvp/submissions',
  '/api/v1/public/invitations/{publicCode}/gifts/reservations',
  '/api/v1/public/invitations/{publicCode}/memories',
  '/api/v1/public/invitations/{publicCode}/memories/with-media',
  '/api/v1/public/invitations/{publicCode}/memories/{memoryId}/media/intents',
  '/api/v1/public/invitations/{publicCode}/memories/{memoryId}/finalize',
  '/api/v1/public/invitations/{publicCode}/memories/{memoryId}/media/{assetId}/delivery',
  '/api/v1/public/invitations/{publicCode}/media/{assetId}/delivery',
  '/api/v1/invitations/{invitationId}/memories/{memoryId}/media/{assetId}/delivery',
]

const requiredPutPaths = [
  '/api/v1/account/consents/marketing',
  '/api/v1/account/preferences',
  '/api/v1/admin/plans/{planId}',
  '/api/v1/admin/settings/{key}',
  '/api/v1/admin/templates/{templateId}',
  '/api/v1/invitations/{invitationId}/draft',
  '/api/v1/invitations/{invitationId}/template',
  '/api/v1/invitations/{invitationId}/rsvp',
  '/api/v1/invitations/{invitationId}/rsvp/questions/{questionId}',
  '/api/v1/invitations/{invitationId}/rsvp/questions/order',
  '/api/v1/invitations/{invitationId}/memories/configuration',
  '/api/v1/invitations/{invitationId}/memories/{memoryId}/hide',
  '/api/v1/invitations/{invitationId}/gifts/{itemId}',
  '/api/v1/invitations/{invitationId}/gifts/order',
  '/api/v1/creator/invitations/{invitationId}/media/{assetId}/placement',
  '/api/v1/public/invitations/{publicCode}/rsvp/submissions/{submissionId}',
]

const requiredDeletePaths = [
  '/api/v1/creator/invitations/{invitationId}/media/{assetId}',
  '/api/v1/invitations/{invitationId}/rsvp/questions/{questionId}',
  '/api/v1/invitations/{invitationId}/rsvp/submissions/{submissionId}',
  '/api/v1/invitations/{invitationId}/memories/{memoryId}',
  '/api/v1/invitations/{invitationId}/gifts/{itemId}',
  '/api/v1/invitations/{invitationId}/gifts/reservations/{reservationId}',
  '/api/v1/public/invitations/{publicCode}/gifts/reservations/{reservationId}',
]

const requiredRegisterFields = ['email', 'password', 'displayName', 'accountType', 'serviceNoticeAcknowledged']
const requiredGoogleLinkFields = ['password']
const requiredCreateDraftFields = ['contentSchemaVersion', 'content', 'templateKey']
const requiredAutosaveDraftFields = ['contentSchemaVersion', 'content', 'expectedContentRevision']
const requiredSelectTemplateFields = ['templateKey', 'expectedInvitationRevision']
const requiredDraftDetailsFields = [
  'id', 'templateKey', 'rendererVersion', 'createdAt', 'invitationRevision',
  'contentSchemaVersion', 'content', 'updatedAt', 'contentRevision',
]
const publicationSchemas = [
  ['PublicationActionRequest', ['action', 'expected', 'publication', 'publishWorkingContent', 'proceedWithRecommendedWarnings']],
  ['PublicationRevisions', ['invitationRevision', 'workingContentRevision', 'publishedContentRevision', 'windowId', 'windowRevision']],
  ['PublicationWindowRequest', ['mode', 'startsAtLocal', 'endsAtLocal', 'timeZoneId', 'requestedGrantId']],
  ['PublicationStatus', ['invitationId', 'publicCode', 'storedState', 'effectiveState', 'serverNowUtc', 'timeZoneId', 'expected', 'currentWindow', 'published', 'hasPendingChanges', 'allowedActions', 'preflight', 'grantChoices', 'currentEntitlements', 'trashRetentionDays']],
  ['PublicationWindowStatus', ['id', 'grantId', 'startsAtUtc', 'endsAtUtc', 'timeZoneId', 'revision']],
  ['PublishedSnapshotStatus', ['revision', 'sourceWorkingRevision', 'templateKey', 'rendererVersion']],
  ['PublicationPreflightStatus', ['templateAvailable', 'isPremiumTemplate', 'missingRequiredFields', 'missingRecommendedFields']],
  ['PublicationGrantChoice', ['grantId', 'label', 'kind', 'maxPublishDays', 'maxActiveInvitations', 'premiumTemplatesEnabled']],
  ['PublicationEntitlementStatus', ['maxPublishDays', 'maxActiveInvitations', 'premiumTemplatesEnabled']],
  ['InvitationDraftSummary', ['effectiveState', 'headline']],
  ['InvitationTrashRequest', ['expected', 'expectedRetentionDays']],
  ['InvitationTrashItem', ['invitationId', 'headline', 'deletedAtUtc', 'purgeAfterUtc', 'expected']],
  ['InvitationTrashPage', ['items', 'page', 'pageSize', 'totalCount', 'serverNowUtc']],
  ['PublicInvitationCapabilities', ['canonicalBaseUrl', 'mapEmbedEnabled', 'mapEmbedOrigin', 'mapEmbedPath', 'mapEmbedKey']],
  ['InvitationStatistics', ['totalPageViews', 'rsvpResponseCount', 'participantCountTotal', 'memoryCount', 'readyMediaCount', 'activeGiftReservationCount']],
  ['DraftContentInput', ['contacts', 'announcement', 'faqs', 'transportStops']],
  ['DraftContactInput', ['name', 'role', 'phone']],
  ['DraftFaqInput', ['question', 'answer']],
  ['DraftTransportStopInput', ['name', 'address', 'departureTime']],
]
const publicInvitationSchemas = [
  ['PublicInvitationActive', ['status', 'templateKey', 'rendererVersion', 'contentSchemaVersion', 'content', 'media']],
  ['PublicInvitationContent', ['eventType', 'headline', 'hostNames', 'message', 'startsAt', 'timeZoneId', 'venue', 'programItems', 'contacts', 'announcement', 'faqs', 'transportStops']],
  ['PublicInvitationVenue', ['name', 'address']],
  ['PublicInvitationProgramItem', ['title', 'description', 'startsAt']],
  ['PublicInvitationContact', ['name', 'role', 'phone']],
  ['PublicInvitationFaq', ['question', 'answer']],
  ['PublicInvitationTransportStop', ['name', 'address', 'departureTime']],
  ['PublicSnapshotMediaPlacement', ['assetId', 'kind', 'role', 'sortOrder']],
  ['PublicMediaDeliveryResponse', ['kind', 'url', 'expiresAt']],
  ['CreatorMediaLibraryResponse', ['assets']],
  ['CreatorMediaAssetView', ['assetId', 'kind', 'state', 'byteLength', 'durationSeconds', 'requestedPresentationRole', 'placements']],
  ['CreatorMediaPlacementView', ['role', 'sortOrder']],
  ['SetCreatorMediaPlacementRequest', ['role', 'sortOrder']],
]
const publicPlanCatalogFields = [
  'key', 'displayName', 'description', 'priceAmount', 'currency', 'billingPeriod', 'maxPublishDays',
  'maxActiveInvitations', 'maxImages', 'maxVideos', 'maxRSVPResponses', 'memoriesEnabled', 'giftRegistryEnabled',
  'premiumTemplatesEnabled',
]
const publicRsvpSchemas = [
  ['PublicRsvpConfiguration', ['status', 'questions', 'answerLimits']],
  ['PublicRsvpQuestion', ['id', 'prompt', 'type', 'isRequired', 'sortOrder', 'options', 'minimumNumberValue', 'maximumNumberValue']],
  ['PublicRsvpOption', ['id', 'label', 'sortOrder']],
  ['PublicRsvpAnswerLimits', ['maxShortTextAnswerCharacters', 'maxLongTextAnswerCharacters', 'minimumParticipantCount', 'maximumParticipantCount', 'maxMultipleChoiceSelections']],
  ['PublicRsvpGuestSubmission', ['submissionId', 'updatedAt', 'answers']],
  ['PublicRsvpGuestAnswer', ['questionId', 'textValue', 'numberValue', 'booleanValue', 'selectedOptionIds']],
]
const creatorRsvpSchemas = [
  ['CreatorRsvpConfiguration', ['invitationId', 'enabled', 'revision', 'effectiveState', 'questions', 'inputLimits']],
  ['CreatorRsvpInputLimits', ['maxActiveQuestionsPerInvitation', 'maxQuestionPromptCharacters', 'maxOptionLabelCharacters', 'maxDefinedOptionsPerChoiceQuestion', 'maxShortTextAnswerCharacters', 'maxLongTextAnswerCharacters', 'minimumParticipantCount', 'maximumParticipantCount', 'maxMultipleChoiceSelections']],
  ['CreatorRsvpSubmissionPage', ['invitationId', 'page', 'pageSize', 'totalCount', 'summary', 'submissions']],
  ['CreatorRsvpSummary', ['responseCount', 'questions', 'totalParticipants', 'participantCountAvailable']],
  ['CreatorRsvpQuestionSummary', ['questionId', 'prompt', 'type', 'answeredCount', 'valueCounts']],
  ['CreatorRsvpValueCount', ['value', 'label', 'count']],
  ['CreatorRsvpSubmissionListItem', ['submissionId', 'submittedAt', 'updatedAt']],
  ['CreatorRsvpSubmissionDetail', ['submissionId', 'submittedAt', 'updatedAt', 'answers']],
  ['CreatorRsvpSubmissionAnswer', ['questionId', 'prompt', 'type', 'semanticRole', 'textValue', 'numberValue', 'booleanValue', 'selectedOptions']],
  ['CreatorRsvpSelectedOption', ['optionId', 'label']],
]
const creatorMemorySchemas = [
  ['CreatorMemoriesPage', ['page', 'pageSize', 'totalCount', 'items']],
  ['CreatorMemoryItem', ['id', 'displayName', 'text', 'emoji', 'state', 'createdAt', 'media']],
  ['CreatorMemoryMediaItem', ['assetId', 'kind', 'status']],
  ['CreatorMemoryPreviewResponse', ['mediaKind', 'deliveryUrl', 'expiresAt']],
]
const creatorGiftSchemas = [
  ['CreatorGiftItem', ['id', 'name', 'requestedQuantity', 'reservedQuantity', 'remainingQuantity', 'ordinal', 'revision']],
]
const requiredDraftPageFields = ['items', 'page', 'pageSize', 'totalCount']
const requiredTemplateCatalogFields = [
  'key', 'name', 'description', 'category', 'isPremium', 'rendererVersion', 'previewImageUrl',
  'supportedModules', 'requiredFields', 'recommendedFields',
]
const requiredDraftValidationFields = [
  'invitationId', 'templateKey', 'rendererVersion', 'invitationRevision', 'contentRevision',
  'templateSelected', 'templateAvailable',
  'requiredFields', 'recommendedFields',
]
const requiredDraftFieldValidationFields = ['field', 'isRecognized', 'isPresent']

function assertContract(condition, message) {
  if (!condition) {
    throw new Error(`OpenAPI snapshot does not satisfy the Creator API client contract: ${message}`)
  }
}

function schemaReferences(schema, name) {
  const reference = `#/components/schemas/${name}`
  return schema?.$ref === reference
    || ['oneOf', 'anyOf'].some(composition => schema?.[composition]?.some(item => item.$ref === reference))
}

assertContract(String(spec.openapi).startsWith('3.1.'), 'unexpected openapi version')

for (const path of requiredGetPaths) {
  assertContract(Boolean(spec.paths?.[path]?.get), `missing GET ${path}`)
}

for (const path of requiredPostPaths) {
  assertContract(Boolean(spec.paths?.[path]?.post), `missing POST ${path}`)
}

assertContract(
  spec.paths?.['/api/v1/auth/session']?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/SessionAccessSnapshot',
  'GET /api/v1/auth/session must return SessionAccessSnapshot',
)
assertContract(
  ['authenticated', 'serviceNoticeRequired'].every(field =>
    spec.components?.schemas?.SessionAccessSnapshot?.properties?.[field]?.type === 'boolean'
      && spec.components?.schemas?.SessionAccessSnapshot?.required?.includes(field)),
  'SessionAccessSnapshot must require authenticated and serviceNoticeRequired flags',
)
assertContract(
  spec.components?.schemas?.SessionAccessType?.enum?.includes('mfa-setup-required-super-admin'),
  'SessionAccessSnapshot.access must include mfa-setup-required-super-admin',
)
assertContract(
  spec.paths?.['/api/v1/admin/mfa/enroll']?.post?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminMfaEnrollment',
  'POST /api/v1/admin/mfa/enroll must return AdminMfaEnrollment',
)
assertContract(
  spec.paths?.['/api/v1/admin/mfa/verify']?.post?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminMfaVerification',
  'POST /api/v1/admin/mfa/verify must return AdminMfaVerification',
)

for (const path of requiredPutPaths) {
  assertContract(Boolean(spec.paths?.[path]?.put), `missing PUT ${path}`)
}

for (const path of requiredDeletePaths) {
  assertContract(Boolean(spec.paths?.[path]?.delete), `missing DELETE ${path}`)
}

assertContract(
  spec.paths?.['/api/v1/admin/settings']?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminSystemSettingsResponse',
  'GET /api/v1/admin/settings must return AdminSystemSettingsResponse',
)
assertContract(
  (() => {
    const schema = spec.paths?.['/api/v1/admin/settings/{key}']?.put?.requestBody?.content?.['application/json']?.schema
    return schema?.$ref === '#/components/schemas/AdminSystemSettingUpdateRequest'
      || schema?.oneOf?.some(item => item.$ref === '#/components/schemas/AdminSystemSettingUpdateRequest')
  })(),
  'PUT /api/v1/admin/settings/{key} must accept AdminSystemSettingUpdateRequest',
)
assertContract(
  spec.paths?.['/api/v1/admin/settings/{key}']?.put?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminSystemSettingItem',
  'PUT /api/v1/admin/settings/{key} must return AdminSystemSettingItem',
)
assertContract(
  ['key', 'displayName', 'description', 'value', 'minimum', 'maximum', 'revision'].every(field =>
    Object.prototype.hasOwnProperty.call(spec.components?.schemas?.AdminSystemSettingItem?.properties ?? {}, field)
      && spec.components?.schemas?.AdminSystemSettingItem?.required?.includes(field)),
  'AdminSystemSettingItem must require and expose all approved setting fields',
)
assertContract(
  ['expectedRevision', 'value'].every(field =>
    Object.prototype.hasOwnProperty.call(spec.components?.schemas?.AdminSystemSettingUpdateRequest?.properties ?? {}, field)
      && spec.components?.schemas?.AdminSystemSettingUpdateRequest?.required?.includes(field)),
  'AdminSystemSettingUpdateRequest must require expectedRevision and value',
)

const creatorRsvpListPath = '/api/v1/invitations/{invitationId}/rsvp/submissions'
const creatorRsvpDetailPath = '/api/v1/invitations/{invitationId}/rsvp/submissions/{submissionId}'
const creatorRsvpListOperation = spec.paths?.[creatorRsvpListPath]?.get
for (const name of ['page', 'pageSize']) {
  assertContract(
    creatorRsvpListOperation?.parameters?.some(parameter => parameter.name === name && parameter.in === 'query'),
    `GET ${creatorRsvpListPath} must expose ${name} pagination query parameter`,
  )
}
assertContract(
  creatorRsvpListOperation?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/CreatorRsvpSubmissionPage',
  `GET ${creatorRsvpListPath} must return CreatorRsvpSubmissionPage`,
)
assertContract(
  spec.paths?.[creatorRsvpDetailPath]?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/CreatorRsvpSubmissionDetail',
  `GET ${creatorRsvpDetailPath} must return CreatorRsvpSubmissionDetail`,
)
assertContract(
  Boolean(spec.paths?.[creatorRsvpDetailPath]?.delete?.responses?.['204']),
  `DELETE ${creatorRsvpDetailPath} must declare 204 No Content`,
)

const organizationSubscriptionPath = '/api/v1/payments/organization-subscription'
const organizationSubscriptionResponse = spec.paths?.[organizationSubscriptionPath]?.get?.responses?.['200']?.content?.['application/json']?.schema
const organizationSubscriptionSnapshotSchema = spec.components?.schemas?.OrganizationSubscriptionSnapshotResponse
const organizationSubscriptionSnapshotBranch = organizationSubscriptionResponse?.anyOf?.find(item =>
  item.$ref === '#/components/schemas/OrganizationSubscriptionSnapshotResponse' || item.type === 'object',
)
const inlineOrganizationSubscriptionProperties = organizationSubscriptionSnapshotBranch?.properties
const namedOrganizationSubscriptionProperties = organizationSubscriptionSnapshotSchema?.properties
const organizationSubscriptionShapeMatches = organizationSubscriptionSnapshotBranch?.$ref === '#/components/schemas/OrganizationSubscriptionSnapshotResponse'
  || (organizationSubscriptionSnapshotBranch?.type === 'object'
    && namedOrganizationSubscriptionProperties
    && inlineOrganizationSubscriptionProperties
    && Object.keys(namedOrganizationSubscriptionProperties).length === Object.keys(inlineOrganizationSubscriptionProperties).length
    && Object.entries(namedOrganizationSubscriptionProperties).every(([name, schema]) =>
      JSON.stringify(inlineOrganizationSubscriptionProperties[name]) === JSON.stringify(schema),
    ))
assertContract(Boolean(organizationSubscriptionShapeMatches),
  `GET ${organizationSubscriptionPath} must return an optional OrganizationSubscriptionSnapshot`)
assertContract(Boolean(organizationSubscriptionResponse?.anyOf?.some(item => item.type === 'null')),
  `GET ${organizationSubscriptionPath} must describe a null snapshot when no subscription exists`)
const cancelOrganizationSubscriptionPath = '/api/v1/payments/organization-subscription/{subscriptionId}/cancel'
assertContract(spec.paths?.[cancelOrganizationSubscriptionPath]?.post?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/OrganizationSubscriptionCancellationResponse',
  `POST ${cancelOrganizationSubscriptionPath} must return OrganizationSubscriptionCancellationResponse`)
assertContract(!spec.paths?.[cancelOrganizationSubscriptionPath]?.post?.requestBody,
  `POST ${cancelOrganizationSubscriptionPath} must not accept a client-defined subscription change`)

const adminBanPath = '/api/v1/admin/accounts/{accountId}/ban'
const adminBanOperation = spec.paths?.[adminBanPath]?.post
const adminBanBodySchema = adminBanOperation?.requestBody?.content?.['application/json']?.schema
const adminBanBodyReferencesRequest = schemaReferences(adminBanBodySchema, 'AdminBanRequest')
assertContract(adminBanOperation?.parameters?.some(parameter => parameter.name === 'accountId' && parameter.in === 'path' && parameter.required),
  `POST ${adminBanPath} must require accountId`)
assertContract(adminBanBodyReferencesRequest,
  `POST ${adminBanPath} must accept AdminBanRequest`)
assertContract(Boolean(adminBanOperation?.responses?.['204']),
  `POST ${adminBanPath} must declare 204 No Content`)
const adminBanRequestSchema = spec.components?.schemas?.AdminBanRequest
assertContract(Boolean(adminBanRequestSchema?.properties?.reason) && adminBanRequestSchema.required?.includes('reason'),
  'AdminBanRequest.reason must be required')
assertContract(Boolean(adminBanRequestSchema?.properties?.internalNote) && !adminBanRequestSchema.required?.includes('internalNote'),
  'AdminBanRequest.internalNote must be optional')
assertContract(adminBanRequestSchema?.properties?.internalNote?.type?.includes('null'),
  'AdminBanRequest.internalNote must allow null')

const adminUnbanPath = '/api/v1/admin/accounts/{accountId}/unban'
const adminUnbanOperation = spec.paths?.[adminUnbanPath]?.post
assertContract(adminUnbanOperation?.parameters?.some(parameter => parameter.name === 'accountId' && parameter.in === 'path' && parameter.required),
  `POST ${adminUnbanPath} must require accountId`)
assertContract(!adminUnbanOperation?.requestBody,
  `POST ${adminUnbanPath} must not accept a request body`)
assertContract(Boolean(adminUnbanOperation?.responses?.['204']),
  `POST ${adminUnbanPath} must declare 204 No Content`)

const adminBannedAccountsPath = '/api/v1/admin/accounts/banned/search'
const adminBannedAccountsOperation = spec.paths?.[adminBannedAccountsPath]?.post
assertContract(adminBannedAccountsOperation?.requestBody?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminBannedAccountSearchRequest',
  `POST ${adminBannedAccountsPath} must accept AdminBannedAccountSearchRequest`)
assertContract(!adminBannedAccountsOperation?.parameters?.some(parameter => parameter.in === 'query'),
  `POST ${adminBannedAccountsPath} must not expose search values in the URL`)
assertContract(adminBannedAccountsOperation?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminBannedAccountPage',
  `POST ${adminBannedAccountsPath} must return AdminBannedAccountPage`)
const adminBannedAccountSearchSchema = spec.components?.schemas?.AdminBannedAccountSearchRequest
assertContract(JSON.stringify(Object.keys(adminBannedAccountSearchSchema?.properties ?? []).sort()) === JSON.stringify(['emailPrefix', 'page', 'pageSize']),
  'AdminBannedAccountSearchRequest must contain only page, pageSize, and optional emailPrefix')
assertContract(!['page', 'pageSize', 'emailPrefix'].some(field => adminBannedAccountSearchSchema?.required?.includes(field)),
  'AdminBannedAccountSearchRequest paging and emailPrefix fields must be optional')
assertContract(['page', 'pageSize'].every(field =>
  adminBannedAccountSearchSchema?.properties?.[field]?.type?.includes('null')
    && adminBannedAccountSearchSchema?.properties?.[field]?.type?.includes('integer'),
), 'AdminBannedAccountSearchRequest paging fields must allow optional nullable integers')
assertContract(adminBannedAccountSearchSchema?.properties?.emailPrefix?.type?.includes('null')
  && adminBannedAccountSearchSchema?.properties?.emailPrefix?.type?.includes('string'),
  'AdminBannedAccountSearchRequest.emailPrefix must allow an optional nullable string')
const adminBannedAccountPageSchema = spec.components?.schemas?.AdminBannedAccountPage
for (const field of ['items', 'page', 'pageSize', 'totalCount']) {
  assertContract(Boolean(adminBannedAccountPageSchema?.properties?.[field]) && adminBannedAccountPageSchema.required?.includes(field),
    `AdminBannedAccountPage.${field} must be required`)
}
const adminBannedAccountSchemaName = adminBannedAccountPageSchema?.properties?.items?.items?.$ref?.split('/').at(-1)
const adminBannedAccountSchema = spec.components?.schemas?.[adminBannedAccountSchemaName]
const adminBannedAccountFields = ['accountId', 'displayName', 'accountType', 'email', 'createdAtUtc', 'bannedAtUtc', 'reason']
assertContract(Boolean(adminBannedAccountSchema), 'AdminBannedAccountPage.items must reference an item schema')
assertContract(JSON.stringify(Object.keys(adminBannedAccountSchema.properties ?? []).sort()) === JSON.stringify([...adminBannedAccountFields].sort()),
  `${adminBannedAccountSchemaName} must contain only the allowlisted account fields`)
for (const field of adminBannedAccountFields) {
  assertContract(adminBannedAccountSchema.required?.includes(field), `${adminBannedAccountSchemaName}.${field} must be required`)
}
const adminBannedAccountTypeSchemaName = adminBannedAccountSchema.properties?.accountType?.$ref?.split('/').at(-1)
const adminBannedAccountTypeSchema = spec.components?.schemas?.[adminBannedAccountTypeSchemaName]
assertContract(JSON.stringify(adminBannedAccountTypeSchema?.enum) === JSON.stringify(['individual', 'organization']),
  `${adminBannedAccountTypeSchemaName} must allow individual or organization`)
assertContract(adminBannedAccountSchema.properties?.email?.type?.includes('null'),
  `${adminBannedAccountSchemaName}.email must allow null`)

const adminTemplatesPath = '/api/v1/admin/templates'
const adminTemplatesSchema = spec.paths?.[adminTemplatesPath]?.get?.responses?.['200']?.content?.['application/json']?.schema
assertContract(adminTemplatesSchema?.type === 'array' && adminTemplatesSchema.items?.$ref === '#/components/schemas/AdminTemplateItem',
  `GET ${adminTemplatesPath} must return AdminTemplateItem[]`)
const adminTemplateFields = ['id', 'key', 'name', 'description', 'isActive', 'revision']
const adminTemplateItemSchema = spec.components?.schemas?.AdminTemplateItem
assertContract(Boolean(adminTemplateItemSchema), 'missing AdminTemplateItem schema')
assertContract(JSON.stringify(Object.keys(adminTemplateItemSchema.properties ?? []).sort()) === JSON.stringify([...adminTemplateFields].sort()),
  'AdminTemplateItem must contain only identity, editable metadata, and revision')
for (const field of adminTemplateFields) assertContract(adminTemplateItemSchema.required?.includes(field), `AdminTemplateItem.${field} must be required`)
assertContract(adminTemplateItemSchema.properties?.description?.type?.includes('null'), 'AdminTemplateItem.description must allow null')

const adminTemplateUpdatePath = '/api/v1/admin/templates/{templateId}'
const adminTemplateUpdateOperation = spec.paths?.[adminTemplateUpdatePath]?.put
assertContract(adminTemplateUpdateOperation?.parameters?.some(parameter => parameter.name === 'templateId' && parameter.in === 'path' && parameter.required),
  `PUT ${adminTemplateUpdatePath} must require templateId`)
assertContract(schemaReferences(adminTemplateUpdateOperation?.requestBody?.content?.['application/json']?.schema, 'AdminTemplateUpdateRequest'),
  `PUT ${adminTemplateUpdatePath} must accept AdminTemplateUpdateRequest`)
assertContract(adminTemplateUpdateOperation?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminTemplateItem',
  `PUT ${adminTemplateUpdatePath} must return AdminTemplateItem`)
assertContract(Boolean(adminTemplateUpdateOperation?.responses?.['409']), `PUT ${adminTemplateUpdatePath} must declare revision conflicts`)
const adminTemplateRequestSchema = spec.components?.schemas?.AdminTemplateUpdateRequest
const adminTemplateRequestFields = ['expectedRevision', 'name', 'description', 'isActive']
assertContract(JSON.stringify(Object.keys(adminTemplateRequestSchema?.properties ?? []).sort()) === JSON.stringify([...adminTemplateRequestFields].sort()),
  'AdminTemplateUpdateRequest must contain only expectedRevision, name, description, and isActive')
for (const field of adminTemplateRequestFields) assertContract(adminTemplateRequestSchema.required?.includes(field), `AdminTemplateUpdateRequest.${field} must be required`)
assertContract(adminTemplateRequestSchema.properties?.description?.type?.includes('null'), 'AdminTemplateUpdateRequest.description must allow null')

const adminPlansPath = '/api/v1/admin/plans'
const adminPlansOperation = spec.paths?.[adminPlansPath]?.get
assertContract(adminPlansOperation?.responses?.['200']?.content?.['application/json']?.schema?.type === 'array' && adminPlansOperation.responses['200'].content['application/json'].schema.items?.$ref === '#/components/schemas/AdminPlanItem',
  `GET ${adminPlansPath} must return AdminPlanItem[]`)
const adminPlanFields = ['id', 'key', 'displayName', 'description', 'priceAmount', 'currency', 'billingKind', 'entitlements', 'revision']
const adminPlanItemSchema = spec.components?.schemas?.AdminPlanItem
assertContract(Boolean(adminPlanItemSchema), 'missing AdminPlanItem schema')
assertContract(JSON.stringify(Object.keys(adminPlanItemSchema.properties ?? []).sort()) === JSON.stringify([...adminPlanFields].sort()),
  'AdminPlanItem must contain identity, price snapshot, billing kind, typed entitlements, and revision')
for (const field of adminPlanFields) assertContract(adminPlanItemSchema.required?.includes(field), `AdminPlanItem.${field} must be required`)
const adminPlanEntitlementSchema = spec.components?.schemas?.AdminPlanEntitlementItem
assertContract(Boolean(adminPlanEntitlementSchema), 'missing AdminPlanEntitlementItem schema')
assertContract(JSON.stringify(Object.keys(adminPlanEntitlementSchema.properties ?? []).sort()) === JSON.stringify(['key', 'numericValue', 'booleanValue'].sort()),
  'AdminPlanEntitlementItem must contain only key and typed values')
assertContract(adminPlanEntitlementSchema.properties?.numericValue?.type?.includes('null') && adminPlanEntitlementSchema.properties?.booleanValue?.type?.includes('null'),
  'AdminPlanEntitlementItem must represent nullable numeric and boolean variants')

const adminPlanUpdatePath = '/api/v1/admin/plans/{planId}'
const adminPlanUpdateOperation = spec.paths?.[adminPlanUpdatePath]?.put
assertContract(adminPlanUpdateOperation?.parameters?.some(parameter => parameter.name === 'planId' && parameter.in === 'path' && parameter.required),
  `PUT ${adminPlanUpdatePath} must require planId`)
assertContract(schemaReferences(adminPlanUpdateOperation?.requestBody?.content?.['application/json']?.schema, 'AdminPlanUpdateRequest'),
  `PUT ${adminPlanUpdatePath} must accept AdminPlanUpdateRequest`)
assertContract(adminPlanUpdateOperation?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AdminPlanItem',
  `PUT ${adminPlanUpdatePath} must return AdminPlanItem`)
assertContract(Boolean(adminPlanUpdateOperation?.responses?.['409']), `PUT ${adminPlanUpdatePath} must declare revision conflicts`)
const adminPlanRequestSchema = spec.components?.schemas?.AdminPlanUpdateRequest
const adminPlanRequestFields = ['expectedRevision', 'displayName', 'description', 'priceAmount', 'billingKind', 'entitlements']
assertContract(Boolean(adminPlanRequestSchema), 'missing AdminPlanUpdateRequest schema')
assertContract(JSON.stringify(Object.keys(adminPlanRequestSchema.properties ?? []).sort()) === JSON.stringify([...adminPlanRequestFields].sort()),
  'AdminPlanUpdateRequest must contain only approved editable fields and revision')
for (const field of adminPlanRequestFields) assertContract(adminPlanRequestSchema.required?.includes(field), `AdminPlanUpdateRequest.${field} must be required`)
assertContract(adminPlanRequestSchema.properties?.description?.type?.includes('null'), 'AdminPlanUpdateRequest.description must allow null')
assertContract(Boolean(adminPlanRequestSchema.properties?.priceAmount) && !adminPlanRequestSchema.properties?.currency,
  'AdminPlanUpdateRequest must allow priceAmount but keep currency server-owned')
assertContract(adminPlanRequestSchema.properties?.priceAmount?.type?.some?.(type => type === 'number') || adminPlanRequestSchema.properties?.priceAmount?.type === 'number',
  'AdminPlanUpdateRequest.priceAmount must be numeric')

for (const [path, schemaName, allowedItemFields] of [
  ['/api/v1/admin/payments', 'AdminPaymentPage', ['id', 'reference', 'status', 'planKey', 'amount', 'currency', 'createdAtUtc', 'updatedAtUtc', 'reversalKind', 'reversedAtUtc']],
  ['/api/v1/admin/audit', 'AdminAuditPage', ['id', 'actorId', 'subjectId', 'occurredAtUtc', 'eventType']],
]) {
  const operation = spec.paths?.[path]?.get
  for (const name of ['page', 'pageSize']) {
    assertContract(operation?.parameters?.some(parameter => parameter.name === name && parameter.in === 'query'),
      `GET ${path} must expose ${name} pagination query parameter`)
  }
  assertContract(operation?.responses?.['200']?.content?.['application/json']?.schema?.$ref === `#/components/schemas/${schemaName}`,
    `GET ${path} must return ${schemaName}`)
  const pageSchema = spec.components?.schemas?.[schemaName]
  for (const field of ['items', 'page', 'pageSize', 'totalCount']) {
    assertContract(Boolean(pageSchema?.properties?.[field]) && pageSchema.required?.includes(field),
      `${schemaName}.${field} must be required`)
  }
  const itemSchemaName = pageSchema?.properties?.items?.items?.$ref?.split('/').at(-1)
  const itemSchema = spec.components?.schemas?.[itemSchemaName]
  assertContract(Boolean(itemSchema), `${schemaName}.items must reference an item schema`)
  assertContract(JSON.stringify(Object.keys(itemSchema.properties ?? []).sort()) === JSON.stringify([...allowedItemFields].sort()),
    `${itemSchemaName} must contain only the allowlisted operational fields`)
  for (const field of allowedItemFields) {
    assertContract(itemSchema.required?.includes(field), `${itemSchemaName}.${field} must be required`)
  }
}

assertContract(
  !spec.paths?.['/api/v1/public/invitations/{publicCode}/media/{assetId}/delivery']?.post?.requestBody,
  'public media delivery exchange must not accept a request body',
)

const googleChallenge = spec.paths?.['/api/v1/auth/google/challenge']?.post
assertContract(Boolean(googleChallenge), 'missing POST /api/v1/auth/google/challenge')
assertContract(!spec.paths?.['/api/v1/auth/google/challenge']?.get, 'Google challenge must not accept GET/query consent')
assertContract(Boolean(spec.paths?.['/api/v1/auth/google/complete']?.get), 'missing GET /api/v1/auth/google/complete')
assertContract(Boolean(spec.paths?.['/api/v1/auth/google/link/confirm']?.post), 'missing POST /api/v1/auth/google/link/confirm')

const googleChallengeFormReference = googleChallenge.requestBody?.content?.['application/x-www-form-urlencoded']?.schema
const googleChallengeFormSchema = googleChallengeFormReference?.$ref
  ? spec.components?.schemas?.[googleChallengeFormReference.$ref.split('/').at(-1)]
  : googleChallengeFormReference
assertContract(schemaReferences(googleChallengeFormReference, 'GoogleChallengeFormData'),
  'POST /api/v1/auth/google/challenge must accept GoogleChallengeFormData as a native form')
assertContract(!googleChallenge.parameters?.some(parameter =>
  ['accountType', 'serviceNoticeAcknowledged', 'marketingOptIn'].includes(parameter.name)),
  'Google challenge consent and account type must not be query parameters')
assertContract(
  googleChallenge.responses?.['302'] !== undefined,
  'POST /api/v1/auth/google/challenge must return a top-level OAuth redirect',
)
for (const field of ['accountType', 'serviceNoticeAcknowledged', '__RequestVerificationToken']) {
  assertContract(googleChallengeFormSchema.required?.includes(field),
    `GoogleChallengeFormData must require ${field}`)
}
assertContract(
  googleChallengeFormSchema.properties?.marketingOptIn?.type === 'boolean' ||
    googleChallengeFormSchema.properties?.marketingOptIn?.type?.includes('boolean'),
  'GoogleChallengeFormData must support the separate marketing preference',
)
assertContract(
  spec.paths?.['/api/v1/account/consents']?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AccountConsentSnapshot',
  'GET /api/v1/account/consents must return AccountConsentSnapshot',
)
assertContract(
  spec.paths?.['/api/v1/account/preferences']?.get?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AccountPreferences'
    && spec.paths?.['/api/v1/account/preferences']?.put?.requestBody?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AccountPreferences'
    && spec.paths?.['/api/v1/account/preferences']?.put?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AccountPreferences',
  'GET/PUT /api/v1/account/preferences must use the AccountPreferences schema',
)
assertContract(
  spec.paths?.['/api/v1/account/consents/marketing']?.put?.requestBody?.content?.['application/json']?.schema?.$ref === '#/components/schemas/MarketingPreferenceHttpRequest',
  'PUT /api/v1/account/consents/marketing must accept MarketingPreferenceHttpRequest',
)
assertContract(
  spec.paths?.['/api/v1/account/consents/service-notice']?.post?.requestBody?.content?.['application/json']?.schema?.$ref === '#/components/schemas/ServiceNoticeAcknowledgementHttpRequest'
    && spec.paths?.['/api/v1/account/consents/service-notice']?.post?.responses?.['200']?.content?.['application/json']?.schema?.$ref === '#/components/schemas/AccountConsentSnapshot',
  'POST /api/v1/account/consents/service-notice must accept explicit acknowledgement and return AccountConsentSnapshot',
)
const googleLinkSchema = spec.components?.schemas?.ConfirmGoogleAccountLinkRequest
assertContract(Boolean(googleLinkSchema), 'missing ConfirmGoogleAccountLinkRequest schema')
for (const field of requiredGoogleLinkFields) {
  assertContract(
    Boolean(googleLinkSchema.properties?.[field]) && googleLinkSchema.required?.includes(field),
    `ConfirmGoogleAccountLinkRequest.${field} missing or not required`,
  )
}

const registerSchema = spec.components?.schemas?.RegisterAccountRequest
assertContract(Boolean(registerSchema), 'missing RegisterAccountRequest schema')
for (const field of requiredRegisterFields) {
  assertContract(
    Boolean(registerSchema.properties?.[field]) && registerSchema.required?.includes(field),
    `RegisterAccountRequest.${field} missing or not required`,
  )
}
assertContract(
  registerSchema.properties?.marketingOptIn?.type === 'boolean' && registerSchema.properties?.marketingOptIn?.default === false,
  'RegisterAccountRequest.marketingOptIn must be a boolean defaulting false',
)


for (const [schemaName, requiredFields] of [
  ['CreateInvitationDraftRequest', requiredCreateDraftFields],
  ['AutosaveInvitationDraftRequest', requiredAutosaveDraftFields],
  ['SelectInvitationTemplateRequest', requiredSelectTemplateFields],
  ['StartPaymentCheckoutBody', ['planKey']],
]) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(Boolean(schema), `missing ${schemaName} schema`)
  for (const field of requiredFields) {
    assertContract(
      Boolean(schema.properties?.[field]) && schema.required?.includes(field),
      `${schemaName}.${field} missing or not required`,
    )
  }
}

const invitationStatisticsFields = ['totalPageViews', 'rsvpResponseCount', 'participantCountTotal', 'memoryCount', 'readyMediaCount', 'activeGiftReservationCount']
const invitationStatisticsSchema = spec.components?.schemas?.InvitationStatistics
assertContract(
  JSON.stringify(Object.keys(invitationStatisticsSchema?.properties ?? {}).sort()) === JSON.stringify([...invitationStatisticsFields].sort())
    && invitationStatisticsFields.every(field => {
      const type = invitationStatisticsSchema?.properties?.[field]?.type
      return (type === 'integer' || (Array.isArray(type) && type.includes('integer')))
        && invitationStatisticsSchema.required?.includes(field)
    }),
  'InvitationStatistics must require exactly the six approved non-negative integer aggregate fields',
)

for (const [schemaName, requiredFields] of [
  ['InvitationDraftDetails', requiredDraftDetailsFields],
  ['InvitationDraftPage', requiredDraftPageFields],
  ['TemplateCatalogItem', requiredTemplateCatalogFields],
  ['IndividualPurchasePlan', ['key', 'displayName', 'amount', 'currency', 'billingPeriod']],
  ['PaymentCheckoutResponse', ['attemptId', 'reference', 'status', 'planKey', 'amount', 'currency', 'billingPeriod', 'checkoutUrl']],
  ['OrganizationSubscriptionSnapshotResponse', ['subscriptionId', 'status', 'paidThroughAtUtc', 'cancelAtPeriodEnd', 'cancelRequestedAtUtc', 'planDisplayName', 'priceAmount', 'currency', 'billingPeriod']],
  ['OrganizationSubscriptionCancellationResponse', ['outcome', 'paidThroughAtUtc']],
  ['AdminOverview', ['generatedAtUtc', 'accounts', 'invitations', 'plans', 'grants', 'payments', 'storage', 'health']],
  ['InvitationDraftValidationReport', requiredDraftValidationFields],
  ...publicationSchemas,
  ...publicInvitationSchemas,
  ...publicRsvpSchemas,
  ...creatorRsvpSchemas,
  ...creatorMemorySchemas,
  ...creatorGiftSchemas,
]) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(Boolean(schema), `missing ${schemaName} response schema`)
  for (const field of requiredFields) {
    assertContract(Boolean(schema.properties?.[field]), `${schemaName}.${field} response field missing`)
  }
}

for (const [schemaName, requiredFields] of [
  ['AdminOverviewAccounts', ['total', 'individual', 'organization', 'banned']],
  ['AdminOverviewInvitations', ['draft', 'scheduled', 'active', 'paused', 'expired', 'deleted']],
  ['AdminOverviewPlans', ['total', 'active', 'inactive']],
  ['AdminOverviewGrants', ['total', 'free', 'individualPurchase', 'organizationSubscription', 'revoked']],
  ['AdminOverviewPayments', ['pending', 'unknown', 'succeeded', 'failed', 'canceled', 'reversed']],
  ['AdminOverviewStorage', ['assets', 'ready', 'pendingUpload', 'processing', 'pendingDeletion', 'deleted', 'rejected', 'verifiedBytes']],
  ['AdminOverviewHealth', ['api', 'database']],
]) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(Boolean(schema), `missing ${schemaName} response schema`)
  for (const field of requiredFields) {
    assertContract(Boolean(schema.properties?.[field]), `${schemaName}.${field} response field missing`)
  }
}

for (const [schemaName, allowedFields] of creatorRsvpSchemas) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(Object.keys(schema.properties).every(field => allowedFields.includes(field)),
    `${schemaName} contains a field outside the Creator RSVP contract`)
}

for (const [schemaName, allowedFields] of publicRsvpSchemas) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(Object.keys(schema.properties).every(field => allowedFields.includes(field)),
    `${schemaName} contains a field outside the public RSVP allowlist`)
}

for (const [schemaName, requiredFields] of [
  ['PublicRsvpSubmissionResponse', ['submissionId', 'submittedAt']],
  ['PublicRsvpUpdateResponse', ['submissionId', 'updatedAt']],
  ['SubmitPublicRsvpRequest', ['answers']],
]) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(Boolean(schema), `missing ${schemaName} schema`)
  for (const field of requiredFields) {
    assertContract(Boolean(schema.properties?.[field]) && schema.required?.includes(field),
      `${schemaName}.${field} missing or not required`)
  }
}

// Anonymous landing-page plan cards: exactly the approved display fields, never IDs/revisions/grants.
const publicPlansPath = '/api/v1/public/plans'
const publicPlansResponse = spec.paths?.[publicPlansPath]?.get?.responses?.['200']?.content?.['application/json']?.schema
assertContract(publicPlansResponse?.type === 'array' && publicPlansResponse?.items?.$ref === '#/components/schemas/PublicPlanCatalogItem',
  `GET ${publicPlansPath} must return PublicPlanCatalogItem[]`)
const publicPlanCatalogSchema = spec.components?.schemas?.PublicPlanCatalogItem
assertContract(
  JSON.stringify(Object.keys(publicPlanCatalogSchema?.properties ?? {}).sort()) === JSON.stringify([...publicPlanCatalogFields].sort())
    && publicPlanCatalogFields.every(field => publicPlanCatalogSchema.required?.includes(field)),
  'PublicPlanCatalogItem must require exactly the approved public plan display fields',
)

// Public response shapes are an allowlist: adding management fields is contract drift too.
for (const [schemaName, allowedFields] of publicInvitationSchemas) {
  const schema = spec.components?.schemas?.[schemaName]
  assertContract(
    Object.keys(schema.properties).every(field => allowedFields.includes(field)),
    `${schemaName} contains a field outside the public allowlist`,
  )
}

const publicResponseSchema = spec.paths['/api/v1/public/invitations/{publicCode}'].get
  .responses?.['200']?.content?.['application/json']?.schema
const responseVariants = publicResponseSchema?.anyOf ?? publicResponseSchema?.oneOf ?? []
const resolveSchema = schema => schema?.$ref
  ? spec.components?.schemas?.[schema.$ref.split('/').at(-1)]
  : schema
const resolvedVariants = responseVariants.map(resolveSchema)
assertContract(resolvedVariants.length === 2, 'public GET 200 must describe both active and unavailable variants')
assertContract(resolvedVariants.some(schema => {
  const fields = Object.keys(schema?.properties ?? {})
  return fields.length === 1 && fields[0] === 'status'
}), 'unavailable public response must contain only status')
assertContract(resolvedVariants.some(schema => {
  const fields = Object.keys(schema?.properties ?? {})
  const allowed = ['status', 'templateKey', 'rendererVersion', 'contentSchemaVersion', 'content', 'media']
  return fields.length === allowed.length && allowed.every(field => fields.includes(field))
}), 'active public response must contain only pinned renderer and content fields')
const activePublicVariant = resolvedVariants.find(schema => schema?.properties?.content)
for (const [schema, allowedFields, label] of [
  [resolveSchema(activePublicVariant.properties.content), publicInvitationSchemas.find(([name]) => name === 'PublicInvitationContent')[1], 'active content'],
  [resolveSchema(resolveSchema(activePublicVariant.properties.content)?.properties?.venue), ['name', 'address'], 'public venue'],
  [resolveSchema(resolveSchema(activePublicVariant.properties.content)?.properties?.programItems?.items), ['title', 'description', 'startsAt'], 'public program item'],
]) {
  const fields = Object.keys(schema?.properties ?? {})
  assertContract(fields.length === allowedFields.length && allowedFields.every(field => fields.includes(field)),
    `${label} must contain only allowlisted fields`)
}

const draftFieldValidationSchema = spec.components?.schemas?.InvitationDraftFieldValidation
assertContract(Boolean(draftFieldValidationSchema), 'missing InvitationDraftFieldValidation response schema')
for (const field of requiredDraftFieldValidationFields) {
  assertContract(
    Boolean(draftFieldValidationSchema.properties?.[field]) && draftFieldValidationSchema.required?.includes(field),
    `InvitationDraftFieldValidation.${field} response field missing or not required`,
  )
}
const templateText = await readFile(templateUrl, 'utf8')

if (process.argv.includes('--check')) {
  const current = await readFile(outputUrl, 'utf8')
  if (current !== templateText) {
    throw new Error('Generated API client is stale. Run npm run api:generate.')
  }
} else {
  await writeFile(outputUrl, templateText)
}
