import { useId } from 'react'
import { useTranslation } from 'react-i18next'

import './zarf.css'
import { resolveZarfAccent, zarfAccentKeys, zarfPalettes, type ZarfAccentKey } from './zarfAccent'

interface ZarfAccentPickerProps {
  value: string | null | undefined
  onChange: (key: ZarfAccentKey) => void
  disabled?: boolean
  /** Small inline variant for floating bars (the legend stays available to assistive tech). */
  compact?: boolean
}

/** Single-colour choice for zarf-davetiye: curated keys only, each swatch has an accessible name. */
export function ZarfAccentPicker({ value, onChange, disabled, compact }: ZarfAccentPickerProps) {
  const { t } = useTranslation()
  const name = useId()
  const current = resolveZarfAccent(value)
  return <fieldset className={`zarf-accent-picker${compact ? ' zarf-accent-picker--compact' : ''}`} disabled={disabled}>
    <legend>{t('templateUi.zarfAccent.label')}</legend>
    {compact ? null : <p className="fieldset-help">{t('templateUi.zarfAccent.help')}</p>}
    <div className="zarf-accent-picker__swatches">
      {zarfAccentKeys.map(key => <label key={key} className="zarf-accent-picker__swatch">
        <input type="radio" name={name} value={key} checked={current === key} onChange={() => onChange(key)} aria-label={t(`templateUi.zarfAccent.colors.${key}`)} />
        <span className="zarf-accent-picker__dot" style={{ background: zarfPalettes[key].base }} title={t(`templateUi.zarfAccent.colors.${key}`)} />
      </label>)}
    </div>
  </fieldset>
}
