import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { InternalLink } from '../ui/InternalLink'
import { LogoutButton } from '../../features/auth/AuthPages'
import { AccountPreferenceControls } from '../../features/preferences/preferences'
import { useOptionalAccountPreferences } from '../../features/preferences/preferencesContext'
import { useSessionAccess } from '../../features/session/sessionAccess'

export type SiteHeaderZone = 'public' | 'creator' | 'admin'
export interface SiteHeaderSectionLink { href: string; label: string }

interface SiteHeaderProps {
  zone: SiteHeaderZone
  /** In-page anchors (landing only). They live in their own labelled navigation next to the site links. */
  sectionLinks?: readonly SiteHeaderSectionLink[]
  sectionLabel?: string
}

const creatorItems = [
  ['/panel/davetiyeler', 'navigation.invitations'], ['/panel/cop-kutusu', 'navigation.trash'],
  ['/panel/plan-odeme', 'navigation.billing'], ['/panel/hesap', 'navigation.account'],
] as const
const adminItems = [
  ['/admin', 'navigation.overview'], ['/admin/accounts', 'navigation.accounts'], ['/admin/plans', 'navigation.plans'],
  ['/admin/settings', 'navigation.settings'], ['/admin/templates', 'navigation.templates'], ['/admin/payments', 'navigation.payments'], ['/admin/audit', 'navigation.audit'],
] as const
const publicItems = [['/sablonlar', 'siteHeader.templates']] as const

/**
 * The one site-wide bar. Every page except the public invitation renders this component: the brand
 * lockup, spacing, controls and focus styles are identical; only the link set depends on the zone
 * and on the (PII-free) session hint. Authorization is never decided here; the backend enforces it.
 */
export function SiteHeader({ zone, sectionLinks, sectionLabel }: SiteHeaderProps) {
  const { t } = useTranslation()
  const session = useSessionAccess()
  const hydrate = useOptionalAccountPreferences()?.hydrate
  const drawerReference = useRef<HTMLDialogElement>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)

  const authenticated = zone !== 'public' || session.status === 'authenticated'
  const access = session.status === 'authenticated' ? session.access : zone === 'admin' ? 'mfa-complete-super-admin' : zone === 'creator' ? 'creator' : null

  useEffect(() => {
    if (authenticated) void hydrate?.()
  }, [authenticated, hydrate])

  useEffect(() => {
    const dialog = drawerReference.current
    if (!dialog) return
    if (drawerOpen && !dialog.open) dialog.showModal()
    if (!drawerOpen && dialog.open) dialog.close()
  }, [drawerOpen])

  const currentPath = window.location.pathname
  const items = zone === 'creator' ? creatorItems : zone === 'admin' ? adminItems : publicItems
  const navLabel = zone === 'creator' ? t('navigation.creatorLabel') : zone === 'admin' ? t('navigation.adminLabel') : t('siteHeader.publicNavLabel')
  const isCurrent = (href: string) => currentPath === href
    || (href === '/panel/davetiyeler' && currentPath === '/panel')
    || (href === '/sablonlar' && currentPath.startsWith('/sablonlar/'))
  const sessionLink = zone !== 'public' ? null
    : access === 'creator' ? { to: '/panel/davetiyeler', label: t('siteHeader.goToPanel') }
    : access === 'mfa-complete-super-admin' ? { to: '/admin', label: t('siteHeader.goToAdmin') }
    : access === 'mfa-setup-required-super-admin' ? { to: '/admin/mfa/setup', label: t('siteHeader.finishMfaSetup') }
    : null
  const showAccountSettings = access === 'creator'

  return <header className="site-header" data-site-header={zone}>
    <div className="site-header__inner">
      <InternalLink className="site-brand" to="/" aria-label={t('siteHeader.brandLabel')}>{t('siteHeader.brand')}</InternalLink>
      <div className="site-header__nav">
        <nav className="site-nav" aria-label={navLabel}>
          {items.map(([href, label]) => <InternalLink key={href} to={href} aria-current={isCurrent(href) ? 'page' : undefined}>{t(label)}</InternalLink>)}
        </nav>
        {sectionLinks && sectionLinks.length > 0 ? <nav className="site-nav site-nav--sections" aria-label={sectionLabel}>
          {sectionLinks.map(link => <a key={link.href} href={link.href}>{link.label}</a>)}
        </nav> : null}
      </div>
      <div className="site-header__actions">
        {authenticated ? null : <InternalLink className="site-action site-action--login" to="/giris">{t('siteHeader.login')}</InternalLink>}
        {sessionLink ? <InternalLink className="site-action site-action--session" to={sessionLink.to}>{sessionLink.label}</InternalLink> : null}
        {authenticated && hydrate ? <button className="site-account" type="button" aria-haspopup="dialog" aria-expanded={drawerOpen} aria-label={t('siteHeader.accountMenu')} onClick={() => setDrawerOpen(true)}>
          {/* Avatar slot: a generic person icon until a profile picture feature exists. The session projection is PII-free, so nothing personal is rendered here. */}
          <span className="site-account__avatar" data-avatar-slot aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="8.5" r="3.5" /><path d="M5 20c.7-3.6 3.4-5.5 7-5.5s6.3 1.9 7 5.5" /></svg>
          </span>
          <span className="site-account__label">{t('siteHeader.account')}</span>
        </button> : null}
      </div>
    </div>
    {authenticated && hydrate ? <dialog ref={drawerReference} className="preferences-drawer" aria-labelledby="preferences-drawer-title" aria-describedby="preferences-drawer-description" onClose={() => setDrawerOpen(false)} onClick={(event) => { if (event.target === drawerReference.current) setDrawerOpen(false) }}>
      <div className="preferences-drawer__content">
        <div className="preferences-drawer__heading">
          <div><h2 id="preferences-drawer-title">{t('navigation.drawerTitle')}</h2><p id="preferences-drawer-description">{t('navigation.drawerDescription')}</p></div>
          <button className="preferences-drawer__close" type="button" aria-label={t('common.close')} onClick={() => setDrawerOpen(false)}>×</button>
        </div>
        <AccountPreferenceControls />
        {showAccountSettings ? <p className="preferences-drawer__account-link"><InternalLink to="/panel/hesap" onClick={() => setDrawerOpen(false)}>{t('siteHeader.accountSettings')}</InternalLink><small>{t('siteHeader.accountSettingsHint')}</small></p> : null}
        <div className="preferences-drawer__logout"><LogoutButton /></div>
      </div>
    </dialog> : null}
  </header>
}
