import { memo, useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'

import './dantel.css'
import './zarf.css'
import { InvitationOptionalSections } from './InvitationOptionalSections'
import { DateVenue, Program, type TemplateRendererProps } from './renderers'
import { RoyalGateScene, RoyalGateSteps, RoyalLeaf } from './DantelRoyalGate'
import { FloraHead } from './ZarfFlowers'
import { defaultZarfAccent2, resolveZarfAccent, resolveZarfAccent2, zarfPalettes, zarfPalettes2 } from './zarfAccent'
import { primaryFlora, resolveZarfAmbiance, resolveZarfGarden, zarfGardenSpecs, type ZarfAmbianceKey } from './zarfTheme'

/**
 * One state, three phases: the whole opening is a class flip (`dantel--open`) driven by CSS: the camera
 * push-in on the gate layers, the leaves swinging and the paper unrolling out of the doorway. The paper
 * stays laid out while it rolls back up (`closing`) and is only removed from layout afterwards.
 */
type Phase = 'closed' | 'open' | 'closing'
const closeMs = 900
const animatingMs = 1300

/** Doorway width as a fraction of the scene width, per theme (the camera zoom makes it as wide as the paper). */
const doorwayRatio = (royal: boolean) => (royal ? 0.525 : 1)
const maxPaperRem = 44

const dantelAmbiance: Record<ZarfAmbianceKey, { tint: string; background: string }> = {
  sabah: { tint: '#f3df9a', background: 'radial-gradient(ellipse at 50% 8%, #fff7d7 0, transparent 36%), linear-gradient(145deg, #e7f0df, #f7f0df 58%, #dcebe3)' },
  cayir: { tint: '#b9d87b', background: 'radial-gradient(ellipse at 82% 14%, #ffe695 0, transparent 30%), linear-gradient(160deg, #d3e9c5, #f6efc8 54%, #b6d9c6)' },
  gunbatimi: { tint: '#f29b72', background: 'radial-gradient(ellipse at 50% 12%, #ffe7a8 0, transparent 32%), linear-gradient(165deg, #f3b79f, #f1c0b9 48%, #cb879b)' },
  gece: { tint: '#8b7bc6', background: 'radial-gradient(1.5px 1.5px at 12% 18%, #fff 98%, transparent), radial-gradient(1.5px 1.5px at 78% 27%, #fff 98%, transparent), radial-gradient(1px 1px at 34% 68%, #fff 98%, transparent), radial-gradient(1.5px 1.5px at 90% 82%, #fff 98%, transparent), linear-gradient(155deg, #172345, #313363 58%, #503e70)' },
  safak: { tint: '#ef9fbd', background: 'radial-gradient(ellipse at 50% 10%, #fff 0, transparent 38%), linear-gradient(155deg, #f9e5ed, #f1b8cf 55%, #d98fab)' },
  orman: { tint: '#61966b', background: 'radial-gradient(ellipse at 50% 15%, rgb(198 220 159 / 0.3), transparent 44%), linear-gradient(155deg, #142e27, #285343 58%, #3c604f)' },
  okyanus: { tint: '#5fb3c2', background: 'radial-gradient(ellipse at 50% 12%, #fff 0, transparent 38%), repeating-linear-gradient(172deg, transparent 0 72px, rgb(255 255 255 / 0.22) 73px 76px), linear-gradient(160deg, #c6e9ed, #8ecbd5 58%, #61a9ba)' },
  lila: { tint: '#a48acb', background: 'radial-gradient(ellipse at 50% 14%, #fff 0, transparent 36%), linear-gradient(150deg, #eee8f8, #d2bce9 56%, #a992cf)' },
}

const LaceFiligree = memo(function LaceFiligree({ side, flower }: { side: 'left' | 'right'; flower: 'gul' | 'papatya' | 'lale' | 'lavanta' | 'kiraz' }) {
  return <svg className={`dantel__filigree dantel__filigree--${side}`} viewBox="0 0 100 720" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">
    <path className="dantel__vine-shadow" d="M17 0C65 68 20 115 68 182S24 293 68 360 21 476 68 540 26 662 18 720" />
    <path className="dantel__vine-highlight" d="M14 0C62 68 17 115 65 182S21 293 65 360 18 476 65 540 23 662 15 720" />
    {[92, 246, 404, 564, 678].map((y, index) => <g key={y} className="dantel__lace-bloom" transform={`translate(${index % 2 === 0 ? 40 : 58} ${y})`}>
      <circle className="dantel__emboss-shadow" r="23" />
      <circle className="dantel__emboss-highlight" r="20" />
      <FloraHead kind={flower} transform="scale(0.78)" full />
      <circle className="dantel__pearl" r="3.2" />
    </g>)}
    {[156, 322, 488, 632].map((y, index) => <g key={y} className="dantel__lace-leaf" transform={`translate(${index % 2 === 0 ? 58 : 25} ${y}) rotate(${index % 2 === 0 ? 36 : -34})`}>
      <path className="dantel__emboss-shadow" d="M0 0C-17-19-14-35 0-42 14-35 17-19 0 0Z" />
      <path className="dantel__emboss-highlight" d="M0-2C-15-20-12-33 0-39 12-33 15-20 0-2Z" />
      <path className="dantel__leaf-vein" d="M0-4v-30" />
    </g>)}
    <path className="dantel__lace-loop" d="M6 46c24-23 48 23 72 0m-72 628c24-23 48 23 72 0" />
  </svg>
})

const RoyalInteriorArtwork = memo(function RoyalInteriorArtwork() {
  return <svg className="dantel__royal-interior" viewBox="0 0 800 1200" preserveAspectRatio="none" aria-hidden="true" focusable="false">
    <path className="dantel__royal-interior-arch" d="M38 1180V435C38 180 208 45 400 30c192 15 362 150 362 405v745M62 1180V438C62 198 222 72 400 58c178 14 338 140 338 380v742" />
    <path className="dantel__royal-interior-crown" d="M352 154l12-42 27 28 9-54 26 54 28-28 12 42zM376 160h80" />
    <path className="dantel__royal-interior-lily" d="M400 1080c-32-25-44-4-26 16l18 15 8-14 8 14 18-15c18-20 6-41-26-16zm0 15v48m-16 0h32" />
    <path className="dantel__royal-interior-rule" d="M125 230h170m210 0h170M125 990h170m210 0h170" />
  </svg>
})

function prefersReducedMotion() {
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/** The part of the gate that stays behind the paper: steps and the soft ground shadow. */
const BackScene = memo(function BackScene({ royal }: { royal: boolean }) {
  return royal ? <RoyalGateSteps /> : <div className="dantel__ground" aria-hidden="true" />
})

interface FrontSceneProps { royal: boolean; flower: 'gul' | 'papatya' | 'lale' | 'lavanta' | 'kiraz'; hideFiligree: boolean; monogram: string; onMedallion: () => void }

/** The stone portal (royal) or valance (lace), the two leaves and the monogram plaque. Static: memoised. */
const FrontScene = memo(function FrontScene({ royal, flower, hideFiligree, monogram, onMedallion }: FrontSceneProps) {
  return <>
    {royal ? <RoyalGateScene /> : <div className="dantel__valance" aria-hidden="true" />}
    <div className="dantel__doors" aria-hidden="true">
      {(['left', 'right'] as const).map(side => <div key={side} className={`dantel__door dantel__door--${side}`}>
        <div className="dantel__door-wash" />
        {royal ? <RoyalLeaf side={side} /> : hideFiligree ? null : <LaceFiligree side={side} flower={flower} />}
        <span className="dantel__panel-edge" />
      </div>)}
      <span className="dantel__center-seam" />
    </div>
    <button type="button" className="dantel__medallion" aria-label="Davetiye mührünü aç" onClick={onMedallion}>
      <span className="dantel__medallion-inner"><span>{monogram}</span><i aria-hidden="true">✦</i></span>
    </button>
  </>
})

/** A separate, code-owned renderer for the folding gate invitation (palace gate and lace themes). */
export function DantelInvitationRenderer({ model, onOpenChange, initialOpen = false, open: controlledOpen, hideInlineControls = false }: TemplateRendererProps) {
  const { t } = useTranslation()
  const [phase, setPhase] = useState<Phase>(initialOpen ? 'open' : 'closed')
  const open = phase === 'open'
  const rootRef = useRef<HTMLElement>(null)
  const stageRef = useRef<HTMLElement>(null)
  const bookRef = useRef<HTMLDivElement>(null)
  const insideRef = useRef<HTMLDivElement>(null)
  const openActionRef = useRef<HTMLButtonElement>(null)
  const closeTimer = useRef<number | undefined>(undefined)
  const animTimer = useRef<number | undefined>(undefined)
  const palette = zarfPalettes[resolveZarfAccent(model.accentColor ?? 'pudra-pembe')]
  const palette2 = zarfPalettes2[resolveZarfAccent2(model.accentColor2 ?? defaultZarfAccent2)]
  const ambiance = resolveZarfAmbiance(model.ambiance)
  const gardenKey = model.garden ? resolveZarfGarden(model.garden) : 'yok'
  const royal = gardenKey === 'saray-kapisi'
  const spec = zarfGardenSpecs[gardenKey]
  const flower = primaryFlora(spec)
  const selectedFlower = flower === 'none' ? 'gul' : flower
  const names = model.hostNames.join(' & ') || model.headline
  const monogram = model.hostNames.map(name => name.trim().charAt(0).toLocaleUpperCase('tr-TR')).filter(Boolean).slice(0, 2).join('') || 'D'
  const style = {
    '--z-base': palette.base,
    '--z-ink': palette.ink,
    '--z-rose': palette.rose,
    '--z-seal': palette2.main,
    '--z-wax': palette2.main,
    '--z-line': palette2.main,
    '--z-ink2': palette2.ink,
    '--z-onseal': palette2.onSeal,
    '--dantel-atmosphere-tint': dantelAmbiance[ambiance].tint,
    '--dantel-atmosphere-bg': dantelAmbiance[ambiance].background,
  } as CSSProperties

  // The camera zoom makes the doorway as wide as the paper: measured once per size change, never per frame.
  useLayoutEffect(() => {
    const root = rootRef.current
    const stage = stageRef.current
    const book = bookRef.current
    if (!root || !stage || !book) return
    const measure = () => {
      const paperWidth = Math.min(stage.clientWidth - 24, maxPaperRem * 16)
      const doorway = book.offsetWidth * doorwayRatio(royal)
      if (doorway > 0) root.style.setProperty('--dantel-zoom', Math.min(3.4, Math.max(1.12, paperWidth / doorway)).toFixed(3))
    }
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    observer.observe(stage)
    observer.observe(book)
    return () => observer.disconnect()
  }, [royal])

  useEffect(() => () => {
    window.clearTimeout(closeTimer.current)
    window.clearTimeout(animTimer.current)
  }, [])

  // will-change is only present while the animation runs (a data attribute survives React re-renders).
  const markAnimating = () => {
    const root = rootRef.current
    if (!root) return
    root.dataset.animating = '1'
    window.clearTimeout(animTimer.current)
    animTimer.current = window.setTimeout(() => { delete root.dataset.animating }, animatingMs)
  }

  const openInvitation = (fromCover = false) => {
    window.clearTimeout(closeTimer.current)
    markAnimating()
    setPhase('open')
    onOpenChange?.(true)
    // Opened from the cover itself: hand keyboard focus to the invitation (the plaque has just gone).
    if (fromCover) window.requestAnimationFrame(() => insideRef.current?.focus({ preventScroll: true }))
  }

  const closeInvitation = () => {
    window.clearTimeout(closeTimer.current)
    onOpenChange?.(false)
    const stage = stageRef.current
    const reduced = prefersReducedMotion()
    // The paper rolls back up into the doorway; bring the cover into view while it does.
    if (stage && stage.getBoundingClientRect().top < 0) stage.scrollIntoView({ behavior: reduced ? 'auto' : 'smooth', block: 'start' })
    const finish = () => {
      setPhase('closed')
      window.requestAnimationFrame(() => openActionRef.current?.focus({ preventScroll: true }))
    }
    if (reduced) finish()
    else {
      markAnimating()
      setPhase('closing')
      closeTimer.current = window.setTimeout(finish, closeMs)
    }
  }

  // Stable handler so the memoised gate scene is not re-rendered by state changes.
  const openRef = useRef(openInvitation)
  useEffect(() => { openRef.current = openInvitation })
  const onMedallion = useCallback(() => openRef.current(true), [])

  useEffect(() => {
    if (controlledOpen === undefined) return
    /* eslint-disable react-hooks/set-state-in-effect */
    if (controlledOpen && phase !== 'open') openInvitation()
    else if (!controlledOpen && phase === 'open') closeInvitation()
    /* eslint-enable react-hooks/set-state-in-effect */
    // eslint-disable-next-line react-hooks/exhaustive-deps -- controlled state is the host's request.
  }, [controlledOpen])

  return <article ref={rootRef} className={`invitation-template dantel zarf--amb-${ambiance} zarf--flora-${selectedFlower} dantel--theme-${gardenKey}${phase !== 'closed' ? ' dantel--shown' : ''}${open ? ' dantel--open' : ''}${phase === 'closing' ? ' dantel--closing' : ''}`} aria-label={model.headline} style={style}>
    <section className="dantel__stage" ref={stageRef}>
      <h1 className="visually-hidden">{model.headline}</h1>
      <div className="dantel__book" ref={bookRef} role="group" aria-label={t(open ? 'templateUi.gate.openInvitation' : 'templateUi.gate.closedInvitation')}>
        <div className="dantel__layer dantel__layer--back"><BackScene royal={royal} /></div>
        <div className="dantel__layer dantel__layer--front">
          <FrontScene royal={royal} flower={selectedFlower} hideFiligree={gardenKey === 'yok'} monogram={monogram} onMedallion={onMedallion} />
        </div>
      </div>

      {/* The invitation: hidden while closed, it unrolls downward out of the doorway and continues as the page. */}
      <div className="dantel__paper">
      <div className="dantel__inside" ref={insideRef} tabIndex={-1} aria-hidden={!open}>
        {royal ? <RoyalInteriorArtwork /> : null}
        <div className="dantel__inside-rule" aria-hidden="true" />
        <div className="dantel__content">
          <header className="dantel__inside-heading">
            <p className="dantel__eyebrow">Birlikte kutlamaya davetlisiniz</p>
            <h2>{names}</h2>
            <span className="dantel__inside-flourish" aria-hidden="true">✦</span>
          </header>
          {model.coverImageUrl ? <figure className="dantel__photo"><img src={model.coverImageUrl} alt="Davetiye fotoğrafı" width="300" height="400" /></figure> : null}
          {model.message ? <p className="dantel__message">{model.message}</p> : null}
          <DateVenue model={model} />
          <Program model={model} title="Günün akışı" />
          <InvitationOptionalSections model={model} />
          <footer className="dantel__inside-footer">Sevgiyle bekliyoruz</footer>
        </div>
      </div>
      </div>

      {!hideInlineControls ? <div className="dantel__controls">
        {!open ? <button type="button" ref={openActionRef} className="dantel__action" onClick={() => openInvitation(true)}>{t('templateUi.dantel.openControl')}</button>
          : <button type="button" ref={openActionRef} className="dantel__action dantel__action--secondary" onClick={closeInvitation}>{t('templateUi.dantel.close')}</button>}
      </div> : null}
    </section>
  </article>
}
