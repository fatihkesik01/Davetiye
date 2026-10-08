import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { ApiRequestError, DavetiyeApiClient, type PublicInvitationMedia as PublicMediaSnapshotItem, type PublicInvitationResponse } from '../../api/generated/client'
import { InvitationRenderer } from '../templates/rendering/InvitationRenderer'
import { normalizeDraftContent } from '../templates/rendering/model'
import { isRenderablePublicInvitation, publicCodeFromPath } from './publicInvitationModel'
import { PublicEventTools } from './PublicEventTools'
import { PublicInvitationMedia } from './PublicInvitationMedia'
import { PublicMemoriesSection } from './PublicMemoriesSection'
import { PublicRsvpSection } from './PublicRsvpSection'
import { PublicGiftRegistrySection } from './PublicGiftRegistrySection'

// Revalidation is a delivery cadence, never a local replacement for the server access gate.
const publicRefreshIntervalMs = 60_000
type PublicPageView = { state: 'loading' | 'unavailable' | 'not-found' | 'error' } | {
  state: 'active'
  invitation: Extract<PublicInvitationResponse, { status: 'active' }>
}

const noPublicMedia: PublicMediaSnapshotItem[] = []

export function PublicInvitationPage({ pathname }: { pathname: string }) {
  const { t } = useTranslation()
  const genericTitle = `${t('guest.publicTitle')} | Kutlio`
  const genericDescription = t('guest.publicDescription')
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const publicCode = publicCodeFromPath(pathname)
  const [view, setView] = useState<PublicPageView>(publicCode ? { state: 'loading' } : { state: 'not-found' })
  const [attempt, setAttempt] = useState(0)
  const mainRef = useRef<HTMLElement>(null)
  const countedPublicCode = useRef<string | null>(null)

  useEffect(() => {
    if (view.state !== 'active' || !publicCode || countedPublicCode.current === publicCode) return
    countedPublicCode.current = publicCode
    void api.recordPublicInvitationView(publicCode).catch(() => {})
  }, [api, publicCode, view.state])

  useEffect(() => {
    // Server metadata serves the initial crawler response; access revalidation removes its stale PII.
    document.head.querySelectorAll('meta[data-public-invitation-meta="true"], link[data-public-invitation-meta="true"], title[data-public-invitation-meta="true"]').forEach(element => element.remove())
    if (view.state !== 'active') {
      document.head.querySelectorAll('meta[property^="og:"], meta[name^="twitter:"], meta[property^="twitter:"]').forEach(element => element.remove())
    }
    document.title = view.state === 'active' ? `${view.invitation.content.headline?.trim() || 'Dijital davetiye'} | Kutlio` : genericTitle
    const description = document.head.querySelector<HTMLMetaElement>('meta[name="description"]') ?? document.createElement('meta')
    description.name = 'description'
    description.content = view.state === 'active' ? view.invitation.content.message?.trim() || 'Dijital davetiye sayfası.' : genericDescription
    if (!description.isConnected) document.head.appendChild(description)
  }, [genericDescription, genericTitle, view])

  useEffect(() => {
    document.title = genericTitle
    const cleanup: Array<() => void> = []
    for (const [name, content] of [['description', genericDescription], ['robots', 'noindex, nofollow']]) {
      const previous = document.head.querySelector<HTMLMetaElement>(`meta[name="${name}"]`)
      const element = previous ?? document.createElement('meta')
      const oldContent = element.getAttribute('content')
      element.name = name!
      element.content = content!
      if (!previous) document.head.appendChild(element)
      cleanup.push(() => {
        if (!previous) element.remove()
        else if (oldContent === null) element.removeAttribute('content')
        else element.content = oldContent
      })
    }
    mainRef.current?.focus()
    return () => cleanup.forEach(clear => clear())
  }, [genericDescription, genericTitle])

  useEffect(() => {
    if (!publicCode) return
    const controller = new AbortController()
    queueMicrotask(() => { if (!controller.signal.aborted) setView({ state: 'loading' }) })
    void api.getPublicInvitation(publicCode, controller.signal).then(response => {
      if (controller.signal.aborted) return
      if (response.status === 'unavailable') {
        setView({ state: 'unavailable' })
      } else if (isRenderablePublicInvitation(response)) {
        setView({ state: 'active', invitation: response })
      } else {
        setView({ state: 'error' })
      }
    }).catch(error => {
      if (controller.signal.aborted) return
      setView({ state: error instanceof ApiRequestError && error.status === 404 ? 'not-found' : 'error' })
    })
    return () => controller.abort()
  }, [api, attempt, publicCode])

  useEffect(() => {
    if (!publicCode) return
    const refresh = () => {
      // Remove a previously visible snapshot before a new access check starts.
      setView({ state: 'loading' })
      setAttempt(value => value + 1)
    }
    const visibilityChanged = () => { if (document.visibilityState === 'visible') refresh() }
    const interval = window.setInterval(() => { if (document.visibilityState === 'visible') refresh() }, publicRefreshIntervalMs)
    window.addEventListener('focus', refresh)
    document.addEventListener('visibilitychange', visibilityChanged)
    return () => {
      window.clearInterval(interval)
      window.removeEventListener('focus', refresh)
      document.removeEventListener('visibilitychange', visibilityChanged)
    }
  }, [publicCode])

  return <div className="public-invitation-page">
    <a className="skip-link" href="#public-invitation-content">{t('guest.invitationContent')}</a>
    <main id="public-invitation-content" ref={mainRef} tabIndex={-1} aria-busy={view.state === 'loading'}>
      {view.state === 'active' ? <>
        <PublicInvitationMedia key={`${publicCode ?? ''}-cover`} api={api} publicCode={publicCode ?? ''} templateKey={view.invitation.templateKey}
          media={view.invitation.media ?? noPublicMedia} role="Cover" />
        <InvitationRenderer
          templateKey={view.invitation.templateKey}
          rendererVersion={view.invitation.rendererVersion}
          model={normalizeDraftContent(view.invitation.content)}
          previewContext="public"
        />
        <PublicInvitationMedia key={`${publicCode ?? ''}-gallery`} api={api} publicCode={publicCode ?? ''} templateKey={view.invitation.templateKey}
          media={view.invitation.media ?? noPublicMedia} role="Gallery" />
        <p className="public-invitation-zone">{t('guest.timeZone')}: {view.invitation.content.timeZoneId}</p>
        {publicCode ? <PublicRsvpSection key={`${publicCode}-rsvp`} api={api} publicCode={publicCode} templateKey={view.invitation.templateKey} /> : null}
        {publicCode ? <PublicMemoriesSection key={`${publicCode}-memories`} api={api} publicCode={publicCode} /> : null}
        {publicCode ? <PublicGiftRegistrySection key={`${publicCode}-gifts`} api={api} publicCode={publicCode} templateKey={view.invitation.templateKey} /> : null}
        {view.invitation.rendererVersion >= 2 && publicCode ? <PublicEventTools model={normalizeDraftContent(view.invitation.content)} publicCode={publicCode} /> : null}
      </> : <section className="public-invitation-feedback" aria-labelledby="public-invitation-heading">
        <h1 id="public-invitation-heading">{view.state === 'not-found' ? t('guest.missing') : view.state === 'error' ? t('guest.failed') : view.state === 'unavailable' ? t('guest.unavailable') : t('guest.loading')}</h1>
        <p role={view.state === 'error' ? 'alert' : 'status'}>
          {view.state === 'unavailable' ? t('guest.unavailableDetails')
            : view.state === 'not-found' ? t('guest.checkLink')
            : view.state === 'error' ? t('guest.cannotShow')
            : t('guest.wait')}
        </p>
        {view.state === 'error' || view.state === 'unavailable' ? <button type="button" className="button button--secondary" onClick={() => setAttempt(value => value + 1)}>{t('guest.retry')}</button> : null}
      </section>}
    </main>
  </div>
}
