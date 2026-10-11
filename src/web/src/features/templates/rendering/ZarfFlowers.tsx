import type { CSSProperties, ReactNode } from 'react'

import { zarfCritterByAmbiance, type ZarfAmbianceKey, type ZarfFloraKey } from './zarfTheme'

/**
 * Original, lightweight inline SVG botanicals and tiny creatures for the zarf-davetiye envelope. Every
 * colour is a CSS variable derived from the accent, the flora and the ambiance (see zarf.css). All
 * pieces are decorative (aria-hidden by their parents) and static or driven by CSS keyframes only.
 */

const callaPetal = 'M0 0C-13 3-15 24-3 36C2 40 9 37 11 29C14 15 10 2 0 0Z'
const callaFold = 'M0 0C-4 8-3 22 3 33C8 24 8 8 0 0Z'
const blossomPath = 'M0-1.6C-2-5-6-5-6-2C-6 0-4 1-2.4 1.2C-4 2.6-4.4 5.6-2 6.4C0 7 1.2 4 0 2.4C1.2 4 4 7 6 5.6C7.6 4 5.6 1.6 3 1.2C5.6 1 7.4-1 6-3C4-5.4 1-4.4 0-1.6Z'

function Bloom({ transform }: { transform: string }) {
  return <g transform={transform}>
    <path className="zarf-art__petal" d={callaPetal} />
    <path className="zarf-art__petal-shade" d={callaFold} />
    <ellipse className="zarf-art__spadix" cx="1" cy="9" rx="1.6" ry="6" />
  </g>
}

/** A layered rose (about 30 units across), centred on the origin; colours come from CSS variables. */
export function Rose({ transform }: { transform?: string }) {
  return <g transform={transform}>
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} className="zarf-art__rose-1" cx="0" cy="-8" rx="9" ry="10" transform={`rotate(${angle})`} />)}
    {[36, 108, 180, 252, 324].map(angle => <ellipse key={angle} className="zarf-art__rose-2" cx="0" cy="-5" rx="6.2" ry="7" transform={`rotate(${angle})`} />)}
    <circle className="zarf-art__rose-2" r="5" />
    <path className="zarf-art__rose-swirl" d="M0 -3.2C3.8 -3.4 4.6 1.6 0.8 2.6C-2.6 3.4-4.2-0.6-1.4-2.2" fill="none" strokeLinecap="round" />
  </g>
}

/** A lighter rose for the botanical frame: five petals, a heart and a swirl (7 elements). */
function RoseLite() {
  return <>
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} className="zarf-art__rose-1" cx="0" cy="-8" rx="9" ry="10" transform={`rotate(${angle})`} />)}
    <circle className="zarf-art__rose-2" r="7" />
    <path className="zarf-art__rose-swirl" d="M0 -3.2C3.8 -3.4 4.6 1.6 0.8 2.6C-2.6 3.4-4.2-0.6-1.4-2.2" fill="none" strokeLinecap="round" />
  </>
}

/**
 * One flower head of the chosen flora, centred on the origin and roughly 30 units across. `full` uses
 * the richer rose; everything else is deliberately simple (a handful of shapes each).
 */
