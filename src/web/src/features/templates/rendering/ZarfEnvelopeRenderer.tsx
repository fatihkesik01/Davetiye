import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'

import './zarf.css'
import { InvitationOptionalSections } from './InvitationOptionalSections'
import { type NormalizedInvitationRenderModel } from './model'
import { DateVenue, Program, type TemplateRendererProps } from './renderers'
import { CallaLilies, PressedFlower, Rose, RoseCluster } from './ZarfFlowers'
import { resolveZarfAccent, zarfPalettes } from './zarfAccent'

/**
 * Scalloped heart outline for the Save-the-Date card: the classic parametric heart sampled at equal
 * arc length, each step closed with an outward half-circle (drawn clockwise). Static, built once.
 */
function scallopedHeart(width: number, height: number, bumps: number, margin: number): string {
  const samples = 720
  const raw: Array<[number, number]> = []
  for (let i = 0; i < samples; i += 1) {
    const t = (2 * Math.PI * i) / samples
    raw.push([16 * Math.sin(t) ** 3, -(13 * Math.cos(t) - 5 * Math.cos(2 * t) - 2 * Math.cos(3 * t) - Math.cos(4 * t))])
  }
  const ys = raw.map(point => point[1])
  const minY = Math.min(...ys)
  const maxY = Math.max(...ys)
  const pts = raw.map(([x, y]): [number, number] => [
    margin + ((x + 16) / 32) * (width - 2 * margin),
    margin + ((y - minY) / (maxY - minY)) * (height - 2 * margin),
  ])
  const cumulative = [0]
  for (let i = 1; i <= samples; i += 1) {
    const [x0, y0] = pts[i - 1]!
    const [x1, y1] = pts[i % samples]!
    cumulative.push(cumulative[i - 1]! + Math.hypot(x1 - x0, y1 - y0))
  }
  const total = cumulative[samples]!
  const picked: Array<[number, number]> = []
  let cursor = 0
  for (let k = 0; k < bumps; k += 1) {
    const target = (total * k) / bumps
    while (cursor < samples && cumulative[cursor + 1]! < target) cursor += 1
    picked.push(pts[cursor]!)
  }
  let d = `M${picked[0]![0].toFixed(1)} ${picked[0]![1].toFixed(1)}`
  for (let k = 1; k <= bumps; k += 1) {
    const [x0, y0] = picked[k - 1]!
    const [x1, y1] = picked[k % bumps]!
    const radius = Math.hypot(x1 - x0, y1 - y0) / 2
    d += `A${radius.toFixed(1)} ${radius.toFixed(1)} 0 0 1 ${x1.toFixed(1)} ${y1.toFixed(1)}`
  }
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

const cardPath = scallopedHeart(240, 228, 36, 9)
const flapPath = scallopedFlap()
const heartPath = 'M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z'

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
  }
}

/** The invitation content mounts as the card starts sliding out (the flap has lifted by then). */
const revealDelayMs = 500

/** On closing the content fades out this fast, then unmounts, while the envelope keeps closing. */
const leaveMs = 140

