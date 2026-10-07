import type {
  DraftContentInput,
  DraftProgramItemInput,
  InvitationDraftDetails,
  InvitationContactInput,
  InvitationFaqInput,
  InvitationTransportStopInput,
} from '../../api/generated/client'

export const eventTypes = [
  { value: 'dugun', label: 'Düğün' },
  { value: 'nisan', label: 'Nişan' },
  { value: 'kina', label: 'Kına' },
  { value: 'sunnet', label: 'Sünnet' },
  { value: 'dogum-gunu', label: 'Doğum Günü' },
  { value: 'baby-shower', label: 'Baby Shower' },
  { value: 'mezuniyet', label: 'Mezuniyet' },
  { value: 'acilis-genel', label: 'Açılış / Genel' },
] as const

export interface EditableDraftContent {
  contacts: InvitationContactInput[]
  announcement: string
  faqs: InvitationFaqInput[]
  transportStops: InvitationTransportStopInput[]
  eventType: string
  headline: string
  hostNamesText: string
  message: string
  startsAtLocal: string
  timeZoneId: string | null
  venueName: string
  venueAddress: string
  mapUrl: string
  programTitle: string
  programDescription: string
  programStartsAtLocal: string
  additionalProgramItems: DraftProgramItemInput[]
}

export const emptyEditableDraft: EditableDraftContent = {
  contacts: [], announcement: '', faqs: [], transportStops: [],
  eventType: '',
  headline: '',
  hostNamesText: '',
  message: '',
  startsAtLocal: '',
  timeZoneId: null,
  venueName: '',
  venueAddress: '',
  mapUrl: '',
  programTitle: '',
  programDescription: '',
  programStartsAtLocal: '',
  additionalProgramItems: [],
}

export function toEditableDraft(content: DraftContentInput): EditableDraftContent {
  const programItems = (content.programItems ?? []).filter(
    (item): item is DraftProgramItemInput => item !== null,
  )
  const firstProgramItem = programItems[0]
  return {
    contacts: content.contacts ?? [], announcement: content.announcement ?? '', faqs: content.faqs ?? [], transportStops: content.transportStops ?? [],
    eventType: content.eventType ?? '',
    headline: content.headline ?? '',
    hostNamesText: (content.hostNames ?? []).join(', '),
    message: content.message ?? '',
    startsAtLocal: toLocalDateTime(content.startsAt),
    timeZoneId: content.timeZoneId?.trim() || null,
    venueName: content.venue?.name ?? '',
    venueAddress: content.venue?.address ?? '',
    mapUrl: content.venue?.mapUrl ?? '',
    programTitle: firstProgramItem?.title ?? '',
    programDescription: firstProgramItem?.description ?? '',
    programStartsAtLocal: toLocalDateTime(firstProgramItem?.startsAt),
    additionalProgramItems: programItems.slice(1),
  }
}

export function toDraftContent(content: EditableDraftContent): DraftContentInput {
  const hasVenue = Boolean(content.venueName.trim() || content.venueAddress.trim() || content.mapUrl.trim())
  const hasProgram = Boolean(
    content.programTitle.trim() || content.programDescription.trim() || content.programStartsAtLocal,
  )

  // The editor does not yet ask the Creator to choose an IANA time zone. The
  // browser's local clock is used to create the instant, but an unconfirmed
  // zone (for example Europe/Istanbul) must not be sent as authoritative data.
  return {
    ...(content.contacts.length ? { contacts: content.contacts } : {}),
    ...(content.announcement.trim() ? { announcement: content.announcement.trim() } : {}),
    ...(content.faqs.length ? { faqs: content.faqs } : {}),
    ...(content.transportStops.length ? { transportStops: content.transportStops } : {}),
    eventType: nullIfBlank(content.eventType),
    headline: nullIfBlank(content.headline),
    hostNames: content.hostNamesText
      .split(',')
      .map(value => value.trim())
      .filter(Boolean),
    message: nullIfBlank(content.message),
    startsAt: toIsoDateTime(content.startsAtLocal),
    ...(content.timeZoneId ? { timeZoneId: content.timeZoneId } : {}),
    venue: hasVenue ? {
      name: nullIfBlank(content.venueName),
      address: nullIfBlank(content.venueAddress),
      mapUrl: nullIfBlank(content.mapUrl),
    } : null,
    programItems: [
      ...(hasProgram ? [{
        title: nullIfBlank(content.programTitle),
        description: nullIfBlank(content.programDescription),
        startsAt: toIsoDateTime(content.programStartsAtLocal),
      }] : []),
      ...content.additionalProgramItems,
    ],
  }
}

export function draftRecoveryKey(invitationId: string): string {
  return `davetiye:draft-recovery:${invitationId}`
}

export interface DraftRecoverySnapshot {
  content: EditableDraftContent
  baseContentRevision: number
}

export function parseRecoverySnapshot(value: string | null): DraftRecoverySnapshot | null {
  if (!value) return null
  try {
    const candidate = JSON.parse(value) as Partial<DraftRecoverySnapshot>
    if (!candidate.content || typeof candidate.baseContentRevision !== 'number') return null
    return { content: { ...emptyEditableDraft, ...candidate.content }, baseContentRevision: candidate.baseContentRevision }
  } catch {
    return null
  }
}

export function draftDisplayName(draft: Pick<InvitationDraftDetails, 'id' | 'content'>): string {
  return draft.content.headline?.trim() || `İsimsiz taslak · ${draft.id.slice(0, 8)}`
}

function nullIfBlank(value: string): string | null {
  return value.trim() || null
}

function toIsoDateTime(value: string): string | null {
  if (!value) return null
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? null : date.toISOString()
}

function toLocalDateTime(value: string | null | undefined): string {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.valueOf())) return ''
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.valueOf() - offset).toISOString().slice(0, 16)
}
