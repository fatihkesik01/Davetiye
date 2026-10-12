import type { ZarfGardenKey } from './zarfTheme'

export type DantelThemeKey = Extract<ZarfGardenKey,
  'japon-bahcesi' | 'akdeniz-avlusu' | 'buz-sarayi' | 'gul-serasi' | 'venedik-karnavali' | 'yildiz-gecidi'
>

export const dantelThemeKeys: readonly DantelThemeKey[] = [
  'japon-bahcesi', 'akdeniz-avlusu', 'buz-sarayi', 'gul-serasi', 'venedik-karnavali', 'yildiz-gecidi',
]

export function isDantelTheme(key: ZarfGardenKey): key is DantelThemeKey {
  return (dantelThemeKeys as readonly string[]).includes(key)
}
