import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError, type DavetiyeApiClient } from '../../api/generated/client'
import { InvitationCheckoutPanel } from './InvitationCheckoutPanel'

const plans = [
  { key: 'standard', displayName: 'Standard', amount: 699, currency: 'TRY', billingPeriod: 'one-time' },
  { key: 'premium', displayName: 'Premium', amount: 1199, currency: 'TRY', billingPeriod: 'one-time' },
]
const checkout = {
  attemptId: 'attempt-1', reference: 'ref-1', status: 'Pending', planKey: 'standard',
  amount: 699, currency: 'TRY', billingPeriod: 'one-time', checkoutUrl: 'https://sandbox-api.iyzipay.com/session',
}

describe('InvitationCheckoutPanel', () => {
  it('shows catalog prices, creates an antiforgery attempt and redirects only after explicit server URL handoff', async () => {
    const api = {
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      listPaymentPlans: vi.fn().mockResolvedValue(plans),
      startInvitationCheckout: vi.fn().mockResolvedValue(checkout),
    } as unknown as DavetiyeApiClient
    const redirectTo = vi.fn()

    render(<InvitationCheckoutPanel invitationId="invitation-1" api={api} redirectTo={redirectTo} />)

    expect(await screen.findByText('699 TRY / tek sefer')).toBeTruthy()
    expect(screen.getByText('1.199 TRY / tek sefer')).toBeTruthy()
    fireEvent.click(screen.getByLabelText(/Standard/))
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))

    await waitFor(() => expect(screen.getByLabelText('Ödeme özeti')).toBeTruthy())
    expect(screen.getByText('Bu deneme henüz ödeme veya yayın hakkı anlamına gelmez. Sağlayıcıda ödeme tamamlanıp sunucu tarafından doğrulanmalıdır.')).toBeTruthy()
    expect(api.startInvitationCheckout).toHaveBeenCalledWith('invitation-1', 'standard', expect.any(String), 'csrf', undefined)
    expect(redirectTo).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))
    expect(redirectTo).toHaveBeenCalledWith('https://sandbox-api.iyzipay.com/session')
  })

  it('blocks new attempts on 409 and tells the Creator not to retry payment', async () => {
    const api = {
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      listPaymentPlans: vi.fn().mockResolvedValue(plans),
      startInvitationCheckout: vi.fn().mockRejectedValue(new ApiRequestError(409, { title: 'Conflict' })),
    } as unknown as DavetiyeApiClient

    render(<InvitationCheckoutPanel invitationId="invitation-1" api={api} />)
    fireEvent.click(await screen.findByLabelText(/Standard/))
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))

    expect((await screen.findByRole('alert')).textContent).toContain('Yeni bir deneme başlatılamaz')
    expect(screen.queryByRole('button', { name: 'Ödemeye geç' })).toBeNull()
    expect(api.startInvitationCheckout).toHaveBeenCalledTimes(1)
  })

  it('retries uncertain responses with the original idempotency key and refuses redirect without a verified pending URL', async () => {
    const api = {
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      listPaymentPlans: vi.fn().mockResolvedValue(plans),
      startInvitationCheckout: vi.fn()
        .mockRejectedValueOnce(new ApiRequestError(503, { title: 'Unavailable' }))
        .mockResolvedValueOnce({ ...checkout, checkoutUrl: null }),
    } as unknown as DavetiyeApiClient

    render(<InvitationCheckoutPanel invitationId="invitation-1" api={api} />)
    fireEvent.click(await screen.findByLabelText(/Standard/))
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Aynı isteği tekrar dene' }))

    expect((await screen.findByRole('status')).textContent).toContain('güvenli bir ödeme bağlantısı doğrulanamadı')
    const first = vi.mocked(api.startInvitationCheckout).mock.calls[0]
    const second = vi.mocked(api.startInvitationCheckout).mock.calls[1]
    expect(first?.[2]).toBe(second?.[2])
    expect(screen.queryByRole('button', { name: 'Ödemeye geç' })).toBeNull()
  })

  it.each(['Failed', 'Canceled'] as const)('allows a fresh explicit attempt after a provider-confirmed %s response', async status => {
    const api = {
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      listPaymentPlans: vi.fn().mockResolvedValue(plans),
      startInvitationCheckout: vi.fn()
        .mockResolvedValueOnce({ ...checkout, status, checkoutUrl: null })
        .mockResolvedValueOnce(checkout),
    } as unknown as DavetiyeApiClient

    render(<InvitationCheckoutPanel invitationId="invitation-1" api={api} />)
    fireEvent.click(await screen.findByLabelText(/Standard/))
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))

    expect((await screen.findByRole('alert')).textContent).toContain('Yeni bir ödeme denemesi başlatabilirsiniz')
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))
    await waitFor(() => expect(screen.getByLabelText('Ödeme özeti')).toBeTruthy())

    const previousKey = vi.mocked(api.startInvitationCheckout).mock.calls[0]?.[2]
    const nextKey = vi.mocked(api.startInvitationCheckout).mock.calls[1]?.[2]
    expect(nextKey).not.toBe(previousKey)
    expect(api.startInvitationCheckout).toHaveBeenCalledTimes(2)
  })

  it('does not redirect to an untrusted HTTPS host returned by the API', async () => {
    const api = {
      getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
      listPaymentPlans: vi.fn().mockResolvedValue(plans),
      startInvitationCheckout: vi.fn().mockResolvedValue({ ...checkout, checkoutUrl: 'https://sandbox-api.iyzipay.com.attacker.test/pay' }),
    } as unknown as DavetiyeApiClient
    const redirectTo = vi.fn()

    render(<InvitationCheckoutPanel invitationId="invitation-1" api={api} redirectTo={redirectTo} />)
    fireEvent.click(await screen.findByLabelText(/Standard/))
    fireEvent.click(screen.getByRole('button', { name: 'Ödemeye geç' }))

    expect((await screen.findByRole('status')).textContent).toContain('güvenli bir ödeme bağlantısı doğrulanamadı')
    fireEvent.click(screen.getByRole('button', { name: 'Aynı isteği tekrar dene' }))
    expect(redirectTo).not.toHaveBeenCalled()
  })
})
