import { useId, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'

import './zarf.css'
import { FloraHead } from './ZarfFlowers'
import { ZarfAccentPicker } from './ZarfAccentPicker'
import { zarfPalettes } from './zarfAccent'
import {
  resolveZarfAmbiance,
  resolveZarfFlora,
  zarfAmbianceKeys,
  zarfFloraKeys,
  type ZarfAmbianceKey,
  type ZarfFloraKey,
} from './zarfTheme'

interface PickerProps<Key extends string> {
  value: string | null | undefined
  onChange: (key: Key) => void
  disabled?: boolean
  compact?: boolean
}

const chipScope = (() => {
  const palette = zarfPalettes.adacayi
  return { '--z-base': palette.base, '--z-ink': palette.ink, '--z-seal': palette.seal, '--z-rose': palette.rose } as CSSProperties
})()

/** Five-way mood choice: each chip is a tiny gradient of the ambiance, with an accessible name. */
export function ZarfAmbiancePicker({ value, onChange, disabled, compact }: PickerProps<ZarfAmbianceKey>) {
  const { t } = useTranslation()
  const name = useId()
  const current = resolveZarfAmbiance(value)
  return <fieldset className={`zarf-accent-picker${compact ? ' zarf-accent-picker--compact' : ''}`} disabled={disabled}>
    <legend>{t('templateUi.zarfAmbiance.label')}</legend>
    <div className="zarf-accent-picker__swatches">
      {zarfAmbianceKeys.map(key => <label key={key} className="zarf-accent-picker__swatch">
        <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={t(`templateUi.zarfAmbiance.options.${key}`)} />
        <span className={`zarf-accent-picker__dot zarf-chip-amb zarf-chip-amb--${key}`} title={t(`templateUi.zarfAmbiance.options.${key}`)} />
      </label>)}
    </div>
  </fieldset>
}

/** Five-way flower choice: each chip shows a tiny flower of that kind. */
export function ZarfFloraPicker({ value, onChange, disabled, compact }: PickerProps<ZarfFloraKey>) {
  const { t } = useTranslation()
  const name = useId()
  const current = resolveZarfFlora(value)
  return <fieldset className={`zarf-accent-picker${compact ? ' zarf-accent-picker--compact' : ''}`} disabled={disabled}>
    <legend>{t('templateUi.zarfFlora.label')}</legend>
    <div className="zarf-accent-picker__swatches">
      {zarfFloraKeys.map(key => <label key={key} className="zarf-accent-picker__swatch">
        <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={t(`templateUi.zarfFlora.options.${key}`)} />
        <span className={`zarf-accent-picker__dot zarf-chip-flora zarf--flora-${key}`} style={chipScope} title={t(`templateUi.zarfFlora.options.${key}`)}>
          <svg viewBox="-20 -20 40 40" focusable="false" aria-hidden="true"><FloraHead kind={key} transform="scale(0.95)" full /></svg>
        </span>
      </label>)}
    </div>
  </fieldset>
}

interface LookValues { accentColor?: string | null; ambiance?: string | null; flora?: string | null }

/** The three zarf-davetiye look pickers (envelope colour, ambiance, flowers) in one stack. */
export function ZarfLookPickers({ values, onAccent, onAmbiance, onFlora, disabled }: {
  values: LookValues
  onAccent: (key: string) => void
  onAmbiance: (key: ZarfAmbianceKey) => void
  onFlora: (key: ZarfFloraKey) => void
  disabled?: boolean
}) {
  return <div className="zarf-look-pickers">
    <ZarfAccentPicker value={values.accentColor} onChange={onAccent} disabled={disabled} />
    <ZarfAmbiancePicker value={values.ambiance} onChange={onAmbiance} disabled={disabled} />
    <ZarfFloraPicker value={values.flora} onChange={onFlora} disabled={disabled} />
  </div>
}
