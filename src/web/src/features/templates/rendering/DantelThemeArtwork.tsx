import { memo } from 'react'

import type { DantelThemeKey } from './dantelThemes'

function CherryBlossom({ x, y, scale = 1 }: { x: number; y: number; scale?: number }) {
  return <g transform={`translate(${x} ${y}) scale(${scale})`} className="dantel-theme-art__blossom">
    {[0, 72, 144, 216, 288].map(angle => <ellipse key={angle} cx="0" cy="-6" rx="4" ry="7" transform={`rotate(${angle})`} />)}
    <circle r="3" />
  </g>
}

function Star({ x, y, size = 7 }: { x: number; y: number; size?: number }) {
  return <path className="dantel-theme-art__star" d={`M${x} ${y - size}l${size * 0.28} ${size * 0.7} ${size * 0.72} ${size * 0.3} -${size * 0.72} ${size * 0.3} -${size * 0.28} ${size * 0.7} -${size * 0.28} -${size * 0.7} -${size * 0.72} -${size * 0.3} ${size * 0.72} -${size * 0.3}z`} />
}

/** Theme-specific scenery behind the door leaves. Each option has its own silhouette and material language. */
export const DantelThemeScene = memo(function DantelThemeScene({ theme }: { theme: DantelThemeKey }) {
  return <svg className={`dantel-theme-scene dantel-theme-scene--${theme}`} viewBox="0 0 400 500" preserveAspectRatio="none" aria-hidden="true" focusable="false">
    {theme === 'japon-bahcesi' ? <>
      <circle className="dantel-theme-art__moon" cx="200" cy="112" r="32" />
      <path className="dantel-theme-art__frame" d="M30 112Q38 76 70 76H330Q362 76 370 112M43 113V466M357 113V466" />
      <path className="dantel-theme-art__frame-shadow" d="M20 83Q200 55 380 83L374 98Q200 78 26 98Z" />
      <path className="dantel-theme-art__frame" d="M14 76Q200 35 386 76L378 92Q200 56 22 92Z" />
      <path className="dantel-theme-art__detail" d="M28 460H78M322 460H372M31 126H72M328 126H369" />
      <path className="dantel-theme-art__branch" d="M44 214Q70 193 67 163m288 51q-26-21-23-51" />
      <CherryBlossom x={64} y={173} /><CherryBlossom x={337} y={173} />
      <CherryBlossom x={45} y={282} scale={0.7} /><CherryBlossom x={355} y={282} scale={0.7} />
      <path className="dantel-theme-art__lantern" d="M47 219h28l-3 35H50zM42 216h38v6H42zm7 38h28m-14 0v12" />
      <path className="dantel-theme-art__lantern" d="M325 219h28l-3 35h-22zM320 216h38v6h-38zm7 38h28m-14 0v12" />
    </> : null}
    {theme === 'akdeniz-avlusu' ? <>
      <path className="dantel-theme-art__frame" d="M49 472V194A151 151 0 0 1 351 194V472M66 472V194A134 134 0 0 1 334 194V472" />
      <path className="dantel-theme-art__tile" d="M32 156h28v28H32zm310 0h28v28h-28zM38 352h23v23H38zm301 0h23v23h-23zM48 420h18v18H48zm286 0h18v18h-18z" />
      <path className="dantel-theme-art__detail" d="M29 472h44m254 0h44M37 145h34m258 0h34" />
      <path className="dantel-theme-art__plant" d="M48 430q-17-20-7-38 18 7 15 27 17-24 31-17-2 22-28 27m293 1q17-20 7-38-18 7-15 27-17-24-31-17 2 22 28 27" />
      <path className="dantel-theme-art__plant-stem" d="M53 461v-36m294 36v-36" />
    </> : null}
    {theme === 'buz-sarayi' ? <>
      <path className="dantel-theme-art__frame" d="M53 472V195L72 181 67 158 91 146 86 118 112 107 111 80 143 87 160 57 181 77 200 43 219 77 240 57 257 87 289 80 288 107 314 118 309 146 333 158 328 181 347 195V472" />
      <path className="dantel-theme-art__facet" d="M69 194l31 15-29 45zm262 0l-31 15 29 45zM200 62l-25 47 25-11 25 11zM99 300l36 42-43 18zm202 0l-36 42 43 18z" />
      <path className="dantel-theme-art__detail" d="M53 472h48m198 0h48M77 173l24 13m222-13-24 13" />
      {[120, 280].map(x => <g key={x} className="dantel-theme-art__snowflake" transform={`translate(${x} 255)`}><path d="M0-15v30m-13-23 26 16m0-16-26 16" /></g>)}
    </> : null}
    {theme === 'gul-serasi' ? <>
      <path className="dantel-theme-art__frame" d="M42 472V205A158 158 0 0 1 358 205V472M58 472V205A142 142 0 0 1 342 205V472" />
      <path className="dantel-theme-art__glass" d="M72 463V207A128 128 0 0 1 328 207V463M200 77V464M75 275h250M59 365h282" />
      <path className="dantel-theme-art__vine" d="M49 466q38-30 6-64t8-70q33-32 2-71t12-70m277 345q-38-30-6-64t-8-70q-33-32-2-71t-12-70" />
      {([[62, 262], [55, 394], [337, 262], [345, 394]] as const).map(([x, y], i) => <g key={i} className="dantel-theme-art__rose" transform={`translate(${x} ${y})`}>
        <circle r="12" /><circle r="7" /><circle r="2.5" />
      </g>)}
      <path className="dantel-theme-art__leaf" d="M56 325q-22-29-34-14 7 23 34 14m284 0q22-29 34-14-7 23-34 14M54 435q-22-29-34-14 7 23 34 14m292 0q22-29 34-14-7 23-34 14" />
    </> : null}
    {theme === 'venedik-karnavali' ? <>
      <path className="dantel-theme-art__frame-shadow" d="M45 472V202A155 155 0 0 1 355 202V472" />
      <path className="dantel-theme-art__frame" d="M54 472V198A146 146 0 0 1 346 198V472M72 472V198A128 128 0 0 1 328 198V472" />
      <path className="dantel-theme-art__curtain" d="M54 194q38 14 55 78l-5 126q-24-54-50-66zm292 0q-38 14-55 78l5 126q24-54 50-66z" />
      <path className="dantel-theme-art__mask" d="M173 146q27-20 54 0l-4 26q-7 15-23 3-16 12-23-3z" />
      <path className="dantel-theme-art__mask-detail" d="M178 153q10-8 19 0l-7 5zm25 0q10-8 19 0l-7 5zm-3 15 3 3 3-3" />
      <path className="dantel-theme-art__detail" d="M46 472h48m212 0h48M68 183h30m204 0h30" />
      <Star x={200} y={120} size={9} />
    </> : null}
    {theme === 'yildiz-gecidi' ? <>
      <path className="dantel-theme-art__frame" d="M48 472V194A152 152 0 0 1 352 194V472M65 472V194A135 135 0 0 1 335 194V472" />
      <path className="dantel-theme-art__orbit" d="M75 230Q200 76 325 230M78 319Q200 418 322 319" />
      <path className="dantel-theme-art__constellation" d="m83 155 31-24 30 13 30-36m82 50 31-24 30 13 16-32M86 375l28-19 22 15 24-29m74 36 27-22 31 15 24-23" />
      {([[83, 155], [114, 131], [144, 144], [174, 108], [257, 158], [288, 134], [318, 147], [334, 115], [86, 375], [114, 356], [136, 371], [160, 342], [234, 378], [261, 356], [292, 371], [316, 348]] as const).map(([x, y], i) => <Star key={i} x={x} y={y} size={i % 3 === 0 ? 6 : 4} />)}
      <path className="dantel-theme-art__crescent" d="M200 76a22 22 0 1 0 18 34 19 19 0 1 1-18-34z" />
    </> : null}
  </svg>
})

