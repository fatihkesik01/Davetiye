import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  getAdminPlans: vi.fn(),
  getAntiforgeryToken: vi.fn(),
  updateAdminPlan: vi.fn(),
}))

vi.mock('../../api/generated/client', async importOriginal => {
  const original = await importOriginal<typeof import('../../api/generated/client')>()
  return {
    ...original,
    DavetiyeApiClient: class {
      getAdminPlans = apiMocks.getAdminPlans
      getAntiforgeryToken = apiMocks.getAntiforgeryToken
      updateAdminPlan = apiMocks.updateAdminPlan
    },
  }
})

import { AdminPlansPage } from './AdminPlansPage'
import { AccountPreferencesProvider } from '../preferences/preferences'
import AdminRoutes from '../../routes/AdminRoutes'

const keys = [
  'maxPublishDays', 'maxActiveInvitations', 'maxImages', 'maxVideos', 'maxImageSizeMb', 'maxVideoSizeMb',
  'maxVideoDurationSeconds', 'maxGuestImages', 'maxGuestVideos', 'maxGuestImageSizeMb', 'maxGuestVideoSizeMb',
  'maxGuestVideoDurationSeconds', 'maxRSVPResponses', 'memoriesEnabled', 'giftRegistryEnabled', 'premiumTemplatesEnabled',
]
const plan = {
  id: 'plan-1', key: 'standard', displayName: 'Standard', description: 'Başlangıç paketi', priceAmount: 699,
  currency: 'TRY', billingKind: 'OneTime' as const,
  entitlements: keys.map(key => key.endsWith('Enabled')
    ? { key, numericValue: null, booleanValue: false }
    : { key, numericValue: 50, booleanValue: null }),
  revision: 3,
}

