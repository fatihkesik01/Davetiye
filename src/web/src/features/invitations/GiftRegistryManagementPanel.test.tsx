import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { CreatorGiftItem, DavetiyeApiClient } from '../../api/generated/client'
import { GiftRegistryManagementPanel } from './GiftRegistryManagementPanel'

const item: CreatorGiftItem = {
  id: 'gift-1', name: 'Çay seti', requestedQuantity: 4, reservedQuantity: 2,
  remainingQuantity: 2, ordinal: 0, revision: 3,
}

function createApi(overrides: Record<string, unknown> = {}) {
  return {
    getCreatorGiftItems: vi.fn().mockResolvedValue([item]),
    getCreatorGiftReservations: vi.fn().mockResolvedValue([]),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
    createCreatorGiftItem: vi.fn().mockImplementation(async (_id, request) => ({ ...item, ...request, id: 'gift-2', ordinal: 1 })),
    updateCreatorGiftItem: vi.fn().mockImplementation(async (_id, giftId, request) => ({ ...item, ...request, id: giftId, revision: 4 })),
    deleteCreatorGiftItem: vi.fn().mockResolvedValue(undefined),
    reorderCreatorGiftItems: vi.fn().mockResolvedValue([item]),
    ...overrides,
  } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
}

describe('GiftRegistryManagementPanel', () => {
  it('prevents deleting an item with active reservations and communicates why', async () => {
    render(<GiftRegistryManagementPanel api={createApi()} invitationId="invitation-1" />)
    await screen.findByText('Çay seti')
    const deleteButton = screen.getByRole('button', { name: 'Sil' }) as HTMLButtonElement
    expect(deleteButton.disabled).toBe(true)
    expect(screen.getByText('Önce aktif rezervasyonları tek tek kaldırın.')).toBeTruthy()
    expect(screen.getByText(/İstenen 4 · Rezerve 2 · Kalan 2/)).toBeTruthy()
  })

  it('creates items with CSRF and adopts the API response', async () => {
    const api = createApi()
    render(<GiftRegistryManagementPanel api={api} invitationId="invitation-1" />)
    fireEvent.change(await screen.findByLabelText('Ürün adı'), { target: { value: 'Vazo' } })
    fireEvent.change(screen.getByLabelText('İstenen miktar'), { target: { value: '3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ürün ekle' }))

    await waitFor(() => expect(api.createCreatorGiftItem).toHaveBeenCalledWith('invitation-1', { name: 'Vazo', requestedQuantity: 3 }, 'csrf'))
    expect(await screen.findByText('Vazo')).toBeTruthy()
  })

  it('exposes keyboard-operable, item-specific reorder controls', async () => {
    const second = { ...item, id: 'gift-2', name: 'Vazo', ordinal: 1, revision: 7 }
    const api = createApi({ getCreatorGiftItems: vi.fn().mockResolvedValue([item, second]) })
    render(<GiftRegistryManagementPanel api={api} invitationId="invitation-1" />)
    const down = await screen.findByRole('button', { name: 'Çay seti ürününü aşağı taşı' })
    expect((screen.getByRole('button', { name: 'Çay seti ürününü yukarı taşı' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(down)
    await waitFor(() => expect(api.reorderCreatorGiftItems).toHaveBeenCalledWith('invitation-1', {
      items: [{ id: 'gift-2', revision: 7 }, { id: 'gift-1', revision: 3 }],
    }, 'csrf'))
  })
})
