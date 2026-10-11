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

/** One schedule line as edited: the time is a browser-local date-time, like the event date. */
export interface EditableProgramItem {
  title: string
  description: string
  startsAtLocal: string
}

/** Backend limits for the schedule (InvitationDraftService): at most 20 items, 200 / 1000 characters. */
export const programLimits = { maxItems: 20, maxTitle: 200, maxDescription: 1000 } as const

export interface ProgramItemProblems { titleTooLong?: boolean; descriptionTooLong?: boolean; controlCharacters?: boolean }

const hasControlCharacter = (value: string) => [...value].some(character => {
  const code = character.codePointAt(0) ?? 0
  return code < 32 || (code >= 127 && code < 160)
})

/** Mirrors the backend's schedule validation so the Creator sees problems before the save fails. */
export function validateProgramItems(items: EditableProgramItem[]): { tooMany: boolean; items: ProgramItemProblems[]; outOfOrder: boolean } {
  const times = items.map(item => item.startsAtLocal).filter(Boolean)
  return {
    tooMany: items.length > programLimits.maxItems,
    items: items.map(item => ({
      ...(item.title.trim().length > programLimits.maxTitle ? { titleTooLong: true } : {}),
      ...(item.description.trim().length > programLimits.maxDescription ? { descriptionTooLong: true } : {}),
      ...(hasControlCharacter(item.title) || hasControlCharacter(item.description) ? { controlCharacters: true } : {}),
    })),
    // Times are compared as the strings the browser produces (YYYY-MM-DDTHH:mm), which sort chronologically.
    outOfOrder: times.some((time, index) => index > 0 && time < times[index - 1]!),
  }
}

export function sortProgramItemsByTime(items: EditableProgramItem[]): EditableProgramItem[] {
  // Stable: lines without a time keep their relative place after the timed ones.
  return [...items].sort((a, b) => (a.startsAtLocal || '9999').localeCompare(b.startsAtLocal || '9999'))
}

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
  /** The schedule ("Günün akışı"), in the order the Creator listed it. */
  programItems: EditableProgramItem[]
  /** zarf-davetiye palette key; empty means the template default. */
  accentColor: string
  /** zarf-davetiye page mood and flower type; empty means the template defaults. */
  ambiance: string
  flora: string
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
  programItems: [],
  accentColor: '',
  ambiance: '',
  flora: '',
}

export function toEditableDraft(content: DraftContentInput): EditableDraftContent {
  const programItems = (content.programItems ?? []).filter(
    (item): item is DraftProgramItemInput => item !== null,
  )
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
    programItems: programItems.map(item => ({
      title: item.title ?? '',
      description: item.description ?? '',
      startsAtLocal: toLocalDateTime(item.startsAt),
    })),
    accentColor: content.accentColor ?? '',
    ambiance: content.ambiance ?? '',
    flora: content.flora ?? '',
  }
}

export function toDraftContent(content: EditableDraftContent): DraftContentInput {
  const hasVenue = Boolean(content.venueName.trim() || content.venueAddress.trim() || content.mapUrl.trim())

  // The editor does not yet ask the Creator to choose an IANA time zone. The
  // browser's local clock is used to create the instant, but an unconfirmed
  // zone (for example Europe/Istanbul) must not be sent as authoritative data.
  return {
    ...(content.contacts.length ? { contacts: content.contacts } : {}),
    ...(content.announcement.trim() ? { announcement: content.announcement.trim() } : {}),
    ...(content.faqs.length ? { faqs: content.faqs } : {}),
    ...(content.transportStops.length ? { transportStops: content.transportStops } : {}),
    ...(content.accentColor ? { accentColor: content.accentColor } : {}),
    ...(content.ambiance ? { ambiance: content.ambiance } : {}),
    ...(content.flora ? { flora: content.flora } : {}),
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
    // Lines the Creator left completely blank are not sent; order is the order of the list.
    programItems: content.programItems
      .filter(item => item.title.trim() || item.description.trim() || item.startsAtLocal)
      .map(item => ({
        title: nullIfBlank(item.title),
        description: nullIfBlank(item.description),
        startsAt: toIsoDateTime(item.startsAtLocal),
      })),
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
    const legacy = candidate.content as Partial<EditableDraftContent> & { programTitle?: string; programDescription?: string; programStartsAtLocal?: string; additionalProgramItems?: Array<{ title?: string | null; description?: string | null; startsAt?: string | null }> }
    const content = { ...emptyEditableDraft, ...candidate.content }
    // Recovery snapshots written before the schedule became a list carry the single-line fields.
    if (!Array.isArray(legacy.programItems) && (legacy.programTitle || legacy.programDescription || legacy.programStartsAtLocal || legacy.additionalProgramItems?.length)) {
      content.programItems = [
        ...(legacy.programTitle || legacy.programDescription || legacy.programStartsAtLocal ? [{ title: legacy.programTitle ?? '', description: legacy.programDescription ?? '', startsAtLocal: legacy.programStartsAtLocal ?? '' }] : []),
        ...(legacy.additionalProgramItems ?? []).map(item => ({ title: item.title ?? '', description: item.description ?? '', startsAtLocal: toLocalDateTime(item.startsAt) })),
      ]
    }
    return { content, baseContentRevision: candidate.baseContentRevision }
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
