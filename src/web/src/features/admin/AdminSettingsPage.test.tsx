import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AdminSettingsPage } from './AdminSettingsPage'

const settings = [
  { key: 'deletedInvitationRetentionDays', displayName: 'Deleted invitations', description: 'Invitation retention setting.', value: 3, minimum: 0, maximum: 365, revision: 5 },
  { key: 'abandonedMemoryRetentionDays', displayName: 'Abandoned memories', description: 'Old abandoned records are cleaned up.', value: 30, minimum: 0, maximum: 365, revision: 2 },
  { key: 'arbitrarySetting', displayName: 'Should not appear', description: null, value: 10, minimum: 0, maximum: 999, revision: 1 },
]

describe('AdminSettingsPage', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('shows only the approved retention keys and explains their existing data semantics', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: settings }))))
    render(<AdminSettingsPage />)

    expect(await screen.findByText('Deleted invitations')).toBeTruthy()
    expect(screen.getByText('Abandoned memories')).toBeTruthy()
    expect(screen.queryByText('Should not appear')).toBeNull()
    expect(screen.getByText(/MediaAsset, PendingUpload/)).toBeTruthy()
    const effects = document.querySelectorAll('.admin-settings__effect')
    expect(effects).toHaveLength(2)
    expect(effects[0]!.textContent?.toLowerCase()).toContain('tarih')
    expect(effects[0]!.textContent?.toLowerCase()).toContain('mevcut')
    expect(effects[1]!.textContent).toContain('MediaAsset')
    expect(effects[1]!.textContent).toContain('PendingUpload')
  })

  it('requires confirmation and antiforgery, then saves the expected revision and integer value', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const fetch = vi.fn((url: string) => {
      if (url === '/api/v1/admin/settings') return Promise.resolve(new Response(JSON.stringify({ items: settings })))
      if (url === '/api/v1/antiforgery/token') return Promise.resolve(new Response(JSON.stringify({ token: 'csrf-token' })))
      if (url === '/api/v1/admin/settings/deletedInvitationRetentionDays') {
        return Promise.resolve(new Response(JSON.stringify({ ...settings[0], value: 7, revision: 6 })))
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetch)
    render(<AdminSettingsPage />)
    const input = await screen.findByLabelText(/Süre/i, { selector: '#admin-setting-value-deletedInvitationRetentionDays' }) as HTMLInputElement
    fireEvent.change(input, { target: { value: '7' } })
    fireEvent.submit(document.querySelector('#admin-setting-title-deletedInvitationRetentionDays')!.closest('form')!)

    await waitFor(() => expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Deleted invitations')))
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/v1/admin/settings/deletedInvitationRetentionDays', expect.objectContaining({
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
      body: JSON.stringify({ expectedRevision: 5, value: 7 }),
    })))
    expect(await screen.findByRole('status')).toBeTruthy()
    expect(input.value).toBe('7')
  })

  it('rejects values outside 0–365 and retains an unsaved draft after a stale revision response', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    const fetch = vi.fn((url: string) => {
      if (url === '/api/v1/admin/settings') return Promise.resolve(new Response(JSON.stringify({ items: settings })))
      if (url === '/api/v1/antiforgery/token') return Promise.resolve(new Response(JSON.stringify({ token: 'csrf-token' })))
      if (url.endsWith('/deletedInvitationRetentionDays')) return Promise.resolve(new Response(JSON.stringify({ title: 'Revision conflict' }), { status: 409 }))
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetch)
    render(<AdminSettingsPage />)
    const input = await screen.findByLabelText(/Süre/i, { selector: '#admin-setting-value-deletedInvitationRetentionDays' }) as HTMLInputElement
    fireEvent.change(input, { target: { value: '366' } })
    expect((document.querySelector('#admin-setting-title-deletedInvitationRetentionDays')!.closest('form')!.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(input, { target: { value: '12' } })
    fireEvent.submit(input.closest('form')!)

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(input.value).toBe('12')
    expect(confirm).toHaveBeenCalled()
    expect(document.querySelector('.admin-settings__conflict button')).toBeTruthy()
  })

  it('renders a retry action when loading fails', async () => {
    const fetch = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(new Response(JSON.stringify({ items: settings })))
    vi.stubGlobal('fetch', fetch)
    render(<AdminSettingsPage />)
    expect(await screen.findByRole('heading', { name: 'Ayarlar yüklenemedi' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /yeniden dene/i }))
    expect(await screen.findByText('Deleted invitations')).toBeTruthy()
  })
})
