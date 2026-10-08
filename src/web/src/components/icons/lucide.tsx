/*
 * Icons below are from Lucide (https://lucide.dev), drawn on a 24x24 grid with a 2px round stroke.
 *
 * `UserRoundCogIcon` (user-round-cog): ISC License
 *   Copyright (c) 2026 Lucide Icons and Contributors
 *   Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is
 *   hereby granted, provided that the above copyright notice and this permission notice appear in all copies.
 *
 * `LogOutIcon` (log-out) derives from Feather: MIT License
 *   Copyright (c) 2013-present Cole Bemis
 *   The above copyright notice and this permission notice shall be included in all copies or substantial
 *   portions of the Software.
 */
const svgProps = {
  viewBox: '0 0 24 24', width: 20, height: 20, fill: 'none', stroke: 'currentColor', strokeWidth: 2,
  strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, 'aria-hidden': true, focusable: false,
}

export function UserRoundCogIcon() {
  return <svg {...svgProps}>
    <path d="m14.305 19.53.923-.382" /><path d="m15.228 16.852-.923-.383" /><path d="m16.852 15.228-.383-.923" />
    <path d="m16.852 20.772-.383.924" /><path d="m19.148 15.228.383-.923" /><path d="m19.53 21.696-.382-.924" />
    <path d="M2 21a8 8 0 0 1 10.434-7.62" /><path d="m20.772 16.852.924-.383" /><path d="m20.772 19.148.924.383" />
    <circle cx="10" cy="8" r="5" /><circle cx="18" cy="18" r="3" />
  </svg>
}

export function LogOutIcon() {
  return <svg {...svgProps}>
    <path d="m16 17 5-5-5-5" /><path d="M21 12H9" /><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
  </svg>
}
