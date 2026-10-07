import { LogoutButton } from '../features/auth/AuthPages'
import {
  FullDraftPreviewPage,
  InvitationDraftDashboard,
  InvitationEditorPage,
  NewInvitationPage,
} from '../features/invitations/InvitationDraftPages'
import { RouteShell } from './RouteShell'
import { InternalLink } from '../components/ui/InternalLink'
import { InvitationTrashPage } from '../features/invitations/InvitationTrashPage'
import { RsvpResultsPage } from '../features/invitations/RsvpResultsPage'
import { OrganizationSubscriptionPage } from '../features/payments/OrganizationSubscriptionPage'
import { AccountConsentSettingsPage } from '../features/privacy/AccountConsentSettingsPage'

export default function CreatorRoutes() {
  const pathname = window.location.pathname
  const editorMatch = /^\/panel\/davetiyeler\/([0-9a-f-]+)\/duzenle$/i.exec(pathname)
  const rsvpResultsMatch = /^\/panel\/davetiyeler\/([0-9a-f-]+)\/rsvp-yanitlari$/i.exec(pathname)
  const previewMatch = /^\/panel\/davetiyeler\/([0-9a-f-]+)\/onizleme$/i.exec(pathname)

  let title = 'Davetiye taslakları'
  let content = <InvitationDraftDashboard />
  if (pathname === '/panel/cop-kutusu') {
    title = 'Çöp kutusu'
    content = <InvitationTrashPage />
  } else if (pathname === '/panel/plan-odeme') {
    title = 'Plan ve ödeme'
    content = <OrganizationSubscriptionPage />
  } else if (pathname === '/panel/hesap') {
    title = 'Hesap tercihleri'
    content = <AccountConsentSettingsPage />
  } else if (pathname === '/panel/davetiyeler/yeni') {
    title = 'Yeni davetiye'
    content = <NewInvitationPage />
  } else if (editorMatch?.[1]) {
    title = 'Taslak editörü'
    content = <InvitationEditorPage invitationId={editorMatch[1]} />
  } else if (rsvpResultsMatch?.[1]) {
    title = 'RSVP yanıtları'
    content = <RsvpResultsPage key={rsvpResultsMatch[1]} invitationId={rsvpResultsMatch[1]} />
  } else if (previewMatch?.[1]) {
    title = 'Taslak önizlemesi'
    content = <FullDraftPreviewPage invitationId={previewMatch[1]} />
  }

  return (
    <RouteShell title={title} zone="creator">
      <nav className="button-row creator-navigation" aria-label="Creator paneli menüsü">
        <InternalLink to="/panel/davetiyeler" aria-current={pathname === '/panel/davetiyeler' || pathname === '/panel' ? 'page' : undefined}>Davetiyeler</InternalLink>
        <InternalLink to="/panel/cop-kutusu" aria-current={pathname === '/panel/cop-kutusu' ? 'page' : undefined}>Çöp kutusu</InternalLink>
        <InternalLink to="/panel/plan-odeme" aria-current={pathname === '/panel/plan-odeme' ? 'page' : undefined}>Plan ve ödeme</InternalLink>
        <InternalLink to="/panel/hesap" aria-current={pathname === '/panel/hesap' ? 'page' : undefined}>Hesap tercihleri</InternalLink>
      </nav>
      {content}
      <LogoutButton />
    </RouteShell>
  )
}
