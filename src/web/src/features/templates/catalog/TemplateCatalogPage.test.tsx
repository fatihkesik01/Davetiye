import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { TemplateCatalogItem } from '../../../api/generated/client'
import { TemplateCatalogPage } from './TemplateCatalogPage'

const premiumTemplate: TemplateCatalogItem = {
  key: 'zamansiz-dugun',
  description: 'Zarif ve sade bir davetiye.',
  name: 'Zamansız Düğün',
  category: 'Düğün',
  isPremium: true,
  rendererVersion: 1,
  previewImageUrl: '/template-previews/zamansiz-dugun.jpg',
  supportedModules: ['hero', 'venue'],
  requiredFields: ['headline'],
  recommendedFields: ['venue.name'],
}

describe('TemplateCatalogPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('announces its loading state', () => {
    vi.stubGlobal('fetch', vi.fn(() => new Promise(() => undefined)))
    render(<TemplateCatalogPage />)

    expect(screen.getByRole('status').textContent).toContain('Şablonlar yükleniyor')
  })

  it('shows a retryable error state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('offline')))
    render(<TemplateCatalogPage />)

    expect(await screen.findByRole('heading', { name: 'Şablonlar yüklenemedi' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
  })

  it('shows the empty catalog state', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([])))
    render(<TemplateCatalogPage />)

    expect(await screen.findByText('Şu anda gösterilebilecek aktif şablon bulunmuyor.')).toBeTruthy()
  })

  it('labels premium templates and links to their public demo', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([premiumTemplate])))
    render(<TemplateCatalogPage />)

    expect(await screen.findByText('Premium')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Demoyu incele' }).getAttribute('href'))
      .toBe('/sablonlar/zamansiz-dugun')
    expect(screen.getByText('Zarif ve sade bir davetiye.')).toBeTruthy()
  })

  it('renders the selected demo with the code-owned renderer', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse([premiumTemplate])))
    const { container } = render(<TemplateCatalogPage demoTemplateKey="zamansiz-dugun" />)

    expect(await screen.findByRole('heading', { name: 'Zamansız Düğün' })).toBeTruthy()
    expect(container.querySelector('[data-renderer="zamansiz-dugun@1"]')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Bu şablonla başla' }).getAttribute('href'))
      .toBe('/panel/davetiyeler/yeni?template=zamansiz-dugun')
  })
})

function jsonResponse(value: unknown): Response {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}
