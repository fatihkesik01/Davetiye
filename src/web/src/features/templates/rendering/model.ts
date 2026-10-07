import type { DraftContentInput } from '../../../api/generated/client'

export interface InvitationRenderVenue {
  name: string
  address?: string
}

export interface InvitationRenderProgramItem {
  title: string
  description?: string
  startsAt?: string
}

/**
 * Template-neutral, executable-code-free presentation model. Demo and Creator preview both enter
 * the renderer through this shape; templates never branch on API DTOs or read raw JSON.
 */
export interface NormalizedInvitationRenderModel {
  contacts?: { name: string; role?: string; phone: string }[]
  announcement?: string
  faqs?: { question: string; answer: string }[]
  transportStops?: { name: string; address?: string; departureTime?: string }[]
  eventType: string
  headline: string
  hostNames: string[]
  message?: string
  startsAt?: string
  timeZoneId?: string
  venue?: InvitationRenderVenue
  programItems: InvitationRenderProgramItem[]
}

export function normalizeDraftContent(content: DraftContentInput): NormalizedInvitationRenderModel {
  const hostNames = (content.hostNames ?? []).map(value => value.trim()).filter(Boolean)
  const venueName = content.venue?.name?.trim()

  return {
    contacts: (content.contacts ?? []).map(contact => ({ name: contact.name.trim(), role: contact.role?.trim() || undefined, phone: contact.phone.trim() })).filter(contact => contact.name && contact.phone),
    announcement: content.announcement?.trim() || undefined,
    faqs: (content.faqs ?? []).map(faq => ({ question: faq.question.trim(), answer: faq.answer.trim() })).filter(faq => faq.question && faq.answer),
    transportStops: (content.transportStops ?? []).map(stop => ({ name: stop.name.trim(), address: stop.address?.trim() || undefined, departureTime: stop.departureTime?.trim() || undefined })).filter(stop => stop.name),
    eventType: content.eventType?.trim() || 'genel',
    headline: content.headline?.trim() || 'Etkinlik başlığı',
    hostNames,
    message: content.message?.trim() || undefined,
    startsAt: validDateTime(content.startsAt),
    timeZoneId: content.timeZoneId?.trim() || undefined,
    venue: venueName
      ? { name: venueName, address: content.venue?.address?.trim() || undefined }
      : undefined,
    programItems: (content.programItems ?? [])
      .filter((item): item is NonNullable<typeof item> => item !== null)
      .map(item => ({
        title: item.title?.trim() || 'Program',
        description: item.description?.trim() || undefined,
        startsAt: validDateTime(item.startsAt),
      })),
  }
}

function validDateTime(value: string | null | undefined): string | undefined {
  if (!value) return undefined
  return Number.isNaN(Date.parse(value)) ? undefined : value
}

export function formatInvitationDate(value: string | undefined, timeZoneId?: string): string {
  if (!value) return 'Tarih daha sonra duyurulacak'

  try {
    return new Intl.DateTimeFormat('tr-TR', {
      dateStyle: 'long',
      timeStyle: 'short',
      timeZone: timeZoneId,
    }).format(new Date(value))
  } catch {
    return new Intl.DateTimeFormat('tr-TR', {
      dateStyle: 'long',
      timeStyle: 'short',
    }).format(new Date(value))
  }
}

export function formatInvitationTime(value: string | undefined, timeZoneId?: string): string | undefined {
  if (!value) return undefined

  try {
    return new Intl.DateTimeFormat('tr-TR', {
      hour: '2-digit',
      minute: '2-digit',
      timeZone: timeZoneId,
    }).format(new Date(value))
  } catch {
    return new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' }).format(new Date(value))
  }
}