export function FloraHead({ kind, transform, full = false }: { kind: ZarfFloraKey; transform: string; full?: boolean }) {
  let head: ReactNode
  switch (kind) {
    case 'papatya':
      head = <>
        {Array.from({ length: 10 }, (_, index) => <ellipse key={index} className="zarf-art__fl-1" cx="0" cy="-10" rx="3" ry="8.6" transform={`rotate(${index * 36})`} />)}
        <circle className="zarf-art__fl-3" r="4.6" />
      </>
      break
    case 'lale':
      head = <>
        <path className="zarf-art__fl-2" d="M-10 -4C-11 -16-5 -22 0 -24C5 -22 11 -16 10 -4C9 6 4 11 0 11C-4 11 -9 6 -10 -4Z" />
        <path className="zarf-art__fl-1" d="M-8 -4C-9 -15-4 -20-1 -22C-3 -12-3 0 0 10C-5 9-8 4-8 -4Z" />
        <path className="zarf-art__fl-1" d="M8 -4C9 -15 4 -20 1 -22C3 -12 3 0 0 10C5 9 8 4 8 -4Z" />
      </>
      break
    case 'lavanta':
      head = <>
        <path className="zarf-art__fl-line" d="M0 12L0 -22" fill="none" strokeLinecap="round" />
        {Array.from({ length: 6 }, (_, index) => <g key={index}>
          <ellipse className="zarf-art__fl-1" cx="-2.8" cy={-21 + index * 5.2} rx="2.5" ry="3.6" transform={`rotate(-28 -2.8 ${-21 + index * 5.2})`} />
          <ellipse className="zarf-art__fl-2" cx="2.8" cy={-18.5 + index * 5.2} rx="2.5" ry="3.6" transform={`rotate(28 2.8 ${-18.5 + index * 5.2})`} />
        </g>)}
      </>
      break
    case 'kiraz':
      head = <>
        <path className="zarf-art__fl-1" d={blossomPath} transform="scale(2.5)" />
        <circle className="zarf-art__fl-3" r="2.6" />
        <path className="zarf-art__fl-line" d="M0 0L-4 -5M0 0L4 -5M0 0L0 -6" fill="none" strokeLinecap="round" />
      </>
      break
    default:
      head = full ? <Rose /> : <RoseLite />
  }
  return <g transform={transform}>{head}</g>
}

/** Calla lilies with long stems and leaves, tucked at the envelope's lower left, with a small flora head. */
export function CallaLilies({ className, flora }: { className?: string; flora: ZarfFloraKey }) {
  return <svg className={className} viewBox="0 0 100 150" aria-hidden="true" focusable="false">
    <g className="zarf-art__leaf">
      <path d="M44 150C28 120 22 92 30 62C42 84 48 116 44 150Z" />
      <path d="M50 150C66 126 78 108 94 96C88 122 70 142 50 150Z" />
      <path d="M40 150C26 138 12 124 4 104C22 108 36 126 40 150Z" />
    </g>
    <g className="zarf-art__stem" fill="none" strokeLinecap="round">
      <path d="M46 150C44 112 40 78 32 48" />
      <path d="M52 150C54 108 58 70 66 38" />
      <path d="M49 150C50 118 52 96 50 70" />
    </g>
    <Bloom transform="translate(31 46) rotate(-14)" />
    <Bloom transform="translate(67 36) rotate(12) scale(0.92)" />
    <Bloom transform="translate(50 68) rotate(2) scale(0.8)" />
    <FloraHead kind={flora} transform="translate(78 132) scale(0.62)" full />
  </svg>
}

/** A small pressed flower with leaves, drawn inside a 300x110 flap viewBox (the flap's inner face). */
export function PressedFlower() {
  return <g className="zarf-art__pressed" transform="translate(150 40)">
    <path className="zarf-art__pressed-stem" d="M0 44C-1 28 1 14 0 4" fill="none" strokeLinecap="round" />
    <path className="zarf-art__pressed-leaf" d="M0 34C-14 30-22 20-24 10C-10 12-2 22 0 34Z" />
    <path className="zarf-art__pressed-leaf" d="M0 28C12 26 20 18 24 8C10 8 2 16 0 28Z" />
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} className="zarf-art__pressed-petal" cx="0" cy="-9" rx="5.2" ry="10" transform={`rotate(${angle})`} />)}
    <circle className="zarf-art__spadix" r="3.4" />
  </g>
}

/** Flowers of the chosen flora with leaves, tucked at the envelope's lower right (viewBox 100x100). */
export function FloraCluster({ className, flora }: { className?: string; flora: ZarfFloraKey }) {
  return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <g className="zarf-art__leaf">
      <path d="M52 100C34 86 26 68 30 48C46 60 54 80 52 100Z" />
      <path d="M60 100C76 90 90 78 98 60C80 62 64 78 60 100Z" />
      <path d="M44 96C30 94 14 86 6 72C22 70 38 80 44 96Z" />
    </g>
    <FloraHead kind={flora} transform="translate(42 76) scale(1.2)" full />
    <FloraHead kind={flora} transform="translate(70 66) scale(0.95)" full />
    <FloraHead kind={flora} transform="translate(58 46) scale(0.78)" full />
  </svg>
}

/** A small five-lobed blossom as a single path plus a golden centre. */
function Blossom({ transform }: { transform: string }) {
  return <g transform={transform}>
    <path className="zarf-art__blossom" d={blossomPath} />
    <circle className="zarf-art__spadix" r="1.5" cy="0.6" />
  </g>
}

