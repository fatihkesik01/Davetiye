/**
 * zarf-davetiye surroundings: the creator picks one ambiance (the mood of the whole page, with its own
 * small animated creatures) and one flora (the flower type of the botanical frame). Content only ever
 * carries these keys; unknown values fall back to the defaults.
 */

export const zarfAmbianceKeys = ['sabah', 'cayir', 'gunbatimi', 'gece', 'safak'] as const
export type ZarfAmbianceKey = (typeof zarfAmbianceKeys)[number]
export const defaultZarfAmbiance: ZarfAmbianceKey = 'sabah'

export const zarfFloraKeys = ['gul', 'papatya', 'lale', 'lavanta', 'kiraz'] as const
export type ZarfFloraKey = (typeof zarfFloraKeys)[number]
export const defaultZarfFlora: ZarfFloraKey = 'gul'

/** The creature that lives in each ambiance. */
export const zarfCritterByAmbiance: Record<ZarfAmbianceKey, 'butterfly' | 'bee' | 'dragonfly' | 'firefly' | 'ladybug'> = {
  sabah: 'butterfly',
  cayir: 'bee',
  gunbatimi: 'dragonfly',
  gece: 'firefly',
  safak: 'ladybug',
}

export function resolveZarfAmbiance(value: string | null | undefined): ZarfAmbianceKey {
  const key = value?.trim()
  return (zarfAmbianceKeys as readonly string[]).includes(key ?? '') ? key as ZarfAmbianceKey : defaultZarfAmbiance
}

export function resolveZarfFlora(value: string | null | undefined): ZarfFloraKey {
  const key = value?.trim()
  return (zarfFloraKeys as readonly string[]).includes(key ?? '') ? key as ZarfFloraKey : defaultZarfFlora
}