/** A quieter companion illustration on the open sheet, framing the content without covering it. */
export const DantelThemeInterior = memo(function DantelThemeInterior({ theme }: { theme: DantelThemeKey }) {
  return <svg className={`dantel-theme-interior dantel-theme-interior--${theme}`} viewBox="0 0 800 1200" preserveAspectRatio="none" aria-hidden="true" focusable="false">
    {theme === 'japon-bahcesi' ? <><path className="dantel-theme-art__branch" d="M55 270q120-75 168-194m-150 63q-36-66-8-100 53 22 44 73m-19 54q-66-24-78 12 41 47 100 24m633 138q-120-75-168-194m150 63q36-66 8-100-53 22-44 73"/><CherryBlossom x={108} y={176} scale={1.8}/><CherryBlossom x={176} y={117} scale={1.3}/><CherryBlossom x={691} y={176} scale={1.8}/></> : null}
    {theme === 'akdeniz-avlusu' ? <><path className="dantel-theme-art__tile" d="M76 91h48v48H76zm0 18 24-18 24 24-24 24-24-24zm600-18h48v48h-48zm0 18 24-18 24 24-24 24-24-24zM76 1058h48v48H76zm600 0h48v48h-48z"/><path className="dantel-theme-art__plant" d="M64 1030q-35-51-10-75 45 19 32 57 42-40 66-15-15 45-71 47m655-14q35-51 10-75-45 19-32 57-42-40-66-15 15 45 71 47"/></> : null}
    {theme === 'buz-sarayi' ? <><path className="dantel-theme-art__facet" d="M64 170l55-92 51 92-51 30zm672 0-55-92-51 92 51 30zM64 1010l55-92 51 92-51 30zm672 0-55-92-51 92 51 30z"/><g className="dantel-theme-art__snowflake" transform="translate(90 300) scale(2)"><path d="M0-18v36m-16-28 32 20m0-20-32 20"/></g><g className="dantel-theme-art__snowflake" transform="translate(710 900) scale(2)"><path d="M0-18v36m-16-28 32 20m0-20-32 20"/></g></> : null}
    {theme === 'gul-serasi' ? <><path className="dantel-theme-art__vine" d="M48 1100q90-120 34-210t30-175q88-80 20-180t40-190m580 755q-90-120-34-210t-30-175q-88-80-20-180t-40-190"/><path className="dantel-theme-art__leaf" d="M82 850q-55-60-73-34 17 63 74 56m625-22q55-60 73-34-17 63-74 56M122 620q-55-60-73-34 17 63 74 56m545-22q55-60 73-34-17 63-74 56"/><g className="dantel-theme-art__rose" transform="translate(84 720)"><circle r="24"/><circle r="14"/><circle r="5"/></g><g className="dantel-theme-art__rose" transform="translate(716 720)"><circle r="24"/><circle r="14"/><circle r="5"/></g></> : null}
    {theme === 'venedik-karnavali' ? <><path className="dantel-theme-art__curtain" d="M54 65q105 42 120 184l-15 178Q120 302 54 276zm692 0Q641 107 626 249l15 178q39-125 105-151z"/><path className="dantel-theme-art__mask" d="M68 480q64-52 128 0l-12 57q-23 28-52 6-29 22-52-6zM604 480q64-52 128 0l-12 57q-23 28-52 6-29 22-52-6z"/><path className="dantel-theme-art__mask-detail" d="m91 492 26 10-20 10m72-20-26 10 20 10m441-20 26 10-20 10m72-20-26 10 20 10"/></> : null}
    {theme === 'yildiz-gecidi' ? <><path className="dantel-theme-art__constellation" d="m72 300 72-53 72 36 78-91m434 108-72-53-72 36-78-91M72 910l72-53 72 36 78-91m434 108-72-53-72 36-78-91"/><Star x={72} y={300} size={12}/><Star x={144} y={247} size={10}/><Star x={216} y={283} size={11}/><Star x={294} y={192} size={12}/><Star x={728} y={300} size={12}/><Star x={656} y={247} size={10}/><Star x={584} y={283} size={11}/><Star x={506} y={192} size={12}/><path className="dantel-theme-art__crescent" d="M400 111a34 34 0 1 0 28 52 29 29 0 1 1-28-52z"/></> : null}
  </svg>
})