/* ---- creatures: tiny, original, CSS-animated (transform/opacity keyframes only) ---- */

function CritterArt({ kind }: { kind: ReturnType<typeof critterKind> }) {
  switch (kind) {
    case 'bee':
      return <svg viewBox="0 0 24 20" focusable="false">
        <g className="zarf-critter__wings"><ellipse cx="9" cy="5" rx="4.4" ry="3" className="zarf-critter__wing" /><ellipse cx="15" cy="5" rx="4.4" ry="3" className="zarf-critter__wing" /></g>
        <ellipse cx="12" cy="12" rx="7" ry="5" className="zarf-critter__bee" />
        <path d="M9 7.6V16.4M13 7.2V16.8M17 8.4V15.6" className="zarf-critter__stripe" fill="none" strokeLinecap="round" />
        <circle cx="19" cy="11.5" r="1.8" className="zarf-critter__stripe-fill" />
      </svg>
    case 'dragonfly':
      return <svg viewBox="0 0 30 20" focusable="false">
        <g className="zarf-critter__wings">
          <ellipse cx="11" cy="6" rx="6.5" ry="2" className="zarf-critter__wing" transform="rotate(-12 11 6)" />
          <ellipse cx="11" cy="14" rx="6.5" ry="2" className="zarf-critter__wing" transform="rotate(12 11 14)" />
          <ellipse cx="17" cy="6" rx="6.5" ry="2" className="zarf-critter__wing" transform="rotate(-12 17 6)" />
          <ellipse cx="17" cy="14" rx="6.5" ry="2" className="zarf-critter__wing" transform="rotate(12 17 14)" />
        </g>
        <rect x="3" y="9" width="24" height="2" rx="1" className="zarf-critter__dragon" />
        <circle cx="4" cy="10" r="2" className="zarf-critter__dragon" />
      </svg>
    case 'firefly':
      return <svg viewBox="0 0 16 16" focusable="false">
        <circle cx="8" cy="8" r="7" className="zarf-critter__halo" />
        <circle cx="8" cy="8" r="2.4" className="zarf-critter__spark" />
      </svg>
    case 'ladybug':
      return <svg viewBox="0 0 20 20" focusable="false">
        <ellipse cx="10" cy="11" rx="7" ry="7.4" className="zarf-critter__lady" />
        <path d="M10 4V18" className="zarf-critter__stripe" fill="none" />
        <circle cx="10" cy="4" r="3" className="zarf-critter__stripe-fill" />
        <circle cx="6.8" cy="9" r="1.3" className="zarf-critter__stripe-fill" /><circle cx="13.2" cy="9" r="1.3" className="zarf-critter__stripe-fill" />
        <circle cx="7" cy="14" r="1.2" className="zarf-critter__stripe-fill" /><circle cx="13" cy="14" r="1.2" className="zarf-critter__stripe-fill" />
      </svg>
    default:
      return <svg viewBox="0 0 24 20" focusable="false">
        <g className="zarf-critter__wings">
          <path d="M12 10C13 2 22 0 22 6C22 11 16 12 12 10Z" className="zarf-critter__wing-a" />
          <path d="M12 10C11 2 2 0 2 6C2 11 8 12 12 10Z" className="zarf-critter__wing-a" />
          <path d="M12 10C16 12 21 14 19 18C17 20 13 16 12 10Z" className="zarf-critter__wing-b" />
          <path d="M12 10C8 12 3 14 5 18C7 20 11 16 12 10Z" className="zarf-critter__wing-b" />
        </g>
        <ellipse cx="12" cy="10" rx="0.9" ry="5" className="zarf-critter__stripe-fill" />
      </svg>
  }
}

function critterKind(ambiance: ZarfAmbianceKey) {
  return zarfCritterByAmbiance[ambiance]
}

/** Where the three creatures of a side start, as percentages of the side box (ladybugs sit on the stems). */
const critterSpots: Record<ReturnType<typeof critterKind>, Array<[number, number]>> = {
  butterfly: [[34, 16], [58, 44], [20, 66]],
  bee: [[36, 20], [60, 48], [24, 70]],
  dragonfly: [[40, 14], [62, 40], [22, 62]],
  firefly: [[30, 20], [60, 36], [18, 56]],
  ladybug: [[22, 57], [44, 31], [58, 83]],
}

