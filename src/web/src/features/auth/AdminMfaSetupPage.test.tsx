import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AccountPreferencesProvider } from '../preferences/preferences'
import { AdminMfaSetupPage } from './AdminMfaSetupPage'

describe('AdminMfaSetupPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('enrolls with CSRF, verifies TOTP, shows recovery codes once, and gates continuation on explicit save', async () => {
    const fetch = vi.fn((url: string) => {
      if (url === '/api/v1/account/preferences') return Promise.resolve(new Response(JSON.stringify({ locale: 'tr', colorTheme: 'kutlio', appearance: 'system' })))
      if (url === '/api/v1/antiforgery/token') return Promise.resolve(new Response(JSON.stringify({ token: 'csrf' })))
      if (url === '/api/v1/admin/mfa/enroll') return Promise.resolve(new Response(JSON.stringify({ sharedKey: 'JBSWY3DPEHPK3PXP', authenticatorUri: 'otpauth://totp/Davetiye:admin?secret=JBSWY3DPEHPK3PXP' })))
      if (url === '/api/v1/admin/mfa/verify') return Promise.resolve(new Response(JSON.stringify({ recoveryCodes: ['alpha-one', 'beta-two'] })))
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    })
    vi.stubGlobal('fetch', fetch)
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: vi.fn().mockResolvedValue(undefined) } })
    window.history.replaceState({}, '', '/admin/mfa/setup')
    render(<AccountPreferencesProvider><AdminMfaSetupPage /></AccountPreferencesProvider>)

    expect(await screen.findByText('JBSWY3DPEHPK3PXP')).toBeTruthy()
    expect(await screen.findByRole('img')).toBeTruthy()
    fireEvent.change(document.getElementById('admin-mfa-verify-code')!, { target: { value: '123456' } })
    fireEvent.submit(document.querySelector('.auth-form')!)

    expect(await screen.findByText('alpha-one')).toBeTruthy()
    expect(screen.getByText('beta-two')).toBeTruthy()
    expect(screen.queryByText('JBSWY3DPEHPK3PXP')).toBeNull()
    expect(screen.queryByRole('img')).toBeNull()
    const continueButton = document.querySelector('.admin-mfa-setup__recovery > button') as HTMLButtonElement
    expect(continueButton.disabled).toBe(true)
    fireEvent.click(document.querySelector('.admin-mfa-setup__actions button')!)
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(['alpha-one', 'beta-two'].join(String.fromCharCode(10)))
    await waitFor(() => expect(continueButton.disabled).toBe(false))
    expect(screen.getByText(/parola ve yeni authenticator kodunuzla tekrar giriş yapın/i)).toBeTruthy()
    fireEvent.click(continueButton)
    expect(window.location.pathname).toBe('/giris')
    expect(new URLSearchParams(window.location.search).get('returnUrl')).toBe('/admin')
    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/mfa/enroll', expect.objectContaining({ method: 'POST', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf' }) }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/mfa/verify', expect.objectContaining({ method: 'POST', body: JSON.stringify({ code: '123456' }), headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf' }) }))
  })
})
