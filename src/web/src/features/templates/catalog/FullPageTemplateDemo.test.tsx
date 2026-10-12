import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { zarfDemoPhotoUrl } from '../rendering/zarfAccent'
import { FullPageTemplateDemo } from './FullPageTemplateDemo'

describe('FullPageTemplateDemo bar', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders the zarf invitation as a page with a start link and a customise panel that Escape closes', () => {
    const { container } = render(<FullPageTemplateDemo templateKey="zarf-davetiye" />)
    expect(screen.getByRole('link', { name: 'Bu şablonla başla' }).getAttribute('href')).toBe('/panel/davetiyeler/yeni?template=zarf-davetiye')
    expect(container.querySelector('.template-demo')).toBeNull()
    expect(container.querySelector('.zarf__photo img')?.getAttribute('src')).toBe(zarfDemoPhotoUrl)
    expect(screen.getByRole('article', { name: 'Hande & Fatih' })).toBeTruthy()

    const toggle = screen.getByRole('button', { name: 'Özelleştir' })
    expect(toggle.getAttribute('aria-expanded')).toBe('false')
    expect(screen.queryByRole('dialog')).toBeNull()
    fireEvent.click(toggle)
    expect(toggle.getAttribute('aria-expanded')).toBe('true')
    expect(screen.getByRole('dialog')).toBeTruthy()
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(toggle)
  })

  it.each([
    ['zarf-davetiye', 'Zarfı aç', 'Zarfı kapat'],
    ['dantel-davetiye', 'Kapıları aç', 'Kapıları kapat'],
  ])('%s: one control opens or closes (never both), the content is hidden before opening and a reload starts closed', (templateKey, openName, closeName) => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const first = render(<FullPageTemplateDemo templateKey={templateKey} />)
    const bar = screen.getByRole('region')

    // Closed: only the open control, with its full accessible name (no duplicated label text).
    const open = screen.getByRole('button', { name: openName })
    expect(screen.queryByRole('button', { name: closeName })).toBeNull()
    expect(open.textContent).toBe(openName)
    expect(bar.querySelectorAll('.template-fullpage-demo__toggle')).toHaveLength(1)

    act(() => { fireEvent.click(open) })
    // Open: the same slot now holds the close control; the open control is gone.
    expect(screen.queryByRole('button', { name: openName })).toBeNull()
    const close = screen.getByRole('button', { name: closeName })
    expect(bar.querySelectorAll('.template-fullpage-demo__toggle')).toHaveLength(1)
    expect(document.activeElement).toBe(close)

    act(() => { fireEvent.click(close) })
    expect(screen.queryByRole('button', { name: closeName })).toBeNull()
    // Focus returns to the open control.
    expect(document.activeElement).toBe(screen.getByRole('button', { name: openName }))

    act(() => { fireEvent.click(screen.getByRole('button', { name: openName })) })
    first.unmount() // a reload: all state is in memory only
    render(<FullPageTemplateDemo templateKey={templateKey} />)
    expect(screen.getByRole('button', { name: openName })).toBeTruthy()
    expect(screen.queryByRole('button', { name: closeName })).toBeNull()
    expect(localStorage.length + sessionStorage.length).toBe(0)
  })
})
