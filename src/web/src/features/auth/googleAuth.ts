import { useEffect, useState } from 'react'

import { DavetiyeApiClient } from '../../api/generated/client'

export type GoogleSignInAvailability = 'checking' | 'enabled' | 'disabled' | 'unavailable'

export interface GoogleSignInAvailabilityState {
  availability: GoogleSignInAvailability
  retry: () => void
}

/**
 * The server is the single source of truth: Google endpoints are intentionally not mapped while
 * OAuth is disabled. The UI therefore stays fail-closed until the always-mapped capability endpoint
 * explicitly says the challenge route is available.
 */
export function useGoogleSignInAvailability(api: DavetiyeApiClient): GoogleSignInAvailabilityState {
  const [availability, setAvailability] = useState<GoogleSignInAvailability>('checking')
  const [requestNumber, setRequestNumber] = useState(0)

  const retry = () => {
    setAvailability('checking')
    setRequestNumber(value => value + 1)
  }

  useEffect(() => {
    let current = true

    void api.getAuthenticationCapabilities()
      .then(({ googleSignInEnabled }) => {
        if (current) setAvailability(googleSignInEnabled ? 'enabled' : 'disabled')
      })
      .catch(() => {
        if (current) setAvailability('unavailable')
      })

    return () => { current = false }
  }, [api, requestNumber])

  return { availability, retry }
}
