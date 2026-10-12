/** Public slugs stay readable while persisted template keys remain backward compatible. */
const publicSlugByTemplateKey: Readonly<Record<string, string>> = {
  'dantel-davetiye': 'kapi-davetiye',
}

const legacyPublicSlugsByTemplateKey: Readonly<Record<string, readonly string[]>> = {
  'dantel-davetiye': ['gate', 'dantel-davetiye'],
}

export function templatePublicSlug(templateKey: string): string {
  return publicSlugByTemplateKey[templateKey] ?? templateKey
}

export function templateKeyFromPublicSlug(slug: string): string {
  return Object.entries(publicSlugByTemplateKey).find(([key, publicSlug]) =>
    publicSlug === slug || legacyPublicSlugsByTemplateKey[key]?.includes(slug),
  )?.[0] ?? slug
}
