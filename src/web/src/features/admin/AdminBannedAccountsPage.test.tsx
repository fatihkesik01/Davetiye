import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const apiMocks = vi.hoisted(() => ({
  searchAdminBannedAccounts: vi.fn(),
  getAntiforgeryToken: vi.fn(),
  unbanAccount: vi.fn(),
}))

vi.mock('../../api/generated/client', async importOriginal => {
  const original = await importOriginal<typeof import('../../api/generated/client')>()
  return {
    ...original,
    DavetiyeApiClient: class {
      searchAdminBannedAccounts = apiMocks.searchAdminBannedAccounts
      getAntiforgeryToken = apiMocks.getAntiforgeryToken
      unbanAccount = apiMocks.unbanAccount
    },
  }
})

import { AdminBannedAccountsPage } from './AdminBannedAccountsPage'
import AdminRoutes from '../../routes/AdminRoutes'

type AccountFixture = {
  accountId: string
  displayName: string
  accountType: 'individual' | 'organization'
  email: string | null
  createdAtUtc: string
  bannedAtUtc: string
  reason: string
  [key: string]: unknown
}

const account: AccountFixture = {
  accountId: 'account-1', displayName: 'Örnek Kullanıcı', accountType: 'individual',
  email: 'ornek@example.com', createdAtUtc: '2026-09-01T10:00:00Z',
  bannedAtUtc: '2026-10-01T10:00:00Z', reason: 'Abuse',
}

function page(items: AccountFixture[] = [account], totalCount = items.length) {
  return { page: 1, pageSize: 50, totalCount, items }
}

describe('Admin banned accounts', () => {
  beforeEach(() => {
    apiMocks.searchAdminBannedAccounts.mockReset().mockResolvedValue(page())
    apiMocks.getAntiforgeryToken.mockReset().mockResolvedValue('csrf-token')
    apiMocks.unbanAccount.mockReset().mockResolvedValue(undefined)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    window.history.replaceState({}, '', '/')
  })

  it('shows the account summary, excludes internal notes and searches by email prefix', async () => {
    apiMocks.searchAdminBannedAccounts.mockResolvedValueOnce(page([{ ...account, internalNote: 'never display this' }]))
    render(<AdminBannedAccountsPage />)

    expect(await screen.findByText('ornek@example.com')).toBeTruthy()
    expect(apiMocks.getAntiforgeryToken).toHaveBeenCalledWith(expect.any(AbortSignal))
    expect(apiMocks.searchAdminBannedAccounts).toHaveBeenCalledWith(1, 50, undefined, 'csrf-token', expect.any(AbortSignal))
    expect(screen.getByText('Örnek Kullanıcı')).toBeTruthy()
    expect(screen.getByText('Abuse')).toBeTruthy()
    expect(screen.queryByText('never display this')).toBeNull()

    fireEvent.change(screen.getByRole('searchbox', { name: 'E-posta başlangıcı' }), { target: { value: 'ornek@' } })
    fireEvent.click(screen.getByRole('button', { name: 'Ara' }))
    await waitFor(() => expect(apiMocks.searchAdminBannedAccounts).toHaveBeenLastCalledWith(1, 50, 'ornek@', 'csrf-token', expect.any(AbortSignal)))
    expect(apiMocks.getAntiforgeryToken.mock.invocationCallOrder.at(-1)).toBeLessThan(apiMocks.searchAdminBannedAccounts.mock.invocationCallOrder.at(-1)!)
  })

  it('bounds previous and next page navigation to the available results', async () => {
    apiMocks.searchAdminBannedAccounts.mockResolvedValue(page([account], 51))
    render(<AdminBannedAccountsPage />)

    expect(await screen.findByText('ornek@example.com')).toBeTruthy()
    const previous = screen.getByRole('button', { name: 'Önceki' })
    const next = screen.getByRole('button', { name: 'Sonraki' })
    expect(previous.hasAttribute('disabled')).toBe(true)
    expect(next.hasAttribute('disabled')).toBe(false)
    fireEvent.click(next)
    await waitFor(() => expect(apiMocks.searchAdminBannedAccounts).toHaveBeenLastCalledWith(2, 50, undefined, 'csrf-token', expect.any(AbortSignal)))
    expect(await screen.findByText('Sayfa 2 / 2')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Sonraki' }).hasAttribute('disabled')).toBe(true)
  })

  it('confirms unban, obtains CSRF, submits and refreshes the list', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminBannedAccountsPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'ornek@example.com hesabının banını kaldır' }))
    await waitFor(() => expect(apiMocks.unbanAccount).toHaveBeenCalledWith('account-1', 'csrf-token'))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('hesabının banını kaldırmak istiyor musunuz'))
    await waitFor(() => expect(apiMocks.searchAdminBannedAccounts).toHaveBeenCalledTimes(2))
    expect(apiMocks.getAntiforgeryToken.mock.invocationCallOrder[1]).toBeLessThan(apiMocks.unbanAccount.mock.invocationCallOrder[0]!)
    expect(await screen.findByText('ornek@example.com hesabının banı kaldırıldı.')).toBeTruthy()
  })

  it('reports a failed unban without losing the row', async () => {
    apiMocks.unbanAccount.mockRejectedValue(new Error('request failed'))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<AdminBannedAccountsPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'ornek@example.com hesabının banını kaldır' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Ban kaldırılamadı')
    expect(screen.getByText('ornek@example.com')).toBeTruthy()
  })

  it('safely labels an account without email', async () => {
    apiMocks.searchAdminBannedAccounts.mockResolvedValue(page([{ ...account, email: null }]))
    render(<AdminBannedAccountsPage />)
    expect(await screen.findByText('E-posta yok')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Örnek Kullanıcı hesabının banını kaldır' })).toBeTruthy()
  })

  it('renders a helpful empty state', async () => {
    apiMocks.searchAdminBannedAccounts.mockResolvedValue(page([], 0))
    render(<AdminBannedAccountsPage />)
    expect(await screen.findByText('Banlı hesap yok')).toBeTruthy()
  })

  it('adds the accounts view to Admin navigation and selects its route', async () => {
    window.history.replaceState({}, '', '/admin/accounts')
    render(<AdminRoutes />)
    expect(await screen.findByRole('heading', { name: 'Banlı hesaplar' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Banlı hesaplar' }).getAttribute('aria-current')).toBe('page')
  })
})
