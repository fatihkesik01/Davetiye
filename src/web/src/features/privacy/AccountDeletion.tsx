import { type FormEvent, useLayoutEffect, useMemo, useRef, useState } from 'react'

import { DavetiyeApiClient } from '../../api/generated/client'
import { InternalLink } from '../../components/ui/InternalLink'
import { consumeAccountDeletionToken } from '../../routes/navigation'

type RequestState = 'closed' | 'confirming' | 'submitting' | 'pending' | 'error'

export function AccountDeletionRequest() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [state, setState] = useState<RequestState>('closed')
  const [confirmed, setConfirmed] = useState(false)
  const [error, setError] = useState('')

  const requestDeletion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!confirmed || state === 'submitting') return
    setState('submitting')
    setError('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      await api.requestAccountDeletion(csrfToken)
      setState('pending')
    } catch {
      setState('error')
      setError('Silme talebi şu anda başlatılamadı. Bağlantınızı kontrol edip yeniden deneyin.')
    }
  }

  return <section className="account-deletion" aria-labelledby="account-deletion-heading">
    <h3 id="account-deletion-heading">Hesabı sil</h3>
    <p>Silme talebini e-posta bağlantısıyla doğrulamanız gerekir. Doğrulama yapılana kadar hesabınız kullanılabilir durumda kalır.</p>
    {state === 'pending' ? <div role="status">
      <p>Hesap e-posta adresinize doğrulama bağlantısı gönderildi. İşlemi başlatmak için bu bağlantıyı açın; bağlantıyı açmadıkça hesap silinmez.</p>
      <p>Bu sayfayı kapatabilirsiniz.</p>
    </div> : state === 'confirming' || state === 'submitting' || state === 'error' ? <form onSubmit={(event) => void requestDeletion(event)}>
      <div className="auth-card__security-note">
        <p>E-posta bağlantısını doğruladığınız anda:</p>
        <ul>
          <li>Hesabınız kapatılır, oturumlarınız sonlandırılır ve herkese açık erişim durur.</li>
          <li>Davetiyeleriniz, konuk yanıtları ve ilişkili içerikler kalıcı silme sürecine hemen girer.</li>
          <li>Varsa Organization aboneliğinizin uygulamadaki otomatik yenilemesi hemen durdurulur. Ödeme sağlayıcısındaki iptal henüz tamamlanmış veya doğrulanmış değildir; entegrasyon tamamlanana kadar beklemededir.</li>
          <li>Uygulamadaki herkese açık erişim hemen kapanır. Daha önce oluşturulmuş medya teslim bağlantıları, süresi dolana veya sağlayıcıdaki medya silinene kadar çalışabilir.</li>
          <li>Otomatik iade yapılmaz; ödeme konuları destek üzerinden değerlendirilir.</li>
        </ul>
        <p>Ödeme, denetim, günlük ve yedek kayıtları için kesin saklama süreleri Phase 11 hukuk incelemesine bırakılmıştır; bu inceleme öncesinde bu kayıtlar için otomatik silme yapılmaz.</p>
      </div>
      <label htmlFor="account-deletion-confirmation">
        <input id="account-deletion-confirmation" type="checkbox" checked={confirmed} disabled={state === 'submitting'} onChange={(event) => setConfirmed(event.target.checked)} />
        <span>Silme talebini başlatmak istediğimi ve e-posta doğrulamasından sonraki sonuçları anladığımı onaylıyorum.</span>
      </label>
      <div className="button-row">
        <button className="button button--danger" type="submit" disabled={!confirmed || state === 'submitting'}>{state === 'submitting' ? 'Talep gönderiliyor…' : 'Doğrulama e-postası gönder'}</button>
        <button className="button button--secondary" type="button" disabled={state === 'submitting'} onClick={() => { setState('closed'); setConfirmed(false); setError('') }}>Vazgeç</button>
      </div>
      {error ? <p role="alert">{error}</p> : null}
    </form> : <button className="button button--danger" type="button" onClick={() => setState('confirming')}>Hesabı silme talebi oluştur</button>}
  </section>
}

type ConfirmationState = 'loading' | 'ready' | 'submitting' | 'success' | 'error'

export function AccountDeletionConfirmationPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [token, setToken] = useState('')
  const [validLink, setValidLink] = useState(false)
  const [state, setState] = useState<ConfirmationState>('loading')
  const [consumed, setConsumed] = useState(false)
  const consumedOnce = useRef(false)

  useLayoutEffect(() => {
    if (consumedOnce.current) return
    consumedOnce.current = true
    const result = consumeAccountDeletionToken()
    setToken(result.token)
    setValidLink(result.validLocation)
    setState(result.validLocation ? 'ready' : 'error')
  }, [])

  const confirm = async () => {
    if (!validLink || !token || state === 'submitting') return
    setState('submitting')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      await api.confirmAccountDeletion({ token }, csrfToken)
      setToken('')
      setConsumed(true)
      setState('success')
    } catch {
      setToken('')
      setState('error')
    }
  }

  return <section className="auth-card" aria-labelledby="account-deletion-confirm-heading">
    <h2 id="account-deletion-confirm-heading">Hesap silme talebi</h2>
    {state === 'loading' ? <p role="status">Bağlantı kontrol ediliyor…</p> : null}
    {state === 'ready' ? <>
      <p>Bu e-posta bağlantısını onaylarsanız hesabınız hemen kapatılır ve davetiye/konuk içerikleriniz kalıcı silme sürecine girer. Uygulamadaki herkese açık erişim hemen durur; önceden oluşturulmuş medya teslim bağlantıları, süresi dolana veya sağlayıcıdaki medya silinene kadar çalışabilir.</p>
      <p>Varsa Organization aboneliğinizin uygulamadaki otomatik yenilemesi hemen durdurulur. Ödeme sağlayıcısındaki iptal henüz tamamlanmış veya doğrulanmış değildir; entegrasyon tamamlanana kadar beklemededir. Otomatik iade yapılmaz.</p>
      <p>Ödeme ve denetim kayıtlarının saklama süreleri Phase 11 hukuk incelemesine bırakılmıştır.</p>
      <button className="button button--danger" type="button" onClick={() => void confirm()}>Hesabı silmeyi onayla</button>
    </> : null}
    {state === 'submitting' ? <p role="status">Talebiniz doğrulanıyor…</p> : null}
    {state === 'success' && consumed ? <>
      <p role="status">Hesap silme doğrulandı. Oturumlarınız kapatıldı ve silme süreci başlatıldı.</p>
      <InternalLink to="/giris">Giriş sayfasına git</InternalLink>
    </> : null}
    {state === 'error' ? <>
      <p role="alert">Bu bağlantı eksik, geçersiz, süresi dolmuş veya daha önce kullanılmış olabilir. Güvenliğiniz için hesap silme durumu açıklanmıyor.</p>
      <InternalLink to="/giris">Giriş sayfasına git</InternalLink>
    </> : null}
  </section>
}
