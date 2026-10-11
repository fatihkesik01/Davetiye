import type { ReactNode } from 'react'

/**
 * Original, lightweight inline SVG botanicals for the zarf-davetiye envelope. Every colour is a CSS
 * variable derived from the single accent (see zarf.css), so the artwork follows the chosen palette.
 * All pieces are decorative (aria-hidden by their parents) and static: no filters, no per-frame work.
 */

const callaPetal = 'M0 0C-13 3-15 24-3 36C2 40 9 37 11 29C14 15 10 2 0 0Z'
const callaFold = 'M0 0C-4 8-3 22 3 33C8 24 8 8 0 0Z'

function Bloom({ transform }: { transform: string }) {
  return <g transform={transform}>
    <path className="zarf-art__petal" d={callaPetal} />
    <path className="zarf-art__petal-shade" d={callaFold} />
    <ellipse className="zarf-art__spadix" cx="1" cy="9" rx="1.6" ry="6" />
  </g>
}

/** Three calla lilies with long stems and leaves, tucked at the envelope's lower left. */
export function CallaLilies({ className }: { className?: string }) {
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
    <Rose transform="translate(78 132) scale(0.62)" />
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

/** A layered rose (about 30 units across), centred on the origin; colours come from CSS variables. */
export function Rose({ transform }: { transform?: string }) {
  return <g transform={transform}>
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} className="zarf-art__rose-1" cx="0" cy="-8" rx="9" ry="10" transform={`rotate(${angle})`} />)}
    {[36, 108, 180, 252, 324].map(angle => <ellipse key={angle} className="zarf-art__rose-2" cx="0" cy="-5" rx="6.2" ry="7" transform={`rotate(${angle})`} />)}
    <circle className="zarf-art__rose-2" r="5" />
    <path className="zarf-art__rose-swirl" d="M0 -3.2C3.8 -3.4 4.6 1.6 0.8 2.6C-2.6 3.4-4.2-0.6-1.4-2.2" fill="none" strokeLinecap="round" />
  </g>
}

/** Roses with leaves, tucked at the envelope's lower right (viewBox 100x100). */
export function RoseCluster({ className }: { className?: string }) {
  return <svg className={className} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <g className="zarf-art__leaf">
      <path d="M52 100C34 86 26 68 30 48C46 60 54 80 52 100Z" />
      <path d="M60 100C76 90 90 78 98 60C80 62 64 78 60 100Z" />
      <path d="M44 96C30 94 14 86 6 72C22 70 38 80 44 96Z" />
    </g>
    <Rose transform="translate(42 76) scale(1.2)" />
    <Rose transform="translate(70 66) scale(0.95)" />
    <Rose transform="translate(58 46) scale(0.78)" />
  </svg>
}

const blossomPath = 'M0-1.6C-2-5-6-5-6-2C-6 0-4 1-2.4 1.2C-4 2.6-4.4 5.6-2 6.4C0 7 1.2 4 0 2.4C1.2 4 4 7 6 5.6C7.6 4 5.6 1.6 3 1.2C5.6 1 7.4-1 6-3C4-5.4 1-4.4 0-1.6Z'

/** A small five-lobed blossom as a single path plus a golden centre. */
function Blossom({ transform }: { transform: string }) {
  return <g transform={transform}>
    <path className="zarf-art__blossom" d={blossomPath} />
    <circle className="zarf-art__spadix" r="1.5" cy="0.6" />
  </g>
}

/** A lighter rose for the botanical frame: five petals, a heart and a swirl (8 elements). */
function RoseLite({ transform }: { transform: string }) {
  return <g transform={transform}>
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} className="zarf-art__rose-1" cx="0" cy="-8" rx="9" ry="10" transform={`rotate(${angle})`} />)}
    <circle className="zarf-art__rose-2" r="7" />
    <path className="zarf-art__rose-swirl" d="M0 -3.2C3.8 -3.4 4.6 1.6 0.8 2.6C-2.6 3.4-4.2-0.6-1.4-2.2" fill="none" strokeLinecap="round" />
  </g>
}

/**
 * One side of the botanical frame that grows in when the envelope opens (viewBox 200x360, anchored
 * at the left edge; the right side is the same drawing mirrored). Three stacked, separately animated
 * layers (stems and leaves, roses, blooms) so each grows as a composited transform/opacity animation.
 */
function GardenSide({ side }: { side: 'left' | 'right' }) {
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
      <RoseLite transform="translate(46 196) scale(1.25)" />
      <RoseLite transform="translate(92 88) scale(1)" />
      <RoseLite transform="translate(14 300) scale(1)" />
      <RoseLite transform="translate(112 296) scale(0.8)" />
    </>)}
    {layer('blooms', <>
      <Bloom transform="translate(62 238) rotate(-20) scale(0.95)" />
      <Blossom transform="translate(118 56) scale(1.3)" />
      <Blossom transform="translate(76 150) scale(1.1)" />
      <Blossom transform="translate(28 252) scale(1.1)" />
      <Blossom transform="translate(128 290) scale(1)" />
    </>)}
  </div>
}

/** Decorative botanical frame (left and right) behind the open envelope: aria-hidden, no pointer events. */
export function Garden({ leaving }: { leaving: boolean }) {
  return <div className={`zarf-garden${leaving ? ' zarf-garden--leaving' : ''}`} aria-hidden="true">
    <GardenSide side="left" />
    <GardenSide side="right" />
  </div>
}
