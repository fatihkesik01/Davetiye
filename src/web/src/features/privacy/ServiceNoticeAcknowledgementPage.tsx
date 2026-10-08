import { type FormEvent, useMemo, useState } from 'react'

import { DavetiyeApiClient } from '../../api/generated/client'
import { ErrorSummary, type ErrorSummaryEntry } from '../../components/ui/ErrorSummary'
import { InternalLink } from '../../components/ui/InternalLink'
import { LogoutButton } from '../auth/AuthPages'

interface ServiceNoticeAcknowledgementPageProps {
  onAcknowledged: () => void
}

export function ServiceNoticeAcknowledgementPage({ onAcknowledged }: ServiceNoticeAcknowledgementPageProps) {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [acknowledged, setAcknowledged] = useState(false)
  const [errors, setErrors] = useState<ErrorSummaryEntry[]>([])
  const [state, setState] = useState<'idle' | 'submitting' | 'error'>('idle')
  const [message, setMessage] = useState('')

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!acknowledged) {
      setErrors([{ fieldId: 'existing-service-notice', message: 'Devam etmek için hizmet bildirimini okuduğunuzu onaylayın.' }])
      return
    }
    setErrors([])
    setMessage('')
    setState('submitting')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const snapshot = await api.acknowledgeServiceNotice({ acknowledged: true }, csrfToken)
      if (!snapshot.serviceNotice.acknowledged) {
        setState('error')
        setMessage('Onayınız doğrulanamadı. Lütfen yeniden deneyin.')
        return
      }
      onAcknowledged()
    } catch {
      setState('error')
      setMessage('Onayınız kaydedilemedi. Bağlantınızı kontrol edip yeniden deneyin.')
    }
  }

  return <section className="service-notice-gate" aria-labelledby="service-notice-gate-heading">
    <p className="service-notice-gate__status">Bir defalık hesap adımı</p>
    <h2 id="service-notice-gate-heading">Hizmet bildirimi</h2>
    <p>Kutlio hesabınızı ve seçtiğiniz ürün özelliklerini sunabilmek için hesap bilgileri, davetiye içeriği ve kullandığınız modüllere bağlı konuk bilgileri işlenir. Ayrıntılar için taslak <InternalLink to="/gizlilik">hizmet bildirimi ve gizlilik bilgisi</InternalLink> ile <InternalLink to="/kullanim-kosullari">kullanım koşullarını</InternalLink> inceleyin.</p>
    <p>Bu hesap daha önce oluşturulduğu için hizmet bildirimi kaydınız bulunmuyor. Panele devam etmek için bildirimi okuduğunuzu onaylayın. Metinler Phase 11 hukuk incelemesinde kesinleştirilecektir.</p>
    <form className="service-notice-gate__form" onSubmit={(event) => void submit(event)} noValidate>
      <ErrorSummary id="service-notice-error-summary" errors={errors} />
      <label htmlFor="existing-service-notice">
        <input id="existing-service-notice" type="checkbox" checked={acknowledged} disabled={state === 'submitting'} required aria-required="true" aria-invalid={errors.length > 0 || undefined} aria-describedby={errors.length > 0 ? 'existing-service-notice-error' : undefined} onChange={(event) => setAcknowledged(event.target.checked)} />
        <span>Hizmet bildirimini okudum.</span>
      </label>
      {errors.length > 0 ? <p id="existing-service-notice-error" className="form-field__error">{errors[0]?.message}</p> : null}
      <button className="button button--primary" type="submit" disabled={state === 'submitting'}>{state === 'submitting' ? 'Kaydediliyor…' : 'Onayla ve panele devam et'}</button>
    </form>
    {message ? <p role="alert">{message}</p> : null}
    <p>Bu taslağın hukuki yeterliliği onaylanmış değildir. <InternalLink to="/gizlilik">Gizlilik bilgisi</InternalLink>.</p>
    <LogoutButton />
  </section>
}
