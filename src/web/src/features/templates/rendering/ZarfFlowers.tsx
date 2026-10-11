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

/** A small leaf-and-blossom sprig for the card edges (local 30x56 box, origin top-left). */
export function Sprig({ transform }: { transform: string }) {
  return <g transform={transform}>
    <path className="zarf-art__sprig-stem" d="M15 56C14 40 16 22 15 8" fill="none" strokeLinecap="round" />
    <path className="zarf-art__sprig-leaf" d="M15 44C4 42-1 34-2 26C9 28 14 34 15 44Z" />
    <path className="zarf-art__sprig-leaf" d="M15 34C25 32 30 25 31 17C21 19 16 25 15 34Z" />
    <path className="zarf-art__sprig-leaf" d="M15 22C7 20 4 14 4 8C11 10 14 15 15 22Z" />
    <circle className="zarf-art__blossom" cx="15" cy="6" r="3.6" />
    <circle className="zarf-art__blossom" cx="9" cy="11" r="2.4" />
    <circle className="zarf-art__blossom" cx="21" cy="12" r="2.4" />
  </g>
}
