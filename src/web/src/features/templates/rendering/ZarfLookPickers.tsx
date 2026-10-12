import { useId, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'

import './zarf.css'
import { ZarfAccentPicker } from './ZarfAccentPicker'
import { CritterArt, FloraHead, ZarfThemeOrnament } from './ZarfFlowers'
import { type DantelThemeKey } from './dantelThemes'
import { resolveZarfAccent2, zarfAccent2Keys, zarfPalettes, zarfPalettes2, type ZarfAccent2Key } from './zarfAccent'
import {
  resolveZarfAmbiance,
  resolveZarfGarden,
  zarfAmbianceKeys,
  zarfThemePickerKeys,
  zarfGardenSpecs,
  type ZarfAmbianceKey,
  type ZarfGardenKey,
} from './zarfTheme'

interface PickerProps<Key extends string> {
  value: string | null | undefined
  onChange: (key: Key) => void
  disabled?: boolean
  compact?: boolean
}

const chipScope = (() => {
  const palette = zarfPalettes.adacayi
  const palette2 = zarfPalettes2.altin
  return { '--z-base': palette.base, '--z-ink': palette.ink, '--z-seal': palette2.main, '--z-rose': palette.rose } as CSSProperties
})()

/** Colour of the seal, the fine lines and the card lettering (the "second colour"). */
export function ZarfAccent2Picker({ value, onChange, disabled, compact, template = 'zarf' }: PickerProps<ZarfAccent2Key> & { template?: 'zarf' | 'dantel' }) {
  const { t } = useTranslation()
  const name = useId()
  const current = resolveZarfAccent2(value)
  return <fieldset className={`zarf-accent-picker${compact ? ' zarf-accent-picker--compact' : ''}`} disabled={disabled}>
    <legend>{t(template === 'dantel' ? 'templateUi.dantelAccent2.label' : 'templateUi.zarfAccent2.label')}</legend>
    {compact ? null : <p className="fieldset-help">{t(template === 'dantel' ? 'templateUi.dantelAccent2.help' : 'templateUi.zarfAccent2.help')}</p>}
    <div className="zarf-accent-picker__swatches">
      {zarfAccent2Keys.map(key => <label key={key} className="zarf-accent-picker__swatch">
        <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={t(`templateUi.zarfAccent2.colors.${key}`)} />
        <span className="zarf-accent-picker__dot" style={{ background: `linear-gradient(135deg, ${zarfPalettes2[key].main}, color-mix(in srgb, ${zarfPalettes2[key].main} 70%, #fff))` }} title={t(`templateUi.zarfAccent2.colors.${key}`)} />
      </label>)}
    </div>
  </fieldset>
}

/** Mood of the whole page: each chip is a tiny gradient of the ambiance, with an accessible name. */
export function ZarfAmbiancePicker({ value, onChange, disabled, compact, template = 'zarf' }: PickerProps<ZarfAmbianceKey> & { template?: 'zarf' | 'dantel' }) {
  const { t } = useTranslation()
  const name = useId()
  const current = resolveZarfAmbiance(value)
  return <fieldset className={`zarf-accent-picker${compact ? ' zarf-accent-picker--compact' : ''}`} disabled={disabled}>
    <legend>{t(template === 'dantel' ? 'templateUi.dantelAmbiance.label' : 'templateUi.zarfAmbiance.label')}</legend>
    {compact ? null : <p className="fieldset-help">{t(template === 'dantel' ? 'templateUi.dantelAmbiance.help' : 'templateUi.zarfAmbiance.help')}</p>}
    <div className="zarf-accent-picker__swatches">
      {zarfAmbianceKeys.map(key => <label key={key} className="zarf-accent-picker__swatch">
        <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={t(`templateUi.zarfAmbiance.options.${key}`)} />
        <span className={`zarf-accent-picker__dot zarf-chip-amb zarf-chip-amb--${key}`} title={t(`templateUi.zarfAmbiance.options.${key}`)} />
      </label>)}
    </div>
  </fieldset>
}

/** A theme choice shown with a miniature of its flowers and/or creatures. */
export function ZarfGardenPicker({ value, onChange, disabled, compact, showOptionNames }: PickerProps<ZarfGardenKey> & { showOptionNames?: boolean }) {
  const { t } = useTranslation()
  const name = useId()
  const current = resolveZarfGarden(value)
  return <fieldset className={`zarf-accent-picker zarf-accent-picker--theme${compact ? ' zarf-accent-picker--compact' : ''}`} disabled={disabled}>
    <legend>{t('templateUi.zarfGarden.label')}</legend>
    {compact ? null : <p className="fieldset-help">{t('templateUi.zarfGarden.help')}</p>}
    <div className="zarf-accent-picker__swatches">
      {zarfThemePickerKeys.map(key => {
        const spec = zarfGardenSpecs[key]
        const flower = spec.flowers.find(kind => kind !== 'none')
        const critter = spec.critters[0]
        return <label key={key} className="zarf-accent-picker__swatch">
          <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={t(`templateUi.zarfGarden.options.${key}`)} />
          <span className={`zarf-accent-picker__dot zarf-chip-garden zarf-chip-garden--${spec.motif} zarf--flora-papatya`} style={chipScope} title={t(`templateUi.zarfGarden.options.${key}`)}>
            {flower ? <svg className="zarf-chip-garden__flower" viewBox="-20 -20 40 40" focusable="false" aria-hidden="true"><FloraHead kind={flower} transform={critter ? 'translate(-3 3) scale(0.8)' : 'scale(0.95)'} full /></svg> : null}
            {critter ? <span className={`zarf-chip-garden__critter${flower ? '' : ' zarf-chip-garden__critter--alone'}`} aria-hidden="true"><CritterArt kind={critter} /></span> : null}
            {!flower && !critter && spec.motif === 'none'
              ? <span className="zarf-chip-garden__none" aria-hidden="true">×</span>
              : null}
            {!flower && !critter && ['love', 'space', 'sea', 'aurora', 'music'].includes(spec.motif)
              ? <ZarfThemeOrnament motif={spec.motif as 'love' | 'space' | 'sea' | 'aurora' | 'music'} variant="picker" className="zarf-chip-garden__motif" />
              : null}
          </span>
          {showOptionNames ? <span className="zarf-accent-picker__theme-name" aria-hidden="true">{t(`templateUi.zarfGarden.options.${key}`)}</span> : null}
        </label>
      })}
    </div>
  </fieldset>
}

/** Gate themes use the shared persisted decor key without changing the Zarf theme list. */
function RoyalGateThemeIcon() {
  return <svg className="zarf-chip-garden__motif" viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    <path d="M13 84V43l13 8V32l14 9V24h20v17l14-9v19l13-8v41z" fill="#76503a" stroke="#c8a65e" strokeWidth="4" strokeLinejoin="round" />
    <path d="M40 84V63a10 10 0 0 1 20 0v21M20 58h11m38 0h11M47 35h6" fill="none" stroke="#f2dfaa" strokeWidth="4" />
    <path d="M50 5l5 10 11 1-8 7 2 11-10-6-10 6 2-11-8-7 11-1z" fill="#f2dfaa" />
  </svg>
}

function DantelThemeIcon({ theme }: { theme: DantelThemeKey }) {
  const line = { fill: 'none', stroke: 'currentColor', strokeWidth: 5, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const }
  return <svg className={`zarf-chip-garden__motif zarf-chip-garden__motif--${theme}`} viewBox="0 0 100 100" aria-hidden="true" focusable="false">
    {theme === 'japon-bahcesi' ? <><path {...line} d="M12 30Q50 12 88 30M22 30v58m56-58v58M24 20Q50 10 76 20"/><circle cx="50" cy="47" r="14" fill="currentColor" opacity=".28"/></> : null}
    {theme === 'akdeniz-avlusu' ? <><path {...line} d="M18 88V52a32 32 0 0 1 64 0v36M25 52a25 25 0 0 1 50 0M12 40h12m52 0h12M18 70h8m48 0h8"/></> : null}
    {theme === 'buz-sarayi' ? <><path {...line} d="M18 88V44l12-8-4-12 18 6L50 12l8 18 18-6-4 12 10 8v44M20 55h60M30 70h40M50 42v20m-9-15 18 11m0-11L41 58"/></> : null}
    {theme === 'gul-serasi' ? <><path {...line} d="M18 88V46a32 32 0 0 1 64 0v42M50 15v73M22 56h56M22 72h56"/><circle cx="34" cy="51" r="7" fill="currentColor"/><circle cx="69" cy="70" r="7" fill="currentColor"/></> : null}
    {theme === 'venedik-karnavali' ? <><path {...line} d="M18 88V52a32 32 0 0 1 64 0v36M25 53a25 25 0 0 1 50 0"/><path d="M34 47q16-13 32 0l-4 13q-5 8-12 1-7 7-12-1z" fill="currentColor" opacity=".55"/><path {...line} d="M43 49l5 3-4 2m13-5-5 3 4 2"/></> : null}
    {theme === 'yildiz-gecidi' ? <><path {...line} d="M14 64Q50 20 86 64M20 82Q50 52 80 82M24 38l10-8 10 8m20-12 8-7 8 8"/><path d="M50 12l4 10 10 1-8 7 2 10-8-5-9 5 2-10-8-7 10-1z" fill="currentColor"/></> : null}
  </svg>
}

/** Gate themes are persisted in the existing garden key without changing Zarf's theme choices. */
export function DantelFlowerPicker({ value, onChange, disabled, compact, showOptionNames }: PickerProps<ZarfGardenKey> & { showOptionNames?: boolean }) {
  const { t } = useTranslation()
  const name = useId()
  const current = value?.trim() ? resolveZarfGarden(value) : 'yok'
  const options: ZarfGardenKey[] = ['saray-kapisi', 'japon-bahcesi', 'akdeniz-avlusu', 'buz-sarayi', 'gul-serasi', 'venedik-karnavali', 'yildiz-gecidi', 'yok']
  return <fieldset className="zarf-accent-picker zarf-accent-picker--theme" disabled={disabled}>
    <legend>{t('templateUi.dantelFlower.label')}</legend>
    {compact ? null : <p className="fieldset-help">{t('templateUi.dantelFlower.help')}</p>}
    <div className="zarf-accent-picker__swatches">
      {options.map(key => {
        const kind = zarfGardenSpecs[key].flowers.find(item => item !== 'none')
        const optionLabel = key === 'saray-kapisi' ? t('templateUi.dantelFlower.options.saray') : key === 'yok' ? t('templateUi.dantelFlower.options.yok') : t(`templateUi.dantelFlower.options.${key}`)
        return <label key={key} className="zarf-accent-picker__swatch">
          <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={optionLabel} />
          <span className="zarf-accent-picker__dot zarf-chip-garden zarf--flora-papatya" style={chipScope} title={optionLabel}>
            {kind ? <svg className="zarf-chip-garden__flower" viewBox="-20 -20 40 40" focusable="false" aria-hidden="true"><FloraHead kind={kind} transform="scale(0.95)" full /></svg> : key === 'saray-kapisi' ? <RoyalGateThemeIcon /> : key === 'yok' ? <span className="zarf-chip-garden__none" aria-hidden="true">×</span> : <DantelThemeIcon theme={key as DantelThemeKey} />}
          </span>
          {showOptionNames ? <span className="zarf-accent-picker__theme-name" aria-hidden="true">{optionLabel}</span> : null}
        </label>
      })}
    </div>
  </fieldset>
}

interface LookValues { accentColor?: string | null; accentColor2?: string | null; ambiance?: string | null; garden?: string | null }

/** The four zarf-davetiye look pickers (envelope colour, seal colour, ambiance, garden) in one stack. */
export function ZarfLookPickers({ values, onAccent, onAccent2, onAmbiance, onGarden, disabled, showThemeNames }: {
  values: LookValues
  onAccent: (key: string) => void
  onAccent2: (key: ZarfAccent2Key) => void
  onAmbiance: (key: ZarfAmbianceKey) => void
  onGarden: (key: ZarfGardenKey) => void
  disabled?: boolean
  showThemeNames?: boolean
}) {
  return <div className="zarf-look-pickers">
    <ZarfAccentPicker value={values.accentColor} onChange={onAccent} disabled={disabled} />
    <ZarfAccent2Picker value={values.accentColor2} onChange={onAccent2} disabled={disabled} />
    <ZarfAmbiancePicker value={values.ambiance} onChange={onAmbiance} disabled={disabled} />
    <ZarfGardenPicker value={values.garden} onChange={onGarden} disabled={disabled} showOptionNames={showThemeNames} />
  </div>
}

export function DantelLookPickers({ values, onAccent, onAccent2, onAmbiance, onGarden, disabled, showThemeNames }: {
  values: LookValues
  onAccent: (key: string) => void
  onAccent2: (key: ZarfAccent2Key) => void
  onAmbiance: (key: ZarfAmbianceKey) => void
  onGarden: (key: ZarfGardenKey) => void
  disabled?: boolean
  showThemeNames?: boolean
}) {
  return <div className="zarf-look-pickers">
    <ZarfAccentPicker value={values.accentColor} onChange={onAccent} disabled={disabled} template="dantel" />
    <ZarfAccent2Picker value={values.accentColor2} onChange={onAccent2} disabled={disabled} template="dantel" />
    <ZarfAmbiancePicker value={values.ambiance} onChange={onAmbiance} disabled={disabled} template="dantel" />
    <DantelFlowerPicker value={values.garden} onChange={onGarden} disabled={disabled} showOptionNames={showThemeNames} />
  </div>
}