function prefersReducedMotion(): boolean {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function ZarfEnvelopeRenderer({ model, onOpenChange, initialOpen = false, open: controlledOpen, hideInlineClose = false }: TemplateRendererProps) {
  // `open` drives the animation classes; `revealed` mounts the rest of the invitation. Nothing but the
  // cover exists in the DOM until the envelope has been opened, and it is removed again after closing.
  const { t } = useTranslation()
  const [open, setOpen] = useState(initialOpen)
  const [revealed, setRevealed] = useState(initialOpen)
  const [leaving, setLeaving] = useState(false)
  const cardRef = useRef<HTMLDivElement>(null)
  const ctaRef = useRef<HTMLButtonElement>(null)
  const stageRef = useRef<HTMLElement>(null)
  const closeTimer = useRef<number | undefined>(undefined)
  const revealTimer = useRef<number | undefined>(undefined)
  const palette = zarfPalettes[resolveZarfAccent(model.accentColor)]
  const style = { '--z-base': palette.base, '--z-ink': palette.ink, '--z-seal': palette.seal, '--z-leaf': palette.leaf, '--z-stem': palette.stem, '--z-rose': palette.rose } as CSSProperties
  const date = cardDate(model)
  const names = model.hostNames.join(' & ') || model.headline
  // Longer names get a smaller script size so they stay on at most two lines inside the heart.
  const nameSize = names.length > 24 ? 'long' : names.length > 14 ? 'medium' : 'short'
  const monogram = model.hostNames.map(name => name.at(0)?.toLocaleUpperCase('tr-TR')).filter(Boolean).slice(0, 2).join('') || 'D'

  useEffect(() => () => {
    window.clearTimeout(closeTimer.current)
    window.clearTimeout(revealTimer.current)
  }, [])

  const openEnvelope = () => {
    window.clearTimeout(closeTimer.current)
    window.clearTimeout(revealTimer.current)
    setLeaving(false)
    setOpen(true)
    const reveal = () => {
      setRevealed(true)
      onOpenChange?.(true)
    }
    // Mounting the content under the cover is the heaviest part of opening: do it after the flap lifts.
    if (prefersReducedMotion()) reveal()
    else revealTimer.current = window.setTimeout(reveal, revealDelayMs)
    // The button leaves the page once opened: hand keyboard/screen-reader focus to the card.
    window.requestAnimationFrame(() => cardRef.current?.focus({ preventScroll: true }))
  }

  const openRef = useRef(open)
  useEffect(() => { openRef.current = open }, [open])

  const closeEnvelope = () => {
    window.clearTimeout(revealTimer.current)
    window.clearTimeout(closeTimer.current)
    setOpen(false)
    const finish = () => {
      // If the guest scrolled down into the content, bring the cover back into view in the same
      // frame the content is removed, so the page never jumps away from what they were looking at.
      const stage = stageRef.current
      if (stage && stage.getBoundingClientRect().top < 0) stage.scrollIntoView({ behavior: 'auto', block: 'start' })
      setRevealed(false)
      setLeaving(false)
      onOpenChange?.(false)
      window.requestAnimationFrame(() => ctaRef.current?.focus({ preventScroll: true }))
    }
    // The texts leave at once (a very short fade) while the card and flap play their closing animation.
    if (prefersReducedMotion() || !revealed) finish()
    else {
      setLeaving(true)
      closeTimer.current = window.setTimeout(finish, leaveMs)
    }
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
      <h1 className="visually-hidden">{model.headline}</h1>

      <div className="zarf__scene">
        <div className={`zarf__envelope${open ? '' : ' zarf__envelope--closed'}`} onClick={open ? undefined : openEnvelope}>
          <svg className="zarf__back" viewBox="0 0 300 200" preserveAspectRatio="none" aria-hidden="true" focusable="false">
            <rect width="300" height="200" rx="6" />
          </svg>

          <div className="zarf__card" ref={cardRef} tabIndex={-1} role="group" aria-label="Tarihi not edin kartı" aria-hidden={!open}>
            <svg className="zarf__card-shape" viewBox="0 0 240 228" preserveAspectRatio="none" aria-hidden="true" focusable="false">
              <path d={cardPath} />
              <path className="zarf__card-emboss" d={cardPath} transform="translate(120 114) scale(0.94) translate(-120 -114)" />
              <path className="zarf__card-gold" d={cardPath} transform="translate(120 114) scale(0.88) translate(-120 -114)" />
              <Rose transform="translate(58 30) scale(0.78)" />
              <Rose transform="translate(190 34) scale(0.62) rotate(30)" />
            </svg>
            <div className="zarf__card-body">
              <p className={`zarf__card-names zarf__card-names--${nameSize}`}>{names}</p>
              {date
                ? <time dateTime={model.startsAt} className="zarf__date">
                  <span className="zarf__date-day">{date.day}</span>
                  <span className="zarf__date-month">{date.month} {date.year}</span>
                </time>
                : <p className="zarf__date zarf__date--tbd">Tarih daha sonra duyurulacak</p>}
            </div>
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
          <RoseCluster className="zarf__roses" />
        </div>
      </div>

      {/* One fixed-height slot: the open and close controls swap places without moving anything. */}
      <div className="zarf__cta-row">
        {open
          ? (hideInlineClose ? null : <button type="button" className="zarf__close" onClick={closeEnvelope}>{t('templateUi.zarf.close')}</button>)
          : <button type="button" className="zarf__cta" ref={ctaRef} aria-expanded={false} onClick={openEnvelope}>{t('templateUi.zarf.open')}</button>}
      </div>
    </section>

    {revealed ? <div className={`zarf__content${leaving ? ' zarf__content--leaving' : ''}`}>
    <div className="zarf__details">
      <p className="zarf__hint">{model.message ?? t('templateUi.zarf.defaultMessage')}</p>
      <DateVenue model={model} compact />
      <Program model={model} title="Günün akışı" />
      {model.hostNames.length ? <p className="zarf__signature">{model.hostNames.join(' & ')}</p> : null}
    </div>
    <InvitationOptionalSections model={model} />
    </div> : null}
  </article>
}
