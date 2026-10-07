import { describe, expect, it } from 'vitest'
import { countdownParts, createInvitationCalendar } from './publicEventUtilities'

describe('public event clock and calendar', () => {
  it('uses the same instant across offset/DST boundaries and clamps elapsed events', () => {
    expect(countdownParts('2026-03-29T03:00:00+02:00', Date.parse('2026-03-29T00:59:59Z'))).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 1 })
    expect(countdownParts('2026-03-29T01:00:00Z', Date.parse('2026-03-29T01:00:01Z'))).toEqual({ days: 0, hours: 0, minutes: 0, seconds: 0 })
  })
  it('exports UTC, escapes content injection and folds Unicode without inventing an end time', () => {
    const calendar = createInvitationCalendar({ startsAt: '2026-10-02T12:00:00+03:00', headline: 'İ'.repeat(70) + ',\\;', message: 'Satır\r\nBEGIN:VEVENT', location: 'Mekân, İstanbul' }, 'a'.repeat(64), new Date('2026-10-01T00:00:00Z'))
    expect(calendar).toContain('DTSTART:20261002T090000Z\r\n')
    expect(calendar).toContain('DESCRIPTION:Satır\\nBEGIN:VEVENT\r\n')
    expect(calendar).not.toMatch(/DTEND|DURATION/)
    for (const line of calendar.split('\r\n')) expect(new TextEncoder().encode(line).length).toBeLessThanOrEqual(75)
    expect(calendar.replace(/\r\n /g, '')).toContain('\\,\\\\\\;')
  })
})
