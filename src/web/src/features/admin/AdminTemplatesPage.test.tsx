import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  getAdminTemplates: vi.fn(),
  getAntiforgeryToken: vi.fn(),
  updateAdminTemplate: vi.fn(),
}))

vi.mock('../../api/generated/client', async importOriginal => {
  const original = await importOriginal<typeof import('../../api/generated/client')>()
  return {
    ...original,
    DavetiyeApiClient: class {
      getAdminTemplates = apiMocks.getAdminTemplates
      getAntiforgeryToken = apiMocks.getAntiforgeryToken
      updateAdminTemplate = apiMocks.updateAdminTemplate
    },
  }
})

import { AdminTemplatesPage } from './AdminTemplatesPage'
import { AccountPreferencesProvider } from '../preferences/preferences'
import AdminRoutes from '../../routes/AdminRoutes'

const template = {
  id: 'template-1', key: 'zamansiz-dugun', name: 'Zamansız Düğün', description: 'Zamansız bir tasarım.',
  isActive: true, revision: 4,
}

describe('Admin template management', () => {
  beforeEach(() => {
    apiMocks.getAdminTemplates.mockReset().mockResolvedValue([template])
    apiMocks.getAntiforgeryToken.mockReset().mockResolvedValue('csrf-token')
    apiMocks.updateAdminTemplate.mockReset().mockImplementation(async (id, request) => ({ id, key: template.key, ...request, description: request.description, revision: 5 }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
    window.history.replaceState({}, '', '/')
  })

  it('lists editable templates and keeps renderer metadata out of the editor', async () => {
    render(<AdminTemplatesPage />)
    expect(await screen.findByRole('heading', { name: 'Şablon bilgileri' })).toBeTruthy()
    expect(screen.getByLabelText('Ad')).toHaveProperty('value', template.name)
    expect(screen.getByLabelText(/Açıklama/)).toHaveProperty('value', template.description)
    expect(screen.getByRole('checkbox', { name: /Katalogda göster/ })).toHaveProperty('checked', true)
    expect(screen.queryByLabelText(/renderer/i)).toBeNull()
  })

  it('requires confirmation and sends the current revision with antiforgery', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminTemplatesPage />)
    await screen.findByRole('heading', { name: 'Şablon bilgileri' })
    fireEvent.change(screen.getByLabelText('Ad'), { target: { value: 'Yeni İsim' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))

    await waitFor(() => expect(apiMocks.updateAdminTemplate).toHaveBeenCalledWith('template-1', {
      expectedRevision: 4, name: 'Yeni İsim', description: template.description, isActive: true,
    }, 'csrf-token'))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Yeni İsim'))
    expect(apiMocks.getAntiforgeryToken.mock.invocationCallOrder[0]).toBeLessThan(apiMocks.updateAdminTemplate.mock.invocationCallOrder[0]!)
    expect((await screen.findByRole('status')).textContent).toContain('Yeni İsim şablonu kaydedildi.')
  })

  it('keeps edits after a revision conflict and offers a refresh of server values', async () => {
    // Use the real error type so the page can reliably classify the conflict response.
    const { ApiRequestError } = await import('../../api/generated/client')
    apiMocks.updateAdminTemplate.mockRejectedValueOnce(new ApiRequestError(409, null))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminTemplatesPage />)
    await screen.findByRole('heading', { name: 'Şablon bilgileri' })
    fireEvent.change(screen.getByLabelText('Ad'), { target: { value: 'Yerel değişiklik' } })
    fireEvent.click(screen.getByRole('button', { name: 'Değişiklikleri kaydet' }))

    expect(await screen.findByRole('button', { name: 'Güncel sürümü yükle' })).toBeTruthy()
    expect(screen.getByLabelText('Ad')).toHaveProperty('value', 'Yerel değişiklik')
    apiMocks.getAdminTemplates.mockResolvedValueOnce([{ ...template, name: 'Sunucu değişikliği', revision: 5 }])
    fireEvent.click(screen.getByRole('button', { name: 'Güncel sürümü yükle' }))
    await waitFor(() => expect(screen.getByLabelText('Ad')).toHaveProperty('value', 'Sunucu değişikliği'))
    expect(apiMocks.updateAdminTemplate).toHaveBeenCalledTimes(1)
  })

  it('renders loading, retryable error and empty states', async () => {
    apiMocks.getAdminTemplates.mockReturnValueOnce(new Promise(() => undefined))
    const pending = render(<AdminTemplatesPage />)
    expect(screen.getByRole('status').textContent).toContain('Şablonlar yükleniyor')
    pending.unmount()

    apiMocks.getAdminTemplates.mockRejectedValueOnce(new Error('offline'))
    render(<AdminTemplatesPage />)
    expect(await screen.findByRole('heading', { name: 'Şablonlar yüklenemedi' })).toBeTruthy()

    apiMocks.getAdminTemplates.mockResolvedValueOnce([])
    fireEvent.click(screen.getByRole('button', { name: 'Yeniden dene' }))
    expect(await screen.findByText('Yönetilecek şablon yok')).toBeTruthy()
  })

  it('adds the template view to Admin navigation', async () => {
    window.history.replaceState({}, '', '/admin/templates')
    render(<AccountPreferencesProvider><AdminRoutes /></AccountPreferencesProvider>)
    expect(await screen.findByRole('heading', { name: 'Şablonlar' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Şablonlar' }).getAttribute('aria-current')).toBe('page')
  })
})
