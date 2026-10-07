import type { PublicInvitationResponse } from '../../api/generated/client'
import { resolveTemplateRenderer } from '../templates/rendering/registry'

type ActivePublicInvitation = Extract<PublicInvitationResponse, { status: 'active' }>

export function publicCodeFromPath(pathname: string): string | null {
  return /^\/davetiye\/(?:[^/]+-)?([a-f0-9]{64})\/?$/.exec(pathname)?.[1] ?? null
}

function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function optionalText(value: unknown): boolean {
  return value === undefined || value === null || typeof value === 'string'
}

function optionalDate(value: unknown): boolean {
  return value === undefined || value === null || (typeof value === 'string' && !Number.isNaN(Date.parse(value)))
}

/** Public rendering fails closed for malformed projections and unsupported browser time zones. */
export function isRenderablePublicInvitation(value: unknown): value is ActivePublicInvitation {
  if (!object(value) || value.status !== 'active' || value.contentSchemaVersion !== 1 ||
    typeof value.templateKey !== 'string' || typeof value.rendererVersion !== 'number' ||
    !resolveTemplateRenderer(value.templateKey, value.rendererVersion) || !object(value.content)) return false
  const content = value.content
  if (value.media !== undefined && value.media !== null && (!Array.isArray(value.media) || value.media.some(item =>
    !object(item) || typeof item.assetId !== 'string' ||
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(item.assetId) ||
    !['Image', 'Video'].includes(String(item.kind)) || !['Cover', 'Gallery'].includes(String(item.role)) ||
    !Number.isInteger(item.sortOrder) || (item.sortOrder as number) < 0 || (item.sortOrder as number) > 1000))) return false
  if (!optionalText(content.announcement)) return false
  if (content.contacts !== undefined && content.contacts !== null && (!Array.isArray(content.contacts) || content.contacts.some(item => !object(item) || typeof item.name !== 'string' || typeof item.phone !== 'string' || !optionalText(item.role)))) return false
  if (content.faqs !== undefined && content.faqs !== null && (!Array.isArray(content.faqs) || content.faqs.some(item => !object(item) || typeof item.question !== 'string' || typeof item.answer !== 'string'))) return false
  if (content.transportStops !== undefined && content.transportStops !== null && (!Array.isArray(content.transportStops) || content.transportStops.some(item => !object(item) || typeof item.name !== 'string' || !optionalText(item.address) || !optionalText(item.departureTime) || (item.departureTime != null && !/^([01]\d|2[0-3]):[0-5]\d$/.test(String(item.departureTime)))))) return false
  if (!['eventType', 'headline', 'message'].every(field => optionalText(content[field])) || !optionalDate(content.startsAt) ||
    typeof content.timeZoneId !== 'string' || !content.timeZoneId.trim()) return false
  try {
    new Intl.DateTimeFormat('tr-TR', { timeZone: content.timeZoneId }).format(new Date(0))
  } catch {
    return false
  }
  if (content.hostNames !== undefined && content.hostNames !== null &&
    (!Array.isArray(content.hostNames) || content.hostNames.some(name => typeof name !== 'string'))) return false
  if (content.venue !== undefined && content.venue !== null &&
    (!object(content.venue) || !optionalText(content.venue.name) || !optionalText(content.venue.address))) return false
  if (content.programItems !== undefined && content.programItems !== null &&
    (!Array.isArray(content.programItems) || content.programItems.some(item => !object(item) ||
      !optionalText(item.title) || !optionalText(item.description) || !optionalDate(item.startsAt)))) return false
  return true
}
