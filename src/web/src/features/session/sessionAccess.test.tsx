import { act, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { invalidateSessionAccess, publishSessionAccess, toSessionView, useSessionAccess } from './sessionAccess'

const json = (value: unknown) => new Response(JSON.stringify(value), { status: 200, headers: { 'Content-Type': 'application/json' } })

function Probe({ name }: { name: string }) {
  const session = useSessionAccess()
  return <p data-testid={name}>{session.status === 'authenticated' ? `authenticated:${session.access}` : session.status}</p>
}

describe('useSessionAccess', () => {
  afterEach(() => { vi.unstubAllGlobals() })

  it('asks the session endpoint once per page load for any number of consumers', async () => {
    const fetch = vi.fn(() => Promise.resolve(json({ authenticated: true, access: 'creator' })))
    vi.stubGlobal('fetch', fetch)
    const view = render(<><Probe name="a" /><Probe name="b" /></>)
    expect(screen.getByTestId('a').textContent).toBe('unknown')
    await waitFor(() => expect(screen.getByTestId('a').textContent).toBe('authenticated:creator'))
    expect(screen.getByTestId('b').textContent).toBe('authenticated:creator')
    view.unmount()
    render(<Probe name="c" />)
    expect(screen.getByTestId('c').textContent).toBe('authenticated:creator')
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/session', expect.objectContaining({ credentials: 'include' }))
  })

  it.each([
    ['network failure', () => Promise.reject(new Error('offline'))],
    ['server error', () => Promise.resolve(new Response('{}', { status: 503 }))],
  ])('treats a %s as anonymous', async (_label, answer) => {
    vi.stubGlobal('fetch', vi.fn(answer))
    render(<Probe name="a" />)
    await waitFor(() => expect(screen.getByTestId('a').textContent).toBe('anonymous'))
  })

  it('shares the answer the route guard already received and refetches after sign-in or sign-out', async () => {
    const fetch = vi.fn(() => Promise.resolve(json({ authenticated: false, access: 'none' })))
    vi.stubGlobal('fetch', fetch)
    act(() => publishSessionAccess({ authenticated: true, access: 'mfa-complete-super-admin', serviceNoticeRequired: false }))
    render(<Probe name="a" />)
    expect(screen.getByTestId('a').textContent).toBe('authenticated:mfa-complete-super-admin')
    expect(fetch).not.toHaveBeenCalled()

    act(() => invalidateSessionAccess())
    await waitFor(() => expect(screen.getByTestId('a').textContent).toBe('anonymous'))
    expect(fetch).toHaveBeenCalledTimes(1)
  })

  it('projects only the access level and never keeps other response fields', () => {
    const view = toSessionView({ authenticated: true, access: 'creator', email: 'ada@example.com' } as never)
    expect(view).toEqual({ status: 'authenticated', access: 'creator' })
    expect(toSessionView({ authenticated: true, access: 'none', serviceNoticeRequired: false })).toEqual({ status: 'anonymous' })
  })
})
