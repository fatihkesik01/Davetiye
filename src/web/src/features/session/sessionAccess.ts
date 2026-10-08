import { useEffect, useSyncExternalStore } from 'react'
import { DavetiyeApiClient, type SessionAccess, type SessionAccessSnapshot } from '../../api/generated/client'

/**
 * PII-free session view used only to choose which header links to show. It is a UX hint, never an
 * authorization boundary: every protected route and API call is still enforced by the backend.
 */
export type SessionView =
  | { status: 'unknown' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; access: Exclude<SessionAccess, 'none'> }

const ANONYMOUS: SessionView = { status: 'anonymous' }
let view: SessionView = { status: 'unknown' }
let inflight: Promise<void> | null = null
let generation = 0
const listeners = new Set<() => void>()

function publish(next: SessionView) {
  view = next
  for (const listener of listeners) listener()
}

export function toSessionView(snapshot: SessionAccessSnapshot | null | undefined): SessionView {
  if (!snapshot || snapshot.authenticated !== true || !snapshot.access || snapshot.access === 'none') return ANONYMOUS
  return { status: 'authenticated', access: snapshot.access }
}

/** Lets the route guard, which already asked the server, share its answer instead of a second request. */
export function publishSessionAccess(snapshot: SessionAccessSnapshot | null | undefined) {
  generation += 1
  inflight = null
  publish(toSessionView(snapshot))
}

/** Called after sign-in or sign-out so a client-side navigation to a public page shows the right state. */
export function invalidateSessionAccess() {
  generation += 1
  inflight = null
  publish({ status: 'unknown' })
}

export function resetSessionAccessForTests() {
  generation += 1
  inflight = null
  view = { status: 'unknown' }
  listeners.clear()
}

function ensureSessionAccess() {
  if (view.status !== 'unknown' || inflight) return
  const ticket = generation
  const request = Promise.resolve()
    .then(() => new DavetiyeApiClient().getSessionAccess())
    .then(snapshot => toSessionView(snapshot), () => ANONYMOUS)
    .then(next => { if (ticket === generation) publish(next) })
  inflight = request
  void request.finally(() => { if (inflight === request) inflight = null })
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

/**
 * Asks the session endpoint at most once per page load (shared by every header mounted on any
 * client-side route). Until it answers, the value is 'unknown' and the header renders as anonymous,
 * so there is no spinner and no layout shift. A failed request is treated as anonymous.
 */
export function useSessionAccess(): SessionView {
  const current = useSyncExternalStore(subscribe, () => view, () => view)
  useEffect(() => { ensureSessionAccess() }, [current])
  return current
}
