import { memo, useId } from 'react'

/**
 * Original palace-gate artwork for the "saray kapısı" door theme, drawn as plain static SVG (no
 * filters, no patterns; depth comes from a few gradient stops and overlapping lit/shadow shapes). The
 * scene is a stone portal (400 x 500 view box) around an arched opening (x 95..305, springline y 190,
 * apex y 85); each leaf is an arch-topped half of that opening (105 x 385).
 */

const OPEN_LEFT = 95
const OPEN_RIGHT = 305
const SPRING = 190
const BOTTOM = 470

const arch = (offset: number) => `M${OPEN_LEFT - offset} ${BOTTOM}V${SPRING}A${105 + offset} ${105 + offset} 0 0 1 ${OPEN_RIGHT + offset} ${SPRING}V${BOTTOM}`

const stop = (offset: number, color: string, opacity?: number) => <stop offset={offset} style={{ stopColor: color, stopOpacity: opacity }} />

function Lantern({ x }: { x: number }) {
  return <g className="dantel-gate__lantern" transform={`translate(${x} 232)`}>
    <path className="dantel-gate__iron" d="M-14 -8H0M-14 -8Q-14 4-6 4" fill="none" />
    <path className="dantel-gate__iron" d="M-6 -8V-2M-11 -2H-1L-3 18H-9Z" />
    <path className="dantel-gate__glass" d="M-10 0H-2L-4 16H-8Z" />
    <circle className="dantel-gate__glow" cx="-6" cy="8" r="13" />
    <path className="dantel-gate__flame" d="M-6 3Q-3 8-6 14Q-9 8-6 3Z" />
  </g>
}

function Pilaster({ x, side, shaft }: { x: number; side: 'left' | 'right'; shaft: string }) {
  return <g transform={`translate(${x} 0)`}>
    <rect x="6" y="170" width="36" height="282" style={{ fill: `url(#${shaft})` }} />
    <path className="dantel-gate__flute" d="M16 172V450M24 172V450M32 172V450" />
    <rect className="dantel-gate__lit" x="6" y="170" width="4" height="282" />
    <rect className="dantel-gate__shade" x="38" y="170" width="4" height="282" />
    <rect className="dantel-gate__stone-mid" x="-2" y="152" width="52" height="18" />
    <rect className="dantel-gate__lit" x="-2" y="152" width="52" height="3" />
    <rect className="dantel-gate__shade" x="-2" y="166" width="52" height="4" />
    <rect className="dantel-gate__stone-dark" x="-6" y="144" width="60" height="9" />
    <rect className="dantel-gate__stone-mid" x="-2" y="452" width="52" height="18" />
    <rect className="dantel-gate__lit" x="-2" y="452" width="52" height="2.5" />
    <rect className="dantel-gate__stone-dark" x="-6" y="466" width="60" height="6" />
    {/* spire finial on the capital */}
    <path className="dantel-gate__metal" d={`M24 144L${side === 'left' ? 18 : 30} 122L24 90L${side === 'left' ? 30 : 18} 122Z`} />
    <path className="dantel-gate__metal-lit" d={side === 'left' ? 'M24 144L18 122L24 90Z' : 'M24 144L30 122L24 90Z'} />
    <circle className="dantel-gate__metal" cx="24" cy="86" r="4.5" />
    <path className="dantel-gate__iron" d="M17 128H31" />
  </g>
}

