import type { CSSProperties, ReactNode } from 'react'

import { zarfGardenSpecs, type ZarfCritterKind, type ZarfFloraKind, type ZarfGardenKey } from './zarfTheme'

/**
 * Original, lightweight inline SVG botanicals and tiny creatures for the zarf-davetiye envelope. Every
 * colour is a CSS variable derived from the accent, the flora and the ambiance (see zarf.css). All
 * pieces are decorative (aria-hidden by their parents) and static or driven by CSS keyframes only.
 */

const blossomPath = 'M0-1.6C-2-5-6-5-6-2C-6 0-4 1-2.4 1.2C-4 2.6-4.4 5.6-2 6.4C0 7 1.2 4 0 2.4C1.2 4 4 7 6 5.6C7.6 4 5.6 1.6 3 1.2C5.6 1 7.4-1 6-3C4-5.4 1-4.4 0-1.6Z'

/** A layered rose (about 30 units across), centred on the origin; colours come from CSS variables. */
export function Rose({ transform }: { transform?: string }) {
  return <g transform={transform}>
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} className="zarf-art__rose-1" cx="0" cy="-8" rx="9" ry="10" transform={`rotate(${angle})`} />)}
    {[36, 108, 180, 252, 324].map(angle => <ellipse key={angle} className="zarf-art__rose-2" cx="0" cy="-5" rx="6.2" ry="7" transform={`rotate(${angle})`} />)}
    <circle className="zarf-art__rose-center" r="4.4" />
    <circle className="zarf-art__rose-glint" cx="-1.2" cy="-1.5" r="1.1" />
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
export function FloraHead({ kind, transform, full = false }: { kind: ZarfFloraKind; transform: string; full?: boolean }) {
  let head: ReactNode
  switch (kind) {
    case 'none':
      return null
    case 'papatya':
      head = <>
        {Array.from({ length: 12 }, (_, index) => <ellipse key={index} className="zarf-art__fl-1" cx="0" cy="-10" rx="2.7" ry="8.8" transform={`rotate(${index * 30})`} />)}
        <circle className="zarf-art__fl-center" r="4.7" />
        <circle className="zarf-art__fl-center-glint" cx="-1.3" cy="-1.5" r="1.1" />
      </>
      break
    case 'lale':
      head = <>
        <path className="zarf-art__fl-sepal" d="M-10 3C-15-1-17-8-15-12C-10-9-7-5-6 0M10 3C15-1 17-8 15-12C10-9 7-5 6 0" />
        <path className="zarf-art__fl-2" d="M-10 -4C-11 -16-5 -22 0 -24C5 -22 11 -16 10 -4C9 6 4 11 0 11C-4 11 -9 6 -10 -4Z" />
        <path className="zarf-art__fl-1" d="M-8 -4C-9 -15-4 -20-1 -22C-3 -12-3 0 0 10C-5 9-8 4-8 -4Z" />
        <path className="zarf-art__fl-1" d="M8 -4C9 -15 4 -20 1 -22C3 -12 3 0 0 10C5 9 8 4 8 -4Z" />
        <path className="zarf-art__fl-highlight" d="M0-19C3-14 3-7 2-2" />
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
  // The class scopes this flower's own petal colours, so a mixed garden can hold several kinds.
  return <g transform={transform} className={`zarf-fl-${kind}`}>{head}</g>
}

/** Garden-matched flowers on long stems and leaves, tucked at the envelope's lower left. */
export function CallaLilies({ className, flowers }: { className?: string; flowers: readonly ZarfFloraKind[] }) {
  const style = flowers[0]
  return <svg className={className} viewBox="0 0 100 150" aria-hidden="true" focusable="false">
    <g className="zarf-art__leaf">
      {style === 'gul' ? <>
        <path d="M48 150C24 132 17 107 23 82C42 97 51 122 48 150Z" />
        <path d="M50 150C62 117 80 98 98 93C92 121 73 143 50 150Z" />
        <path d="M42 150C19 148 7 132 2 114C21 117 38 132 42 150Z" />
      </> : style === 'papatya' ? <>
        <path d="M45 150C34 128 33 105 39 83C49 105 51 129 45 150Z" />
        <path d="M52 150C60 123 72 105 84 94C82 121 68 142 52 150Z" />
        <path d="M41 150C24 139 17 122 17 108C33 117 41 133 41 150Z" />
      </> : <>
        <path d="M45 150C24 124 21 87 28 55C43 82 48 117 45 150Z" />
        <path d="M51 150C67 117 72 77 68 43C57 77 52 115 51 150Z" />
        <path d="M42 150C18 137 9 111 11 83C30 103 40 127 42 150Z" />
      </>}
    </g>
    <g className="zarf-art__stem" fill="none" strokeLinecap="round">
      {style === 'gul' ? <>
        <path d="M48 150C44 118 37 92 31 65C28 52 29 39 34 28" />
        <path d="M49 150C54 119 65 96 72 71C76 57 76 43 70 32" />
        <path d="M46 150C56 122 48 99 50 76C51 62 58 51 66 44" />
      </> : style === 'papatya' ? <>
        <path d="M43 150C40 112 39 73 36 34" />
        <path d="M49 150C51 112 54 76 58 46" />
        <path d="M55 150C61 115 68 89 76 61" />
      </> : <>
        <path d="M42 150C40 113 36 75 32 31" />
        <path d="M49 150C48 112 49 70 50 20" />
        <path d="M56 150C58 113 63 75 69 34" />
      </>}
    </g>
    {style === 'gul' ? <>
      <FloraHead kind="gul" transform="translate(34 29) scale(0.82)" full />
      <FloraHead kind="gul" transform="translate(70 33) scale(0.72)" full />
      <FloraHead kind="gul" transform="translate(66 44) scale(0.53)" />
    </> : style === 'papatya' ? <>
      <FloraHead kind="papatya" transform="translate(36 34) scale(0.74)" full />
      <FloraHead kind="papatya" transform="translate(58 46) scale(0.68)" full />
      <FloraHead kind="papatya" transform="translate(76 61) scale(0.61)" full />
    </> : <>
      <FloraHead kind="lale" transform="translate(32 31) scale(0.78)" full />
      <FloraHead kind="lale" transform="translate(50 20) scale(0.9)" full />
      <FloraHead kind="lale" transform="translate(69 34) scale(0.76)" full />
    </>}
  </svg>
}

/** A small pressed flower with leaves, drawn inside a 300x110 flap viewBox (the flap's inner face). */
export function PressedFlower({ flora }: { flora: ZarfFloraKind }) {
  return <g className="zarf-art__pressed" transform="translate(150 40)">
    <path className="zarf-art__pressed-stem" d="M0 44C-1 28 1 14 0 4" fill="none" strokeLinecap="round" />
    <path className="zarf-art__pressed-leaf" d="M0 34C-14 30-22 20-24 10C-10 12-2 22 0 34Z" />
    <path className="zarf-art__pressed-leaf" d="M0 28C12 26 20 18 24 8C10 8 2 16 0 28Z" />
    <FloraHead kind={flora} transform="translate(0 -9) scale(0.68)" full />
  </g>
}

/** Flowers of the chosen flora with leaves, tucked at the envelope's lower right (viewBox 100x100). */
export function FloraCluster({ className, flowers }: { className?: string; flowers: readonly ZarfFloraKind[] }) {
  const style = flowers[0]
  return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <g className="zarf-art__leaf">
      {style === 'gul' ? <>
        <path d="M50 100C26 82 24 60 31 42C49 58 56 79 50 100Z" />
        <path d="M54 100C60 74 78 53 96 49C91 74 74 94 54 100Z" />
        <path d="M43 98C22 98 8 86 2 70C22 70 38 81 43 98Z" />
      </> : style === 'papatya' ? <>
        <path d="M48 100C39 78 39 58 45 39C53 59 54 81 48 100Z" />
        <path d="M54 100C61 79 72 63 88 54C85 76 71 93 54 100Z" />
        <path d="M43 98C25 91 17 78 16 63C33 70 42 83 43 98Z" />
      </> : <>
        <path d="M48 100C30 75 29 48 36 23C49 49 52 77 48 100Z" />
        <path d="M54 100C69 75 72 48 68 19C58 48 54 77 54 100Z" />
        <path d="M42 100C22 84 15 62 18 39C35 58 42 80 42 100Z" />
      </>}
    </g>
    {style === 'gul' ? <>
      <FloraHead kind="gul" transform="translate(36 62) scale(1.05)" full />
      <FloraHead kind="gul" transform="translate(67 54) scale(0.88)" full />
      <FloraHead kind="gul" transform="translate(53 36) scale(0.72)" />
    </> : style === 'papatya' ? <>
      <FloraHead kind="papatya" transform="translate(31 57) scale(0.95)" full />
      <FloraHead kind="papatya" transform="translate(61 43) scale(0.82)" full />
      <FloraHead kind="papatya" transform="translate(77 66) scale(0.72)" full />
    </> : <>
      <FloraHead kind="lale" transform="translate(34 61) scale(0.72)" full />
      <FloraHead kind="lale" transform="translate(54 42) scale(0.92)" full />
      <FloraHead kind="lale" transform="translate(73 58) scale(0.76)" full />
    </>}
    <path className="zarf-art__leaf" d="M66 100C70 88 80 82 90 84C86 94 78 100 66 100Z" />
  </svg>
}

/** Distinct non-botanical ornaments for the love-letter, space and sea themes. */
type ZarfThemeMotif = 'love' | 'space' | 'sea' | 'aurora' | 'music'
type ZarfThemeArtVariant = 'picker' | 'envelope' | 'scene'

/** Each surface gets its own composition: picker glyph, tiny envelope vignette, or full open-page scene. */
export function ZarfThemeOrnament({ motif, className, variant = 'picker', side = 'left' }: { motif: ZarfThemeMotif; className?: string; variant?: ZarfThemeArtVariant; side?: 'left' | 'right' }) {
  if (variant === 'scene') return <svg className={className} viewBox="0 0 200 360" aria-hidden="true" focusable="false">
    <defs>
      <linearGradient id="zarf-scene-paper" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#fffaf0"/><stop offset=".52" stopColor="#f6e9d7"/><stop offset="1" stopColor="#dfc8aa"/></linearGradient>
      <linearGradient id="zarf-scene-sea" x1="0" y1="0" x2="0" y2="1"><stop stopColor="#78c8cb"/><stop offset=".28" stopColor="#348fa2"/><stop offset="1" stopColor="#165c76"/></linearGradient>
      <linearGradient id="zarf-scene-planet" x1=".1" y1=".1" x2=".85" y2=".9"><stop stopColor="#a69ae8"/><stop offset=".48" stopColor="#685bb2"/><stop offset="1" stopColor="#342e70"/></linearGradient>
      <linearGradient id="zarf-scene-record" x1=".15" y1="0" x2=".85" y2="1"><stop stopColor="#564c68"/><stop offset=".42" stopColor="#211f31"/><stop offset="1" stopColor="#100f19"/></linearGradient>
      <linearGradient id="zarf-scene-rose" x1=".2" y1="0" x2=".7" y2="1"><stop stopColor="#ffd1d0"/><stop offset=".48" stopColor="#e98791"/><stop offset="1" stopColor="#b64c68"/></linearGradient>
      <linearGradient id="zarf-scene-metal" x1="0" y1="0" x2="1" y2="1"><stop stopColor="#fff0b5"/><stop offset=".45" stopColor="#d8ad54"/><stop offset="1" stopColor="#8d6534"/></linearGradient>
    </defs>
    {motif === 'love' ? <>
      <path className="zarf-scene__ground" d="M0 286Q55 250 110 282T200 264V360H0Z" />
      <path className="zarf-scene__vine" d="M8 340C28 291 18 250 51 211S91 144 78 104" />
      <path className="zarf-scene__leaf" d="M27 284q-22-25-19-43 22 8 26 30m12-41q-3-25 10-37 13 18 2 37m19-55q6-20 22-24 6 20-12 31" />
      {side === 'left' ? <>
        <path className="zarf-scene__letter" d="M90 104l54 18v83l-54-18z" /><path className="zarf-scene__letter-fold" d="M90 104l27 45 27-27M90 187l27-38 27 56" />
        <path className="zarf-scene__heart" d="M115 166l-3-3c-11-10-17-16-17-24 0-6 5-11 11-11 4 0 7 2 9 5 2-3 5-5 9-5 6 0 11 5 11 11 0 8-7 15-17 24z" />
      </> : <>
        <path className="zarf-scene__ring" d="M91 147a25 25 0 1 1 50 0 25 25 0 1 1-50 0m26-25v50" />
        <path className="zarf-scene__ribbon" d="M91 147q27-17 53 0-26 18-53 0zm27 1 2 37m-8-22-15 18m29-18 15 17" />
        <path className="zarf-scene__heart" d="M119 168l-2-2c-7-6-10-10-10-15 0-4 3-7 7-7 2 0 4 1 5 3 1-2 3-3 5-3 4 0 7 3 7 7 0 5-4 9-10 15z" />
      </>}
      <circle className="zarf-scene__spark" cx="54" cy="127" r="3"/><circle className="zarf-scene__spark" cx="151" cy="92" r="2"/>
    </> : null}
    {motif === 'space' ? <>
      {side === 'left' ? <>
        <path className="zarf-scene__rocket" d="M67 217c-2-33 9-65 32-84 16 23 17 52 2 79l-16 18z"/><path className="zarf-scene__rocket-fin" d="M67 190l-20 14 21 5m36-28 18 12-17 9"/><circle className="zarf-scene__window" cx="91" cy="174" r="8"/>
        <path className="zarf-scene__flame" d="M73 218q-2 23 12 35l8-20m8-1q15 7 23 0l-15-20"/>
        <path className="zarf-scene__orbit" d="M28 146C56 70 141 63 174 139" />
      </> : <>
        <circle className="zarf-scene__planet" cx="119" cy="154" r="47"/><ellipse className="zarf-scene__ring" cx="119" cy="154" rx="68" ry="20" transform="rotate(-24 119 154)" />
        <path className="zarf-scene__planet-mark" d="M90 145q23-19 54-8m-48 28q25-13 50-3" />
        <circle className="zarf-scene__planet-crater" cx="104" cy="133" r="5"/><circle className="zarf-scene__planet-crater" cx="136" cy="166" r="3"/>
        <path className="zarf-scene__orbit" d="M20 236C54 194 105 210 170 252" />
        <path className="zarf-scene__satellite" d="M47 98l14 14m-22 2 14-14m-8-7 17 17m-29-4 13-13"/><circle className="zarf-scene__window" cx="54" cy="105" r="7"/>
      </>}
      <path className="zarf-scene__star" d="M30 62l4 11 11 4-11 4-4 11-4-11-11-4 11-4zM174 198l3 7 7 3-7 3-3 7-3-7-7-3 7-3z"/>
      {Array.from({ length: 14 }, (_, i) => <circle key={i} className="zarf-scene__constellation" cx={(i * 47 + 13) % 190 + 5} cy={(i * 71 + 21) % 330 + 10} r={i % 4 === 0 ? 2 : 1} />)}
    </> : null}
    {motif === 'sea' ? <>
      <circle className="zarf-scene__sun" cx="146" cy="104" r="29"/><path className="zarf-scene__horizon" d="M0 156q48-7 95 0t105 0"/>
      <path className="zarf-scene__sea" d="M0 158q24-12 48 0t48 0 48 0 56 0v202H0z"/>
      <path className="zarf-scene__wave" d="M-5 197q22-10 44 0t44 0 44 0 44 0 44 0M-7 235q25-9 50 0t50 0 50 0 60 0M-5 280q20-8 40 0t40 0 40 0 40 0 50 0"/>
      {side === 'left' ? <>
        <path className="zarf-scene__island" d="M0 177q30-37 66-20 25 11 43-4-4 40-44 48-35 6-65-24z"/>
        <path className="zarf-scene__boat" d="M62 220h67q-9 17-34 17-24 0-33-17z"/><path className="zarf-scene__mast" d="M96 220v-63m0 6-25 42h25m3-28 26 28H99"/>
      </> : <>
        <path className="zarf-scene__cliff" d="M125 177q28-33 75-23v74q-37-4-53-19z"/><path className="zarf-scene__lighthouse" d="M151 171h28l-4-49h-20z"/><path className="zarf-scene__lighthouse-roof" d="M149 123l16-14 16 14z"/><path className="zarf-scene__lighthouse-light" d="M155 134h20"/>
      </>}
      <path className="zarf-scene__cloud" d="M27 83q5-14 19-9 8-18 25-9 12 6 9 18z"/>
      <path className="zarf-scene__bird" d="M116 76q9-10 18 0 9-10 18 0"/>
      {side === 'left' ? <path className="zarf-scene__coral" d="M166 351v-37m0 18-13-14m13 5 13-17m-13 36 15-7m-40 7v-28m0 14-10-9m10 0 10-12"/> : <><path className="zarf-scene__coral" d="M42 351v-34m0 16-13-14m13 5 13-16"/><path className="zarf-scene__fish" d="M72 267q13-13 27 0-14 14-27 0zm27 0 9-8v16z"/><circle className="zarf-scene__fish-eye" cx="80" cy="265" r="1.5"/></>}
    </> : null}
    {motif === 'aurora' ? <>
      <circle className="zarf-scene__moon" cx={side === 'left' ? 151 : 48} cy="72" r="19"/>
      <path className="zarf-scene__aurora-band zarf-scene__aurora-band--a" d="M-22 176C24 100 27 28 72-8q27 98-6 207z"/><path className="zarf-scene__aurora-band zarf-scene__aurora-band--b" d="M49 191C95 107 95 42 139-2q31 91-3 204z"/><path className="zarf-scene__aurora-band zarf-scene__aurora-band--c" d="M112 191c42-67 44-111 90-154q20 90-15 157z"/>
      <path className="zarf-scene__mountains-back" d="M0 251l39-56 26 36 35-58 43 65 28-37 29 40v119H0z"/><path className="zarf-scene__mountains-front" d={side === 'left' ? 'M0 292l48-54 33 42 39-48 40 51 40-32v109H0z' : 'M0 293l34-39 31 37 35-55 40 45 27-36 33 39v76H0z'} />
      {side === 'left' ? <><path className="zarf-scene__cabin" d="M24 286l27-20 27 20v34H24z"/><path className="zarf-scene__cabin-roof" d="M18 288l33-28 34 28z"/><rect className="zarf-scene__window" x="43" y="294" width="13" height="17" rx="2"/></> : <><path className="zarf-scene__pine" d="M145 287l17-32 17 32h-9l15 27h-46l15-27z"/><path className="zarf-scene__lake" d="M12 333q22-8 44 0t44 0 44 0 56 0"/></>}
      <path className="zarf-scene__stars" d="M25 57l3 7 7 3-7 3-3 7-3-7-7-3 7-3zM102 36l2 5 5 2-5 2-2 5-2-5-5-2 5-2z"/>
    </> : null}
    {motif === 'music' ? <>
      <path className="zarf-scene__stage" d="M0 278q45-19 93 0t107-4v86H0z"/>
      {side === 'left' ? <>
        <circle className="zarf-scene__record" cx="105" cy="152" r="69"/><circle className="zarf-scene__record-groove" cx="105" cy="152" r="59"/><circle className="zarf-scene__record-groove" cx="105" cy="152" r="51"/><circle className="zarf-scene__record-groove" cx="105" cy="152" r="42"/><circle className="zarf-scene__record-groove" cx="105" cy="152" r="34"/><circle className="zarf-scene__record-label" cx="105" cy="152" r="18"/><circle className="zarf-scene__record-hole" cx="105" cy="152" r="4"/>
        <path className="zarf-scene__tonearm" d="M168 91l17-18 11 10-16 17m-1-2 14 21-13 13"/><circle className="zarf-scene__tonearm-joint" cx="168" cy="91" r="7"/>
        <path className="zarf-scene__note" d="M39 221v-44l26-6v42m0-5q0 11-14 13-12 2-12-5t14-12q7-2 12 0"/>
      </> : <>
        <path className="zarf-scene__guitar" d="M133 120c-7-12-23-11-27 2-3 8 3 14 0 20-6 10-19 7-21 18-3 14 12 22 24 14 8-5 6-16 15-21 7-4 16 2 23-5 10-9 1-24-14-28z"/>
        <path className="zarf-scene__guitar-neck" d="M127 126l43-53 10 8-43 53m-10-10 10 8m-4-15 10 8m-4-15 10 8m-4-15 10 8"/>
        <circle className="zarf-scene__guitar-hole" cx="115" cy="155" r="8"/><path className="zarf-scene__string" d="M111 184l63-108m-59 110L177 78"/>
        <path className="zarf-scene__note" d="M50 212v-38l20-5v36m0-4q0 10-12 12-10 2-10-4t12-10q5-1 10 0M72 95v-25l15-4v23m0-3q0 7-9 9-8 1-8-3t9-7q4-1 8 0"/>
      </>}
      <path className="zarf-scene__music-line" d="M12 316q48-22 92 0t93 0M13 332q47-20 91 0t92 0"/>
      <path className="zarf-scene__spark" d="M35 84l3 8 8 3-8 3-3 8-3-8-8-3 8-3zM157 225l2 5 5 2-5 2-2 5-2-5-5-2 5-2z"/>
    </> : null}
  </svg>

  if (variant === 'envelope') return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    {motif === 'love' ? <><path className="zarf-theme-art__envelope" d="M13 34h74v49H13z"/><path className="zarf-theme-art__line" d="M13 36l37 28 37-28M13 82l25-23m49 23L62 59"/><path className="zarf-theme-art__heart" d="M50 55l-2-2c-7-6-10-9-10-14 0-4 3-7 7-7 2 0 4 1 5 3 1-2 3-3 5-3 4 0 7 3 7 7 0 5-4 8-10 14z" /></>
      : motif === 'space' ? <><circle className="zarf-theme-art__planet" cx="50" cy="48" r="23"/><ellipse className="zarf-theme-art__ring" cx="50" cy="48" rx="37" ry="11" transform="rotate(-22 50 48)"/><path className="zarf-theme-art__star" d="M22 17l2 6 6 2-6 2-2 6-2-6-6-2 6-2zM80 72l2 5 5 2-5 2-2 5-2-5-5-2 5-2z"/></>
      : motif === 'sea' ? <><path className="zarf-theme-art__wave" d="M2 61q12-12 24 0t24 0 24 0 24 0M2 76q12-12 24 0t24 0 24 0 24 0"/><path className="zarf-theme-art__sail" d="M48 59V20L26 57zm4 0V31l22 26z"/><path className="zarf-theme-art__horizon" d="M7 85h86"/><circle className="zarf-theme-art__sun" cx="75" cy="21" r="10"/></>
      : motif === 'aurora' ? <><path className="zarf-theme-art__aurora zarf-theme-art__aurora--one" d="M10 74C27 47 22 20 44 7c7 25 0 47-11 70z"/><path className="zarf-theme-art__aurora zarf-theme-art__aurora--two" d="M39 76C56 50 54 22 77 9c6 25-2 47-11 68z"/><path className="zarf-theme-art__mountain" d="M5 88l22-27 15 19 20-32 34 40z"/></>
      : <><path className="zarf-theme-art__staff" d="M10 40h80m-80 9h80m-80 9h80m-80 9h80m-80 9h80"/><path className="zarf-theme-art__note" d="M32 56V24l20-5v32"/><ellipse className="zarf-theme-art__notehead" cx="28" cy="59" rx="7" ry="5"/><path className="zarf-theme-art__note" d="M68 67V35l19-5v31"/><ellipse className="zarf-theme-art__notehead" cx="64" cy="70" rx="7" ry="5"/></>}
  </svg>

  if (motif === 'love') return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <path className="zarf-theme-art__line" d="M8 80C25 62 24 38 47 28C63 20 76 27 82 39" fill="none" />
    <path className="zarf-theme-art__heart" d="M64 76l-2-2c-8-7-13-12-13-18 0-5 4-9 9-9 3 0 5 1 7 4 2-3 4-4 7-4 5 0 9 4 9 9 0 6-5 11-13 18z" />
    <circle className="zarf-theme-art__dot" cx="22" cy="31" r="3" /><circle className="zarf-theme-art__dot" cx="35" cy="18" r="2" />
  </svg>
  if (motif === 'space') return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <circle className="zarf-theme-art__planet" cx="54" cy="53" r="19" /><ellipse className="zarf-theme-art__ring" cx="54" cy="53" rx="31" ry="9" transform="rotate(-24 54 53)" />
    <path className="zarf-theme-art__star" d="M20 17l2.5 7.5L30 27l-7.5 2.5L20 37l-2.5-7.5L10 27l7.5-2.5zM83 68l2 5 5 2-5 2-2 5-2-5-5-2 5-2z" />
    <circle className="zarf-theme-art__dot" cx="80" cy="20" r="2" /><circle className="zarf-theme-art__dot" cx="20" cy="68" r="1.5" />
  </svg>
  if (motif === 'sea') return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <path className="zarf-theme-art__wave" d="M2 38q12-12 24 0t24 0t24 0t24 0M2 55q12-12 24 0t24 0t24 0t24 0M2 72q12-12 24 0t24 0t24 0t24 0" fill="none" />
    <circle className="zarf-theme-art__bubble" cx="25" cy="20" r="4" /><circle className="zarf-theme-art__bubble" cx="73" cy="22" r="2.5" /><path className="zarf-theme-art__shell" d="M48 31c-12 0-20 10-20 23 0 9 8 13 20 13s20-4 20-13c0-13-8-23-20-23zM48 33v31M38 36l7 27M58 36l-7 27" />
  </svg>
  if (motif === 'aurora') return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <path className="zarf-theme-art__aurora zarf-theme-art__aurora--one" d="M4 70C20 48 18 22 38 13C45 35 37 58 28 74Z" />
    <path className="zarf-theme-art__aurora zarf-theme-art__aurora--two" d="M28 74C43 52 44 22 61 8C69 32 60 58 51 78Z" />
    <path className="zarf-theme-art__aurora zarf-theme-art__aurora--three" d="M54 79C69 58 73 33 91 22C95 47 84 67 76 82Z" />
    <path className="zarf-theme-art__mountain" d="M3 87l20-20 14 13 15-22 21 25 12-15 12 19z" />
    <path className="zarf-theme-art__star" d="M14 20l2 5 5 2-5 2-2 5-2-5-5-2 5-2zM79 10l1.5 3.5L84 15l-3.5 1.5L79 20l-1.5-3.5L74 15l3.5-1.5z" />
  </svg>
  return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <path className="zarf-theme-art__staff" d="M8 40C32 34 62 37 91 28M8 49C32 43 62 46 91 37M8 58C32 52 62 55 91 46M8 67C32 61 62 64 91 55M8 76C32 70 62 73 91 64" />
    <path className="zarf-theme-art__note" d="M28 49V25l21-5v24M49 44c0 6-9 10-14 7-6-3 1-9 8-9 2 0 4 1 6 2M70 54V30l19-5v23M89 44c0 6-9 10-14 7-5-3 1-9 8-9 2 0 4 1 6 2" />
    <circle className="zarf-theme-art__notehead" cx="27" cy="52" r="5" /><circle className="zarf-theme-art__notehead" cx="69" cy="57" r="5" />
  </svg>
}

/* ---- creatures: tiny, original, CSS-animated (transform/opacity keyframes only) ---- */

export function CritterArt({ kind }: { kind: ZarfCritterKind }) {
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

/** Start spots (percent of the side box): free flyers and the spots on the stems where ladybugs sit. */
const freeSpots: Array<[number, number]> = [[34, 16], [58, 44], [20, 66], [46, 28]]
const stemSpots: Array<[number, number]> = [[22, 57], [58, 83], [44, 31]]

function Critters({ kinds }: { kinds: readonly ZarfCritterKind[] }) {
  if (kinds.length === 0) return null
  // One kind: three of it. Two kinds: two of the first and two of the second (a mixed little crowd).
  const perKind = kinds.length === 1 ? 3 : 2
  let free = 0
  let stem = 0
  const critters: Array<{ kind: ZarfCritterKind; left: number; top: number }> = []
  for (const kind of kinds) {
    for (let count = 0; count < perKind; count += 1) {
      const [left, top] = kind === 'ladybug' ? stemSpots[stem++ % stemSpots.length]! : freeSpots[free++ % freeSpots.length]!
      critters.push({ kind, left, top })
    }
  }
  return <>
    {critters.map(({ kind, left, top }, index) => <span
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
 * at the left edge; the right side is the same drawing mirrored). Each garden uses its own foliage
 * silhouette and flower placement; stems and blossoms animate in separate composited layers.
 */
function GardenSide({ side, garden }: { side: 'left' | 'right'; garden: ZarfGardenKey }) {
  const spec = zarfGardenSpecs[garden]
  const standaloneMotif = spec.motif === 'love' || spec.motif === 'space' || spec.motif === 'sea' || spec.motif === 'aurora' || spec.motif === 'music' ? spec.motif : null
  const head = (index: number) => spec.flowers[index % spec.flowers.length]!
  const layer = (name: string, children: ReactNode) => <svg className={`zarf-garden__layer zarf-garden__layer--${name}`} viewBox="0 0 200 360" aria-hidden="true" focusable="false">{children}</svg>
  return <div className={`zarf-garden__side zarf-garden__side--${side}`}>
    {standaloneMotif
      ? <ZarfThemeOrnament motif={standaloneMotif} variant="scene" side={side} className={`zarf-garden__theme-art zarf-garden__theme-art--${spec.motif}`} />
      : null}
    {!standaloneMotif ? layer('stems', <>
      <g className="zarf-art__stem" fill="none" strokeLinecap="round">
        {garden === 'gul' ? <>
          <path d="M-5 362C18 315 38 268 46 218C52 173 72 117 116 58" />
          <path d="M20 316C54 292 76 267 92 231" />
          <path d="M43 229C16 210 4 185 8 160" />
          <path d="M75 146C105 136 124 116 132 88" />
        </> : garden === 'papatya-kelebek' ? <>
          <path d="M-5 362C22 300 28 231 35 173C40 123 49 82 60 35" />
          <path d="M18 362C52 297 62 234 74 173C83 128 95 91 108 62" />
          <path d="M48 362C77 319 103 272 124 220C140 180 150 140 154 96" />
        </> : <>
          <path d="M-5 362C28 300 31 224 34 151C36 102 37 60 38 24" />
          <path d="M20 362C52 294 59 215 64 136C68 90 71 51 74 13" />
          <path d="M50 362C85 304 101 241 111 174C118 126 123 80 124 39" />
        </>}
      </g>
      <g className="zarf-art__leaf">
        {garden === 'gul' ? <>
          <path d="M28 302C1 292-5 267 2 244C25 255 35 279 28 302Z" />
          <path d="M51 244C79 235 94 213 91 188C65 198 50 220 51 244Z" />
          <path d="M66 189C40 176 32 151 42 130C63 145 72 166 66 189Z" />
          <path d="M86 128C111 127 132 108 134 83C109 88 91 105 86 128Z" />
          <path d="M16 345C31 322 52 316 70 324C58 345 38 354 16 345Z" />
        </> : garden === 'papatya-kelebek' ? <>
          <path d="M30 278C10 260 12 234 25 214C41 235 42 259 30 278Z" />
          <path d="M37 221C60 205 65 180 54 159C36 179 31 202 37 221Z" />
          <path d="M70 281C52 260 57 236 74 217C86 239 84 263 70 281Z" />
          <path d="M80 187C105 173 114 148 105 128C84 146 76 168 80 187Z" />
          <path d="M122 258C105 239 111 216 129 199C138 220 135 243 122 258Z" />
        </> : <>
          <path d="M33 316C3 286-2 248 8 209C32 239 41 279 33 316Z" />
          <path d="M41 270C67 236 69 194 54 158C34 194 32 236 41 270Z" />
          <path d="M64 305C39 266 39 221 53 181C75 222 77 268 64 305Z" />
          <path d="M74 209C98 179 104 142 92 110C73 142 69 179 74 209Z" />
          <path d="M111 293C86 256 89 216 105 180C125 218 126 259 111 293Z" />
        </>}
      </g>
    </>) : null}
    {!standaloneMotif ? layer('roses', <>
      {garden === 'gul' ? <>
        <FloraHead kind={head(0)} transform="translate(46 218) scale(1.45)" />
        <FloraHead kind={head(1)} transform="translate(116 73) scale(1.12)" />
        <FloraHead kind={head(2)} transform="translate(8 157) scale(0.96)" />
        <FloraHead kind={head(3)} transform="translate(91 229) scale(0.84)" />
      </> : garden === 'papatya-kelebek' ? <>
        <FloraHead kind={head(0)} transform="translate(58 42) scale(1.18)" />
        <FloraHead kind={head(1)} transform="translate(105 68) scale(0.92)" />
        <FloraHead kind={head(2)} transform="translate(31 155) scale(1.04)" />
        <FloraHead kind={head(3)} transform="translate(145 88) scale(0.8)" />
      </> : <>
        <FloraHead kind={head(0)} transform="translate(38 28) scale(1.02)" />
        <FloraHead kind={head(1)} transform="translate(74 17) scale(1.18)" />
        <FloraHead kind={head(2)} transform="translate(124 43) scale(0.96)" />
      </>}
    </>) : null}
    <Critters kinds={spec.critters} />
    {spec.petals ? <Petals /> : null}
  </div>
}

/** Decorative botanical frame (left and right) behind the open envelope: aria-hidden, no pointer events. */
export function Garden({ leaving, garden }: { leaving: boolean; garden: ZarfGardenKey }) {
  return <div className={`zarf-garden${leaving ? ' zarf-garden--leaving' : ''}`} aria-hidden="true">
    <GardenSide key={`left-${garden}`} side="left" garden={garden} />
    <GardenSide key={`right-${garden}`} side="right" garden={garden} />
  </div>
}
