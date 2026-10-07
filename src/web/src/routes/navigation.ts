// Hand-rolled client-side navigation. App.tsx's usePathname() hook only listens for `popstate`
// (browser back/forward), which history.pushState() never fires on its own — so every in-app
// navigation must push the new entry AND dispatch a synthetic popstate for App to pick up. This is
// the standard technique for a router-library-free SPA; docs/PHASE_1_PLAN.md §10's route-focus
// contract (RouteShell moving focus to the new h1) already covers what happens once App re-renders.

/**
 * Navigates within the app. Never accepts an absolute URL or protocol-relative path — callers that
 * have an untrusted (e.g. query-string) destination must run it through resolveSafeReturnPath first.
 */
export function navigate(path: string): void {
  if (path === `${window.location.pathname}${window.location.search}`) {
    return
  }

  window.history.pushState({}, '', path)
  window.dispatchEvent(new PopStateEvent('popstate'))
}

/**
 * Mirrors the backend's own ResolveSafeReturnUrl heuristic (GoogleSignInService.cs /
 * docs/PHASE_1_PLAN.md §10 route-guard rule 5): only an app-relative path — no scheme/host, no
 * protocol-relative "//" prefix, no backslash — is ever accepted as a post-auth return target.
 * Anything else (including null/absent) falls back to the caller-supplied default. This is a UX
 * convenience only; it is never the security boundary (the backend independently re-validates its
 * own returnUrl query parameter the same way).
 */
export function resolveSafeReturnPath(candidate: string | null | undefined, fallback: string): string {
  if (
    !candidate ||
    !candidate.startsWith('/') ||
    candidate.startsWith('//') ||
    candidate.includes('\\') ||
    Array.from(candidate).some(character => {
      const code = character.charCodeAt(0)
      return code <= 0x1f || code === 0x7f
    })
  ) {
    return fallback
  }

  return candidate
}

/**
 * Reads one-time email/reset link material and immediately removes it from the visible URL/history
 * entry. The values stay only in component memory, reducing accidental referrer, screenshot and
 * copied-address exposure while the user completes the explicit action.
 */
export function consumeSensitiveLinkParameters(): { userId: string; token: string } {
  const queryParameters = new URLSearchParams(window.location.search)
  const fragmentParameters = new URLSearchParams(window.location.hash.slice(1))
  const hasSensitiveParameters = ['userId', 'token'].some(key => queryParameters.has(key) || fragmentParameters.has(key))
  // Only fragments are accepted as auth credentials: query strings have already reached the
  // reverse proxy and cannot be made safe by client-side cleanup. Legacy query links are scrubbed
  // and rejected so the user can request a fresh link.
  const userId = fragmentParameters.get('userId') ?? ''
  const token = fragmentParameters.get('token') ?? ''

  if (hasSensitiveParameters) {
    for (const key of ['userId', 'token']) {
      queryParameters.delete(key)
      fragmentParameters.delete(key)
    }
    const remainingQuery = queryParameters.toString()
    const remainingFragment = fragmentParameters.toString()
    window.history.replaceState(
      window.history.state,
      '',
      `${window.location.pathname}${remainingQuery ? `?${remainingQuery}` : ''}${remainingFragment ? `#${remainingFragment}` : ''}`,
    )
  }

  return { userId, token }
}

/**
 * Reads an account-deletion confirmation credential from the URL fragment and removes it from
 * the address bar/history before returning it. Query-string credentials are scrubbed but rejected:
 * they have already crossed the proxy/request-log boundary and must not be used.
 */
export function consumeAccountDeletionToken(): { token: string; validLocation: boolean } {
  const queryParameters = new URLSearchParams(window.location.search)
  const fragmentParameters = new URLSearchParams(window.location.hash.slice(1))
  const queryHasToken = queryParameters.has('token')
  const token = queryHasToken ? '' : fragmentParameters.get('token') ?? ''
  const hadToken = queryHasToken || fragmentParameters.has('token')

  if (hadToken) {
    queryParameters.delete('token')
    fragmentParameters.delete('token')
    const remainingQuery = queryParameters.toString()
    const remainingFragment = fragmentParameters.toString()
    window.history.replaceState(
      window.history.state,
      '',
      `${window.location.pathname}${remainingQuery ? `?${remainingQuery}` : ''}${remainingFragment ? `#${remainingFragment}` : ''}`,
    )
  }

  return { token, validLocation: Boolean(token) && !queryHasToken }
}

export function currentReturnUrlParam(): string | null {
  return new URLSearchParams(window.location.search).get('returnUrl')
}
