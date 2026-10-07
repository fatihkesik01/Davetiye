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
      navigate(mfaReturnPath)
    } catch (error) {
      setState('error')
      setMessage(authErrorMessage(error, 'mfa-login'))
    }
  }

  if (requiresTwoFactor) return (
    <AuthCard intro="Yönetici hesabınız için ikinci doğrulama adımını tamamlayın.">
      <ErrorSummary errors={errors} />
      <div className="auth-card__alternatives" role="group" aria-label="Doğrulama yöntemi">
        <button className={`button ${isRecoveryCode ? 'button--secondary' : 'button--primary'}`} type="button" aria-pressed={!isRecoveryCode} onClick={() => { setIsRecoveryCode(false); setTwoFactorCode(''); setErrors([]) }}>Authenticator kodu</button>
        <button className={`button ${isRecoveryCode ? 'button--primary' : 'button--secondary'}`} type="button" aria-pressed={isRecoveryCode} onClick={() => { setIsRecoveryCode(true); setTwoFactorCode(''); setErrors([]) }}>Kurtarma kodu</button>
      </div>
      <form className="auth-form" onSubmit={(event) => void submitTwoFactor(event)} noValidate>
        <TextField id="login-two-factor-code" label={isRecoveryCode ? 'Tek kullanımlık kurtarma kodu' : 'Authenticator doğrulama kodu'} value={twoFactorCode} onChange={setTwoFactorCode} autoComplete="one-time-code" required helpText={isRecoveryCode ? 'Her kurtarma kodu yalnızca bir kez kullanılabilir.' : 'Authenticator uygulamanızdaki güncel kodu yazın.'} errorText={errors.find(error => error.fieldId === 'login-two-factor-code')?.message} />
        <button className="button button--primary" type="submit" disabled={state === 'submitting'}>Doğrula ve devam et</button>
      </form>
      <FormStatus state={state} message={message} />
      <button className="button button--secondary" type="button" onClick={() => { setRequiresTwoFactor(false); setTwoFactorCode(''); setMessage(''); setState('idle') }}>Girişe dön</button>
    </AuthCard>
  )

  return (
    <AuthCard intro="Davetiye panelinize güvenli oturum çereziyle erişin.">
      <ErrorSummary errors={errors} />
      <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
        <TextField id="login-email" label="E-posta" type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'login-email')?.message} />
        <TextField id="login-password" label="Şifre" type="password" value={password} onChange={setPassword} autoComplete="current-password" required errorText={errors.find(error => error.fieldId === 'login-password')?.message} />
        <button className="button button--primary" type="submit" disabled={state === 'submitting'}>Giriş yap</button>
      </form>
      <FormStatus state={state} message={message} />
      <div className="auth-card__alternatives">
        {googleAvailability === 'enabled' ? <form className="auth-form auth-form--google" method="post" encType="application/x-www-form-urlencoded" action="/api/v1/auth/google/challenge" aria-label="Google ile devam et">
        <RadioGroupField
          id="google-account-type"
          legend="Google ile devam etmek için hesap türü"
          name="googleAccountType"
          value={googleAccountType}
          onChange={(value) => setGoogleAccountType(value as AccountTypeInput)}
          required
          helpText="Yeni hesapta bu seçim kalıcıdır; mevcut Google bağlantılı hesabınız varsa kayıtlı hesap türünüz korunur."
          options={[
            { value: 'Individual', label: 'Bireysel' },
            { value: 'Organization', label: 'Organizasyon' },
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
        <button className="button button--google" type="submit" disabled={!googleAccountType || !googleServiceNoticeAcknowledged || googleCsrfState !== 'ready'}>Google ile devam et</button>
        {googleCsrfState === 'loading' ? <p role="status">Güvenli bağlantı hazırlanıyor…</p> : null}
        {googleCsrfState === 'error' ? <p role="alert">Google ile güvenli bağlantı hazırlanamadı. <button className="button button--secondary" type="button" onClick={() => { setGoogleCsrfState('loading'); setGoogleCsrfToken(''); setGoogleCsrfRetry(value => value + 1) }}>Tekrar dene</button></p> : null}
        {!googleServiceNoticeAcknowledged ? <p className="form-field__help">Devam etmek için hizmet bildirimini onaylayın.</p> : null}
        </form> :
          <div className="auth-card__security-note" role="status">{googleAvailability === 'checking'
            ? 'Google ile giriş kullanılabilirliği kontrol ediliyor…'
            : googleAvailability === 'disabled'
              ? 'Google ile giriş bu ortamda henüz kullanıma açık değil. E-posta ve şifrenizle devam edebilirsiniz.'
              : <>Google ile girişin kullanılabilirliği şu anda doğrulanamadı. E-posta ve şifrenizle devam edin. <button className="button button--secondary" type="button" onClick={retryGoogleAvailability}>Tekrar dene</button></>}</div>}
        <InternalLink to="/giris/sifremi-unuttum">Şifremi unuttum</InternalLink>
        <p>Hesabınız yok mu? <InternalLink to={`/giris/kayit?returnUrl=${encodeURIComponent(returnPath)}`}>Hesap oluşturun</InternalLink>.</p>
      </div>
    </AuthCard>
  )
}

