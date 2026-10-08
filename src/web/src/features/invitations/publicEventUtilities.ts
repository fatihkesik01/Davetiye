export function countdownParts(startsAt: string, now: number) {
  const seconds = Math.max(0, Math.floor((Date.parse(startsAt) - now) / 1000))
  return { days: Math.floor(seconds / 86_400), hours: Math.floor(seconds % 86_400 / 3600), minutes: Math.floor(seconds % 3600 / 60), seconds: seconds % 60 }
}

function escapeText(value: string) {
  return Array.from(value.replace(/\\/g, '\\\\').replace(/\r\n|\r|\n/g, '\\n').replace(/;/g, '\\;').replace(/,/g, '\\,'))
    .filter(character => character.charCodeAt(0) >= 32 && character.charCodeAt(0) !== 127).join('')
}

// RFC 5545 folds at 75 octets without splitting a UTF-8 character.
function foldLine(value: string) {
  const lines: string[] = []
  let line = ''
  let bytes = 0
  for (const character of value) {
    const length = new TextEncoder().encode(character).length
    if (bytes + length > 75) { lines.push(line); line = ' '; bytes = 1 }
    line += character
    bytes += length
  }
  lines.push(line)
  return lines.join('\r\n')
}

export function createInvitationCalendar(event: { startsAt: string; headline: string; message?: string; location?: string }, publicCode: string, now = new Date()): string {
  if (!/^[a-f0-9]{64}$/.test(publicCode) || Number.isNaN(Date.parse(event.startsAt))) throw new Error('Invalid calendar event')
  const utc = (date: Date) => date.toISOString().replace(/[-:]/g, '').replace(/\.\d{3}Z$/, 'Z')
  return ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//Kutlio//Invitation//TR', 'CALSCALE:GREGORIAN',
    'BEGIN:VEVENT', `UID:${publicCode}@davetiye`, `DTSTAMP:${utc(now)}`, `DTSTART:${utc(new Date(event.startsAt))}`,
    `SUMMARY:${escapeText(event.headline)}`, ...(event.message ? [`DESCRIPTION:${escapeText(event.message)}`] : []),
    ...(event.location ? [`LOCATION:${escapeText(event.location)}`] : []), 'END:VEVENT', 'END:VCALENDAR'].map(foldLine).join('\r\n') + '\r\n'
}

export function downloadCalendar(calendar: string) {
  const url = URL.createObjectURL(new Blob([calendar], { type: 'text/calendar;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'davetiye.ics'
  link.click()
  window.setTimeout(() => URL.revokeObjectURL(url), 1000)
}
