import { useTranslation } from 'react-i18next'
import { FullDraftPreviewPage, InvitationDraftDashboard, InvitationEditorPage, NewInvitationPage } from '../features/invitations/InvitationDraftPages'
import { InvitationTrashPage } from '../features/invitations/InvitationTrashPage'
import { RsvpResultsPage } from '../features/invitations/RsvpResultsPage'
import { OrganizationSubscriptionPage } from '../features/payments/OrganizationSubscriptionPage'
import { AccountConsentSettingsPage } from '../features/privacy/AccountConsentSettingsPage'
import { AccountPreferenceControls } from '../features/preferences/preferences'
import { AvatarPreferenceSection } from '../features/preferences/AvatarPicker'
import { RouteShell } from './RouteShell'

const titleKeys: Record<string, string> = {
  '/panel': 'creator.invitations', '/panel/davetiyeler': 'creator.invitations',
  '/panel/cop-kutusu': 'creator.trash', '/panel/plan-odeme': 'creator.billing', '/panel/hesap': 'account.title',
  '/panel/davetiyeler/yeni': 'creator.newInvitation',
}

export default function CreatorRoutes() {
  const { t } = useTranslation()
  const pathname = window.location.pathname
  const editorMatch = /^\/panel\/davetiyeler\/([0-9a-f-]+)\/duzenle$/i.exec(pathname)
  const rsvpResultsMatch = /^\/panel\/davetiyeler\/([0-9a-f-]+)\/rsvp-yanitlari$/i.exec(pathname)
  const previewMatch = /^\/panel\/davetiyeler\/([0-9a-f-]+)\/onizleme$/i.exec(pathname)

  let title = t(titleKeys[pathname] ?? 'creator.invitations')
  let content = <InvitationDraftDashboard />
  if (pathname === '/panel/cop-kutusu') content = <InvitationTrashPage />
  else if (pathname === '/panel/plan-odeme') content = <OrganizationSubscriptionPage />
  else if (pathname === '/panel/hesap') content = <AccountConsentSettingsPage profile={<AvatarPreferenceSection />}><AccountPreferenceControls /></AccountConsentSettingsPage>
  else if (pathname === '/panel/davetiyeler/yeni') content = <NewInvitationPage />
  else if (editorMatch?.[1]) { title = t('creator.editor'); content = <InvitationEditorPage invitationId={editorMatch[1]} /> }
  else if (rsvpResultsMatch?.[1]) { title = t('creator.rsvp'); content = <RsvpResultsPage key={rsvpResultsMatch[1]} invitationId={rsvpResultsMatch[1]} /> }
  else if (previewMatch?.[1]) { title = t('creator.preview'); content = <FullDraftPreviewPage invitationId={previewMatch[1]} /> }

  return <RouteShell title={title} zone="creator">{content}</RouteShell>
}