export function RegisterPage() {
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
    return <AuthCard intro="Hesap oluşturma isteğiniz alındı."><FormStatus state={state} message={message} /><p>E-postayı doğruladıktan sonra <InternalLink to={`/giris?returnUrl=${encodeURIComponent(returnPath)}`}>giriş yapabilirsiniz</InternalLink>.</p></AuthCard>
  }

  return (
    <AuthCard intro="Bireysel veya tek sahibi olduğunuz organizasyon hesabınızı oluşturun.">
      <ErrorSummary errors={errors} />
      <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
        <TextField id="register-name" label="Görünen ad" value={displayName} onChange={setDisplayName} autoComplete="name" required errorText={errors.find(error => error.fieldId === 'register-name')?.message} />
        <TextField id="register-email" label="E-posta" type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'register-email')?.message} />
        <RadioGroupField id="register-account-type" legend="Hesap türü" name="accountType" value={accountType} onChange={(value) => setAccountType(value as AccountTypeInput)} required helpText="Bu seçim kayıt sonrasında değiştirilemez. Organizasyon hesabı MVP’de tek kişi tarafından yönetilir." errorText={errors.find(error => error.fieldId.startsWith('register-account-type'))?.message} options={[
          { value: 'Individual', label: 'Bireysel', description: 'Kendi etkinlikleriniz ve davetiyeleriniz için.' },
          { value: 'Organization', label: 'Organizasyon', description: 'Müşteri davetiyelerini tek hesap sahibi olarak yönetmek için.' },
        ]} />
        <TextField id="register-password" label="Şifre" type="password" value={password} onChange={setPassword} autoComplete="new-password" required helpText="Uzun, benzersiz; büyük-küçük harf ve rakam içeren bir şifre kullanın." errorText={errors.find(error => error.fieldId === 'register-password')?.message} />
        <TextField id="register-password-again" label="Şifreyi tekrar yazın" type="password" value={passwordAgain} onChange={setPasswordAgain} autoComplete="new-password" required errorText={errors.find(error => error.fieldId === 'register-password-again')?.message} />
        <div className="consent-choice">
          <label htmlFor="register-service-notice">
            <input id="register-service-notice" type="checkbox" checked={serviceNoticeAcknowledged} onChange={(event) => setServiceNoticeAcknowledged(event.target.checked)} aria-invalid={errors.some(error => error.fieldId === 'register-service-notice') || undefined} aria-describedby={errors.some(error => error.fieldId === 'register-service-notice') ? 'register-service-notice-error' : 'register-service-notice-help'} />
            <span><strong>Hizmet bildirimini okudum.</strong></span>
          </label>
          <p id="register-service-notice-help" className="form-field__help">Bu onay hesap oluşturmak için gereklidir. <InternalLink to="/gizlilik">Hizmet bildirimi ve gizlilik bilgisi</InternalLink> ile <InternalLink to="/kullanim-kosullari">kullanım koşulları</InternalLink> taslaktır; Phase 11 hukuk incelemesi bekliyor.</p>
          {errors.find(error => error.fieldId === 'register-service-notice') ? <p id="register-service-notice-error" className="form-field__error">{errors.find(error => error.fieldId === 'register-service-notice')?.message}</p> : null}
          <label htmlFor="register-marketing-opt-in">
            <input id="register-marketing-opt-in" type="checkbox" checked={marketingOptIn} onChange={(event) => setMarketingOptIn(event.target.checked)} />
            <span>Ürün haberleri ve kampanyalar hakkında e-posta almak istiyorum. Bu tercih isteğe bağlıdır ve daha sonra değiştirilebilir.</span>
          </label>
        </div>
        <button className="button button--primary" type="submit" disabled={state === 'submitting'}>Hesap oluştur</button>
      </form>
      <FormStatus state={state} message={message} />
      <p>Zaten hesabınız var mı? <InternalLink to={`/giris?returnUrl=${encodeURIComponent(returnPath)}`}>Giriş yapın</InternalLink>.</p>
    </AuthCard>
  )
}

