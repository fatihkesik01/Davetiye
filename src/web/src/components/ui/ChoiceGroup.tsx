import { type ReactNode, useId } from 'react'

export interface ChoiceOption<T extends string> {
  value: T
  label: string
  /** Decorative leading graphic (inline SVG). The label always carries the accessible name. */
  icon?: ReactNode
  /** Short decorative code (for example TR / EN), hidden from assistive technology. */
  badge?: string
  /** Language of the label when it differs from the page (endonyms such as "English"). */
  lang?: string
  /** Decorative swatch rendered by the `swatches` variant. */
  swatch?: ReactNode
}

interface ChoiceGroupProps<T extends string> {
  legend: string
  value: T
  options: readonly ChoiceOption<T>[]
  onChange: (value: T) => void
  /** `segmented`: equal-width connected segments. `swatches`: wrapping cards with a leading swatch. */
  variant: 'segmented' | 'swatches'
  disabled?: boolean
}

const CheckIcon = () => <svg className="choice-group__check-icon" viewBox="0 0 16 16" width="14" height="14" aria-hidden="true" focusable="false">
  <path d="M3.5 8.5l3 3 6-7" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
</svg>

/**
 * One single-choice control for the account preferences. It is a native radio group (arrow keys, roving tab
 * stop and the checked state come from the browser) styled either as a segmented control or as swatch cards.
 * The selected option shows a check mark and a filled/ringed face, so the state is never carried by colour alone.
 */
export function ChoiceGroup<T extends string>({ legend, value, options, onChange, variant, disabled }: ChoiceGroupProps<T>) {
  const name = useId()
  const legendId = `${name}-legend`
  return <fieldset className={`choice-group choice-group--${variant}`} role="radiogroup" aria-labelledby={legendId} disabled={disabled}>
    <legend id={legendId} className="choice-group__legend">{legend}</legend>
    <div className="choice-group__options">
      {options.map(option => <label key={option.value} className="choice-group__option">
        <input type="radio" name={name} value={option.value} checked={value === option.value} onChange={() => onChange(option.value)} />
        <span className="choice-group__face">
          {option.swatch}
          {option.icon ? <span className="choice-group__icon" aria-hidden="true">{option.icon}</span> : null}
          {option.badge ? <span className="choice-group__badge" aria-hidden="true">{option.badge}</span> : null}
          <span className="choice-group__label" lang={option.lang}>{option.label}</span>
          <span className="choice-group__check" aria-hidden="true"><CheckIcon /></span>
        </span>
      </label>)}
    </div>
  </fieldset>
}
