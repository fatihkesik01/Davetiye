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
    // The cover control stays in its fixed slot but is disabled once opened (no layout shift).
    expect((screen.getByRole('button', { name: 'Davetiyeyi Aç' }) as HTMLButtonElement).disabled).toBe(true)
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
    expect(card.querySelector('.zarf__date-day')?.textContent).toBe('10')
    expect(card.querySelector('.zarf__date-month')?.textContent).toBe('Ekim 2026')
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

  it('shows the decorative botanical frame only while open: aria-hidden, no interaction, gone after closing', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const { container } = renderZarf()
    expect(container.querySelector('.zarf-garden')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))
    const garden = container.querySelector('.zarf-garden')
    expect(garden?.getAttribute('aria-hidden')).toBe('true')
    expect(garden?.querySelectorAll('.zarf-garden__side')).toHaveLength(2)
    expect(garden?.querySelector('a, button, input, [tabindex]')).toBeNull()
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Zarfı kapat' })) })
    expect(container.querySelector('.zarf-garden')).toBeNull()
    vi.unstubAllGlobals()
  })

  it('maps ambiance, garden and second-colour keys, falls back to the defaults for unknown values', () => {
    const base = getTemplateDemoModel('zarf-davetiye')!
    expect(normalizeDraftContent({ ambiance: 'gece', garden: 'kiraz', accentColor2: 'gumus' })).toMatchObject({ ambiance: 'gece', garden: 'kiraz', accentColor2: 'gumus' })
    expect(normalizeDraftContent({ ambiance: 'url(x)', garden: '<script>', accentColor2: '#ff0000' })).toMatchObject({ ambiance: 'sabah', garden: 'gul', accentColor2: 'altin' })
    // A legacy `flora` still maps onto a garden when no garden is stored.
    expect(normalizeDraftContent({ flora: 'lavanta' }).garden).toBe('lavanta-ari')
    expect(normalizeDraftContent({}).ambiance).toBeUndefined()
    const known = renderZarf({ ...base, ambiance: 'orman', garden: 'lale-ugurbocegi' })
    const cls = known.container.querySelector('article')?.className ?? ''
    expect(cls).toContain('zarf--amb-orman')
    expect(cls).toContain('zarf--garden-lale-ugurbocegi')
    known.unmount()
    const none = renderZarf({ ...base, ambiance: undefined, garden: undefined })
    expect(none.container.querySelector('article')?.className).toContain('zarf--amb-sabah')
    expect(none.container.querySelector('article')?.className).toContain('zarf--garden-gul')
  })

  it.each([
    ['gul', null], ['papatya-kelebek', 'butterfly'], ['lale-ugurbocegi', 'ladybug'], ['lavanta-ari', 'bee'], ['kelebekler', 'butterfly'],
  ] as const)('garden %s shows its creatures (%s) only while open and leaves none behind', (garden, critter) => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const { container } = renderZarf({ ...getTemplateDemoModel('zarf-davetiye')!, garden })
    expect(container.querySelector('.zarf-critter')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))
    if (critter) expect(container.querySelectorAll(`.zarf-critter--${critter}`)).toHaveLength(6)
    else expect(container.querySelector('.zarf-critter')).toBeNull()
    expect(container.querySelector('.zarf-garden')?.getAttribute('aria-hidden')).toBe('true')
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Zarfı kapat' })) })
    expect(container.querySelector('.zarf-critter')).toBeNull()
    expect(container.querySelector('.zarf-garden')).toBeNull()
    vi.unstubAllGlobals()
  })

  it('cherry blossom drops petals only while open, and the seal is two halves that crack apart on opening', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const { container } = renderZarf({ ...getTemplateDemoModel('zarf-davetiye')!, garden: 'kiraz' })
    expect(container.querySelectorAll('.zarf__seal-half')).toHaveLength(2)
    expect(container.querySelector('.zarf-petal')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))
    expect(container.querySelectorAll('.zarf-petal')).toHaveLength(6)
    expect(container.querySelector('.zarf--open .zarf__seal-half')).not.toBeNull()
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Zarfı kapat' })) })
    expect(container.querySelector('.zarf-petal')).toBeNull()
    vi.unstubAllGlobals()
  })
})
