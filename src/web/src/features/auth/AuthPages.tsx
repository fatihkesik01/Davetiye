import { type FormEvent, type ReactNode, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'

import { DavetiyeApiClient, type AccountTypeInput } from '../../api/generated/client'
import { ErrorSummary, type ErrorSummaryEntry } from '../../components/ui/ErrorSummary'
import { InternalLink } from '../../components/ui/InternalLink'
import { RadioGroupField } from '../../components/ui/RadioGroupField'
import { TextField } from '../../components/ui/TextField'
import {
  consumeSensitiveLinkParameters,
  currentReturnUrlParam,
  navigate,
  resolveSafeReturnPath,
} from '../../routes/navigation'
import { authErrorMessage } from './authErrors'
import { useGoogleSignInAvailability } from './googleAuth'
import { useTranslation } from 'react-i18next'
import { useOptionalAccountPreferences } from '../preferences/preferencesContext'
import { invalidateSessionAccess } from '../session/sessionAccess'

type SubmitState = 'idle' | 'submitting' | 'success' | 'error'

function AuthCard({ children, intro }: { children: ReactNode; intro: string }) {
  return <section className="auth-card"><p className="auth-card__intro">{intro}</p>{children}</section>
}

function FormStatus({ state, message }: { state: SubmitState; message: string }) {
  if (!message) return null
  return <p className={`form-status form-status--${state}`} role={state === 'error' ? 'alert' : 'status'} aria-live={state === 'error' ? 'assertive' : 'polite'}>{message}</p>
}

function required(value: string, fieldId: string, message: string): ErrorSummaryEntry | null {
  return value.trim() ? null : { fieldId, message }
}

function useSensitiveLinkParameters() {
  const [parameters, setParameters] = useState({ userId: '', token: '' })
  const consumed = useRef(false)
  useLayoutEffect(() => {
    // StrictMode replays layout effects in development. Retain the first result in component state
    // so the second setup cannot lose credentials after the fragment was already scrubbed.
    if (consumed.current) return
    consumed.current = true
    setParameters(consumeSensitiveLinkParameters())
  }, [])
  return parameters
}

function emailError(value: string, fieldId: string): ErrorSummaryEntry | null {
  if (!value.trim()) return { fieldId, message: 'E-posta adresinizi yazın.' }
  if (!/^\S+@\S+\.\S+$/.test(value)) return { fieldId, message: 'Geçerli bir e-posta adresi yazın.' }
  return null
}

function errorsOnly(entries: Array<ErrorSummaryEntry | null>): ErrorSummaryEntry[] {
  return entries.filter((entry): entry is ErrorSummaryEntry => entry !== null)
}

export function LoginPage() {
  const { t } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [googleAccountType, setGoogleAccountType] = useState<AccountTypeInput | null>(null)
  const [googleServiceNoticeAcknowledged, setGoogleServiceNoticeAcknowledged] = useState(false)
  const [googleMarketingOptIn, setGoogleMarketingOptIn] = useState(false)
  const [googleCsrfToken, setGoogleCsrfToken] = useState('')
  const [googleCsrfState, setGoogleCsrfState] = useState<'loading' | 'ready' | 'error'>('loading')
  const [googleCsrfRetry, setGoogleCsrfRetry] = useState(0)
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const [requiresTwoFactor, setRequiresTwoFactor] = useState(false)
  const [isRecoveryCode, setIsRecoveryCode] = useState(false)
  const [twoFactorCode, setTwoFactorCode] = useState('')
  const requestedReturnPath = currentReturnUrlParam()
  const returnPath = resolveSafeReturnPath(requestedReturnPath, '/panel')
  const mfaReturnPath = resolveSafeReturnPath(requestedReturnPath, '/admin')
  const { availability: googleAvailability, retry: retryGoogleAvailability } = useGoogleSignInAvailability(api)

  useEffect(() => {
    if (googleAvailability !== 'enabled') return
    const controller = new AbortController()
    void api.getAntiforgeryToken(controller.signal).then(token => {
      if (controller.signal.aborted) return
      setGoogleCsrfToken(token)
      setGoogleCsrfState('ready')
    }).catch(() => {
      if (controller.signal.aborted) return
      setGoogleCsrfState('error')
    })
    return () => controller.abort()
  }, [api, googleAvailability, googleCsrfRetry])

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = errorsOnly([emailError(email, 'login-email'), required(password, 'login-password', 'Şifrenizi yazın.')])
    setErrors(nextErrors)
    setMessage('')
    if (nextErrors.length > 0) return
    setState('submitting')
    setMessage('Giriş yapılıyor…')
    try {
      const result = await api.login({ email: email.trim(), password })
      if (result.requiresTwoFactor) {
        setPassword('')
        setRequiresTwoFactor(true)
        setState('idle')
        setMessage('Authenticator uygulamanızdaki kodu veya tek kullanımlık kurtarma kodlarından birini girin.')
        return
      }
      invalidateSessionAccess()
      navigate(returnPath)
    } catch (error) {
      setState('error')
      setMessage(authErrorMessage(error, 'login'))
    }
  }

  const submitTwoFactor = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!twoFactorCode.trim()) {
      setErrors([{ fieldId: 'login-two-factor-code', message: isRecoveryCode ? 'Kurtarma kodunuzu yazın.' : 'Doğrulama kodunuzu yazın.' }])
      return
    }
    setErrors([])
    setState('submitting')
    setMessage('Ek doğrulama tamamlanıyor…')
    try {
      await api.completeAdminMfaLogin(twoFactorCode.trim(), isRecoveryCode)
      invalidateSessionAccess()
      navigate(mfaReturnPath)
    } catch (error) {
      setState('error')
      setMessage(authErrorMessage(error, 'mfa-login'))
    }
  }

  if (requiresTwoFactor) return (
    <AuthCard intro={t('authUi.login.mfaIntro')}>
      <ErrorSummary errors={errors} />
      <div className="auth-card__alternatives" role="group" aria-label={t('authUi.login.mfaMethod')}>
        <button className={`button ${isRecoveryCode ? 'button--secondary' : 'button--primary'}`} type="button" aria-pressed={!isRecoveryCode} onClick={() => { setIsRecoveryCode(false); setTwoFactorCode(''); setErrors([]) }}>{t('authUi.login.authenticator')}</button>
        <button className={`button ${isRecoveryCode ? 'button--primary' : 'button--secondary'}`} type="button" aria-pressed={isRecoveryCode} onClick={() => { setIsRecoveryCode(true); setTwoFactorCode(''); setErrors([]) }}>{t('authUi.login.recovery')}</button>
      </div>
      <form className="auth-form" onSubmit={(event) => void submitTwoFactor(event)} noValidate>
        <TextField id="login-two-factor-code" label={isRecoveryCode ? t('authUi.login.recoveryCode') : t('authUi.login.verificationCode')} value={twoFactorCode} onChange={setTwoFactorCode} autoComplete="one-time-code" required helpText={isRecoveryCode ? t('authUi.login.recoveryHelp') : t('authUi.login.verificationHelp')} errorText={errors.find(error => error.fieldId === 'login-two-factor-code')?.message} />
        <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{t('authUi.login.verifyContinue')}</button>
      </form>
      <FormStatus state={state} message={message} />
      <button className="button button--secondary" type="button" onClick={() => { setRequiresTwoFactor(false); setTwoFactorCode(''); setMessage(''); setState('idle') }}>{t('authUi.login.returnToLogin')}</button>
    </AuthCard>
  )

  return (
    <AuthCard intro={t('authUi.login.intro')}>
      <ErrorSummary errors={errors} />
      <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
        <TextField id="login-email" label={t('authUi.login.email')} type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'login-email')?.message} />
        <TextField id="login-password" label={t('authUi.login.password')} type="password" value={password} onChange={setPassword} autoComplete="current-password" required errorText={errors.find(error => error.fieldId === 'login-password')?.message} />
        <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{t('authUi.login.submit')}</button>
      </form>
      <FormStatus state={state} message={message} />
      <div className="auth-card__alternatives">
        {googleAvailability === 'enabled' ? <form className="auth-form auth-form--google" method="post" encType="application/x-www-form-urlencoded" action="/api/v1/auth/google/challenge" aria-label={t('authUi.login.googleContinue')}>
        <RadioGroupField
          id="google-account-type"
          legend={t('authUi.login.googleAccountType')}
          name="googleAccountType"
          value={googleAccountType}
          onChange={(value) => setGoogleAccountType(value as AccountTypeInput)}
          required
          helpText="Yeni hesapta bu seçim kalıcıdır; mevcut Google bağlantılı hesabınız varsa kayıtlı hesap türünüz korunur."
          options={[
            { value: 'Individual', label: t('authUi.login.individual') },
            { value: 'Organization', label: t('authUi.login.organization') },
          ]}
        />
        <div className="consent-choice">
          <label htmlFor="login-google-service-notice">
            <input id="login-google-service-notice" type="checkbox" checked={googleServiceNoticeAcknowledged} onChange={(event) => setGoogleServiceNoticeAcknowledged(event.target.checked)} required aria-required="true" />
            <span><strong>Hizmet bildirimini okudum.</strong></span>
          </label>
          <p>Yeni bir Google hesabı oluşturmak için bu onay gereklidir. Mevcut Google hesabınızla giriş yaparken onay yalnız bu akışta kaydedilmez; gerekiyorsa panele girişten sonra bir kez daha istenir. <InternalLink to="/gizlilik">Hizmet bildirimi ve gizlilik bilgisi</InternalLink> ile <InternalLink to="/kullanim-kosullari">kullanım koşulları</InternalLink> taslaktır; Phase 11 hukuk incelemesi bekliyor.</p>
          <label htmlFor="login-google-marketing-opt-in">
            <input id="login-google-marketing-opt-in" type="checkbox" checked={googleMarketingOptIn} onChange={(event) => setGoogleMarketingOptIn(event.target.checked)} />
            <span>Yeni oluşturulacak Google hesabı için ürün haberleri ve kampanyalar hakkında e-posta almak istiyorum. Bu tercih isteğe bağlıdır ve daha sonra değiştirilebilir.</span>
          </label>
        </div>
        <input type="hidden" name="returnUrl" value={returnPath} />
        <input type="hidden" name="accountType" value={googleAccountType ?? ''} />
        <input type="hidden" name="serviceNoticeAcknowledged" value={googleServiceNoticeAcknowledged ? 'true' : 'false'} />
        <input type="hidden" name="marketingOptIn" value={googleMarketingOptIn ? 'true' : 'false'} />
        <input type="hidden" name="__RequestVerificationToken" value={googleCsrfToken} />
        <button className="button button--google" type="submit" disabled={!googleAccountType || !googleServiceNoticeAcknowledged || googleCsrfState !== 'ready'}>{t('authUi.login.googleContinue')}</button>
        {googleCsrfState === 'loading' ? <p role="status">{t('authUi.login.secureGooglePreparing')}</p> : null}
        {googleCsrfState === 'error' ? <p role="alert">{t('authUi.login.googleRetry')} <button className="button button--secondary" type="button" onClick={() => { setGoogleCsrfState('loading'); setGoogleCsrfToken(''); setGoogleCsrfRetry(value => value + 1) }}>{t('common.loading')}</button></p> : null}
        {!googleServiceNoticeAcknowledged ? <p className="form-field__help">{t('authUi.login.consentRequired')}</p> : null}
        </form> :
          <div className="auth-card__security-note" role="status">{googleAvailability === 'checking'
            ? 'Google ile giriş kullanılabilirliği kontrol ediliyor…'
            : googleAvailability === 'disabled'
              ? 'Google ile giriş bu ortamda henüz kullanıma açık değil. E-posta ve şifrenizle devam edebilirsiniz.'
              : <>Google ile girişin kullanılabilirliği şu anda doğrulanamadı. E-posta ve şifrenizle devam edin. <button className="button button--secondary" type="button" onClick={retryGoogleAvailability}>Tekrar dene</button></>}</div>}
        <InternalLink to="/giris/sifremi-unuttum">{t('authUi.login.forgot')}</InternalLink>
        <p>{t('authUi.login.noAccount')} <InternalLink to={`/giris/kayit?returnUrl=${encodeURIComponent(returnPath)}`}>{t('authUi.login.createAccount')}</InternalLink>.</p>
      </div>
    </AuthCard>
  )
}