function Critters({ ambiance }: { ambiance: ZarfAmbianceKey }) {
  const kind = critterKind(ambiance)
  return <>
    {critterSpots[kind].map(([left, top], index) => <span
      key={index}
      className={`zarf-critter zarf-critter--${kind} zarf-critter--path-${index % 3}`}
      style={{ left: `${left}%`, top: `${top}%`, '--i': index } as CSSProperties}
    ><CritterArt kind={kind} /></span>)}
  </>
}

/** A few cherry petals that drift down slowly (kiraz only). */
function Petals() {
  return <>
    {[18, 46, 72].map((left, index) => <span key={left} className="zarf-petal" style={{ left: `${left}%`, '--i': index } as CSSProperties}>
      <svg viewBox="0 0 10 12" focusable="false"><path d="M5 0C8 3 10 7 5 12C0 7 2 3 5 0Z" className="zarf-petal__shape" /></svg>
    </span>)}
  </>
}

/**
 * One side of the botanical frame that grows in when the envelope opens (viewBox 200x360, anchored
 * at the left edge; the right side is the same drawing mirrored). Three stacked, separately animated
 * layers (stems and leaves, flowers, blooms) so each grows as a composited transform/opacity animation.
 */
function GardenSide({ side, ambiance, flora }: { side: 'left' | 'right'; ambiance: ZarfAmbianceKey; flora: ZarfFloraKey }) {
  const layer = (name: string, children: ReactNode) => <svg className={`zarf-garden__layer zarf-garden__layer--${name}`} viewBox="0 0 200 360" aria-hidden="true" focusable="false">{children}</svg>
  return <div className={`zarf-garden__side zarf-garden__side--${side}`}>
    {layer('stems', <>
      <g className="zarf-art__stem" fill="none" strokeLinecap="round">
        <path d="M-4 362C34 300 18 236 52 186C74 154 72 104 112 62" />
        <path d="M-4 334C40 324 74 336 118 302" />
        <path d="M-4 290C20 270 40 250 62 244" />
      </g>
      <g className="zarf-art__leaf">
        <path d="M30 280C8 270 4 250 8 236C26 246 34 262 30 280Z" />
        <path d="M44 220C70 214 82 196 82 182C60 186 46 200 44 220Z" />
        <path d="M60 160C40 150 34 132 38 118C56 128 64 144 60 160Z" />
        <path d="M84 112C108 112 122 96 124 84C102 86 88 98 84 112Z" />
        <path d="M70 330C78 308 96 302 104 306C98 322 86 332 70 330Z" />
        <path d="M30 322C38 340 56 346 66 342C58 330 44 322 30 322Z" />
      </g>
    </>)}
    {layer('roses', <>
      <FloraHead kind={flora} transform="translate(46 196) scale(1.25)" />
      <FloraHead kind={flora} transform="translate(92 88) scale(1)" />
      <FloraHead kind={flora} transform="translate(14 300) scale(1)" />
      <FloraHead kind={flora} transform="translate(112 296) scale(0.8)" />
    </>)}
    {layer('blooms', <>
      <Bloom transform="translate(62 238) rotate(-20) scale(0.95)" />
      <Blossom transform="translate(118 56) scale(1.3)" />
      <Blossom transform="translate(76 150) scale(1.1)" />
      <Blossom transform="translate(28 252) scale(1.1)" />
      <Blossom transform="translate(128 290) scale(1)" />
    </>)}
    <Critters ambiance={ambiance} />
    {flora === 'kiraz' ? <Petals /> : null}
  </div>
}

/** Decorative botanical frame (left and right) behind the open envelope: aria-hidden, no pointer events. */
export function Garden({ leaving, ambiance, flora }: { leaving: boolean; ambiance: ZarfAmbianceKey; flora: ZarfFloraKey }) {
  return <div className={`zarf-garden${leaving ? ' zarf-garden--leaving' : ''}`} aria-hidden="true">
    <GardenSide side="left" ambiance={ambiance} flora={flora} />
    <GardenSide side="right" ambiance={ambiance} flora={flora} />
  </div>
}
