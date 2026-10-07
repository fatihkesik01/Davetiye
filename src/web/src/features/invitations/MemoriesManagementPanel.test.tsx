import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError, type CreatorMemoryConfiguration, type DavetiyeApiClient } from '../../api/generated/client'
import { MemoriesManagementPanel } from './MemoriesManagementPanel'

const initial: CreatorMemoryConfiguration = {
  invitationId: 'invitation-1', isEnabled: false, visibility: 'CreatorOnly', revision: 4, effectiveState: 'Draft',
  inputLimits: { maxDisplayNameCharacters: 60, maxTextCharacters: 500, maxEmojiCharacters: 32, maxMemoriesPerInvitation: 500 },
}

function createApi(over: Record<string, unknown> = {}) {
  return {
    getInvitationMemoriesConfiguration: vi.fn().mockResolvedValue(initial),
    getCreatorMemories: vi.fn().mockResolvedValue({ page: 1, pageSize: 25, totalCount: 0, items: [] }),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
    setInvitationMemoriesConfiguration: vi.fn().mockImplementation(async (_id, request) => ({ ...initial, ...request, revision: 6 })),
    ...over,
  } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
}

describe('MemoriesManagementPanel', () => {
  it('defaults to off and Sadece Creator, saves with the loaded revision and adopts the returned revision', async () => {
    const api = createApi()
    render(<MemoriesManagementPanel api={api} invitationId="invitation-1" />)
    const toggle = await screen.findByLabelText('Anılar bölümünü aç') as HTMLInputElement
    expect(toggle.checked).toBe(false)
    expect((screen.getByRole('radio', { name: /Sadece Creator/ }) as HTMLInputElement).checked).toBe(true)
    expect((screen.getByRole('button', { name: 'Kaydet' }) as HTMLButtonElement).disabled).toBe(true)

    fireEvent.click(toggle)
    fireEvent.click(screen.getByRole('radio', { name: /Public/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText('Anılar ayarları kaydedildi.')).toBeTruthy()
    expect(api.setInvitationMemoriesConfiguration).toHaveBeenCalledWith('invitation-1', { expectedRevision: 4, isEnabled: true, visibility: 'Public' }, 'csrf')

    // The revision jumped 4 -> 6; the next save must use 6.
    fireEvent.click(screen.getByRole('radio', { name: /Sadece Creator/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    await waitFor(() => expect(api.setInvitationMemoriesConfiguration).toHaveBeenCalledTimes(2))
    expect((api.setInvitationMemoriesConfiguration as unknown as ReturnType<typeof vi.fn>).mock.calls[1]?.[1]).toMatchObject({ expectedRevision: 6 })
  })

  it('explains the visibility consequences and the plan/Active requirement', async () => {
    render(<MemoriesManagementPanel api={createApi()} invitationId="invitation-1" />)
    await screen.findByLabelText('Anılar bölümünü aç')
    expect(screen.getByText(/hemen görünür/)).toBeTruthy()
    expect(screen.getByText(/tüm anılar herkese kapalı hale gelir/)).toBeTruthy()
    expect(screen.getByText(/planınız bu özelliği içeriyorsa ve davetiye yayında \(Aktif\)/)).toBeTruthy()
  })

  it('reloads current settings and asks to reapply on a stale-revision 409', async () => {
    const fresh = { ...initial, isEnabled: true, revision: 9 }
    const api = createApi({
      getInvitationMemoriesConfiguration: vi.fn().mockResolvedValueOnce(initial).mockResolvedValueOnce(fresh),
      setInvitationMemoriesConfiguration: vi.fn().mockRejectedValue(new ApiRequestError(409, { currentInvitationRevision: 9 })),
    })
    render(<MemoriesManagementPanel api={api} invitationId="invitation-1" />)
    fireEvent.click(await screen.findByLabelText('Anılar bölümünü aç'))
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText(/başka bir sekmede değişti/)).toBeTruthy()
    expect((screen.getByLabelText('Anılar bölümünü aç') as HTMLInputElement).checked).toBe(true)
  })

  it('shows a retry when loading fails and a message when saving is rejected', async () => {
    const api = createApi({
      getInvitationMemoriesConfiguration: vi.fn().mockRejectedValueOnce(new ApiRequestError(404, null)).mockResolvedValue(initial),
      setInvitationMemoriesConfiguration: vi.fn().mockRejectedValue(new ApiRequestError(404, null)),
    })
    render(<MemoriesManagementPanel api={api} invitationId="invitation-1" />)
    fireEvent.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    fireEvent.click(await screen.findByLabelText('Anılar bölümünü aç'))
    fireEvent.click(screen.getByRole('button', { name: 'Kaydet' }))
    expect(await screen.findByText(/Davetiyeye erişilemiyor/)).toBeTruthy()
  })
})
