import { describe, expect, it } from 'vitest'

import { emptyEditableDraft, toDraftContent, toEditableDraft } from './draftModel'

describe('draftModel', () => {
  it('does not persist an unconfirmed time zone', () => {
    const content = toDraftContent({
      ...emptyEditableDraft,
      startsAtLocal: '2026-10-15T19:30',
    })

    expect(content.startsAt).toBeTruthy()
    expect(content).not.toHaveProperty('timeZoneId')
  })

  it('preserves an existing authoritative time zone without inventing one', () => {
    const editable = toEditableDraft({
      startsAt: '2027-06-19T16:00:00+03:00',
      timeZoneId: 'Europe/Istanbul',
    })

    expect(toDraftContent(editable).timeZoneId).toBe('Europe/Istanbul')
  })

  it('keeps incomplete drafts valid as an input shape', () => {
    expect(toDraftContent(emptyEditableDraft)).toEqual({
      eventType: null,
      headline: null,
      hostNames: [],
      message: null,
      startsAt: null,
      venue: null,
      programItems: [],
    })
  })

  it('preserves additional program rows that the compact editor does not display', () => {
    const editable = toEditableDraft({
      programItems: [
        { title: 'Karşılama', description: 'Kapılar açılıyor' },
        { title: 'Nikâh', startsAt: '2027-06-19T17:00:00+03:00' },
        { title: 'Kutlama', description: 'Müzik ve dans' },
      ],
    })

    editable.headline = 'Yeni başlık'
    const saved = toDraftContent(editable)

    expect(saved.programItems).toHaveLength(3)
    expect(saved.programItems?.[1]).toEqual({
      title: 'Nikâh',
      startsAt: '2027-06-19T17:00:00+03:00',
    })
    expect(saved.programItems?.[2]).toEqual({
      title: 'Kutlama',
      description: 'Müzik ve dans',
    })
  })
})
