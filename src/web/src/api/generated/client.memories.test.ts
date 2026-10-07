import { describe, expect, it, vi } from 'vitest'

import { DavetiyeApiClient } from './client'

describe('DavetiyeApiClient memories', () => {
  it('uses the owner-scoped Memories configuration route with an optimistic revision and antiforgery', async () => {
    const config = { invitationId: 'invitation-1', isEnabled: true, visibility: 'Public', revision: 3, effectiveState: 'Draft', inputLimits: {} }
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(config))))
    const client = new DavetiyeApiClient({ fetch })
    const url = '/api/v1/invitations/invitation-1/memories/configuration'

    await client.getInvitationMemoriesConfiguration('invitation-1')
    await client.setInvitationMemoriesConfiguration('invitation-1', { expectedRevision: 1, isEnabled: true, visibility: 'Public' }, 'csrf')

    expect(fetch.mock.calls.map(call => [call[0], (call[1] as RequestInit).method])).toEqual([[url, 'GET'], [url, 'PUT']])
    expect(fetch.mock.calls[1]?.[1]).toMatchObject({
      headers: { 'X-CSRF-TOKEN': 'csrf', 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedRevision: 1, isEnabled: true, visibility: 'Public' }),
    })
  })

  it('reads public memories without credentials, submits with antiforgery and exposes the error code', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ status: 'available', limits: { maxDisplayNameCharacters: 60, maxTextCharacters: 500, maxEmojiCharacters: 32 } })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ page: 2, pageSize: 20, totalCount: 0, items: [] })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ memoryId: 'm1', createdAt: '2026-10-05T10:00:00Z' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'memory_quota_reached' }), { status: 409 }))
    const client = new DavetiyeApiClient({ fetch })

    await client.getPublicMemoriesConfiguration('public-code')
    await client.getPublicMemories('public-code', 2, 20)
    await client.createPublicMemory('public-code', { displayName: null, text: 'Merhaba', emoji: null }, 'csrf')
    await expect(client.createPublicMemory('public-code', { text: 'x' }, 'csrf')).rejects.toMatchObject({ status: 409, problem: { code: 'memory_quota_reached' } })

    expect(fetch.mock.calls[0]?.[0]).toBe('/api/v1/public/invitations/public-code/memories/configuration')
    expect(fetch.mock.calls[0]?.[1]).toMatchObject({ method: 'GET', credentials: 'omit' })
    expect(fetch.mock.calls[1]?.[0]).toBe('/api/v1/public/invitations/public-code/memories?page=2&pageSize=20')
    expect(fetch.mock.calls[1]?.[1]).toMatchObject({ method: 'GET', credentials: 'omit' })
    expect(fetch.mock.calls[2]?.[0]).toBe('/api/v1/public/invitations/public-code/memories')
    expect(fetch.mock.calls[2]?.[1]).toMatchObject({ method: 'POST', credentials: 'include', headers: { 'X-CSRF-TOKEN': 'csrf' } })
  })

  it('lists owner-scoped memories and sends antiforgery for terminal hide/delete actions', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const client = new DavetiyeApiClient({ fetch })
    await client.getCreatorMemories('invitation-1', 2, 25)
    await client.hideCreatorMemory('invitation-1', 'memory-1', 'csrf')
    await client.deleteCreatorMemory('invitation-1', 'memory-2', 'csrf')

    expect(fetch.mock.calls[0]?.[0]).toBe('/api/v1/invitations/invitation-1/memories?page=2&pageSize=25')
    expect(fetch.mock.calls[0]?.[1]).toMatchObject({ method: 'GET', credentials: 'include' })
    expect(fetch.mock.calls[1]?.[0]).toBe('/api/v1/invitations/invitation-1/memories/memory-1/hide')
    expect(fetch.mock.calls[1]?.[1]).toMatchObject({ method: 'PUT', headers: { 'X-CSRF-TOKEN': 'csrf' } })
    expect(fetch.mock.calls[2]?.[0]).toBe('/api/v1/invitations/invitation-1/memories/memory-2')
    expect(fetch.mock.calls[2]?.[1]).toMatchObject({ method: 'DELETE', headers: { 'X-CSRF-TOKEN': 'csrf' } })
  })

  it('creates a Creator-only short-lived Guest media preview with antiforgery', async () => {
    const preview = { mediaKind: 'video', deliveryUrl: 'https://media.example.test/session/iframe', expiresAt: '2026-10-05T12:00:00Z' }
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(preview)))
    const client = new DavetiyeApiClient({ fetch })
    await expect(client.createCreatorMemoryMediaDelivery('invitation-1', 'memory-1', 'asset-1', 'csrf'))
      .resolves.toEqual(preview)
    expect(fetch).toHaveBeenCalledWith(
      '/api/v1/invitations/invitation-1/memories/memory-1/media/asset-1/delivery',
      expect.objectContaining({ method: 'POST', credentials: 'include', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf' }, body: '{}' }),
    )
  })
})

describe('DavetiyeApiClient public catalog', () => {
  it('requests the anonymous template catalog without credentials so public pages never send a Creator session', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response('[]'))
    await new DavetiyeApiClient({ fetch }).listTemplates()
    expect(fetch).toHaveBeenCalledWith('/api/v1/templates', expect.objectContaining({ method: 'GET', credentials: 'omit' }))
  })
})
