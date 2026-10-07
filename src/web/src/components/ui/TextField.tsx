import type { ChangeEvent } from 'react'

interface TextFieldProps {
  id: string
  label: string
  type?: 'text' | 'email' | 'password'
  value: string
  onChange: (value: string) => void
  autoComplete?: string
  required?: boolean
  helpText?: string
  errorText?: string
  disabled?: boolean
}

/**
 * docs/PHASE_1_PLAN.md §10 §9.2 `FormField` contract: programmatic label, required conveyed beyond a
 * bare "*", help/error associated to the control via aria-describedby.
 */
export function TextField({
  id,
  label,
  type = 'text',
  value,
  onChange,
  autoComplete,
  required,
  helpText,
  errorText,
  disabled,
}: TextFieldProps) {
  const helpId = helpText ? `${id}-help` : undefined
  const errorId = errorText ? `${id}-error` : undefined
  const describedBy = [helpId, errorId].filter(Boolean).join(' ') || undefined

  const handleChange = (event: ChangeEvent<HTMLInputElement>) => onChange(event.target.value)

  return (
    <div className="form-field">
      <label htmlFor={id}>
        {label}
        {required ? (
          <>
            {' '}
            <span aria-hidden="true">*</span>
            <span className="visually-hidden"> (zorunlu)</span>
          </>
        ) : null}
      </label>
      {helpText ? (
        <p id={helpId} className="form-field__help">
          {helpText}
        </p>
      ) : null}
      <input
        id={id}
        name={id}
        type={type}
        value={value}
        onChange={handleChange}
        autoComplete={autoComplete}
        required={required}
        aria-describedby={describedBy}
        aria-invalid={errorText ? true : undefined}
        disabled={disabled}
      />
      {errorText ? (
        <p id={errorId} className="form-field__error">
          {errorText}
        </p>
      ) : null}
    </div>
  )
}
