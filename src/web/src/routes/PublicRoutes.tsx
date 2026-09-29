import { RouteShell } from './RouteShell'

export default function PublicRoutes() {
  const isInvitation = window.location.pathname.startsWith('/davetiye/')

  return (
    <RouteShell title={isInvitation ? 'Davetiye sayfası' : 'Şablonlar'} zone="public">
      <section className="route-placeholder" aria-live="polite">
        <p>Bu alanın güvenli, herkese açık kabuğu hazır.</p>
        <p>Şablon kataloğu ve davetiye içeriği sonraki fazlarda eklenecek.</p>
      </section>
    </RouteShell>
  )
}
