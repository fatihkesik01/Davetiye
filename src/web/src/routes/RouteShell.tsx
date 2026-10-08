import { type ReactNode, useEffect, useRef } from 'react'

interface RouteShellProps {
  children: ReactNode
  title: string
  zone: 'public' | 'creator' | 'admin'
}

export function RouteShell({ children, title, zone }: RouteShellProps) {
  const headingReference = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    document.title = `${title} | Kutlio`
    headingReference.current?.focus()
  }, [title])

  return (
    <div className={`route-shell route-shell--${zone}`}>
      <a className="skip-link" href="#main-content">
        Ana içeriğe geç
      </a>
      <header className="shell-header">
        <p className="shell-brand">Kutlio</p>
        {zone === 'creator' ? <p>Creator Paneli</p> : null}
        {zone === 'admin' ? <p>Platform yönetimi</p> : null}
      </header>
      <main id="main-content" className="shell-main" tabIndex={-1}>
        <h1 ref={headingReference} tabIndex={-1}>{title}</h1>
        {children}
      </main>
    </div>
  )
}
