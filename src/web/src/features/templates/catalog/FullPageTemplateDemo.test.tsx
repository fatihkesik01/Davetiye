import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { zarfDemoPhotoUrl, zarfPalettes } from '../rendering/zarfAccent'
import { FullPageTemplateDemo } from './FullPageTemplateDemo'

describe('FullPageTemplateDemo (zarf-davetiye)', () => {
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
})
