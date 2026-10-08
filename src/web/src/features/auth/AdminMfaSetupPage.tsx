import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import QRCode from 'qrcode'

import { DavetiyeApiClient, type AdminMfaEnrollment } from '../../api/generated/client'
import { InternalLink } from '../../components/ui/InternalLink'
import { TextField } from '../../components/ui/TextField'
import { navigate } from '../../routes/navigation'
import { RouteShell } from '../../routes/RouteShell'
import { authErrorMessage } from './authErrors'

type PageState = 'loading' | 'ready' | 'submitting' | 'codes' | 'error'

export function AdminMfaSetupPage() {
  const { t } = useTranslation()
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
      setMessage(t('creatorUi.superAdminMfa.invalidCode'))
      return
    }
    setState('submitting')
    setMessage(t('creatorUi.superAdminMfa.verifying'))
    try {
      const result = await api.verifyAdminMfa(code.trim(), csrfToken)
      setRecoveryCodes(result.recoveryCodes)
      setSavedCodes(false)
      setEnrollment(null)
      setQrDataUrl('')
      setState('codes')
      setMessage(t('creatorUi.superAdminMfa.enabledMessage'))
    } catch (error) {
      setState('ready')
      setMessage(authErrorMessage(error, 'mfa-setup'))
    }
  }

  const downloadRecoveryCodes = () => {
    const blob = new Blob([`${t('creatorUi.superAdminMfa.downloadHeader')}\n\n${recoveryCodes.join('\n')}\n`], { type: 'text/plain;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = 'davetiye-admin-kurtarma-kodlari.txt'
    document.body.append(link)
    link.click()
    link.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
    setSavedCodes(true)
    setMessage(t('creatorUi.superAdminMfa.downloaded'))
  }

  const copyRecoveryCodes = async () => {
    try {
      await navigator.clipboard.writeText(recoveryCodes.join('\n'))
      setSavedCodes(true)
      setMessage(t('creatorUi.superAdminMfa.copied'))
    } catch {
      setMessage(t('creatorUi.superAdminMfa.copyFailed'))
    }
  }

  return <RouteShell title={t('creatorUi.superAdminMfa.title')} zone="admin">
    <section className={`admin-mfa-setup admin-mfa-setup--${state}`} aria-labelledby="admin-mfa-title">
      <h2 id="admin-mfa-title">{t('creatorUi.superAdminMfa.heading')}</h2>
      <p>{t('creatorUi.superAdminMfa.intro')}</p>
      {state === 'loading' ? <p role="status">{t('creatorUi.superAdminMfa.loading')}</p> : null}
      {state === 'error' ? <><p role="alert">{message}</p><button className="button button--secondary" type="button" onClick={() => window.location.reload()}>{t('creatorUi.superAdminMfa.retry')}</button></> : null}
      {enrollment && state !== 'error' && state !== 'codes' ?
        <div className="admin-mfa-setup__enrollment">
          <div>
            <h3>{t('creatorUi.superAdminMfa.step1')}</h3>
            <p>{t('creatorUi.superAdminMfa.step1Help')}</p>
            {qrDataUrl ? <img className="admin-mfa-setup__qr" src={qrDataUrl} alt={t('creatorUi.superAdminMfa.qrAlt')} /> : <p role="status">{t('creatorUi.superAdminMfa.qrLoading')}</p>}
            <p><strong>{t('creatorUi.superAdminMfa.manualKey')}</strong></p>
            <code className="admin-mfa-setup__secret">{enrollment.sharedKey}</code>
          </div>
          <form className="auth-form" onSubmit={(event) => void verify(event)} noValidate>
            <h3>{t('creatorUi.superAdminMfa.step2')}</h3>
            <TextField id="admin-mfa-verify-code" label={t('creatorUi.superAdminMfa.codeLabel')} value={code} onChange={setCode} autoComplete="one-time-code" required helpText={t('creatorUi.superAdminMfa.codeHelp')} />
            <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{t('creatorUi.superAdminMfa.enable')}</button>
          </form>
        </div>
        : null}
        {state === 'codes' ? <section className="admin-mfa-setup__recovery" aria-labelledby="recovery-codes-title">
          <h3 id="recovery-codes-title">{t('creatorUi.superAdminMfa.saveCodes')}</h3>
          <p>{t('creatorUi.superAdminMfa.codeHelpText')}</p>
          <ul aria-label={t('creatorUi.superAdminMfa.recoveryCodes')}>{recoveryCodes.map((recoveryCode, index) => <li key={`${recoveryCode}-${index}`}><code>{recoveryCode}</code></li>)}</ul>
          <div className="admin-mfa-setup__actions">
            <button className="button button--primary" type="button" onClick={() => void copyRecoveryCodes()}>{t('creatorUi.superAdminMfa.copyCodes')}</button>
            <button className="button button--secondary" type="button" onClick={downloadRecoveryCodes}>{t('creatorUi.superAdminMfa.downloadCodes')}</button>
          </div>
          <label className="admin-mfa-setup__ack"><input type="checkbox" checked={savedCodes} onChange={event => setSavedCodes(event.target.checked)} /> {t('creatorUi.superAdminMfa.savedAck')}</label>
          <p>{t('creatorUi.superAdminMfa.reauth')}</p>
          <button className="button button--primary" type="button" disabled={!savedCodes} onClick={() => navigate('/giris?returnUrl=%2Fadmin')}>{t('creatorUi.superAdminMfa.signInAgain')}</button>
        </section> : null}
      {message && state !== 'error' ? <p className="form-status" role="status" aria-live="polite">{message}</p> : null}
      <p><InternalLink to="/giris">{t('creatorUi.superAdminMfa.back')}</InternalLink></p>
    </section>
  </RouteShell>
}
