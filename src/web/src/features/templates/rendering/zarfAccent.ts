export const zarfTemplateKey = 'zarf-davetiye'

/**
 * Bundled stand-in photo (static asset under public/, no auth, no API) shown in the demo, the Creator
 * preview and /onizleme when no real Cover image exists. Swapping the file or its extension is a
 * one-line change here.
 */
export const zarfDemoPhotoUrl = '/template-demo/zarf-davetiye-photo.jpg'

/** Curated accent keys. Content only ever carries one of these keys, never a raw colour value. */
export const zarfAccentKeys = ['adacayi', 'pudra-pembe', 'krem', 'bordo', 'lacivert', 'lavanta', 'seftali', 'zumrut'] as const
export type ZarfAccentKey = (typeof zarfAccentKeys)[number]
export const defaultZarfAccent: ZarfAccentKey = 'adacayi'

interface ZarfPalette {
  /** Envelope fill; the flap, pocket and page tint are derived from it with color-mix. */
  base: string
  /** Dark text colour on the light page tint (AA, at least 7:1 on every tint). */
  ink: string
  /** Wax-seal colour (white monogram on top, decorative). */
  seal: string
  /** Foliage colours for the botanicals: muted greens tuned per palette so they read on the envelope. */
  leaf: string
  stem: string
  /** Petal colour of the roses: ivory/blush on light palettes, a deep rose on bordo. */
  rose: string
}

export const zarfPalettes: Record<ZarfAccentKey, ZarfPalette> = {
  adacayi: { base: '#9db8a0', ink: '#27402f', seal: '#4e6e57', leaf: '#4d7658', stem: '#3e6049', rose: '#efc9c4' },
  'pudra-pembe': { base: '#e8b4bc', ink: '#5e2634', seal: '#a8506a', leaf: '#6d8f6c', stem: '#56764f', rose: '#f4dcd9' },
  krem: { base: '#e6d8bb', ink: '#4d3e1f', seal: '#8a6d2f', leaf: '#7a8a52', stem: '#5f6e3e', rose: '#f1dfca' },
  bordo: { base: '#8c2f3f', ink: '#4a1420', seal: '#5c1522', leaf: '#8fae88', stem: '#a8c2a0', rose: '#a8364f' },
  lacivert: { base: '#2f4a78', ink: '#1b2b4a', seal: '#1d2e4d', leaf: '#86a9a0', stem: '#a3c1b8', rose: '#ecd0d6' },
  seftali: { base: '#f2b896', ink: '#5a2c14', seal: '#c96a3d', leaf: '#6f8f5c', stem: '#58774a', rose: '#fbe4d6' },
  zumrut: { base: '#2f7a5f', ink: '#10382b', seal: '#14503b', leaf: '#9fd0b3', stem: '#b9ddc7', rose: '#f3dbe0' },
  lavanta: { base: '#b7a6d6', ink: '#35295a', seal: '#6b55a0', leaf: '#5f7f66', stem: '#496b52', rose: '#f1dff0' },
}

export function resolveZarfAccent(value: string | null | undefined): ZarfAccentKey {
  const key = value?.trim()
  return (zarfAccentKeys as readonly string[]).includes(key ?? '') ? key as ZarfAccentKey : defaultZarfAccent
}

/** The second colour: the wax seal, the fine lines and the lettering on the card. */
export const zarfAccent2Keys = ['altin', 'gumus', 'bakir', 'bordo', 'zumrut', 'lacivert', 'gulkurusu', 'fildisi'] as const
export type ZarfAccent2Key = (typeof zarfAccent2Keys)[number]
export const defaultZarfAccent2: ZarfAccent2Key = 'altin'

interface ZarfPalette2 {
  /** The pretty colour: seal wax, piping, fine lines and a light tint on the envelope lining. */
  main: string
  /** A dark variant of it for lettering on the ivory card (at least 7:1 on the card paper). */
  ink: string
  /** Colour of the initials on the wax (legible on `main`). */
  onSeal: string
}

export const zarfPalettes2: Record<ZarfAccent2Key, ZarfPalette2> = {
  altin: { main: '#b8913f', ink: '#6b4f12', onSeal: '#3d2c06' },
  gumus: { main: '#a3acb6', ink: '#3f4a57', onSeal: '#27303a' },
  bakir: { main: '#b0643a', ink: '#7a3d1c', onSeal: '#fff1e4' },
  bordo: { main: '#8c2f3f', ink: '#6b1f2e', onSeal: '#fbe9ec' },
  zumrut: { main: '#2f7a5c', ink: '#1d5440', onSeal: '#e9fbf3' },
  lacivert: { main: '#2f4a78', ink: '#1d3157', onSeal: '#e8eefc' },
  gulkurusu: { main: '#b9737f', ink: '#7d3a48', onSeal: '#3f1a22' },
  fildisi: { main: '#dccfb2', ink: '#5c5037', onSeal: '#4b4129' },
}

export function resolveZarfAccent2(value: string | null | undefined): ZarfAccent2Key {
  const key = value?.trim()
  return (zarfAccent2Keys as readonly string[]).includes(key ?? '') ? key as ZarfAccent2Key : defaultZarfAccent2
}
