import { useEffect, useState } from 'react'

import type { NormalizedInvitationRenderModel } from '../templates/rendering/model'
import { countdownParts, createInvitationCalendar, downloadCalendar } from './publicEventUtilities'
import { ConsentMap } from './ConsentMap'

export function PublicEventTools({ model, publicCode }: { model: NormalizedInvitationRenderModel; publicCode: string }) {
  const [now, setNow] = useState(Date.now)
  useEffect(() => {
    const interval = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(interval)
  }, [])
  const countdown = model.startsAt ? countdownParts(model.startsAt, now) : null
  const location = [model.venue?.name, model.venue?.address].filter(Boolean).join(', ')
  return <section className="public-event-tools" aria-label="Etkinlik bilgileri ve ulaşım">
    {countdown ? <div className="event-countdown"><h2>Etkinliğe kalan süre</h2><p aria-live="off">{countdown.days} gün · {countdown.hours} saat · {countdown.minutes} dakika · {countdown.seconds} saniye</p></div> : null}
    {model.startsAt ? <button className="button button--secondary" type="button" onClick={() => downloadCalendar(createInvitationCalendar({ startsAt: model.startsAt!, headline: model.headline, message: model.message, location }, publicCode))}>Takvime ekle</button> : null}
    {location ? <div className="public-directions"><h2>Yol tarifi</h2><div className="button-row">
      <a className="button button--secondary" href={`https://www.google.com/maps/dir/?api=1&destination=${encodeURIComponent(location)}`} target="_blank" rel="noopener noreferrer">Google Maps ile yol tarifi</a>
      <a className="button button--secondary" href={`https://maps.apple.com/?daddr=${encodeURIComponent(location)}`} target="_blank" rel="noopener noreferrer">Apple Maps ile yol tarifi</a>
    </div></div> : null}
    {location ? <ConsentMap location={location} /> : null}
  </section>
}