export function ConfirmEmailPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const { userId, token } = useSensitiveLinkParameters()
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const hasRequest = Boolean(userId && token)

  const confirm = async () => {
    setState('submitting'); setMessage('E-posta adresiniz doğrulanıyor…')
    try {
      await api.confirmEmail({ userId, token })
      setState('success'); setMessage('E-posta adresiniz doğrulandı. Şimdi giriş yapabilirsiniz.')
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'confirm-email'))
    }
  }

  return <AuthCard intro="Hesabınızı etkinleştirmek için e-posta doğrulamasını tamamlayın.">
    {!hasRequest ? <p role="alert">Doğrulama bağlantısı eksik veya geçersiz. Kayıt sırasında gönderilen e-postadaki bağlantıyı açın.</p> : null}
    {hasRequest && state !== 'success' ? <button className="button button--primary" type="button" onClick={() => void confirm()} disabled={state === 'submitting'}>E-postamı doğrula</button> : null}
    <FormStatus state={state} message={message} />
    {state === 'success' ? <InternalLink to="/giris">Giriş sayfasına git</InternalLink> : null}
  </AuthCard>
}

export function ForgotPasswordPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [email, setEmail] = useState('')
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextErrors = errorsOnly([emailError(email, 'forgot-email')]); setErrors(nextErrors)
    if (nextErrors.length > 0) return
    setState('submitting'); setMessage('İstek gönderiliyor…')
    try {
      await api.requestPasswordReset({ email: email.trim() })
      setState('success'); setMessage('Bu adresle eşleşen bir hesap varsa şifre sıfırlama bağlantısı gönderildi.')
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'request-reset'))
    }
  }

  return <AuthCard intro="Şifre sıfırlama bağlantısı istemek için e-posta adresinizi yazın.">
    <ErrorSummary errors={errors} />
    <form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
      <TextField id="forgot-email" label="E-posta" type="email" value={email} onChange={setEmail} autoComplete="email" required errorText={errors.find(error => error.fieldId === 'forgot-email')?.message} />
      <button className="button button--primary" type="submit" disabled={state === 'submitting'}>Sıfırlama bağlantısı gönder</button>
    </form>
    <FormStatus state={state} message={message} /><InternalLink to="/giris">Girişe dön</InternalLink>
  </AuthCard>
}

export function ResetPasswordPage() {
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
    setState('submitting'); setMessage('Şifreniz yenileniyor…')
    try {
      await api.resetPassword({ userId, token, newPassword: password })
      setState('success'); setMessage('Şifreniz yenilendi. Diğer oturumlarınız güvenlik için kapatıldı; yeniden giriş yapın.')
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'reset-password'))
    }
  }

  return <AuthCard intro="Hesabınız için yeni ve güçlü bir şifre belirleyin.">
    {!hasRequest ? <p role="alert">Şifre sıfırlama bağlantısı eksik veya geçersiz.</p> : null}
    {hasRequest && state !== 'success' ? <><ErrorSummary errors={errors} /><form className="auth-form" onSubmit={(event) => void submit(event)} noValidate>
      <TextField id="reset-password" label="Yeni şifre" type="password" value={password} onChange={setPassword} autoComplete="new-password" required helpText="Uzun, benzersiz; büyük-küçük harf ve rakam içeren bir şifre kullanın." errorText={errors.find(error => error.fieldId === 'reset-password')?.message} />
      <TextField id="reset-password-again" label="Yeni şifreyi tekrar yazın" type="password" value={passwordAgain} onChange={setPasswordAgain} autoComplete="new-password" required errorText={errors.find(error => error.fieldId === 'reset-password-again')?.message} />
      <button className="button button--primary" type="submit" disabled={state === 'submitting'}>Şifreyi yenile</button>
    </form></> : null}
    <FormStatus state={state} message={message} />{state === 'success' ? <InternalLink to="/giris">Giriş sayfasına git</InternalLink> : null}
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
      setState('success'); setMessage('Google hesabınız mevcut hesabınıza bağlandı.')
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
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [state, setState] = useState<SubmitState>('idle')
  const [message, setMessage] = useState('')
  const logout = async () => {
    setState('submitting'); setMessage('Oturum kapatılıyor…')
    try {
      const csrfToken = await api.getAntiforgeryToken(); await api.logout(csrfToken); navigate('/giris')
    } catch (error) {
      setState('error'); setMessage(authErrorMessage(error, 'logout'))
    }
  }
  return <div className="session-actions"><button className="button button--secondary" type="button" onClick={() => void logout()} disabled={state === 'submitting'}>Güvenli çıkış yap</button><FormStatus state={state} message={message} /></div>
}