export function RegisterPage() {
  const { t } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [passwordAgain, setPasswordAgain] = useState('')
  const [accountType, setAccountType] = useState<AccountTypeInput | null>(null)
  const [serviceNoticeAcknowledged, setServiceNoticeAcknowledged] = useState(false)
  const [marketingOptIn, setMarketingOptIn] = useState(false)
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const returnPath = resolveSafeReturnPath(currentReturnUrlParam(), '/panel')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = errorsOnly([
      required(displayName, 'register-name', 'Görünen adınızı yazın.'), emailError(email, 'register-email'),
      accountType ? null : { fieldId: 'register-account-type-Individual', message: 'Hesap türünü seçin.' },
      required(password, 'register-password', 'Şifre oluşturun.'),
      password === passwordAgain ? null : { fieldId: 'register-password-again', message: 'Şifreler birbiriyle aynı olmalı.' },
      serviceNoticeAcknowledged ? null : { fieldId: 'register-service-notice', message: 'Hesap oluşturmak için hizmet bildirimini onaylayın.' },
    ])
    setErrors(nextErrors)
    setMessage('')
    if (nextErrors.length > 0 || !accountType || !serviceNoticeAcknowledged) return
    setState('submitting')
    setMessage('Hesabınız oluşturuluyor…')
    try {
      await api.register({ displayName: displayName.trim(), email: email.trim(), password, accountType, serviceNoticeAcknowledged: true, marketingOptIn })
      setState('success')
      setMessage('Hesabınız oluşturuldu. E-posta adresinize gönderilen bağlantıyla hesabınızı doğrulayın.')
    } catch (error) {
      setState('error')
      setMessage(authErrorMessage(error, 'register'))
    }
  }

  if (state === 'success') {
    return <AuthCard intro={t('authUi.register.successIntro')}><FormStatus state={state} message={message} /><p>{t('authUi.register.verifyEmail')} <InternalLink to={`/giris?returnUrl=${encodeURIComponent(returnPath)}`}>{t('authUi.register.loginAfter')}</InternalLink>.</p></AuthCard>
  }

  return (
    <AuthCard intro={t('authUi.register.intro')}>
      <ErrorSummary errors={errors} />
      <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
        <TextField id="register-name" label={t('authUi.register.name')} value={displayName} onChange={setDisplayName} autoComplete="name" required errorText={errors.find(error => error.fieldId === 'register-name')?.message} />
        <TextField id="register-email" label={t('authUi.register.email')} type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'register-email')?.message} />
        <RadioGroupField id="register-account-type" legend={t('authUi.register.accountType')} name="accountType" value={accountType} onChange={(value) => setAccountType(value as AccountTypeInput)} required helpText={t('authUi.register.accountTypeHelp')} errorText={errors.find(error => error.fieldId.startsWith('register-account-type'))?.message} options={[
          { value: 'Individual', label: t('authUi.register.individual'), description: t('authUi.register.individualHelp') },
          { value: 'Organization', label: t('authUi.register.organization'), description: t('authUi.register.organizationHelp') },
        ]} />
        <TextField id="register-password" label={t('authUi.register.password')} type="password" value={password} onChange={setPassword} autoComplete="new-password" required helpText={t('authUi.register.passwordHelp')} errorText={errors.find(error => error.fieldId === 'register-password')?.message} />
        <TextField id="register-password-again" label={t('authUi.register.passwordAgain')} type="password" value={passwordAgain} onChange={setPasswordAgain} autoComplete="new-password" required errorText={errors.find(error => error.fieldId === 'register-password-again')?.message} />
        <div className="consent-choice">
          <label htmlFor="register-service-notice">
            <input id="register-service-notice" type="checkbox" checked={serviceNoticeAcknowledged} onChange={(event) => setServiceNoticeAcknowledged(event.target.checked)} aria-invalid={errors.some(error => error.fieldId === 'register-service-notice') || undefined} aria-describedby={errors.some(error => error.fieldId === 'register-service-notice') ? 'register-service-notice-error' : 'register-service-notice-help'} />
            <span><strong>{t('authUi.register.serviceNotice')}</strong></span>
          </label>
          <p id="register-service-notice-help" className="form-field__help">Bu onay hesap oluşturmak için gereklidir. <InternalLink to="/gizlilik">Hizmet bildirimi ve gizlilik bilgisi</InternalLink> ile <InternalLink to="/kullanim-kosullari">kullanım koşulları</InternalLink> taslaktır; Phase 11 hukuk incelemesi bekliyor.</p>
          {errors.find(error => error.fieldId === 'register-service-notice') ? <p id="register-service-notice-error" className="form-field__error">{errors.find(error => error.fieldId === 'register-service-notice')?.message}</p> : null}
          <label htmlFor="register-marketing-opt-in">
            <input id="register-marketing-opt-in" type="checkbox" checked={marketingOptIn} onChange={(event) => setMarketingOptIn(event.target.checked)} />
            <span>{t('authUi.register.marketing')}</span>
          </label>
        </div>
        <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{t('authUi.register.create')}</button>
      </form>
      <FormStatus state={state} message={message} />
      <p>{t('authUi.register.existing')} <InternalLink to={`/giris?returnUrl=${encodeURIComponent(returnPath)}`}>{t('authUi.register.login')}</InternalLink>.</p>
    </AuthCard>
  )
}

