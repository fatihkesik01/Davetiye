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
