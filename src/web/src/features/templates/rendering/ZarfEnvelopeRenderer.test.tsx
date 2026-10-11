import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Profiler } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { getTemplateDemoModel } from './demoData'
import { InvitationRenderer } from './InvitationRenderer'
import { normalizeDraftContent } from './model'
import { resolveZarfAccent, zarfDemoPhotoUrl, zarfPalettes } from './zarfAccent'

const renderZarf = (model = getTemplateDemoModel('zarf-davetiye')!) =>
  render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={model} previewContext="public" />)

describe('zarf-davetiye renderer', () => {
  it('starts closed and opens from the keyboard-reachable call to action', () => {
    const { container } = renderZarf()
    const button = screen.getByRole('button', { name: 'Davetiyeyi Aç' })
    expect(button.getAttribute('aria-expanded')).toBe('false')
    expect(container.querySelector('.zarf--open')).toBeNull()
    fireEvent.click(button)
    expect(container.querySelector('.zarf--open')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Davetiyeyi Aç' })).toBeNull()
  })

  it('renders nothing but the cover before opening, and the envelope itself is tappable', async () => {
    const model = { ...getTemplateDemoModel('zarf-davetiye')!, announcement: 'Gizli duyuru' }
    const { container } = renderZarf(model)
    expect(container.querySelector('.zarf__details')).toBeNull()
    expect(container.querySelector('.invitation-optional-sections')).toBeNull()
    expect(container.textContent).not.toContain('Gizli duyuru')
    expect(container.textContent).not.toContain('Günün akışı')
    fireEvent.click(container.querySelector('.zarf__envelope')!)
    expect(container.querySelector('.zarf--open')).not.toBeNull()
    expect(container.querySelector('.zarf__details')).toBeNull() // still mounting after the flap lifts
    await waitFor(() => expect(container.textContent).toContain('Gizli duyuru'))
  })

  it('reports open state and closes again (instantly with reduced motion), hiding the content', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const changes: boolean[] = []
    const model = getTemplateDemoModel('zarf-davetiye')!
    const { container } = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={model} previewContext="public" onOpenChange={value => changes.push(value)} />)
    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))
    expect(container.querySelector('.zarf__details')).not.toBeNull()
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Zarfı kapat' })) })
    expect(container.querySelector('.zarf--open')).toBeNull()
    expect(container.querySelector('.zarf__details')).toBeNull()
    expect(screen.getByRole('button', { name: 'Davetiyeyi Aç' })).toBeTruthy()
    expect(changes).toEqual([true, false])
    vi.unstubAllGlobals()
  })

  it('maps unknown or raw colour values to the default palette key', () => {
    expect(resolveZarfAccent('#ff0000')).toBe('adacayi')
    expect(resolveZarfAccent(undefined)).toBe('adacayi')
    expect(resolveZarfAccent('bordo')).toBe('bordo')
    expect(normalizeDraftContent({ accentColor: 'url(javascript:1)' }).accentColor).toBe('adacayi')
    const { container } = renderZarf({ ...getTemplateDemoModel('zarf-davetiye')!, accentColor: 'lavanta' })
    expect((container.querySelector('article') as HTMLElement).style.getPropertyValue('--z-base')).toBe(zarfPalettes.lavanta.base)
  })

  it('falls back to the bundled demo photo outside public pages, never on a public page', () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    for (const context of ['demo', 'creator'] as const) {
      const view = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={base} previewContext={context} />)
      const img = view.container.querySelector('.zarf__photo img')
      expect(img?.getAttribute('src')).toBe(zarfDemoPhotoUrl)
      expect(img?.getAttribute('alt')).toBeTruthy()
      view.unmount()
    }
    const pub = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={base} previewContext="public" />)
    expect(pub.container.querySelector('.zarf__photo img')).toBeNull()
    pub.unmount()
    const own = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={{ ...base, coverImageUrl: 'https://media.example/p.jpg' }} previewContext="creator" />)
    expect(own.container.querySelector('.zarf__photo img')?.getAttribute('src')).toBe('https://media.example/p.jpg')
  })

  it('shows a graceful placeholder without a photo and the photo when provided', () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    const without = renderZarf({ ...base, coverImageUrl: undefined })
    expect(without.container.querySelector('.zarf__photo img')).toBeNull() // public page: placeholder, never the stand-in
    expect(without.container.querySelector('.zarf__photo-placeholder')).not.toBeNull()
    without.unmount()
    const withPhoto = renderZarf({ ...base, coverImageUrl: 'https://media.example/photo.jpg' })
    expect(withPhoto.container.querySelector('.zarf__photo img')?.getAttribute('src')).toBe('https://media.example/photo.jpg')
  })

  it('opens and closes with a small, bounded number of renders (no render/effect loop)', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    let renders = 0
    const model = getTemplateDemoModel('zarf-davetiye')!
    const view = (open?: boolean) => <Profiler id="zarf" onRender={() => { renders += 1 }}>
      <InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={model} previewContext="public" open={open} />
    </Profiler>
    const { rerender } = render(view(false))
    const mounted = renders
    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))
    expect(renders - mounted).toBeLessThanOrEqual(3)
    // Controlled close from the host: one request, one reaction.
    rerender(view(false))
    const beforeClose = renders
    act(() => { rerender(view(true)) })
    act(() => { rerender(view(false)) })
    expect(renders - beforeClose).toBeLessThanOrEqual(6)
    vi.unstubAllGlobals()
  })

  it('puts only the names and the date on the heart card, with a visually hidden h1 for the page', () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    const { container } = renderZarf(base)
    const card = container.querySelector('.zarf__card')!
    expect(card.querySelector('.zarf__card-names')?.textContent).toBe('Hande & Fatih')
    expect(card.querySelector('.zarf__date-day')?.textContent).toBe('11')
    expect(card.querySelector('.zarf__date-month')?.textContent).toBe('Eylül 2027')
    expect(card.textContent).not.toContain('Zeytinlik')
    expect(card.textContent).not.toContain('17:00')
    expect(card.textContent).not.toContain('Cumartesi')
    expect(container.querySelector('.zarf__intro')).toBeNull()
    expect(screen.getByRole('heading', { level: 1 }).className).toContain('visually-hidden')
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe(base.headline)
  })

  it('scales long names down and falls back to the date placeholder when there is no date', () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    const { container } = renderZarf({ ...base, hostNames: ['Alexandra Maria', 'Konstantinos'], startsAt: undefined })
    expect(container.querySelector('.zarf__card-names--long')).not.toBeNull()
    expect(container.querySelector('.zarf__date--tbd')?.textContent).toBe('Tarih daha sonra duyurulacak')
    expect(container.querySelector('time.zarf__date')).toBeNull()
  })

  it('shows a warm default line when no message is set, and the Creator message when there is one', async () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    const first = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={{ ...base, message: undefined }} previewContext="public" initialOpen />)
    expect(first.container.querySelector('.zarf__hint')?.textContent).toContain('Sizi aramızda görmek bizi çok mutlu edecek.')
    first.unmount()
    const second = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={{ ...base, message: 'Kendi mesajım' }} previewContext="public" initialOpen />)
    expect(second.container.querySelector('.zarf__hint')?.textContent).toBe('Kendi mesajım')
  })

  it('fades the content out right away on closing and unmounts it within a fraction of a second', async () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    const { container } = render(<InvitationRenderer templateKey="zarf-davetiye" rendererVersion={1} model={base} previewContext="public" initialOpen />)
    fireEvent.click(screen.getByRole('button', { name: 'Zarfı kapat' }))
    expect(container.querySelector('.zarf--open')).toBeNull()
    expect(container.querySelector('.zarf__content--leaving')).not.toBeNull()
    await waitFor(() => expect(container.querySelector('.zarf__content')).toBeNull(), { timeout: 600 })
  })
})