/** The stone surround, steps, lanterns, finials and crest. The arched opening is a real hole. */
/** Static artwork: memoised so the open/reveal/expand state changes do not re-render hundreds of SVG nodes. */
export const RoyalGateScene = memo(function RoyalGateScene() {
  const id = useId().replace(/:/g, '')
  const wall = `${id}-wall`
  const shaft = `${id}-shaft`
  return <svg className="dantel-gate__scene" viewBox="0 0 400 500" preserveAspectRatio="xMidYMax meet" aria-hidden="true" focusable="false">
    <defs>
      <linearGradient id={wall} x1="0" y1="0" x2="1" y2="0">{stop(0, 'var(--gate-stone-light)')}{stop(0.45, 'var(--gate-stone)')}{stop(1, 'var(--gate-stone-mid)')}</linearGradient>
      <linearGradient id={shaft} x1="0" y1="0" x2="1" y2="0">{stop(0, 'var(--gate-stone-light)')}{stop(0.55, 'var(--gate-stone-light)')}{stop(1, 'var(--gate-stone)')}</linearGradient>
    </defs>
    {/* wall with the arched opening cut out */}
    <path fillRule="evenodd" style={{ fill: `url(#${wall})` }} d={`M8 46H392V${BOTTOM}H8ZM${OPEN_LEFT} ${BOTTOM}V${SPRING}A105 105 0 0 1 ${OPEN_RIGHT} ${SPRING}V${BOTTOM}Z`} />
    {/* cornice with dentils */}
    <rect className="dantel-gate__stone-mid" x="2" y="28" width="396" height="20" />
    <rect className="dantel-gate__lit" x="2" y="28" width="396" height="2.5" />
    <rect className="dantel-gate__stone-dark" x="-2" y="22" width="404" height="8" />
    <rect className="dantel-gate__shade" x="2" y="46" width="396" height="5" />
    <path className="dantel-gate__dentil" d="M6 40H394" />
    {/* stepped arch mouldings: each ring has a shadow and a lit edge */}
    {[10, 22, 34].map(offset => <g key={offset}>
      <path className="dantel-gate__moulding-shade" d={arch(offset)} transform="translate(1.6 1.6)" />
      <path className={`dantel-gate__moulding${offset === 34 ? ' dantel-gate__moulding--bold' : ''}`} d={arch(offset)} />
      <path className="dantel-gate__moulding-lit" d={arch(offset)} transform="translate(-1.2 -1.2)" />
    </g>)}
    <path className="dantel-gate__edge" d={arch(0)} />
    {/* inner reveal shadow just inside the opening */}
    <path className="dantel-gate__reveal" d={arch(-4)} />
    {/* keystone with a carved boss */}
    <path className="dantel-gate__stone-light" d="M181 46H219L214 87H186Z" />
    <path className="dantel-gate__shade" d="M210 46H219L214 87H209Z" />
    <path className="dantel-gate__edge" d="M181 46H219L214 87H186Z" />
    <circle className="dantel-gate__metal" cx="200" cy="66" r="6" />
    {/* crown crest above the cornice */}
    <path className="dantel-gate__metal" d="M178 22L182 4L192 14L200 0L208 14L218 4L222 22Z" />
    <path className="dantel-gate__metal-lit" d="M178 22L182 4L192 14L200 22Z" />
    <path className="dantel-gate__edge" d="M178 22H222" />
    <Pilaster x={22} side="left" shaft={shaft} />
    <Pilaster x={330} side="right" shaft={shaft} />
    <Lantern x={66} />
    <Lantern x={346} />
    {/* masonry joints */}
    <path className="dantel-gate__joint" d="M8 96H88M8 142H18M312 96H392M382 142H392M8 330H18M382 330H392M8 400H18M382 400H392" />
  </svg>
})

/** Steps and the soft ground shadow: a separate layer behind the paper, so the invitation rolls out over them. */
export const RoyalGateSteps = memo(function RoyalGateSteps() {
  return <svg className="dantel-gate__scene" viewBox="0 0 400 500" preserveAspectRatio="xMidYMax meet" aria-hidden="true" focusable="false">
    <ellipse className="dantel-gate__contact" cx="200" cy="499" rx="188" ry="6" />
    {/* stone steps with lit treads */}
    <rect className="dantel-gate__stone-mid" x="70" y="470" width="260" height="10" />
    <rect className="dantel-gate__lit" x="70" y="470" width="260" height="2" />
    <rect className="dantel-gate__stone-light" x="48" y="480" width="304" height="10" />
    <rect className="dantel-gate__lit" x="48" y="480" width="304" height="2" />
    <rect className="dantel-gate__stone-mid" x="26" y="490" width="348" height="10" />
    <path className="dantel-gate__edge" d="M70 480H330M48 490H352" />
  </svg>
})

const fanRays = [100, 112, 124, 136, 148, 160, 172].map(angle => {
  const radians = (angle * Math.PI) / 180
  return `M105 112L${(105 + 96 * Math.cos(radians)).toFixed(1)} ${(105 - 96 * Math.sin(radians)).toFixed(1)}`
}).join('')

const studs: Array<[number, number]> = []
for (const y of [180, 210]) for (const x of [26, 52, 78]) studs.push([x, y])
for (const y of [262, 290]) for (const x of [26, 52, 78]) studs.push([x, y])
for (const y of [318]) for (const x of [26, 52]) studs.push([x, y])

