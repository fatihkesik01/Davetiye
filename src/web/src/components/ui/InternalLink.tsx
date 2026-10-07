import type { AnchorHTMLAttributes, MouseEvent } from 'react'

import { navigate } from '../../routes/navigation'

type InternalLinkProps = AnchorHTMLAttributes<HTMLAnchorElement> & {
  to: string
}

/**
 * A same-app navigation link. Renders a real `<a href>` (so middle-click/ctrl-click/"open in new
 * tab", screen reader link semantics and right-click "copy link" all keep working), but intercepts a
 * plain left click to go through navigate() instead of a full page reload.
 */
export function InternalLink({ to, onClick, children, ...anchorProps }: InternalLinkProps) {
  const handleClick = (event: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(event)

    if (event.defaultPrevented || event.button !== 0) return
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return

    event.preventDefault()
    navigate(to)
  }

  return (
    <a href={to} onClick={handleClick} {...anchorProps}>
      {children}
    </a>
  )
}
