export const invitationFields: Record<string, { label: string; step: number; id?: string }> = {
  eventType: { label: 'Etkinlik türü', step: 0 },
  headline: { label: 'Davetiye başlığı', step: 1, id: 'draft-headline' },
  hostNames: { label: 'İsimler', step: 1, id: 'draft-hosts' },
  message: { label: 'Kısa davet mesajı', step: 1, id: 'draft-message' },
  startsAt: { label: 'Etkinlik tarihi ve saati', step: 3, id: 'draft-starts-at' },
  timeZoneId: { label: 'Saat dilimi', step: 3 },
  'venue.name': { label: 'Mekân adı', step: 3, id: 'draft-venue' },
  'venue.address': { label: 'Adres', step: 3, id: 'draft-address' },
  'venue.mapUrl': { label: 'Harita bağlantısı', step: 3, id: 'draft-map-url' },
  programItems: { label: 'Program', step: 4, id: 'draft-program-title' },
}

export function invitationFieldLabel(field: string): string {
  return invitationFields[field]?.label ?? 'Şablonun gerektirdiği alan'
}
