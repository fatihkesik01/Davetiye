import { RouteShell } from './RouteShell'

export default function AdminRoutes() {
  return (
    <RouteShell title="Yönetim Paneli" zone="admin">
      <section className="route-placeholder" aria-live="polite">
        <p>Bu alan yalnız MFA-tamamlanmış Super Admin oturumları için ayrılmıştır.</p>
        <p>Özel Creator veya misafir içeriği bu kabukta gösterilmez.</p>
      </section>
    </RouteShell>
  )
}
