import { useEffect, useId, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'

import './zarf.css'
import { InvitationOptionalSections } from './InvitationOptionalSections'
import { type NormalizedInvitationRenderModel } from './model'
import { DateVenue, Program, type TemplateRendererProps } from './renderers'
import { CallaLilies, PressedFlower, Sprig } from './ZarfFlowers'
import { resolveZarfAccent, zarfPalettes } from './zarfAccent'

/**
 * Scalloped outline for the Save-the-Date card: a rectangle whose four edges are rows of outward
 * half-circles (all drawn clockwise, so one sweep flag fits every edge). Static, built once.
 */
function scallopedRect(width: number, height: number, bump: number): string {
  const nx = Math.round((width - 2 * bump) / (bump * 2))
  const ny = Math.round((height - 2 * bump) / (bump * 2))
  const sx = (width - 2 * bump) / nx
  const sy = (height - 2 * bump) / ny
  const arc = (rx: number, x: number, y: number) => `A${rx} ${rx} 0 0 1 ${x.toFixed(2)} ${y.toFixed(2)}`
  const left = bump
  const top = bump
  const right = width - bump
  const bottom = height - bump
  let d = `M${left} ${top}`
  for (let i = 1; i <= nx; i += 1) d += arc(sx / 2, left + sx * i, top)
  for (let i = 1; i <= ny; i += 1) d += arc(sy / 2, right, top + sy * i)
  for (let i = 1; i <= nx; i += 1) d += arc(sx / 2, right - sx * i, bottom)
  for (let i = 1; i <= ny; i += 1) d += arc(sy / 2, left, bottom - sy * i)
  return `${d}Z`
}

/** Envelope flap: straight top, V-shaped lower edge made of scallops (drawn right to left, clockwise). */
function scallopedFlap(): string {
  const segments = 8
  const step = 300 / segments
  let d = 'M0 0H300'
  d += 'V38'
  for (let i = 1; i <= segments; i += 1) {
    const x = 300 - step * i
    const y = 38 + 62 * (1 - Math.abs(x - 150) / 150)
    d += `A${(step * 0.62).toFixed(2)} ${(step * 0.62).toFixed(2)} 0 0 1 ${x.toFixed(2)} ${y.toFixed(2)}`
  }
  return `${d}V0Z`
}

const cardPath = scallopedRect(240, 327, 10)
const flapPath = scallopedFlap()
const heartPath = 'M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z'

function Flower({ className }: { className?: string }) {
  return <svg className={className} viewBox="-12 -12 24 24" aria-hidden="true" focusable="false">
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} cx="0" cy="-6" rx="3.4" ry="6" transform={`rotate(${angle})`} />)}
    <circle r="2.6" className="zarf__flower-heart" />
  </svg>
}

function cardDate(model: NormalizedInvitationRenderModel) {
  if (!model.startsAt) return null
  const options = { timeZone: model.timeZoneId }
  const read = (format: Intl.DateTimeFormatOptions) => {
    try {
      return new Intl.DateTimeFormat('tr-TR', { ...format, ...options }).format(new Date(model.startsAt!))
    } catch {
      return new Intl.DateTimeFormat('tr-TR', format).format(new Date(model.startsAt!))
    }
  }
  return {
    day: read({ day: 'numeric' }),
    month: read({ month: 'long' }),
    year: read({ year: 'numeric' }),
    weekday: read({ weekday: 'long' }),
    time: read({ hour: '2-digit', minute: '2-digit' }),
  }
}

/** Longest closing transition (card back down, then flap shut); the content is removed once it ends. */
const closeAnimationMs = 1500

