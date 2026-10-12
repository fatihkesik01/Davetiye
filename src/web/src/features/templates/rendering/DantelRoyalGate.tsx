/**
 * Original palace-gate artwork for the "saray kapısı" door theme, drawn as plain static SVG (no
 * filters, no patterns). The scene is a stone portal (400 x 500 view box) around an arched opening
 * (x 95..305, springline y 190, apex y 85); each leaf is an arch-topped half of that opening (105 x 385).
 */

const OPEN_LEFT = 95
const OPEN_RIGHT = 305
const SPRING = 190
const BOTTOM = 470

const arch = (offset: number) => `M${OPEN_LEFT - offset} ${BOTTOM}V${SPRING}A${105 + offset} ${105 + offset} 0 0 1 ${OPEN_RIGHT + offset} ${SPRING}V${BOTTOM}`

function Lantern({ x }: { x: number }) {
  return <g className="dantel-gate__lantern" transform={`translate(${x} 232)`}>
    <path className="dantel-gate__iron" d="M-14 -8H0M-14 -8Q-14 4-6 4" fill="none" />
    <path className="dantel-gate__iron" d="M-6 -8V-2M-11 -2H-1L-3 18H-9Z" />
    <path className="dantel-gate__glass" d="M-10 0H-2L-4 16H-8Z" />
    <circle className="dantel-gate__glow" cx="-6" cy="8" r="13" />
    <path className="dantel-gate__flame" d="M-6 3Q-3 8-6 14Q-9 8-6 3Z" />
  </g>
}

function Pilaster({ x, side }: { x: number; side: 'left' | 'right' }) {
  return <g transform={`translate(${x} 0)`}>
    <rect className="dantel-gate__stone-light" x="6" y="170" width="36" height="282" />
    <path className="dantel-gate__flute" d="M16 172V450M24 172V450M32 172V450" />
    <rect className="dantel-gate__stone-mid" x="-2" y="152" width="52" height="18" />
    <rect className="dantel-gate__stone-dark" x="-6" y="144" width="60" height="9" />
    <rect className="dantel-gate__stone-mid" x="-2" y="452" width="52" height="18" />
    <rect className="dantel-gate__stone-dark" x="-6" y="466" width="60" height="6" />
    {/* spire finial on the capital */}
    <path className="dantel-gate__metal" d={`M24 144L${side === 'left' ? 18 : 30} 122L24 90L${side === 'left' ? 30 : 18} 122Z`} />
    <circle className="dantel-gate__metal" cx="24" cy="86" r="4.5" />
    <path className="dantel-gate__iron" d="M17 128H31" />
  </g>
}

/** The stone surround, steps, lanterns, finials and crest. The arched opening is a real hole. */
export function RoyalGateScene() {
  return <svg className="dantel-gate__scene" viewBox="0 0 400 500" preserveAspectRatio="xMidYMax meet" aria-hidden="true" focusable="false">
    {/* wall with the arched opening cut out */}
    <path className="dantel-gate__stone" fillRule="evenodd" d={`M8 46H392V${BOTTOM}H8ZM${OPEN_LEFT} ${BOTTOM}V${SPRING}A105 105 0 0 1 ${OPEN_RIGHT} ${SPRING}V${BOTTOM}Z`} />
    {/* cornice with dentils */}
    <rect className="dantel-gate__stone-mid" x="2" y="28" width="396" height="20" />
    <rect className="dantel-gate__stone-dark" x="-2" y="22" width="404" height="8" />
    <path className="dantel-gate__dentil" d="M6 40H394" />
    {/* stepped arch mouldings */}
    <path className="dantel-gate__moulding" d={arch(10)} />
    <path className="dantel-gate__moulding" d={arch(22)} />
    <path className="dantel-gate__moulding dantel-gate__moulding--bold" d={arch(34)} />
    <path className="dantel-gate__edge" d={arch(0)} />
    {/* keystone with a carved boss */}
    <path className="dantel-gate__stone-light" d="M181 46H219L214 87H186Z" />
    <path className="dantel-gate__edge" d="M181 46H219L214 87H186Z" />
    <circle className="dantel-gate__metal" cx="200" cy="66" r="6" />
    {/* crown crest above the cornice */}
    <path className="dantel-gate__metal" d="M178 22L182 4L192 14L200 0L208 14L218 4L222 22Z" />
    <path className="dantel-gate__edge" d="M178 22H222" />
    <Pilaster x={22} side="left" />
    <Pilaster x={330} side="right" />
    <Lantern x={66} />
    <Lantern x={346} />
    {/* stone steps */}
    <rect className="dantel-gate__stone-mid" x="70" y="470" width="260" height="10" />
    <rect className="dantel-gate__stone-light" x="48" y="480" width="304" height="10" />
    <rect className="dantel-gate__stone-mid" x="26" y="490" width="348" height="10" />
    <path className="dantel-gate__edge" d="M70 480H330M48 490H352" />
    {/* masonry joints */}
    <path className="dantel-gate__joint" d="M8 96H88M8 142H18M312 96H392M382 142H392M8 330H18M382 330H392M8 400H18M382 400H392" />
  </svg>
}

