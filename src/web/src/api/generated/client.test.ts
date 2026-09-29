import { describe, expect, it, vi } from 'vitest'

import { DavetiyeApiClient } from './client'

describe('DavetiyeApiClient', () => {
  it('uses the versioned API route and sends cookies without storing secrets', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ service: 'Davetiye.Api', apiVersion: 'v1' }),
    ))
    const client = new DavetiyeApiClient({ baseUrl: 'https://api.example.test/', fetch })

    await expect(client.getSystemInfo()).resolves.toEqual({ service: 'Davetiye.Api', apiVersion: 'v1' })
    expect(fetch).toHaveBeenCalledWith('https://api.example.test/api/v1/system/info', {
      credentials: 'include',
      signal: undefined,
    })
  })

  it('gets only the session access classification without browser persistence', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      authenticated: true,
      access: 'creator',
    })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getSessionAccess()).resolves.toEqual({ authenticated: true, access: 'creator' })
    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/session', {
      credentials: 'include',
      cache: 'no-store',
      signal: undefined,
    })
  })
})
