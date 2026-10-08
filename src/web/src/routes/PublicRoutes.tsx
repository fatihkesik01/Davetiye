import {
  ConfirmEmailPage,
  ForgotPasswordPage,
  GoogleLinkPage,
  LoginPage,
  RegisterPage,
  ResetPasswordPage,
} from '../features/auth/AuthPages'
import { TemplateCatalogPage } from '../features/templates/catalog/TemplateCatalogPage'
import { TemplateDemoRenderer } from '../features/templates/rendering/InvitationRenderer'
import { LandingPage } from '../features/landing/LandingPage'
import { PublicInvitationPage } from '../features/invitations/PublicInvitationPage'
import { PrivacyInformationPage, TermsInformationPage } from '../features/privacy/PrivacyInformationPages'
import { AccountDeletionConfirmationPage } from '../features/privacy/AccountDeletion'
import { InternalLink } from '../components/ui/InternalLink'
import { RouteShell } from './RouteShell'
import { useTranslation } from 'react-i18next'

function authRoute(pathname: string) {
  if (pathname === '/giris') return { title: 'Giriş yap', content: <LoginPage /> }
  if (pathname === '/giris/kayit') return { title: 'Hesap oluştur', content: <RegisterPage /> }
  if (pathname === '/giris/e-posta-dogrula' || pathname === '/auth/confirm-email') return { title: 'E-postanı doğrula', content: <ConfirmEmailPage /> }
  if (pathname === '/giris/sifremi-unuttum') return { title: 'Şifremi unuttum', content: <ForgotPasswordPage /> }
  if (pathname === '/giris/sifre-sifirla' || pathname === '/auth/reset-password') return { title: 'Yeni şifre belirle', content: <ResetPasswordPage /> }
  if (pathname === '/giris/google-baglanti') return { title: 'Google hesabını bağla', content: <GoogleLinkPage /> }
  if (pathname === '/hesap-silme/onayla') return { title: 'Hesap silme talebini doğrula', content: <AccountDeletionConfirmationPage /> }
  if (pathname === '/gizlilik') return { title: 'Hizmet bildirimi ve gizlilik', content: <PrivacyInformationPage /> }
  if (pathname === '/kullanim-kosullari') return { title: 'Kullanım koşulları', content: <TermsInformationPage /> }
  return null
}

export default function PublicRoutes() {
  const { t } = useTranslation()
  const pathname = window.location.pathname
  const auth = authRoute(pathname)
  const isInvitation = pathname.startsWith('/davetiye/')
  const catalogDemoKey = /^\/sablonlar\/([^/]+)$/.exec(pathname)?.[1]
  const visualTemplateKey = import.meta.env.DEV
    ? /^\/__visual\/templates\/([^/]+)$/.exec(pathname)?.[1]
    : undefined

  if (visualTemplateKey) {
    return <main className="template-visual-test-page">
      <TemplateDemoRenderer templateKey={decodeURIComponent(visualTemplateKey)} rendererVersion={1} />
    </main>
  }

  if (pathname === '/') return <LandingPage />

  if (auth) {
    const titleKey = pathname === '/giris' ? 'authUi.routeTitles.login'
      : pathname === '/giris/kayit' ? 'authUi.routeTitles.register'
      : pathname === '/giris/e-posta-dogrula' || pathname === '/auth/confirm-email' ? 'authUi.routeTitles.confirm'
      : pathname === '/giris/sifremi-unuttum' ? 'authUi.routeTitles.forgot'
      : pathname === '/giris/sifre-sifirla' || pathname === '/auth/reset-password' ? 'authUi.routeTitles.reset'
      : ''
    return <RouteShell title={titleKey ? t(titleKey) : auth.title} zone="public">{auth.content}</RouteShell>
  }

  if (isInvitation) return <PublicInvitationPage key={pathname} pathname={pathname} />

  if (pathname === '/sablonlar' || catalogDemoKey) {
    return <RouteShell title={t(catalogDemoKey ? 'templateUi.demoTitle' : 'templateUi.title')} zone="public">
      <TemplateCatalogPage demoTemplateKey={catalogDemoKey ? decodeURIComponent(catalogDemoKey) : undefined} />
    </RouteShell>
  }

  return (
    <RouteShell title="Sayfa bulunamadı" zone="public">
      <section className="feedback-state feedback-state--stacked">
        <p>Aradığınız sayfayı bulamadık. Bağlantıyı kontrol edip yeniden deneyin.</p>
        <InternalLink to="/sablonlar">Şablonlara göz atın</InternalLink>
      </section>
    </RouteShell>
  )
}