const fanRays = [100, 112, 124, 136, 148, 160, 172].map(angle => {
  const radians = (angle * Math.PI) / 180
  return `M105 112L${(105 + 96 * Math.cos(radians)).toFixed(1)} ${(105 - 96 * Math.sin(radians)).toFixed(1)}`
}).join('')

const studs: Array<[number, number]> = []
for (const y of [158, 186]) for (const x of [26, 52, 78]) studs.push([x, y])
for (const y of [262, 290, 318]) for (const x of [26, 52]) studs.push([x, y])

/** One arch-topped door leaf (the right leaf is mirrored by its wrapper). */
export function RoyalLeaf({ side }: { side: 'left' | 'right' }) {
  return <svg className={`dantel-gate__leaf dantel-gate__leaf--${side}`} viewBox="0 0 105 385" preserveAspectRatio="none" aria-hidden="true" focusable="false">
    <g transform={side === 'right' ? 'translate(105 0) scale(-1 1)' : undefined}>
      <path className="dantel-gate__wood" d="M0 385V105A105 105 0 0 1 105 0V385Z" />
      <path className="dantel-gate__grain" d="M20 120V385M45 90V385M70 60V385M90 30V385" />
      {/* transom: iron fan / sunburst tracery in the arched head */}
      <path className="dantel-gate__iron-line" d={`${fanRays}M9 105A96 96 0 0 1 105 9M43 105A62 62 0 0 1 105 43M77 105A28 28 0 0 1 105 77M0 112H105`} />
      <circle className="dantel-gate__rosette" cx="105" cy="112" r="7" />
      <circle className="dantel-gate__rosette-core" cx="105" cy="112" r="3" />
      {/* studded panels with iron scrollwork */}
      <rect className="dantel-gate__panel" x="12" y="140" width="84" height="64" rx="3" />
      <rect className="dantel-gate__panel" x="12" y="236" width="84" height="104" rx="3" />
      <path className="dantel-gate__iron-line" d="M20 172Q34 150 48 172T76 172M20 188Q34 166 48 188T76 188M20 300Q34 270 48 300T76 300M20 322Q34 292 48 322T76 322" />
      {studs.map(([x, y]) => <g key={`${x}-${y}`}>
        <circle className="dantel-gate__stud" cx={x} cy={y} r="3.6" />
        <circle className="dantel-gate__stud-core" cx={x} cy={y} r="1.3" />
      </g>)}
      {/* heavy hinges */}
      <path className="dantel-gate__hinge" d="M0 150H44M0 262H44M0 352H44" />
      {[150, 262, 352].map(y => <circle key={y} className="dantel-gate__stud" cx="46" cy={y} r="5" />)}
      {/* lion-head ring knocker */}
      <circle className="dantel-gate__ring" cx="88" cy="318" r="13" />
      <g transform="translate(88 302)">
        {Array.from({ length: 8 }, (_, index) => <circle key={index} className="dantel-gate__mane" cx={8 * Math.cos((index * Math.PI) / 4)} cy={8 * Math.sin((index * Math.PI) / 4)} r="3.2" />)}
        <circle className="dantel-gate__lion" r="7" />
        <path className="dantel-gate__lion-face" d="M-3 -1H-1M1 -1H3M-1.5 2.5Q0 4 1.5 2.5" />
      </g>
    </g>
  </svg>
}
