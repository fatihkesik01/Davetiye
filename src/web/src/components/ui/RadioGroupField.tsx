export interface RadioGroupOption {
  value: string
  label: string
  description?: string
}

interface RadioGroupFieldProps {
  id: string
  legend: string
  name: string
  options: RadioGroupOption[]
  value: string | null
  onChange: (value: string) => void
  required?: boolean
  helpText?: string
  errorText?: string
}

/**
 * Used for the Individual/Organization registration choice (docs/PRODUCT.md §2-3): a real
 * `<fieldset>/<legend>` grouping (not a select or free-text field), because the account type is a
 * one-time, mutually-exclusive, explicit choice with no "converted later" affordance anywhere. The
 * first option carries the native `required` attribute so an unselected group still fails HTML5
 * constraint validation the same way a single required input would.
 */
export function RadioGroupField({
  id,
  legend,
  name,
  options,
  value,
  onChange,
  required,
  helpText,
  errorText,
}: RadioGroupFieldProps) {
  const helpId = helpText ? `${id}-help` : undefined
  const errorId = errorText ? `${id}-error` : undefined
  const describedBy = [helpId, errorId].filter(Boolean).join(' ') || undefined

  return (
    <fieldset
      className="form-field form-field--radio-group"
      aria-describedby={describedBy}
      aria-invalid={errorText ? true : undefined}
    >
      <legend>
        {legend}
        {required ? (
          <>
            {' '}
            <span aria-hidden="true">*</span>
            <span className="visually-hidden"> (zorunlu)</span>
          </>
        ) : null}
      </legend>
      {helpText ? (
        <p id={helpId} className="form-field__help">
          {helpText}
        </p>
      ) : null}
      <div className="form-field__radio-options">
        {options.map((option, index) => {
          const optionId = `${id}-${option.value}`
          return (
            <label key={option.value} className="form-field__radio-option" htmlFor={optionId}>
              <input
                type="radio"
                id={optionId}
                name={name}
                value={option.value}
                checked={value === option.value}
                onChange={() => onChange(option.value)}
                required={required && index === 0}
              />
              <span>
                <strong>{option.label}</strong>
                {option.description ? <small>{option.description}</small> : null}
              </span>
            </label>
          )
        })}
      </div>
      {errorText ? (
        <p id={errorId} className="form-field__error">
          {errorText}
        </p>
      ) : null}
    </fieldset>
  )
}