function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function ZarfEnvelopeRenderer({ model, onOpenChange, initialOpen = false, open: controlledOpen, hideInlineClose = false }: TemplateRendererProps) {
  // `open` drives the animation classes; `revealed` mounts the rest of the invitation. Nothing but the
  // cover exists in the DOM until the envelope has been opened, and it is removed again after closing.
  const { t } = useTranslation()
  const [open, setOpen] = useState(initialOpen)
  const [revealed, setRevealed] = useState(initialOpen)
  const grainId = `zarf-grain-${useId().replace(/:/g, '')}`
  const cardRef = useRef<HTMLDivElement>(null)
  const ctaRef = useRef<HTMLButtonElement>(null)
  const stageRef = useRef<HTMLElement>(null)
  const closeTimer = useRef<number | undefined>(undefined)
  const palette = zarfPalettes[resolveZarfAccent(model.accentColor)]
  const style = { '--z-base': palette.base, '--z-ink': palette.ink, '--z-seal': palette.seal, '--z-leaf': palette.leaf, '--z-stem': palette.stem } as CSSProperties
  const date = cardDate(model)
  const monogram = model.hostNames.map(name => name.at(0)?.toLocaleUpperCase('tr-TR')).filter(Boolean).slice(0, 2).join('') || 'D'

  useEffect(() => () => window.clearTimeout(closeTimer.current), [])

  const openEnvelope = () => {
    window.clearTimeout(closeTimer.current)
    setOpen(true)
    setRevealed(true)
    onOpenChange?.(true)
    // The button leaves the page once opened: hand keyboard/screen-reader focus to the card.
    window.requestAnimationFrame(() => cardRef.current?.focus({ preventScroll: true }))
  }

  const openRef = useRef(open)
  useEffect(() => { openRef.current = open }, [open])

  const closeEnvelope = () => {
    setOpen(false)
    // If the guest scrolled down into the content, bring the cover back into view before the content
    // is removed so the page never jumps.
    const stage = stageRef.current
    if (stage && stage.getBoundingClientRect().top < 0) {
      stage.scrollIntoView({ behavior: prefersReducedMotion() ? 'auto' : 'smooth', block: 'start' })
    }
    const finish = () => {
      setRevealed(false)
      onOpenChange?.(false)
      window.requestAnimationFrame(() => ctaRef.current?.focus({ preventScroll: true }))
    }
    if (prefersReducedMotion()) finish()
    else closeTimer.current = window.setTimeout(finish, closeAnimationMs)
  }

  // Controlled mode: follow the host's request (its own control opens/closes the envelope). This
  // synchronises with an external prop and starts a timer, so it has to be an effect.
  useEffect(() => {
    if (controlledOpen === undefined || controlledOpen === openRef.current) return
    /* eslint-disable react-hooks/set-state-in-effect */
    if (controlledOpen) openEnvelope()
    else closeEnvelope()
    /* eslint-enable react-hooks/set-state-in-effect */
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reacts only to the host's request
  }, [controlledOpen])

  return <article className={`invitation-template zarf${open ? ' zarf--open' : ''}`} aria-label={model.headline} style={style}>
    <section className="zarf__stage" ref={stageRef}>
      <header className="zarf__intro">
        <p className="zarf__kicker">Sevgiyle gönderildi</p>
        <h1>{model.headline}</h1>
      </header>

      <div className="zarf__scene">
        <div className={`zarf__envelope${open ? '' : ' zarf__envelope--closed'}`} onClick={open ? undefined : openEnvelope}>
          <svg className="zarf__back" viewBox="0 0 300 200" preserveAspectRatio="none" aria-hidden="true" focusable="false">
            <rect width="300" height="200" rx="6" />
          </svg>

          <div className="zarf__card" ref={cardRef} tabIndex={-1} role="group" aria-label="Tarihi not edin kartı" aria-hidden={!open}>
            <svg className="zarf__card-shape" viewBox="0 0 240 327" preserveAspectRatio="none" aria-hidden="true" focusable="false">
              <defs>
                <pattern id={grainId} width="7" height="7" patternUnits="userSpaceOnUse">
                  <circle cx="1.2" cy="1.5" r="0.45" /><circle cx="4.6" cy="3.2" r="0.35" /><circle cx="2.8" cy="5.9" r="0.4" />
                </pattern>
              </defs>
              <path d={cardPath} />
              <path className="zarf__card-grain" d={cardPath} fill={`url(#${grainId})`} />
              <path className="zarf__card-emboss" d={cardPath} transform="translate(120 163.5) scale(0.955) translate(-120 -163.5)" />
              <rect className="zarf__card-gold" x="16" y="18" width="208" height="291" rx="12" />
              <Sprig transform="translate(8 150) rotate(-18 15 28) scale(0.8)" />
              <Sprig transform="translate(206 150) rotate(18 15 28) scale(0.8)" />
            </svg>
            <div className="zarf__card-body">
              <p className="zarf__card-script">Tarihi Not Edin</p>
              <svg className="zarf__heart" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d={heartPath} /></svg>
              {date
                ? <time dateTime={model.startsAt} className="zarf__date">
                  <span className="zarf__date-weekday">{date.weekday}</span>
                  <span className="zarf__date-day">{date.day}</span>
                  <span className="zarf__date-month">{date.month} {date.year}</span>
                  <span className="zarf__date-time">{date.time}</span>
                </time>
                : <p className="zarf__date zarf__date--tbd">Tarih daha sonra duyurulacak</p>}
              {model.venue?.name ? <p className="zarf__card-venue">{model.venue.name}</p> : null}
            </div>
            <Flower className="zarf__flower zarf__flower--card" />
          </div>

          <figure className="zarf__photo" aria-hidden={!open}>
            <div className="zarf__photo-frame">
              {model.coverImageUrl
                ? <img src={model.coverImageUrl} alt="Davetiye fotoğrafı" width="300" height="400" loading="lazy" decoding="async" />
                : <div className="zarf__photo-placeholder" aria-hidden="true">
                  <svg viewBox="0 0 24 24" focusable="false"><path d={heartPath} /></svg>
                </div>}
            </div>
          </figure>

          <svg className="zarf__pocket" viewBox="0 0 300 200" preserveAspectRatio="none" aria-hidden="true" focusable="false">
            <path className="zarf__pocket-side" d="M-2 -2L150 112L-2 204Z" />
            <path className="zarf__pocket-side" d="M302 -2L150 112L302 204Z" />
            <path className="zarf__pocket-bottom" d="M-2 204L150 96L302 204Z" />
            <path className="zarf__pocket-gold" d="M-2 204L150 96L302 204" />
          </svg>

          <div className="zarf__flap" aria-hidden="true">
            <svg className="zarf__flap-face zarf__flap-face--outer" viewBox="0 0 300 110" preserveAspectRatio="none" focusable="false"><path d={flapPath} /><path className="zarf__flap-gold" d={flapPath} transform="translate(0 -5)" /></svg>
            <svg className="zarf__flap-face zarf__flap-face--inner" viewBox="0 0 300 110" preserveAspectRatio="none" focusable="false"><path d={flapPath} /><PressedFlower /></svg>
          </div>

          <div className="zarf__seal" aria-hidden="true"><span>{monogram}</span></div>
          <CallaLilies className="zarf__calla" />
        </div>
      </div>

      {/* One fixed-height slot: the open and close controls swap places without moving anything. */}
      <div className="zarf__cta-row">
        {open
          ? (hideInlineClose ? null : <button type="button" className="zarf__close" onClick={closeEnvelope}>{t('templateUi.zarf.close')}</button>)
          : <button type="button" className="zarf__cta" ref={ctaRef} aria-expanded={false} onClick={openEnvelope}>{t('templateUi.zarf.open')}</button>}
      </div>
    </section>

    {revealed ? <>
    <div className="zarf__details">
      {model.message ? <p className="zarf__hint">{model.message}</p> : null}
      <DateVenue model={model} compact />
      <Program model={model} title="Günün akışı" />
      {model.hostNames.length ? <p className="zarf__signature">{model.hostNames.join(' & ')}</p> : null}
    </div>
    <InvitationOptionalSections model={model} />
    </> : null}
  </article>
}
