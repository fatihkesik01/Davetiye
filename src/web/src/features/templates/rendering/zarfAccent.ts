export const zarfTemplateKey = 'zarf-davetiye'

/**
 * Bundled stand-in photo (static asset under public/, no auth, no API) shown in the demo, the Creator
 * preview and /onizleme when no real Cover image exists. Swapping the file or its extension is a
 * one-line change here.
 */
export const zarfDemoPhotoUrl = '/template-demo/zarf-davetiye-photo.jpg'

/** Curated accent keys. Content only ever carries one of these keys, never a raw colour value. */
export const zarfAccentKeys = ['adacayi', 'pudra-pembe', 'krem', 'bordo', 'lacivert', 'lavanta'] as const
export type ZarfAccentKey = (typeof zarfAccentKeys)[number]
export const defaultZarfAccent: ZarfAccentKey = 'adacayi'

interface ZarfPalette {
  /** Envelope fill; the flap, pocket, page tint and card tint are all derived from it with color-mix. */
  base: string
  /** Dark text colour on the light page tint (AA, at least 7:1 on every tint). */
  ink: string
  /** Wax-seal colour (white monogram on top, decorative). */
  seal: string
  /** Foliage colours for the botanicals: muted greens tuned per palette so they read on the envelope. */
  leaf: string
  stem: string
}

export const zarfPalettes: Record<ZarfAccentKey, ZarfPalette> = {
  adacayi: { base: '#9db8a0', ink: '#27402f', seal: '#4e6e57', leaf: '#4d7658', stem: '#3e6049' },
  'pudra-pembe': { base: '#e8b4bc', ink: '#5e2634', seal: '#a8506a', leaf: '#6d8f6c', stem: '#56764f' },
  krem: { base: '#e6d8bb', ink: '#4d3e1f', seal: '#8a6d2f', leaf: '#7a8a52', stem: '#5f6e3e' },
  bordo: { base: '#8c2f3f', ink: '#4a1420', seal: '#5c1522', leaf: '#8fae88', stem: '#a8c2a0' },
  lacivert: { base: '#2f4a78', ink: '#1b2b4a', seal: '#1d2e4d', leaf: '#86a9a0', stem: '#a3c1b8' },
  lavanta: { base: '#b7a6d6', ink: '#35295a', seal: '#6b55a0', leaf: '#5f7f66', stem: '#496b52' },
}

export function resolveZarfAccent(value: string | null | undefined): ZarfAccentKey {
  const key = value?.trim()
  return (zarfAccentKeys as readonly string[]).includes(key ?? '') ? key as ZarfAccentKey : defaultZarfAccent
}
