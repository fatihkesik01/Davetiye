import { useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { DavetiyeApiClient } from '../../api/generated/client'

export function ConsentMap({ location }: { location: string }) {
  const { t } = useTranslation()
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [source, setSource] = useState<{ location: string; url: string } | null>(null)
  const [consentLocation, setConsentLocation] = useState<string | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    void api.getPublicInvitationCapabilities(controller.signal).then(result => {
      if (controller.signal.aborted || !result.mapEmbedEnabled || result.mapEmbedOrigin !== 'https://www.google.com' || result.mapEmbedPath !== '/maps/embed/v1/place' || typeof result.mapEmbedKey !== 'string' || !result.mapEmbedKey.trim()) return
      try {
        const url = new URL('https://www.google.com/maps/embed/v1/place')
        url.searchParams.set('key', result.mapEmbedKey)
        url.searchParams.set('q', location)
        setSource({ location, url: url.href })
      } catch { /* Untrusted or missing map capability never creates an iframe. */ }
    }).catch(() => {})
    return () => controller.abort()
  }, [api, location])
  if (!source || source.location !== location) return null
  return <section className="consent-map" aria-labelledby="consent-map-heading"><h2 id="consent-map-heading">{t('creatorUi.map.title')}</h2>
    {consentLocation !== location ? <><p>{t('creatorUi.map.notice')}</p><button className="button button--secondary" type="button" onClick={() => setConsentLocation(location)}>{t('creatorUi.map.load')}</button></> : <>
      <iframe title={t('creatorUi.map.iframeTitle')} src={source.url} referrerPolicy="strict-origin-when-cross-origin" loading="lazy" allowFullScreen />
      <button className="button button--secondary" type="button" onClick={() => setConsentLocation(null)}>{t('creatorUi.map.close')}</button>
    </>}
  </section>
}