/** Original ornament etched into each themed leaf; the SVG flips with the hinged right door. */
export const DantelThemeLeaf = memo(function DantelThemeLeaf({ theme, side }: { theme: DantelThemeKey; side: 'left' | 'right' }) {
  return <svg className={`dantel-theme-leaf dantel-theme-leaf--${theme}`} viewBox="0 0 121 385" preserveAspectRatio="none" aria-hidden="true" focusable="false">
    <g transform={side === 'right' ? 'translate(121 0) scale(-1 1)' : undefined}>
      <path className="dantel-theme-leaf__edge" d="M0 385V105A121 105 0 0 1 121 0V385Z" />
      {theme === 'japon-bahcesi' ? <>
        <path className="dantel-theme-leaf__line" d="M15 127h91M18 173h86M22 219h80M25 265h74M28 311h68M31 357h62M23 114v252m24-258v264m26-271v278m25-285v292" />
        <CherryBlossom x={74} y={160} scale={0.8} /><CherryBlossom x={44} y={260} scale={0.7} /><CherryBlossom x={83} y={337} scale={0.68} />
      </> : null}
      {theme === 'akdeniz-avlusu' ? <>
        <path className="dantel-theme-leaf__inset" d="M13 370V119A96 96 0 0 1 109 23V370ZM27 356V131A82 82 0 0 1 109 49" />
        <path className="dantel-theme-leaf__tile" d="M27 180h17v17H27zm50-51h17v17H77zm0 88h17v17H77zm-50 50h17v17H27zm50 55h17v17H77z" />
        <path className="dantel-theme-leaf__line" d="M59 140v191M37 230h51M36 291h52" />
      </> : null}
      {theme === 'buz-sarayi' ? <>
        <path className="dantel-theme-leaf__inset" d="M14 371V111L28 98 24 73 48 83 61 48 76 82 100 72 96 98 109 111V371Z" />
        <path className="dantel-theme-leaf__facet" d="m14 111 48 50 47-50-47-57zm0 111 48-61 47 61-47 68zm0 76 48-69 47 69-47 73z" />
        <g className="dantel-theme-leaf__snowflake" transform="translate(62 204)"><path d="M0-20v40m-18-31 36 22m0-22-36 22" /></g>
        <path className="dantel-theme-leaf__line" d="M20 368h84M31 124h62" />
      </> : null}
      {theme === 'gul-serasi' ? <>
        <path className="dantel-theme-leaf__line" d="M15 32v335m29-350v365m29-370v370m29-355v340M14 126h94M14 220h94M14 314h94" />
        <path className="dantel-theme-leaf__vine" d="M18 364q28-34 5-64t14-58q25-28 4-58t13-60q23-28 9-56" />
        <g className="dantel-theme-leaf__rose" transform="translate(33 253)"><circle r="13"/><circle r="8"/><circle r="3"/></g>
        <g className="dantel-theme-leaf__rose" transform="translate(42 128)"><circle r="11"/><circle r="7"/><circle r="2.5"/></g>
        <path className="dantel-theme-leaf__bud" d="M21 183q-21-18-23-5 5 19 23 15m15 97q20-18 23-5-5 19-23 15" />
      </> : null}
      {theme === 'venedik-karnavali' ? <>
        <path className="dantel-theme-leaf__inset" d="M13 370V118A96 96 0 0 1 109 22V370M24 357V126A84 84 0 0 1 108 42" />
        <path className="dantel-theme-leaf__scroll" d="M22 154q25-31 44 0t37 0M22 282q25-31 44 0t37 0M24 342q24-27 43 0t36 0" />
        <path className="dantel-theme-leaf__mask" d="M39 190q23-19 46 0l-5 22q-8 13-18 2-10 11-18-2z" />
        <path className="dantel-theme-leaf__mask-detail" d="M44 197q8-7 16 0l-6 4zm20 0q8-7 16 0l-6 4zm-4 12 3 3 3-3" />
        <path className="dantel-theme-leaf__line" d="M20 110h87M20 364h87" />
      </> : null}
      {theme === 'yildiz-gecidi' ? <>
        <path className="dantel-theme-leaf__orbit" d="M13 81q49-63 96 0M17 343q45 46 91 0" />
        <path className="dantel-theme-leaf__constellation" d="m28 142 21-17 20 14 23-30m-62 144 20-18 20 15 22-28m-62 105 20-17 19 15 21-25" />
        {([[28, 142], [49, 125], [69, 139], [92, 109], [30, 253], [50, 235], [70, 250], [92, 222], [29, 340], [49, 323], [68, 338], [89, 313]] as const).map(([x, y], i) => <Star key={i} x={x} y={y} size={i % 3 === 0 ? 6 : 4} />)}
        <path className="dantel-theme-leaf__crescent" d="M61 185a15 15 0 1 0 13 24 13 13 0 1 1-13-24z" />
      </> : null}
    </g>
  </svg>
})
