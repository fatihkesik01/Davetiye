import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'

/**
 * Returns a stable ref to the current `t` so data-loading effects can localize
 * their error messages without re-running (and refetching / resetting drafts)
 * every time the interface language changes.
 */
export function useLatestT() {
  const { t } = useTranslation()
  const ref = useRef(t)
  useEffect(() => { ref.current = t }, [t])
  return ref
}
