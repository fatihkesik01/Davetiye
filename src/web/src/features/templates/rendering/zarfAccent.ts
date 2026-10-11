import type { DraftContentInput } from '../../../api/generated/client'

export const zarfTemplateKey = 'zarf-davetiye'

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
}

export const zarfPalettes: Record<ZarfAccentKey, ZarfPalette> = {
  adacayi: { base: '#9db8a0', ink: '#27402f', seal: '#4e6e57' },
  'pudra-pembe': { base: '#e8b4bc', ink: '#5e2634', seal: '#a8506a' },
  krem: { base: '#e6d8bb', ink: '#4d3e1f', seal: '#8a6d2f' },
  bordo: { base: '#8c2f3f', ink: '#4a1420', seal: '#5c1522' },
  lacivert: { base: '#2f4a78', ink: '#1b2b4a', seal: '#1d2e4d' },
  lavanta: { base: '#b7a6d6', ink: '#35295a', seal: '#6b55a0' },
}

export function resolveZarfAccent(value: string | null | undefined): ZarfAccentKey {
  const key = value?.trim()
  return (zarfAccentKeys as readonly string[]).includes(key ?? '') ? key as ZarfAccentKey : defaultZarfAccent
}

/**
 * Draft content with the zarf `accentColor` field. Local extension until the generated client
 * (owned by the backend contract) exposes the field itself.
 */
export type DraftContentWithAccent = DraftContentInput & { accentColor?: string | null }
