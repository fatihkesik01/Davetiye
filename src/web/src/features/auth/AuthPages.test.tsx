import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { StrictMode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ConfirmEmailPage, ForgotPasswordPage, GoogleLinkPage, LoginPage, LogoutButton, RegisterPage, ResetPasswordPage } from './AuthPages'

function okJson(body?: object): Response {
  return new Response(body ? JSON.stringify(body) : null, { status: 200 })
}

describe('Creator authentication pages', () => {
  afterEach(() => {
    window.history.replaceState({}, '', '/')
    vi.unstubAllGlobals()
  })

  it('keeps account type explicit, immutable in copy, and sends the selected Organization value', async () => {
    const fetch = vi.fn().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/giris/kayit')
    render(<RegisterPage />)

    expect(screen.getByText(/kayıt sonrasında değiştirilemez/i)).toBeTruthy()
    fireEvent.change(screen.getByLabelText(/görünen ad/i), { target: { value: 'Ada Organizasyon' } })
    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'ada@example.test' } })
    fireEvent.click(screen.getByRole('radio', { name: /organizasyon/i }))
    fireEvent.change(document.getElementById('register-password')!, { target: { value: 'StrongPassw0rd!' } })
    fireEvent.change(screen.getByLabelText(/şifreyi tekrar/i), { target: { value: 'StrongPassw0rd!' } })
    expect((screen.getByLabelText(/ürün haberleri ve kampanyalar/i) as HTMLInputElement).checked).toBe(false)
    fireEvent.click(screen.getByLabelText(/hizmet bildirimini okudum/i))
    fireEvent.click(screen.getByLabelText(/ürün haberleri ve kampanyalar/i))
    fireEvent.click(screen.getByRole('button', { name: 'Hesap oluştur' }))

    await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/e-posta adresinize gönderilen/i))
    const [, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(JSON.parse(String(init.body))).toMatchObject({ accountType: 'Organization', email: 'ada@example.test', serviceNoticeAcknowledged: true, marketingOptIn: true })
  })

  it('focuses the validation summary and lets its link focus the invalid field', async () => {
    render(<RegisterPage />)
    const submit = screen.getByRole('button', { name: 'Hesap oluştur' })
    submit.focus()
    fireEvent.submit(submit.closest('form')!)

    const summary = await screen.findByRole('alert')
    expect(document.activeElement).toBe(summary)
    fireEvent.click(screen.getByRole('link', { name: 'Görünen adınızı yazın.' }))
    expect(document.activeElement).toBe(screen.getByLabelText(/görünen ad/i))
  })

  it('requires service notice acknowledgement while leaving marketing opt-in unchecked by default', async () => {
    const fetch = vi.fn().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetch)
    render(<RegisterPage />)
    expect((screen.getByLabelText(/ürün haberleri ve kampanyalar/i) as HTMLInputElement).checked).toBe(false)
    fireEvent.change(screen.getByLabelText(/görünen ad/i), { target: { value: 'Ada' } })
    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'ada@example.test' } })
    fireEvent.click(screen.getByRole('radio', { name: /bireysel/i }))
    fireEvent.change(document.getElementById('register-password')!, { target: { value: 'StrongPassw0rd!' } })
    fireEvent.change(screen.getByLabelText(/şifreyi tekrar/i), { target: { value: 'StrongPassw0rd!' } })
    fireEvent.click(screen.getByRole('button', { name: 'Hesap oluştur' }))

    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(fetch).not.toHaveBeenCalled()
    expect(screen.getByRole('link', { name: /hizmet bildirimini onaylayın/i })).toBeTruthy()
  })

  it('falls back to /panel after login when returnUrl is external', async () => {
    let resolveCsrf!: (response: Response) => void
    const csrfResponse = new Promise<Response>(resolve => { resolveCsrf = resolve })
    const fetch = vi.fn((url: string) => url === '/api/v1/auth/capabilities'
      ? Promise.resolve(okJson({ googleSignInEnabled: true }))
      : url === '/api/v1/antiforgery/token' ? csrfResponse : Promise.resolve(okJson()))
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/giris?returnUrl=https%3A%2F%2Fevil.example')
    render(<LoginPage />)

    fireEvent.click(await screen.findByLabelText(/bireysel/i))
    const googleButton = await screen.findByRole('button', { name: 'Google ile devam et' }) as HTMLButtonElement
    expect(googleButton.disabled).toBe(true)
    const googleForm = googleButton.closest('form')!
    expect(googleForm.method).toBe('post')
    expect(googleForm.enctype).toBe('application/x-www-form-urlencoded')
    expect(googleForm.action).toBe(`${window.location.origin}/api/v1/auth/google/challenge`)
    expect(googleForm.querySelector('a[href*="/api/v1/auth/google/challenge"]')).toBeNull()
    expect((googleForm.querySelector('[name="serviceNoticeAcknowledged"]') as HTMLInputElement).value).toBe('false')
    expect((googleForm.querySelector('[name="marketingOptIn"]') as HTMLInputElement).value).toBe('false')
    fireEvent.click(screen.getByLabelText(/hizmet bildirimini okudum/i))
    expect(googleButton.disabled).toBe(true)
    await act(async () => { resolveCsrf(okJson({ token: 'csrf-token' })) })
    await waitFor(() => expect(googleButton.disabled).toBe(false))
    expect((googleForm.querySelector('[name="returnUrl"]') as HTMLInputElement).value).toBe('/panel')
    expect((googleForm.querySelector('[name="accountType"]') as HTMLInputElement).value).toBe('Individual')
    expect((googleForm.querySelector('[name="serviceNoticeAcknowledged"]') as HTMLInputElement).value).toBe('true')
    expect((googleForm.querySelector('[name="__RequestVerificationToken"]') as HTMLInputElement).value).toBe('csrf-token')
    expect(fetch).not.toHaveBeenCalledWith(expect.stringContaining('/api/v1/auth/google/challenge'), expect.anything())
    fireEvent.click(screen.getByLabelText(/yeni oluşturulacak google hesabı için ürün haberleri/i))
    expect((googleForm.querySelector('[name="marketingOptIn"]') as HTMLInputElement).value).toBe('true')
    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'ada@example.test' } })
    fireEvent.change(screen.getByLabelText(/^şifre/i), { target: { value: 'StrongPassw0rd!' } })
    fireEvent.submit(document.querySelector('.auth-form')!)

    await waitFor(() => expect(window.location.pathname).toBe('/panel'))
  })

  it('uses the safe admin fallback for the TOTP login challenge and preserves explicit safe return paths', async () => {
    const fetch = vi.fn((url: string) => Promise.resolve(
      url === '/api/v1/auth/capabilities' ? okJson({ googleSignInEnabled: false })
        : url === '/api/v1/auth/login' ? okJson({ requiresTwoFactor: true })
          : okJson(),
    ))
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/giris?returnUrl=%2Fadmin%2Fplans')
    render(<LoginPage />)

    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'admin@example.test' } })
    fireEvent.change(document.getElementById('login-password')!, { target: { value: 'StrongPassw0rd!' } })
    fireEvent.submit(document.querySelector('.auth-form')!)
    await waitFor(() => expect(document.getElementById('login-two-factor-code')).not.toBeNull())
    fireEvent.change(document.getElementById('login-two-factor-code')!, { target: { value: '123456' } })
    fireEvent.submit(document.querySelector('.auth-form')!)

    await waitFor(() => expect(window.location.pathname).toBe('/admin/plans'))
    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/mfa/login/complete', expect.objectContaining({ body: JSON.stringify({ code: '123456', isRecoveryCode: false }) }))
  })

  it('uses /admin when a recovery-code challenge has no returnUrl', async () => {
    const fetch = vi.fn((url: string) => Promise.resolve(
      url === '/api/v1/auth/capabilities' ? okJson({ googleSignInEnabled: false })
        : url === '/api/v1/auth/login' ? okJson({ requiresTwoFactor: true })
          : okJson(),
    ))
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/giris')
    render(<LoginPage />)
    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'admin@example.test' } })
    fireEvent.change(document.getElementById('login-password')!, { target: { value: 'StrongPassw0rd!' } })
    fireEvent.submit(document.querySelector('.auth-form')!)
    fireEvent.click(await screen.findByRole('button', { name: 'Kurtarma kodu' }))
    fireEvent.change(document.getElementById('login-two-factor-code')!, { target: { value: 'single-use' } })
    fireEvent.submit(document.querySelector('.auth-form')!)
    await waitFor(() => expect(window.location.pathname).toBe('/admin'))
    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/mfa/login/complete', expect.objectContaining({ body: JSON.stringify({ code: 'single-use', isRecoveryCode: true }) }))
  })

  it('does not expose a Google challenge form while the server reports Google OAuth as disabled', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(okJson({ googleSignInEnabled: false })))
    render(<LoginPage />)

    expect((await screen.findByRole('status')).textContent).toMatch(/henüz kullanıma açık değil/i)
    expect(screen.queryByRole('button', { name: 'Google ile devam et' })).toBeNull()
    expect(screen.queryByRole('radio', { name: /bireysel/i })).toBeNull()
  })

  it('keeps Google fail-closed after a capability failure, but lets the user retry it', async () => {
    const fetch = vi.fn()
      .mockRejectedValueOnce(new Error('network unavailable'))
      .mockResolvedValueOnce(okJson({ googleSignInEnabled: true }))
      .mockResolvedValueOnce(okJson({ token: 'csrf-token' }))
    vi.stubGlobal('fetch', fetch)
    render(<LoginPage />)

    expect((await screen.findByRole('status')).textContent).toMatch(/doğrulanamadı/i)
    expect(screen.queryByRole('button', { name: 'Google ile devam et' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByLabelText(/bireysel/i)).toBeTruthy()
    await screen.findByRole('button', { name: 'Google ile devam et' })
    expect(fetch).toHaveBeenCalledTimes(3)
  })

  it('explains password policy failures without exposing the API detail', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Invalid password.', detail: 'Length must be at least 6.' }), { status: 400 }))
    vi.stubGlobal('fetch', fetch)
    render(<RegisterPage />)

    fireEvent.change(screen.getByLabelText(/görünen ad/i), { target: { value: 'Ada' } })
    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'ada@example.test' } })
    fireEvent.click(screen.getByRole('radio', { name: /bireysel/i }))
    fireEvent.change(document.getElementById('register-password')!, { target: { value: 'weak' } })
    fireEvent.change(screen.getByLabelText(/şifreyi tekrar/i), { target: { value: 'weak' } })
    fireEvent.click(screen.getByLabelText(/hizmet bildirimini okudum/i))
    fireEvent.click(screen.getByRole('button', { name: 'Hesap oluştur' }))

    expect((await screen.findByRole('alert')).textContent).toMatch(/güvenlik kurallarını karşılamıyor/i)
    expect(screen.getByRole('alert').textContent).not.toContain('Length must be at least 6')
  })

  it('explains weak password failures on the reset screen without treating the link as expired', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Invalid password.' }), { status: 400 }))
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/auth/reset-password#userId=user-1&token=reset-token')
    render(<ResetPasswordPage />)

    fireEvent.change(screen.getByLabelText(/^yeni şifre\b/i), { target: { value: 'weak' } })
    fireEvent.change(screen.getByLabelText(/yeni şifreyi tekrar/i), { target: { value: 'weak' } })
    fireEvent.click(screen.getByRole('button', { name: 'Şifreyi yenile' }))

    expect((await screen.findByRole('alert')).textContent).toMatch(/güvenlik kurallarını karşılamıyor/i)
    expect(screen.getByRole('alert').textContent).not.toMatch(/süresi dolmuş/i)
  })

  it('uses the same neutral success message for password reset requests', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(okJson()))
    render(<ForgotPasswordPage />)
    fireEvent.change(screen.getByLabelText(/^e-posta/i), { target: { value: 'unknown@example.test' } })
    fireEvent.submit(document.querySelector('.auth-form')!)

    expect((await screen.findByRole('status')).textContent).toMatch(/eşleşen bir hesap varsa/i)
  })

  it('confirms email only after an explicit user action and announces success', async () => {
    const fetch = vi.fn().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/auth/confirm-email#userId=user-1&token=secret-token')
    render(<StrictMode><ConfirmEmailPage /></StrictMode>)

    expect(fetch).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'E-postamı doğrula' }))
    expect(window.location.hash).toBe('')
    expect((await screen.findByRole('status')).textContent).toMatch(/e-posta adresiniz doğrulandı/i)
    const [, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(JSON.parse(String(init.body))).toEqual({ userId: 'user-1', token: 'secret-token' })
  })

  it('submits unchanged reset credentials after scrubbing fragment secrets from the URL', async () => {
    const fetch = vi.fn().mockResolvedValue(okJson())
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/auth/reset-password#userId=user-1&token=reset-token')
    render(<ResetPasswordPage />)

    expect(window.location.hash).toBe('')
    fireEvent.change(screen.getByLabelText(/^yeni şifre\b/i), { target: { value: 'StrongPassw0rd!' } })
    fireEvent.change(screen.getByLabelText(/yeni şifreyi tekrar/i), { target: { value: 'StrongPassw0rd!' } })
    fireEvent.click(screen.getByRole('button', { name: 'Şifreyi yenile' }))

    await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/şifreniz yenilendi/i))
    const [, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(JSON.parse(String(init.body))).toEqual({ userId: 'user-1', token: 'reset-token', newPassword: 'StrongPassw0rd!' })
  })

  it('does not render a reset form when the link parameters are missing', () => {
    window.history.replaceState({}, '', '/auth/reset-password')
    render(<ResetPasswordPage />)
    expect(screen.getByRole('alert').textContent).toMatch(/eksik veya geçersiz/i)
    expect(screen.queryByLabelText(/yeni şifre/i)).toBeNull()
  })

  it('proves account ownership and performs the antiforgery-protected Google link sequence', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(okJson({ googleSignInEnabled: true }))
      .mockResolvedValueOnce(okJson())
      .mockResolvedValueOnce(okJson({ token: 'csrf-token' }))
      .mockResolvedValueOnce(okJson())
    vi.stubGlobal('fetch', fetch)
    render(<GoogleLinkPage />)

    fireEvent.change(await screen.findByLabelText(/mevcut hesap e-postası/i), { target: { value: 'ada@example.test' } })
    fireEvent.change(screen.getByLabelText(/mevcut hesap şifresi/i), { target: { value: 'StrongPassw0rd!' } })
    fireEvent.submit(document.querySelector('.auth-form')!)

    await waitFor(() => expect(screen.getByRole('status').textContent).toMatch(/google hesabınız mevcut hesabınıza bağlandı/i))
    expect(fetch.mock.calls.map(call => call[0])).toEqual([
      '/api/v1/auth/capabilities',
      '/api/v1/auth/login',
      '/api/v1/antiforgery/token',
      '/api/v1/auth/google/link/confirm',
    ])
    const [, confirmInit] = fetch.mock.calls[3] as [string, RequestInit]
    expect(JSON.parse(String(confirmInit.body))).toEqual({ password: 'StrongPassw0rd!' })
    expect(JSON.stringify(fetch.mock.calls)).not.toContain('localStorage')
  })

  it('gets antiforgery protection, logs out, and returns to the public login route', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(okJson({ token: 'csrf-token' }))
      .mockResolvedValueOnce(okJson())
    vi.stubGlobal('fetch', fetch)
    window.history.replaceState({}, '', '/panel')
    render(<LogoutButton />)

    fireEvent.click(screen.getByRole('button', { name: 'Güvenli çıkış yap' }))

    await waitFor(() => expect(window.location.pathname).toBe('/giris'))
    expect(fetch.mock.calls.map(call => call[0])).toEqual(['/api/v1/antiforgery/token', '/api/v1/auth/logout'])
    const [, logoutInit] = fetch.mock.calls[1] as [string, RequestInit]
    expect(logoutInit.headers).toMatchObject({ 'X-CSRF-TOKEN': 'csrf-token' })
  })
})