/** A bevelled panel: dark recess plus a lit top-left edge and a shadow bottom-right edge. */
function Panel({ y, height }: { y: number; height: number }) {
  const x = 12
  const width = 84
  return <g>
    <rect className="dantel-gate__panel" x={x} y={y} width={width} height={height} rx="3" />
    <path className="dantel-gate__bevel-lit" d={`M${x} ${y + height}V${y}H${x + width}`} />
    <path className="dantel-gate__bevel-shade" d={`M${x + width} ${y}V${y + height}H${x}`} />
  </g>
}

/** One arch-topped door leaf (the right leaf is mirrored by its wrapper). */
export const RoyalLeaf = memo(function RoyalLeaf({ side }: { side: 'left' | 'right' }) {
  const id = useId().replace(/:/g, '')
  const wood = `${id}-wood`
  return <svg className={`dantel-gate__leaf dantel-gate__leaf--${side}`} viewBox="0 0 105 385" preserveAspectRatio="none" aria-hidden="true" focusable="false">
    <defs>
      <linearGradient id={wood} x1="0" y1="0" x2="1" y2="0">{stop(0, 'var(--gate-wood-light)')}{stop(0.55, 'var(--gate-wood)')}{stop(1, 'var(--gate-wood-dark)')}</linearGradient>
    </defs>
    <g transform={side === 'right' ? 'translate(105 0) scale(-1 1)' : undefined}>
      <path className="dantel-gate__wood" style={{ fill: `url(#${wood})` }} d="M0 385V105A105 105 0 0 1 105 0V385Z" />
      <path className="dantel-gate__grain" d="M20 120V385M45 90V385M70 60V385M90 30V385" />
      {/* transom: iron fan / sunburst tracery in the arched head */}
      <path className="dantel-gate__iron-line" d={`${fanRays}M9 105A96 96 0 0 1 105 9M43 105A62 62 0 0 1 105 43M77 105A28 28 0 0 1 105 77M0 112H105`} />
      <circle className="dantel-gate__rosette" cx="105" cy="112" r="7" />
      <circle className="dantel-gate__rosette-core" cx="105" cy="112" r="3" />
      {/* studded panels with iron scrollwork; the rail between transom and panels is kept free for the plaque */}
      <Panel y={168} height={52} />
      <Panel y={246} height={100} />
      <path className="dantel-gate__iron-line" d="M20 194Q34 176 48 194T76 194M20 280Q34 262 48 280T76 280M20 304Q34 286 48 304T76 304" />
      {studs.map(([x, y]) => <g key={`${x}-${y}`}>
        <circle className="dantel-gate__stud" cx={x} cy={y} r="3.6" />
        <circle className="dantel-gate__stud-core" cx={x - 0.8} cy={y - 0.8} r="1.3" />
      </g>)}
      {/* heavy hinges */}
      <path className="dantel-gate__hinge" d="M0 142H44M0 232H44M0 352H44" />
      {[142, 232, 352].map(y => <circle key={y} className="dantel-gate__stud" cx="46" cy={y} r="5" />)}
      {/* lion-head ring knocker: a bold silhouette (mane, face, ears) on a large ring */}
      <g className="dantel-gate__knocker">
        <circle className="dantel-gate__ring" cx="88" cy="326" r="15" />
        <circle className="dantel-gate__ring-lit" cx="88" cy="326" r="15" />
        <g transform="translate(88 306)">
          <circle className="dantel-gate__mane" r="12" />
          {Array.from({ length: 10 }, (_, index) => <circle key={index} className="dantel-gate__mane" cx={11 * Math.cos((index * Math.PI) / 5)} cy={11 * Math.sin((index * Math.PI) / 5)} r="3.4" />)}
          <circle className="dantel-gate__lion" r="8.2" />
          <circle className="dantel-gate__lion" cx="-6.5" cy="-6.5" r="2.6" />
          <circle className="dantel-gate__lion" cx="6.5" cy="-6.5" r="2.6" />
          <circle className="dantel-gate__lion-feature" cx="-3" cy="-1.5" r="1.3" />
          <circle className="dantel-gate__lion-feature" cx="3" cy="-1.5" r="1.3" />
          <path className="dantel-gate__lion-feature" d="M-2 2H2L0 5Z" />
        </g>
      </g>
    </g>
  </svg>
})
