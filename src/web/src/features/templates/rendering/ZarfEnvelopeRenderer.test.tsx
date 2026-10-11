import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

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
})