export function ConfirmEmailPage() {
  const { t } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const { userId, token } = useSensitiveLinkParameters()
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const hasRequest = Boolean(userId && token)

  const confirm = async () => {
    setState('submitting'); setMessage(t('authUi.common.verifyingEmail'))
    try {
      await api.confirmEmail({ userId, token })
      setState('success'); setMessage(t('authUi.confirm.success'))
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'confirm-email'))
    }
  }

  return <AuthCard intro={t('authUi.confirm.intro')}>
    {!hasRequest ? <p role="alert">{t('authUi.confirm.missing')}</p> : null}
    {hasRequest && state !== 'success' ? <button className="button button--primary" type="button" onClick={() => void confirm()} disabled={state === 'submitting'}>{t('authUi.confirm.submit')}</button> : null}
    <FormStatus state={state} message={message} />
    {state === 'success' ? <InternalLink to="/giris">{t('authUi.confirm.goToLogin')}</InternalLink> : null}
  </AuthCard>
}

export function ForgotPasswordPage() {
  const { t } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [email, setEmail] = useState('')
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = errorsOnly([emailError(email, 'forgot-email')]); setErrors(nextErrors)
    if (nextErrors.length > 0) return
    setState('submitting'); setMessage(t('authUi.forgot.sending'))
    try {
      await api.requestPasswordReset({ email: email.trim() })
      setState('success'); setMessage(t('authUi.forgot.sent'))
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'request-reset'))
    }
  }

  return <AuthCard intro={t('authUi.forgot.intro')}>
    <ErrorSummary errors={errors} />
    <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
      <TextField id="forgot-email" label={t('authUi.forgot.email')} type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'forgot-email')?.message} />
      <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{t('authUi.forgot.submit')}</button>
    </form>
    <FormStatus state={state} message={message} /><InternalLink to="/giris">{t('authUi.forgot.back')}</InternalLink>
  </AuthCard>
}

