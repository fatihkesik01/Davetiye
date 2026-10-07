import { useEffect, useRef } from 'react'

export interface ErrorSummaryEntry {
  fieldId: string
  message: string
}

interface ErrorSummaryProps {
  id?: string
  title?: string
  errors: ErrorSummaryEntry[]
}

/**
 * docs/PHASE_1_PLAN.md §10 §9.2/§9.3: on submit failure, focus goes to this summary and each listed
 * error links to (and focuses) its field — the user is never auto-jumped field-by-field. Renders
 * nothing when there are no errors, so mounting it unconditionally in a form is safe.
 */
export function ErrorSummary({ id = 'error-summary', title = 'Formu gönderemedik', errors }: ErrorSummaryProps) {
  const headingId = `${id}-heading`
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (errors.length > 0) {
      containerRef.current?.focus()
    }
  }, [errors])

  if (errors.length === 0) {
    return null
  }

  return (
    <div ref={containerRef} id={id} tabIndex={-1} role="alert" aria-labelledby={headingId} className="error-summary">
      <p id={headingId} className="error-summary__title">
        {title}
      </p>
      <ul>
        {errors.map((error) => (
          <li key={error.fieldId}>
            <a
              href={`#${error.fieldId}`}
              onClick={(event) => {
                event.preventDefault()
                document.getElementById(error.fieldId)?.focus()
              }}
            >
              {error.message}
            </a>
          </li>
        ))}
      </ul>
    </div>
  )
}
