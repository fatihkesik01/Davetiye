import { PressedFlower } from './ZarfFlowers'
import type { ZarfFloraKind } from './zarfTheme'

/**
 * Static vintage details for the inside of the flap (viewBox 300x110): an aged-paper lining with faint
 * tissue lines and an aged edge, a circular postmark with wavy cancellation lines, a tiny perforated
 * stamp and the pressed flower. Built from a handful of plain shapes: no patterns, no filters.
 */
export function FlapLining({ flapPath, idPrefix, flora, motif }: { flapPath: string; idPrefix: string; flora: ZarfFloraKind; motif: string }) {
  const clipId = `${idPrefix}-clip`
  const edgeId = `${idPrefix}-edge`
  return <>
    <defs>
      <clipPath id={clipId}><path d={flapPath} /></clipPath>
      <radialGradient id={edgeId} cx="0.5" cy="0.35" r="0.8">
        <stop offset="0.5" stopColor="#5a3c1a" stopOpacity="0" />
        <stop offset="1" stopColor="#5a3c1a" stopOpacity="0.38" />
      </radialGradient>
    </defs>
    <path className="zarf__lining-fill" d={flapPath} />
    <g clipPath={`url(#${clipId})`}>
      {[0, 1, 2, 3, 4, 5, 6, 7, 8].map(index => <path key={index} className="zarf__tissue" d={`M${-30 + index * 42} 130L${70 + index * 42} -20`} />)}
      <rect width="300" height="110" fill={`url(#${edgeId})`} />
      <ellipse className="zarf__stain" cx="262" cy="16" rx="34" ry="22" />
      <ellipse className="zarf__stain" cx="34" cy="70" rx="26" ry="16" />
    </g>
    <g className="zarf__postmark" transform="translate(228 42)">
      <circle r="16" fill="none" />
      <circle r="12" fill="none" strokeDasharray="2 2.4" />
      <path d="M-3 -4H4M-5 0H5M-3 4H4" />
      <path d="M-62 -8q6-5 12 0t12 0t12 0t12 0M-62 0q6-5 12 0t12 0t12 0t12 0M-62 8q6-5 12 0t12 0t12 0t12 0" fill="none" />
    </g>
    <g className="zarf__stamp" transform="translate(262 8)">
      <rect width="26" height="31" rx="1" />
      <rect className="zarf__stamp-inner" x="4" y="4" width="18" height="23" rx="1" />
      {motif === 'none' ? null : motif === 'love' ? <path className="zarf__stamp-mark" d="M13 20l-1.2-1.1C7.5 15 5 12.8 5 10c0-2.2 1.7-4 3.9-4 1.3 0 2.4.6 3.1 1.5C12.7 6.6 13.8 6 15.1 6 17.3 6 19 7.8 19 10c0 2.8-2.5 5-6.8 8.9z" />
        : motif === 'space' ? <path className="zarf__stamp-mark" d="M13 5l2 5 5 2-5 2-2 5-2-5-5-2 5-2z" />
            : motif === 'sea' ? <path className="zarf__stamp-mark" d="M4 10q4-4 8 0t8 0M4 15q4-4 8 0t8 0" />
              : motif === 'aurora' ? <path className="zarf__stamp-mark" d="M4 19C8 13 7 7 12 4c2 7-1 12-3 16M12 20c4-6 3-11 8-14 1 7-2 12-4 15" />
                : motif === 'music' ? <><path className="zarf__stamp-mark" d="M10 18V7l9-2v11" /><ellipse className="zarf__stamp-mark" cx="8" cy="19" rx="3" ry="2" /><ellipse className="zarf__stamp-mark" cx="17" cy="17" rx="3" ry="2" /></>
            : <><circle className="zarf__stamp-mark" cx="13" cy="13" r="4.5" /><path className="zarf__stamp-mark" d="M7 23h12" /></>}
    </g>
    {flora === 'none' ? null : <PressedFlower flora={flora} />}
  </>
}
