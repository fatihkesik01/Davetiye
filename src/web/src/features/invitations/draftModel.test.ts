import { describe, expect, it } from 'vitest'

import { emptyEditableDraft, parseRecoverySnapshot, programLimits, sortProgramItemsByTime, toDraftContent, toEditableDraft, validateProgramItems } from './draftModel'

describe('draftModel', () => {
  it('round-trips a schedule of N items in listed order, dropping fully blank lines', () => {
    const items = Array.from({ length: 5 }, (_, index) => ({ title: `Satır ${index + 1}`, description: index % 2 ? `Açıklama ${index + 1}` : '', startsAtLocal: `2027-06-19T1${index}:00` }))
    const content = toDraftContent({ ...emptyEditableDraft, programItems: [...items, { title: '  ', description: '', startsAtLocal: '' }] })
    expect(content.programItems).toHaveLength(5)
    expect(content.programItems!.map(item => item!.title)).toEqual(items.map(item => item.title))
    expect(content.programItems![0]!.description).toBeNull()
    const back = toEditableDraft(content)
    expect(back.programItems.map(item => item.title)).toEqual(items.map(item => item.title))
    expect(back.programItems[1]!.description).toBe('Açıklama 2')
    expect(back.programItems[3]!.startsAtLocal).toBe(items[3]!.startsAtLocal)
  })

  it('stays compatible with drafts that already hold one or no schedule item', () => {
    expect(toEditableDraft({}).programItems).toEqual([])
    expect(toEditableDraft({ programItems: [{ title: 'Karşılama', startsAt: '2027-06-19T16:00:00Z' }] }).programItems).toHaveLength(1)
    expect(toDraftContent(emptyEditableDraft).programItems).toEqual([])
  })

  it('mirrors the backend limits and detects out-of-order times', () => {
    expect(programLimits).toEqual({ maxItems: 20, maxTitle: 200, maxDescription: 1000 })
    const result = validateProgramItems([
      { title: 'a'.repeat(201), description: '', startsAtLocal: '2027-06-19T18:00' },
      { title: 'b', description: 'c'.repeat(1001), startsAtLocal: '2027-06-19T17:00' },
      { title: 'tab	here', description: '', startsAtLocal: '' },
    ])
    expect(result.items[0]).toEqual({ titleTooLong: true })
    expect(result.items[1]).toEqual({ descriptionTooLong: true })
    expect(result.items[2]).toEqual({ controlCharacters: true })
    expect(result.outOfOrder).toBe(true)
    expect(validateProgramItems(Array.from({ length: 21 }, () => ({ title: 'x', description: '', startsAtLocal: '' }))).tooMany).toBe(true)
    expect(sortProgramItemsByTime([{ title: 'late', description: '', startsAtLocal: '2027-01-01T20:00' }, { title: 'none', description: '', startsAtLocal: '' }, { title: 'early', description: '', startsAtLocal: '2027-01-01T08:00' }]).map(item => item.title)).toEqual(['early', 'late', 'none'])
  })

  it('migrates old recovery snapshots that stored a single schedule line', () => {
    const snapshot = parseRecoverySnapshot(JSON.stringify({ baseContentRevision: 3, content: { headline: 'Eski', programTitle: 'Nikâh', programDescription: 'Salon', programStartsAtLocal: '2027-06-19T17:00', additionalProgramItems: [{ title: 'Yemek' }] } }))
    expect(snapshot?.content.programItems.map(item => item.title)).toEqual(['Nikâh', 'Yemek'])
    expect(snapshot?.content.programItems[0]).toMatchObject({ description: 'Salon', startsAtLocal: '2027-06-19T17:00' })
  })

  it('round-trips the zarf look (colour, ambiance, flowers) through the autosaved content', () => {
    const content = toDraftContent({ ...emptyEditableDraft, accentColor: 'bordo', ambiance: 'gece', flora: 'kiraz' })
    expect(content).toMatchObject({ accentColor: 'bordo', ambiance: 'gece', flora: 'kiraz' })
    expect(toEditableDraft(content)).toMatchObject({ accentColor: 'bordo', ambiance: 'gece', flora: 'kiraz' })
    const defaults = toDraftContent(emptyEditableDraft)
    expect(defaults).not.toHaveProperty('ambiance')
    expect(defaults).not.toHaveProperty('flora')
  })

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

  it('keeps every existing schedule row (all are now editable) and the same instants when saving again', () => {
    const editable = toEditableDraft({
      programItems: [
        { title: 'Karşılama', description: 'Kapılar açılıyor' },
        { title: 'Nikâh', startsAt: '2027-06-19T17:00:00+03:00' },
        { title: 'Kutlama', description: 'Müzik ve dans' },
      ],
    })
    expect(editable.programItems).toHaveLength(3)

    editable.headline = 'Yeni başlık'
    const saved = toDraftContent(editable)

    expect(saved.programItems).toHaveLength(3)
    expect(saved.programItems?.map(item => item?.title)).toEqual(['Karşılama', 'Nikâh', 'Kutlama'])
    expect(Date.parse(saved.programItems![1]!.startsAt!)).toBe(Date.parse('2027-06-19T17:00:00+03:00'))
    expect(saved.programItems?.[2]?.description).toBe('Müzik ve dans')
  })
})
