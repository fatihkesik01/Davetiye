import type { DraftContentInput } from '../../../api/generated/client'

/**
 * zarf-davetiye surroundings. The creator picks: an envelope colour (accentColor), a second colour for
 * the seal, lines and card lettering (accentColor2), an ambiance (colour and atmosphere of the page)
 * and a garden (the flowers and little creatures of the botanical frame). Content only ever carries
 * these keys; unknown values fall back to the defaults.
 */

export const zarfAmbianceKeys = ['sabah', 'cayir', 'gunbatimi', 'gece', 'safak', 'orman', 'okyanus', 'lila'] as const
export type ZarfAmbianceKey = (typeof zarfAmbianceKeys)[number]
export const defaultZarfAmbiance: ZarfAmbianceKey = 'sabah'

/** Ambiances with a dark background: they switch to the light text tokens and lighter foliage. */
export const zarfDarkAmbiances: readonly ZarfAmbianceKey[] = ['gece', 'orman']

/** All keys remain resolvable so invitations saved with retired garden options keep their look. */
export const zarfGardenKeys = ['gul', 'papatya-kelebek', 'lale-ugurbocegi', 'lavanta-ari', 'kiraz', 'kelebekler', 'ugur-isik', 'karisik', 'ask-mektubu', 'yildizli-gece', 'deniz-esintisi', 'aurora-gecesi', 'muzik-notalari', 'saray-kapisi', 'japon-bahcesi', 'akdeniz-avlusu', 'buz-sarayi', 'gul-serasi', 'venedik-karnavali', 'yildiz-gecidi', 'yok'] as const
export type ZarfGardenKey = (typeof zarfGardenKeys)[number]
/** The curated set offered to new invitations, followed by an explicit no-decoration option. */
export const zarfThemePickerKeys: readonly ZarfGardenKey[] = ['gul', 'papatya-kelebek', 'lale-ugurbocegi', 'ask-mektubu', 'yildizli-gece', 'deniz-esintisi', 'muzik-notalari', 'yok']
export const defaultZarfGarden: ZarfGardenKey = 'gul'

/** The flower kinds the frame can draw (`none` = foliage only). */
export type ZarfFloraKind = 'gul' | 'papatya' | 'lale' | 'lavanta' | 'kiraz' | 'none'
export type ZarfCritterKind = 'butterfly' | 'bee' | 'dragonfly' | 'firefly' | 'ladybug'

export interface ZarfGardenSpec {
  motif: 'garden' | 'butterfly' | 'firefly' | 'meadow' | 'cherry' | 'love' | 'space' | 'sea' | 'aurora' | 'music' | 'none'
  /** Flower kinds drawn on the branches, cycled through the flower positions. */
  flowers: readonly ZarfFloraKind[]
  /** The creature kinds that move along the branches (empty = none). */
  critters: readonly ZarfCritterKind[]
  /** Cherry petals drifting down. */
  petals: boolean
}

export const zarfGardenSpecs: Record<ZarfGardenKey, ZarfGardenSpec> = {
  gul: { motif: 'garden', flowers: ['gul'], critters: [], petals: false },
  'papatya-kelebek': { motif: 'garden', flowers: ['papatya'], critters: ['butterfly'], petals: false },
  'lale-ugurbocegi': { motif: 'garden', flowers: ['lale'], critters: ['ladybug'], petals: false },
  'lavanta-ari': { motif: 'garden', flowers: ['lavanta'], critters: ['bee'], petals: false },
  kiraz: { motif: 'cherry', flowers: ['kiraz'], critters: [], petals: true },
  kelebekler: { motif: 'butterfly', flowers: ['none'], critters: ['butterfly'], petals: false },
  'ugur-isik': { motif: 'firefly', flowers: ['none'], critters: ['ladybug', 'firefly'], petals: false },
  karisik: { motif: 'meadow', flowers: ['papatya', 'gul', 'lale', 'kiraz'], critters: ['butterfly', 'dragonfly'], petals: false },
  'ask-mektubu': { motif: 'love', flowers: ['none'], critters: [], petals: false },
  'yildizli-gece': { motif: 'space', flowers: ['none'], critters: [], petals: false },
  'deniz-esintisi': { motif: 'sea', flowers: ['none'], critters: [], petals: false },
  'aurora-gecesi': { motif: 'aurora', flowers: ['none'], critters: [], petals: false },
  'muzik-notalari': { motif: 'music', flowers: ['none'], critters: [], petals: false },
  'saray-kapisi': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  'japon-bahcesi': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  'akdeniz-avlusu': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  'buz-sarayi': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  'gul-serasi': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  'venedik-karnavali': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  'yildiz-gecidi': { motif: 'none', flowers: ['none'], critters: [], petals: false },
  yok: { motif: 'none', flowers: ['none'], critters: [], petals: false },
}

/** The flower kind that tints article-level things (blossoms, petals, creature wings). */
export function primaryFlora(spec: ZarfGardenSpec): ZarfFloraKind {
  return spec.flowers.find(kind => kind !== 'none') ?? 'gul'
}

export function resolveZarfAmbiance(value: string | null | undefined): ZarfAmbianceKey {
  const key = value?.trim()
  return (zarfAmbianceKeys as readonly string[]).includes(key ?? '') ? key as ZarfAmbianceKey : defaultZarfAmbiance
}

/** The old single `flora` field maps onto the new garden keys (new content only writes `garden`). */
const legacyFloraToGarden: Record<string, ZarfGardenKey> = {
  gul: 'gul', papatya: 'papatya-kelebek', lale: 'lale-ugurbocegi', lavanta: 'lavanta-ari', kiraz: 'kiraz',
}

export function resolveZarfGarden(value: string | null | undefined, legacyFlora?: string | null): ZarfGardenKey {
  const key = value?.trim()
  if ((zarfGardenKeys as readonly string[]).includes(key ?? '')) return key as ZarfGardenKey
  const legacy = legacyFlora?.trim()
  return (legacy && legacyFloraToGarden[legacy]) || defaultZarfGarden
}

/** Fields added by the zarf look, until the generated client exposes them itself. */
export type ZarfExtraFields = { accentColor2?: string | null; garden?: string | null }

/** Draft content including the zarf look fields. */
export type ZarfDraftContent = DraftContentInput & ZarfExtraFields
