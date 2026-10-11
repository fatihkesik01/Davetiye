import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { zarfDemoPhotoUrl, zarfPalettes } from '../rendering/zarfAccent'
import { FullPageTemplateDemo } from './FullPageTemplateDemo'

describe('FullPageTemplateDemo (zarf-davetiye)', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders the invitation as a page with a back link, start link and a live colour picker', () => {
    const { container } = render(<FullPageTemplateDemo templateKey="zarf-davetiye" />)

    expect(screen.getByRole('link', { name: /Tüm şablonlar/ }).getAttribute('href')).toBe('/sablonlar')
    expect(screen.getByRole('link', { name: 'Bu şablonla başla' }).getAttribute('href')).toBe('/panel/davetiyeler/yeni?template=zarf-davetiye')
    expect(container.querySelector('.template-demo')).toBeNull()
    expect(container.querySelector('.zarf__photo img')?.getAttribute('src')).toBe(zarfDemoPhotoUrl)
    expect(screen.getByRole('article', { name: 'Hande & Fatih' })).toBeTruthy()

    const article = () => container.querySelector('article') as HTMLElement
    expect(screen.getByRole('group', { name: 'Zarf rengi' })).toBeTruthy()
    expect(article().style.getPropertyValue('--z-base')).toBe(zarfPalettes.adacayi.base)
    fireEvent.click(screen.getByRole('radio', { name: 'Bordo' }))
    expect(article().style.getPropertyValue('--z-base')).toBe(zarfPalettes.bordo.base)
  })

  it('closes from the bottom bar only (no inline close button), and a reload starts closed with nothing stored', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const first = render(<FullPageTemplateDemo templateKey="zarf-davetiye" />)
    expect(screen.queryByRole('button', { name: 'Zarfı kapat' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))

    const bar = screen.getByRole('region', { name: 'Demo renk seçimi' })
    expect(first.container.querySelector('.zarf__close')).toBeNull()
    expect(first.container.querySelector('.zarf__details')).not.toBeNull()
    act(() => { fireEvent.click(within(bar).getByRole('button', { name: 'Zarfı kapat' })) })
    expect(first.container.querySelector('.zarf--open')).toBeNull()
    expect(first.container.querySelector('.zarf__details')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Zarfı kapat' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Davetiyeyi Aç' }))
    first.unmount() // a reload: all state is in memory only
    const second = render(<FullPageTemplateDemo templateKey="zarf-davetiye" />)
    expect(second.container.querySelector('.zarf--open')).toBeNull()
    expect(second.container.querySelector('.zarf__details')).toBeNull()
    expect(localStorage.length + sessionStorage.length).toBe(0)
  })
})
