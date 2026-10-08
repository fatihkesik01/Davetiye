import type { ReactNode } from 'react'
import type { Appearance } from './preferencesContext'

const svg = (children: ReactNode) => <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">{children}</svg>

/** Simple, decorative appearance icons. Drawn with currentColor so they follow the control's text colour. */
export const appearanceIcons: Record<Appearance, ReactNode> = {
  light: svg(<><circle cx="12" cy="12" r="4" /><path d="M12 2.5v2.2M12 19.3v2.2M2.5 12h2.2M19.3 12h2.2M5.3 5.3l1.6 1.6M17.1 17.1l1.6 1.6M18.7 5.3l-1.6 1.6M6.9 17.1l-1.6 1.6" /></>),
  dark: svg(<path d="M20 14.2A8 8 0 0 1 9.8 4a8 8 0 1 0 10.2 10.2z" />),
  system: svg(<><rect x="3" y="4" width="18" height="12" rx="2" /><path d="M8 20h8M12 16v4" /></>),
}
