import type { TemplateCatalogItem } from '../../../api/generated/client'

/** Keep an in-progress code-owned template visible in the local catalog before DB reconciliation. */
export function includeLocalDantelTemplate(templates: TemplateCatalogItem[]): TemplateCatalogItem[] {
  if (!import.meta.env.DEV) return templates

  const existing = templates.find(template => template.key === 'dantel-davetiye')
  if (existing) return templates.map(template => template.key === 'dantel-davetiye'
    ? { ...template, name: 'Kapı', description: 'İnci detaylı, açılır kapılı davetiye.' }
    : template)

  const gate: TemplateCatalogItem = {
    key: 'dantel-davetiye',
    name: 'Kapı',
    description: 'İnci detaylı, açılır kapılı davetiye.',
    category: 'Düğün / Nişan',
    isPremium: false,
    rendererVersion: 1,
    previewImageUrl: '/template-previews/kapi-davetiye.svg',
    supportedModules: ['hero', 'dateVenue', 'countdown', 'calendar', 'contact', 'rsvp', 'gallery', 'giftRegistry', 'qr'],
    requiredFields: ['headline', 'startsAt', 'venue.name'],
    recommendedFields: ['message', 'hostNames'],
  }

  const zarfIndex = templates.findIndex(template => template.key === 'zarf-davetiye')
  const result = [...templates]
  result.splice(zarfIndex < 0 ? 0 : zarfIndex + 1, 0, gate)
  return result
}
