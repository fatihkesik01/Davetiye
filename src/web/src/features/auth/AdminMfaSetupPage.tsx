import { useEffect, useMemo, useState } from 'react'
import QRCode from 'qrcode'

import { DavetiyeApiClient, type AdminMfaEnrollment } from '../../api/generated/client'
import { InternalLink } from '../../components/ui/InternalLink'
import { TextField } from '../../components/ui/TextField'
import { navigate } from '../../routes/navigation'
import { RouteShell } from '../../routes/RouteShell'
import { authErrorMessage } from './authErrors'

type PageState = 'loading' | 'ready' | 'submitting' | 'codes' | 'error'

export function AdminMfaSetupPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [state, setState] = useState<PageState>('loading')
  const [enrollment, setEnrollment] = useState<AdminMfaEnrollment | null>(null)
  const [qrDataUrl, setQrDataUrl] = useState('')
  const [csrfToken, setCsrfToken] = useState('')
  const [code, setCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([])
  const [savedCodes, setSavedCodes] = useState(false)
  const [message, setMessage] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    void (async () => {
      try {
        const token = await api.getAntiforgeryToken(controller.signal)
        const result = await api.enrollAdminMfa(token, controller.signal)
        if (controller.signal.aborted) return
        setCsrfToken(token)
        setEnrollment(result)
        setState('ready')
      } catch (error) {
        if (controller.signal.aborted) return
        setState('error')
        setMessage(authErrorMessage(error, 'mfa-setup'))
      }
    })()
    return () => controller.abort()
  }, [api])

  useEffect(() => {
    if (!enrollment?.authenticatorUri) return
    let active = true
    void QRCode.toDataURL(enrollment.authenticatorUri, { width: 240, margin: 1, errorCorrectionLevel: 'M' })
      .then(value => { if (active) setQrDataUrl(value) })
      .catch(() => { if (active) setQrDataUrl('') })
    return () => { active = false }
  }, [enrollment])

  const verify = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!code.trim()) {
      setMessage('Authenticator uygulamanızdaki kodu yazın.')
      return
    }
    setState('submitting')
    setMessage('Authenticator doğrulanıyor…')
    try {
      const result = await api.verifyAdminMfa(code.trim(), csrfToken)
      setRecoveryCodes(result.recoveryCodes)
      setSavedCodes(false)
      setEnrollment(null)
      setQrDataUrl('')
      setState('codes')
      setMessage('MFA etkinleştirildi. Kurtarma kodlarınızı şimdi kaydedin; bu kodlar daha sonra yeniden gösterilmez.')
    } catch (error) {
      setState('ready')
      setMessage(authErrorMessage(error, 'mfa-setup'))
    }
  }

  const downloadRecoveryCodes = () => {
    const blob = new Blob([`Kutlio Super Admin kurtarma kodları\n\n${recoveryCodes.join('\n')}\n`], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'davetiye-admin-kurtarma-kodlari.txt'
    document.body.append(link)
    link.click()
    link.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
    setSavedCodes(true)
    setMessage('Kurtarma kodu dosyası indirildi. Dosyayı güvenli bir yerde saklayın.')
  }

  const copyRecoveryCodes = async () => {
    try {
      await navigator.clipboard.writeText(recoveryCodes.join('\n'))
      setSavedCodes(true)
      setMessage('Kurtarma kodları panoya kopyalandı. Güvenli bir yerde saklayın.')
    } catch {
      setMessage('Panoya erişilemedi. Kodları seçip kopyalayın veya dosya olarak indirin.')
    }
  }

  return <RouteShell title="Yönetici MFA kurulumu" zone="admin">
    <section className={`admin-mfa-setup admin-mfa-setup--${state}`} aria-labelledby="admin-mfa-title">
      <h2 id="admin-mfa-title">Yönetici hesabı için MFA kurulumu</h2>
      <p>Yönetim alanına geçmeden önce authenticator uygulamasını bağlayıp kurtarma kodlarınızı güvenli bir yere kaydedin.</p>
      {state === 'loading' ? <p role="status">Kurulum bilgileri hazırlanıyor…</p> : null}
      {state === 'error' ? <><p role="alert">{message}</p><button className="button button--secondary" type="button" onClick={() => window.location.reload()}>Tekrar dene</button></> : null}
      {enrollment && state !== 'error' && state !== 'codes' ?
        <div className="admin-mfa-setup__enrollment">
          <div>
            <h3>1. Authenticator uygulamasını bağlayın</h3>
            <p>Uygulamada yeni hesap ekleyip QR kodu tarayın. Tarayamıyorsanız aşağıdaki anahtarı uygulamaya elle girin.</p>
            {qrDataUrl ? <img className="admin-mfa-setup__qr" src={qrDataUrl} alt="Authenticator uygulaması için MFA kurulum QR kodu" /> : <p role="status">QR kodu hazırlanıyor; elle giriş anahtarını kullanabilirsiniz.</p>}
            <p><strong>Elle giriş anahtarı</strong></p>
            <code className="admin-mfa-setup__secret">{enrollment.sharedKey}</code>
          </div>
          <form className="auth-form" onSubmit={(event) => void verify(event)} noValidate>
            <h3>2. Kodu doğrulayın</h3>
            <TextField id="admin-mfa-verify-code" label="Authenticator kodu" value={code} onChange={setCode} autoComplete="one-time-code" required helpText="Uygulamadaki güncel altı haneli kodu yazın." />
            <button className="button button--primary" type="submit" disabled={state === 'submitting'}>MFA’yı etkinleştir</button>
          </form>
        </div>
        : null}
        {state === 'codes' ? <section className="admin-mfa-setup__recovery" aria-labelledby="recovery-codes-title">
          <h3 id="recovery-codes-title">3. Kurtarma kodlarını kaydedin</h3>
          <p>Her kod bir kez kullanılabilir. Bu ekranı kapatırsanız kodlar yeniden gösterilmez.</p>
          <ul aria-label="Tek kullanımlık kurtarma kodları">{recoveryCodes.map((recoveryCode, index) => <li key={`${recoveryCode}-${index}`}><code>{recoveryCode}</code></li>)}</ul>
          <div className="admin-mfa-setup__actions">
            <button className="button button--primary" type="button" onClick={() => void copyRecoveryCodes()}>Kodları kopyala</button>
            <button className="button button--secondary" type="button" onClick={downloadRecoveryCodes}>Dosya olarak indir</button>
          </div>
          <label className="admin-mfa-setup__ack"><input type="checkbox" checked={savedCodes} onChange={event => setSavedCodes(event.target.checked)} /> Kurtarma kodlarını güvenli bir yere kaydettim.</label>
          <p>MFA kuruldu. Bu kurulum oturumu ikinci doğrulamadan önce açıldığı için yönetim paneline geçmeden önce parola ve yeni authenticator kodunuzla tekrar giriş yapın.</p>
          <button className="button button--primary" type="button" disabled={!savedCodes} onClick={() => navigate('/giris?returnUrl=%2Fadmin')}>MFA ile yeniden giriş yap</button>
        </section> : null}
      {message && state !== 'error' ? <p className="form-status" role="status" aria-live="polite">{message}</p> : null}
      <p><InternalLink to="/giris">Giriş sayfasına dön</InternalLink></p>
    </section>
  </RouteShell>
}
