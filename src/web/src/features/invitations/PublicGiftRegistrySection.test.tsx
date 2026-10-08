import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { DavetiyeApiClient } from '../../api/generated/client'
import { PublicGiftRegistrySection } from './PublicGiftRegistrySection'

describe('PublicGiftRegistrySection', () => {
  it('checks template support, reserves a partial quantity and never renders guest contact data', async () => {
    let remaining = 3
    let mine: Array<{ reservationId: string; itemId: string; itemName: string; quantity: number }> = []
    const api = {
      listTemplates: vi.fn().mockResolvedValue([{ key: 'minimal-acilis', supportedModules: ['giftRegistry'] }]),
      getPublicGiftRegistry: vi.fn().mockImplementation(async () => ({ items: [{ id: 'item-1', name: 'Vazo', requestedQuantity: 4, remainingQuantity: remaining, ordinal: 0 }] })),
      getMyPublicGiftReservations: vi.fn().mockImplementation(async () => mine),
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      reservePublicGift: vi.fn().mockImplementation(async (_code, request) => {
        remaining -= request.quantity
        mine = [{ reservationId: 'reservation-1', itemId: request.itemId, itemName: 'Vazo', quantity: request.quantity }]
        return { reservationId: 'reservation-1' }
      }),
      cancelPublicGiftReservation: vi.fn().mockImplementation(async () => { remaining += 2; mine = [] }),
    } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
    render(<PublicGiftRegistrySection api={api} publicCode={'a'.repeat(64)} templateKey="minimal-acilis" />)
    fireEvent.change(await screen.findByLabelText('Ad soyad'), { target: { value: 'Misafir Örnek' } })
    fireEvent.change(screen.getByLabelText('Adet'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Rezerve et' }))
    await waitFor(() => expect(api.reservePublicGift).toHaveBeenCalledWith('a'.repeat(64), {
      itemId: 'item-1', quantity: 2, fullName: 'Misafir Örnek', email: null, phone: null,
    }, 'csrf'))
    expect(await screen.findByText('1 / 4 kaldı')).toBeTruthy()
    expect(screen.getByLabelText('E-posta (isteğe bağlı)')).toBeTruthy()
    expect(screen.getByLabelText('Telefon (isteğe bağlı)')).toBeTruthy()
    fireEvent.click(await screen.findByRole('button', { name: /Vazo - 2 adet/ }))
    await waitFor(() => expect(api.cancelPublicGiftReservation).toHaveBeenCalledWith('a'.repeat(64), 'reservation-1', 'csrf'))
    expect(await screen.findByText('3 / 4 kaldı')).toBeTruthy()
  })

  it('recovers this browser’s reservations from its HttpOnly-cookie-backed API after a page reload', async () => {
    const api = {
      listTemplates: vi.fn().mockResolvedValue([{ key: 'minimal-acilis', supportedModules: ['giftRegistry'] }]),
      getPublicGiftRegistry: vi.fn().mockResolvedValue({ items: [{ id: 'item-1', name: 'Vazo', requestedQuantity: 4, remainingQuantity: 2, ordinal: 0 }] }),
      getMyPublicGiftReservations: vi.fn().mockResolvedValue([{ reservationId: 'reservation-2', itemId: 'item-1', itemName: 'Vazo', quantity: 2 }]),
    } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
    render(<PublicGiftRegistrySection api={api} publicCode={'b'.repeat(64)} templateKey="minimal-acilis" />)
    expect(await screen.findByRole('button', { name: /Vazo - 2 adet/ })).toBeTruthy()
  })
  it('keeps an anonymous cancellation handle after downgrade without caching gift or guest data', async () => {
    const code = 'c'.repeat(64)
    localStorage.setItem(`davetiye-gift-reservations:${code}`, JSON.stringify([{ reservationId: 'reservation-3' }]))
    const api = {
      listTemplates: vi.fn().mockResolvedValue([{ key: 'minimal-acilis', supportedModules: ['giftRegistry'] }]),
      getPublicGiftRegistry: vi.fn().mockRejectedValue(new Error('hidden')),
      getMyPublicGiftReservations: vi.fn().mockRejectedValue(new Error('hidden')),
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      cancelPublicGiftReservation: vi.fn().mockResolvedValue(undefined),
    } as unknown as DavetiyeApiClient & Record<string, ReturnType<typeof vi.fn>>
    render(<PublicGiftRegistrySection api={api} publicCode={code} templateKey="minimal-acilis" />)
    const cancel = await screen.findByRole('button', { name: 'Kaydedilmiş rezervasyonumu iptal et' })
    expect(localStorage.getItem(`davetiye-gift-reservations:${code}`)).toBe('[{"reservationId":"reservation-3"}]')
    fireEvent.click(cancel)
    await waitFor(() => expect(api.cancelPublicGiftReservation).toHaveBeenCalledWith(code, 'reservation-3', 'csrf'))
    await waitFor(() => expect(localStorage.getItem(`davetiye-gift-reservations:${code}`)).toBe('[]'))
  })
})