export function ResetPasswordPage() {
  const { t } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const { userId, token } = useSensitiveLinkParameters()
  const [password, setPassword] = useState('')
  const [passwordAgain, setPasswordAgain] = useState('')
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const hasRequest = Boolean(userId && token)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = errorsOnly([
      required(password, 'reset-password', 'Yeni şifrenizi yazın.'),
      password === passwordAgain ? null : { fieldId: 'reset-password-again', message: 'Şifreler birbiriyle aynı olmalı.' },
    ])
    setErrors(nextErrors)
    if (nextErrors.length > 0 || !hasRequest) return
    setState('submitting'); setMessage(t('authUi.reset.saving'))
    try {
      await api.resetPassword({ userId, token, newPassword: password })
      setState('success'); setMessage(t('authUi.reset.saved'))
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'reset-password'))
    }
  }

  return <AuthCard intro={t('authUi.reset.intro')}>
    {!hasRequest ? <p role="alert">{t('authUi.reset.missing')}</p> : null}
    {hasRequest && state !== 'success' ? <><ErrorSummary errors={errors} /><form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
      <TextField id="reset-password" label={t('authUi.reset.password')} type="password" value={password} onChange={setPassword} autoComplete="new-password" required helpText={t('authUi.reset.passwordHelp')} errorText={errors.find(error => error.fieldId === 'reset-password')?.message} />
      <TextField id="reset-password-again" label={t('authUi.reset.passwordAgain')} type="password" value={passwordAgain} onChange={setPasswordAgain} autoComplete="new-password" required errorText={errors.find(error => error.fieldId === 'reset-password-again')?.message} />
      <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{t('authUi.reset.submit')}</button>
    </form></> : null}
    <FormStatus state={state} message={message} />{state === 'success' ? <InternalLink to="/giris">{t('authUi.reset.goToLogin')}</InternalLink> : null}
  </AuthCard>
}

