import { type FormEvent, type ReactNode, useEffect, useMemo, useState } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'

import { DavetiyeApiClient } from '../../api/generated/client'
import { InternalLink } from '../../components/ui/InternalLink'
import { AccountDeletionRequest } from './AccountDeletion'

type PageState = 'loading' | 'ready' | 'saving' | 'error'

export function AccountConsentSettingsPage({ children, profile }: { children?: ReactNode; profile?: ReactNode }) {
  const { t, i18n } = useTranslation()
  const tRef = useLatestT()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [state, setState] = useState<PageState>('loading')
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState('')
  const [message, setMessage] = useState('')
  const [marketingOptIn, setMarketingOptIn] = useState(false)
  const [serviceNotice, setServiceNotice] = useState<{ acknowledged: boolean; acknowledgedAt: string | null; noticeVersion: string; textStatus: string } | null>(null)
  const [marketingPreference, setMarketingPreference] = useState<{ updatedAt: string | null; version: string } | null>(null)
  const [history, setHistory] = useState<Array<{ kind: string; granted: boolean; version: string; recordedAt: string }>>([])

  useEffect(() => {
    let active = true
    void api.getAccountConsents().then((snapshot) => {
      if (!active) return
      setMarketingOptIn(snapshot.marketing.optedIn)
      setServiceNotice(snapshot.serviceNotice)
      setMarketingPreference(snapshot.marketing)
      setHistory(snapshot.history)
      setLoaded(true)
      setState('ready')
    }).catch(() => {
      if (!active) return
      setError(tRef.current('consent.loadFailed'))
      setState('error')
    })
    return () => { active = false }
  }, [api, tRef])

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (!loaded || state === 'saving') return
    setState('saving')
    setError('')
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const result = await api.updateMarketingConsent({ optedIn: marketingOptIn }, csrfToken)
      setMarketingOptIn(result.marketing.optedIn)
      setMarketingPreference(result.marketing)
      setHistory(result.history)
      setState('ready')
      setMessage(t('preferences.saved'))
    } catch {
      setError(t('consent.savingFailed'))
      setState('error')
    }
  }

  return <div className="account-settings">
    {children ? <section className="account-card" aria-labelledby="appearance-preferences-title">
      <header className="account-card__header"><h2 id="appearance-preferences-title">{t('account.appearanceTitle')}</h2><p>{t('account.appearanceIntro')}</p></header>
      {children}
    </section> : null}
    {profile}
    <section className="account-card account-consent-settings" aria-labelledby="account-consent-heading">
    <header className="account-card__header"><h2 id="account-consent-heading">{t('account.privacyTitle')}</h2><p>{t('consent.intro')}</p></header>
    {state === 'loading' ? <p role="status">{t('common.loading')}</p> : null}
    {serviceNotice ? <div className="account-consent-settings__record">
      <h3>{t('consent.noticeTitle')}</h3>
      <p>{serviceNotice.acknowledged ? t('consent.acknowledged') : t('consent.missing')}</p>
      <p>{t('consent.textStatus')}</p>
      <p>{t('consent.version')}: <code>{serviceNotice.noticeVersion}</code></p>
      {serviceNotice.acknowledgedAt ? <p>{t('consent.acknowledgedAt')}: <time dateTime={serviceNotice.acknowledgedAt}>{new Date(serviceNotice.acknowledgedAt).toLocaleString(i18n.language === 'en' ? 'en-US' : 'tr-TR')}</time></p> : null}
    </div> : null}
    <form className="account-consent-settings__form" onSubmit={(event) => void save(event)}>
      <label htmlFor="account-marketing-opt-in">
        <input id="account-marketing-opt-in" type="checkbox" checked={marketingOptIn} disabled={!loaded || state === 'saving'} onChange={(event) => setMarketingOptIn(event.target.checked)} />
        <span><strong>{t('consent.marketing')}</strong><br />{t('consent.optional')}</span>
      </label>
      {marketingPreference ? <p>{t('consent.preferenceVersion')}: <code>{marketingPreference.version}</code>{marketingPreference.updatedAt ? <> · {t('consent.lastUpdated')}: <time dateTime={marketingPreference.updatedAt}>{new Date(marketingPreference.updatedAt).toLocaleString(i18n.language === 'en' ? 'en-US' : 'tr-TR')}</time></> : null}</p> : null}
      <button className="button button--primary" type="submit" disabled={!loaded || state === 'saving'}>{state === 'saving' ? t('common.saving') : t('consent.save')}</button>
    </form>
    {history.length > 0 ? <details className="account-consent-settings__history">
      <summary>{t('consent.history')} ({history.length})</summary>
      <ul>{history.map((record, index) => <li key={`${record.kind}-${record.recordedAt}-${index}`}>
        <time dateTime={record.recordedAt}>{new Date(record.recordedAt).toLocaleString(i18n.language === 'en' ? 'en-US' : 'tr-TR')}</time> — {record.kind === 'serviceNoticeAcknowledgement' ? t('consent.serviceNotice') : t('consent.marketingChoice')}: {record.granted ? t('consent.accepted') : t('consent.disabledChoice')} (<code>{record.version}</code>)
      </li>)}</ul>
    </details> : null}
    <p><InternalLink to="/gizlilik">{t('consent.privacyLink')}</InternalLink> · <InternalLink to="/kullanim-kosullari">{t('consent.termsLink')}</InternalLink></p>
    <AccountDeletionRequest />
    {error ? <p role="alert">{error}</p> : message ? <p role="status">{message}</p> : state === 'ready' ? <p role="status">{t('consent.loaded')}</p> : null}
    </section>
  </div>
}