describe('Admin plan management', () => {
  beforeEach(() => {
    apiMocks.getAdminPlans.mockReset().mockResolvedValue([plan])
    apiMocks.getAntiforgeryToken.mockReset().mockResolvedValue('csrf-token')
    apiMocks.updateAdminPlan.mockReset().mockImplementation(async (id, request) => ({ ...plan, ...request, id, currency: 'TRY', revision: 4 }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
    window.history.replaceState({}, '', '/')
  })

  it('renders typed entitlement controls and allows editing the amount while currency stays read-only', async () => {
    render(<AdminPlansPage />)
    expect(await screen.findByRole('heading', { name: 'Plan bilgileri' })).toBeTruthy()
    expect(screen.getByLabelText('Plan adı')).toHaveProperty('value', plan.displayName)
    expect(screen.getByLabelText(/Açıklama/)).toHaveProperty('value', plan.description)
    expect(screen.getByLabelText('Faturalandırma türü')).toHaveProperty('value', 'OneTime')
    expect(screen.getByLabelText('Creator fotoğraf kotası')).toHaveProperty('value', '50')
    expect(screen.getByRole('checkbox', { name: /Anı modülü/ })).toHaveProperty('checked', false)
    expect(screen.getByLabelText(/Plan tutarı/)).toHaveProperty('value', '699')
    expect(screen.getAllByText('699 TRY / tek sefer')).toHaveLength(1)
    expect(screen.getByText(/yalnızca yeni alımlara uygulanır/)).toBeTruthy()
    expect(screen.queryByLabelText(/^Para birimi$/i)).toBeNull()
  })

  it('confirms changes and submits supported values, expected revision and CSRF', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminPlansPage />)
    await screen.findByRole('heading', { name: 'Plan bilgileri' })
    fireEvent.change(screen.getByLabelText('Plan adı'), { target: { value: 'Standard Plus' } })
    fireEvent.change(screen.getByLabelText(/Plan tutarı/), { target: { value: '799.125' } })
    fireEvent.change(screen.getByLabelText('Creator fotoğraf kotası'), { target: { value: '72' } })
    fireEvent.click(screen.getByRole('checkbox', { name: /Anı modülü/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))

    await waitFor(() => expect(apiMocks.updateAdminPlan).toHaveBeenCalledTimes(1))
    const [id, request, csrf] = apiMocks.updateAdminPlan.mock.calls[0]!
    expect(id).toBe('plan-1')
    expect(csrf).toBe('csrf-token')
    expect(request.expectedRevision).toBe(3)
    expect(request.displayName).toBe('Standard Plus')
    expect(request.priceAmount).toBe(799.125)
    expect(request.entitlements.find((item: { key: string }) => item.key === 'maxImages')).toMatchObject({ numericValue: 72, booleanValue: null })
    expect(request.entitlements.find((item: { key: string }) => item.key === 'memoriesEnabled')).toMatchObject({ numericValue: null, booleanValue: true })
    expect(request.priceAmount).toBe(799.125)
    expect(request).not.toHaveProperty('currency')
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('799,125 TRY / tek sefer'))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('yalnızca yeni alımlara uygulanır'))
    expect((await screen.findByRole('status')).textContent).toContain('Standard Plus planı kaydedildi.')
  })

  it('shows a billing-type confirmation and rejects free type when displayed price is nonzero', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminPlansPage />)
    await screen.findByRole('heading', { name: 'Plan bilgileri' })
    fireEvent.change(screen.getByLabelText('Faturalandırma türü'), { target: { value: 'Monthly' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
    await waitFor(() => expect(apiMocks.updateAdminPlan).toHaveBeenCalledTimes(1))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('699 TRY / aylık'))
    expect(apiMocks.updateAdminPlan.mock.calls[0]?.[1].billingKind).toBe('Monthly')

    apiMocks.getAdminPlans.mockResolvedValueOnce([{ ...plan, revision: 4 }])
    fireEvent.change(screen.getByLabelText('Faturalandırma türü'), { target: { value: 'Free' } })
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', true)
    expect(screen.getByRole('note').textContent).toContain('tutarı sıfır olmalıdır')
    fireEvent.change(screen.getByLabelText(/Plan tutarı/), { target: { value: '0' } })
    expect(screen.queryByRole('note')).toBeNull()
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', false)
    fireEvent.change(screen.getByLabelText('Faturalandırma türü'), { target: { value: 'Monthly' } })
    expect(screen.getByRole('note').textContent).toContain('sıfırdan büyük olmalıdır')
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', true)
    fireEvent.change(screen.getByLabelText(/Plan tutarı/), { target: { value: '0.01' } })
    expect(screen.queryByRole('note')).toBeNull()
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', false)
  })

  it('keeps a legacy zero-priced paid plan visible but prevents re-saving until its price is positive', async () => {
    apiMocks.getAdminPlans.mockResolvedValueOnce([{ ...plan, priceAmount: 0 }])
    render(<AdminPlansPage />)
    await screen.findByRole('heading', { name: 'Plan bilgileri' })
    expect(screen.getByLabelText(/Plan tutarı/)).toHaveProperty('value', '0')
    expect(screen.getByRole('note').textContent).toContain('sıfırdan büyük olmalıdır')
    fireEvent.change(screen.getByLabelText('Plan adı'), { target: { value: 'Standard güncel' } })
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', true)
    fireEvent.change(screen.getByLabelText(/Plan tutarı/), { target: { value: '1' } })
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', false)
  })

  it('rejects amounts with more than four decimal places before submission', async () => {
    render(<AdminPlansPage />)
    await screen.findByRole('heading', { name: 'Plan bilgileri' })
    fireEvent.change(screen.getByLabelText(/Plan tutarı/), { target: { value: '12.34567' } })
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', true)
    expect(apiMocks.updateAdminPlan).not.toHaveBeenCalled()
  })

  it('preserves local edits after a stale revision and offers an explicit server refresh', async () => {
    const { ApiRequestError } = await import('../../api/generated/client')
    apiMocks.updateAdminPlan.mockRejectedValueOnce(new ApiRequestError(409, null))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminPlansPage />)
    await screen.findByRole('heading', { name: 'Plan bilgileri' })
    fireEvent.change(screen.getByLabelText('Plan adı'), { target: { value: 'Yerel düzenleme' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
    expect(await screen.findByRole('button', { name: 'Güncel sürümü yükle' })).toBeTruthy()
    expect(screen.getByLabelText('Plan adı')).toHaveProperty('value', 'Yerel düzenleme')
    expect(screen.getByRole('button', { name: 'Değişiklikleri kaydet' })).toHaveProperty('disabled', true)
    apiMocks.getAdminPlans.mockResolvedValueOnce([{ ...plan, displayName: 'Sunucudaki ad', revision: 4 }])
    fireEvent.click(screen.getByRole('button', { name: 'Güncel sürümü yükle' }))
    await waitFor(() => expect(screen.getByLabelText('Plan adı')).toHaveProperty('value', 'Sunucudaki ad'))
  })

  it('prevents switching plans while a save is pending so the response cannot replace another plan draft', async () => {
    const secondPlan = {
      ...plan, id: 'plan-2', key: 'premium', displayName: 'Premium', revision: 11,
      entitlements: plan.entitlements.map(item => ({ ...item })),
    }
    apiMocks.getAdminPlans.mockResolvedValueOnce([plan, secondPlan])
    let completeSave: ((value: typeof plan) => void) | undefined
    apiMocks.updateAdminPlan.mockReturnValueOnce(new Promise(resolve => { completeSave = resolve }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminPlansPage />)
    await screen.findByRole('heading', { name: 'Plan bilgileri' })
    fireEvent.change(screen.getByLabelText('Plan adı'), { target: { value: 'Standard Düzenlendi' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))
    await waitFor(() => expect(apiMocks.updateAdminPlan).toHaveBeenCalledTimes(1))

    const planChoices = screen.getAllByRole('button', { name: /standard|premium/i })
    expect(planChoices[0]).toHaveProperty('disabled', true)
    expect(planChoices[1]).toHaveProperty('disabled', true)
    fireEvent.click(planChoices[1]!)
    expect(planChoices[0]?.getAttribute('aria-current')).toBe('true')
    expect(planChoices[1]?.getAttribute('aria-current')).toBeNull()

    completeSave?.({ ...plan, displayName: 'Standard Düzenlendi', revision: 4 })
    await waitFor(() => expect(screen.getByLabelText('Plan adı')).toHaveProperty('value', 'Standard Düzenlendi'))
    expect(screen.getByRole('button', { name: /Premium/ }).getAttribute('aria-current')).toBeNull()
    expect(apiMocks.updateAdminPlan.mock.calls[0]?.[0]).toBe('plan-1')
    expect(apiMocks.updateAdminPlan.mock.calls[0]?.[1].expectedRevision).toBe(3)
  })

  it('renders loading, retryable error and empty states', async () => {
    apiMocks.getAdminPlans.mockReturnValueOnce(new Promise(() => undefined))
    const pending = render(<AdminPlansPage />)
    expect(screen.getByRole('status').textContent).toContain('Planlar yükleniyor')
    pending.unmount()
    apiMocks.getAdminPlans.mockRejectedValueOnce(new Error('offline'))
    render(<AdminPlansPage />)
    expect(await screen.findByRole('heading', { name: 'Planlar yüklenemedi' })).toBeTruthy()
    apiMocks.getAdminPlans.mockResolvedValueOnce([])
    fireEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }))
    expect(await screen.findByText('Yönetilecek plan yok')).toBeTruthy()
  })

  it('adds plan management to the Admin navigation', async () => {
    window.history.replaceState({}, '', '/admin/plans')
    render(<AccountPreferencesProvider><AdminRoutes /></AccountPreferencesProvider>)
    expect(await screen.findByRole('heading', { name: 'Planlar ve haklar' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Planlar ve haklar' }).getAttribute('aria-current')).toBe('page')
  })
})
