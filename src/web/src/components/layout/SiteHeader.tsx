import { type KeyboardEvent, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { InternalLink } from '../ui/InternalLink'
import { LogoutButton } from '../../features/auth/AuthPages'
import { AccountPreferenceControls } from '../../features/preferences/preferences'
import { Avatar } from '../../features/preferences/avatars'
import { PreferenceStatus } from '../../features/preferences/PreferenceStatus'
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
  const accountPreferences = useOptionalAccountPreferences()
  const hydrate = accountPreferences?.hydrate
  // The key comes only from hydrated account preferences; the PII-free session projection never carries it.
  const avatarKey = accountPreferences?.ready ? accountPreferences.preferences.avatar : null
  const drawerReference = useRef<HTMLDialogElement>(null)
  const headerReference = useRef<HTMLElement>(null)
  const menuButtonReference = useRef<HTMLButtonElement>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [scrolled, setScrolled] = useState(false)
  const closeMenu = useCallback((returnFocus: boolean) => {
    setMenuOpen(false)
    if (returnFocus) menuButtonReference.current?.focus()
  }, [])

  // The soft shadow appears only once the page has scrolled under the sticky bar.
  useEffect(() => {
    const update = () => setScrolled(window.scrollY > 4)
    update()
    window.addEventListener('scroll', update, { passive: true })
    return () => window.removeEventListener('scroll', update)
  }, [])

  // Client-side navigation (and back/forward) dispatches popstate; the disclosure menu never outlives a route change.
  useEffect(() => {
    const close = () => setMenuOpen(false)
    window.addEventListener('popstate', close)
    return () => window.removeEventListener('popstate', close)
  }, [])

  // A press outside the open (non-modal) menu dismisses it without stealing focus from what was pressed.
  useEffect(() => {
    if (!menuOpen) return
    const dismiss = (event: PointerEvent) => {
      if (event.target instanceof Node && headerReference.current?.querySelector('.site-header__inner')?.contains(event.target)) return
      setMenuOpen(false)
    }
    document.addEventListener('pointerdown', dismiss)
    return () => document.removeEventListener('pointerdown', dismiss)
  }, [menuOpen])

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

  const handleKeyDown = (event: KeyboardEvent<HTMLElement>) => {
    if (event.key === 'Escape' && menuOpen && !drawerOpen) {
      event.stopPropagation()
      closeMenu(true)
    }
  }

  return <header ref={headerReference} className="site-header" data-site-header={zone} data-scrolled={scrolled ? 'true' : 'false'} onKeyDown={handleKeyDown}>
    <div className="site-header__inner">
      <button className="site-menu-button" type="button" aria-expanded={menuOpen} aria-controls="site-menu" ref={menuButtonReference} onClick={() => setMenuOpen(open => !open)}>
        <svg className="site-menu-button__icon" viewBox="0 0 24 24" width="22" height="22" aria-hidden="true" focusable="false">
          <path className="site-menu-button__bar site-menu-button__bar--top" d="M4 7h16" />
          <path className="site-menu-button__bar site-menu-button__bar--middle" d="M4 12h16" />
          <path className="site-menu-button__bar site-menu-button__bar--bottom" d="M4 17h16" />
        </svg>
        <span className="site-menu-button__label">{t('siteHeader.menu')}</span>
      </button>
      <InternalLink className="site-brand" to="/" aria-label={t('siteHeader.brandLabel')}>{t('siteHeader.brand')}</InternalLink>
      <div className="site-header__nav" id="site-menu" data-open={menuOpen ? 'true' : 'false'} onClick={(event) => { if (event.target instanceof Element && event.target.closest('a')) setMenuOpen(false) }}>
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
          <span className="site-account__avatar" data-avatar-slot aria-hidden="true"><Avatar avatarKey={avatarKey} size={28} /></span>
          <span className="site-account__label">{t('siteHeader.account')}</span>
        </button> : null}
      </div>
    </div>
    {authenticated && hydrate ? <dialog ref={drawerReference} className="preferences-drawer" aria-labelledby="preferences-drawer-title" onClose={() => setDrawerOpen(false)} onClick={(event) => { if (event.target === drawerReference.current) setDrawerOpen(false) }}>
      <div className="preferences-drawer__panel">
        <header className="preferences-drawer__heading">
          <Avatar className="preferences-drawer__avatar" avatarKey={avatarKey} size={48} />
          <div className="preferences-drawer__heading-main"><h2 id="preferences-drawer-title">{t('navigation.drawerTitle')}</h2><div className="preferences-drawer__quick-actions">{showAccountSettings ? <><InternalLink className="preferences-drawer__account-link" to="/panel/hesap" aria-describedby="preferences-drawer-account-hint" onClick={() => setDrawerOpen(false)}>{t('siteHeader.accountSettings')}</InternalLink><span id="preferences-drawer-account-hint" className="visually-hidden">{t('siteHeader.accountSettingsHint')}</span></> : null}<div className="preferences-drawer__logout"><LogoutButton /></div></div></div>
          <button className="preferences-drawer__close" type="button" aria-label={t('common.close')} onClick={() => setDrawerOpen(false)}>
            <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true" focusable="false"><path d="M6 6l12 12M18 6L6 18" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" /></svg>
          </button>
        </header>
        <div className="preferences-drawer__body"><AccountPreferenceControls variant="drawer" showAvatar showStatus={false} /></div>
        <footer className="preferences-drawer__footer"><PreferenceStatus /></footer>
      </div>
    </dialog> : null}
  </header>
}