export function GoogleLinkPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const returnPath = resolveSafeReturnPath(currentReturnUrlParam(), '/panel')
  const { availability: googleAvailability, retry: retryGoogleAvailability } = useGoogleSignInAvailability(api)

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = errorsOnly([emailError(email, 'google-link-email'), required(password, 'google-link-password', 'Mevcut hesap şifrenizi yazın.')])
    setErrors(nextErrors)
    if (nextErrors.length > 0) return
    setState('submitting'); setMessage('Hesap sahipliği doğrulanıyor…')
    try {
      const login = await api.login({ email: email.trim(), password })
      if (login.requiresTwoFactor) {
        setState('error'); setMessage('Bu hesap Google ile girişe bağlanamaz.'); return
      }
      const csrfToken = await api.getAntiforgeryToken()
      await api.confirmGoogleLink({ password }, csrfToken)
      invalidateSessionAccess(); setState('success'); setMessage('Google hesabınız mevcut hesabınıza bağlandı.')
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'google-link'))
    }
  }

  if (googleAvailability !== 'enabled') return <AuthCard intro="Google ile giriş bağlantısı şu anda kullanılamıyor."><div className="auth-card__security-note" role="status">{googleAvailability === 'checking'
    ? 'Google ile giriş kullanılabilirliği kontrol ediliyor…'
    : googleAvailability === 'disabled'
      ? 'Google hesabı bağlamak için önce Google ile girişin güvenli domain ve HTTPS yapılandırmasıyla etkinleştirilmesi gerekir.'
      : <>Google ile girişin kullanılabilirliği şu anda doğrulanamadı. E-posta ve şifrenizle giriş yapın. <button className="button button--secondary" type="button" onClick={retryGoogleAvailability}>Tekrar dene</button></>}</div><InternalLink to={`/giris?returnUrl=${encodeURIComponent(returnPath)}`}>Girişe dön</InternalLink></AuthCard>
  if (state === 'success') return <AuthCard intro="Google ile giriş bağlantısı tamamlandı."><FormStatus state={state} message={message} /><button className="button button--primary" type="button" onClick={() => navigate(returnPath)}>Panele devam et</button></AuthCard>
  return <AuthCard intro="Bu e-posta adresiyle zaten bir hesap var. Google hesabını bağlamak için mevcut hesabınızın bilgileriyle sahipliği doğrulayın.">
    <p className="auth-card__security-note">Google hesabı yalnızca doğrulanmış e-posta adresi mevcut hesabınızla eşleşirse bağlanır.</p>
    <ErrorSummary errors={errors} />
    <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
      <TextField id="google-link-email" label="Mevcut hesap e-postası" type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'google-link-email')?.message} />
      <TextField id="google-link-password" label="Mevcut hesap şifresi" type="password" value={password} onChange={setPassword} autoComplete="current-password" required errorText={errors.find(error => error.fieldId === 'google-link-password')?.message} />
      <button className="button button--primary" type="submit" disabled={state === 'submitting'}>Google hesabını bağla</button>
    </form>
    <FormStatus state={state} message={message} /><InternalLink to="/giris">Vazgeç ve girişe dön</InternalLink>
  </AuthCard>
}

export function LogoutButton() {
  const { t } = useTranslation()
  const preferences = useOptionalAccountPreferences()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const logout = async () => {
    setState('submitting'); setMessage(t('auth.logoutInProgress'))
    try {
      const csrfToken = await api.getAntiforgeryToken(); await api.logout(csrfToken); preferences?.reset(); invalidateSessionAccess(); navigate('/giris')
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'logout'))
    }
  }
  return <div className="session-actions"><button className="button button--secondary" type="button" onClick={() => void logout()} disabled={state === 'submitting'}>{t('common.secureLogout')}</button><FormStatus state={state} message={message} /></div>
}
