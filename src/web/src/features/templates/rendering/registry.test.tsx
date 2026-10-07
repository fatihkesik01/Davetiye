import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { getTemplateDemoModel } from './demoData'
import { InvitationRenderer } from './InvitationRenderer'
import { resolveTemplateRenderer, templateRendererRegistrations } from './registry'

describe('template renderer registry', () => {
  it('retains eight V1 pins alongside eight additive V2 pins and rejects unknown versions', () => {
    expect(templateRendererRegistrations).toHaveLength(16)
    expect(new Set(templateRendererRegistrations.map(item => item.templateKey)).size).toBe(8)

    for (const registration of templateRendererRegistrations) {
      expect(resolveTemplateRenderer(registration.templateKey, registration.rendererVersion)).toBe(registration)
      expect(getTemplateDemoModel(registration.templateKey)).not.toBeNull()
    }
    expect(resolveTemplateRenderer('zamansiz-dugun', 999)).toBeNull()
  })

  it('only V2 renders additive sections while old Published V1 remains unchanged', () => {
    const model = { ...getTemplateDemoModel('zamansiz-dugun')!, announcement: 'Yeni duyuru', contacts: [{ name: 'Deniz', phone: '+905551234567' }], faqs: [{ question: 'Otopark?', answer: 'Var.' }] }
    const old = render(<InvitationRenderer templateKey="zamansiz-dugun" rendererVersion={1} model={model} previewContext="public" />)
    expect(old.container.textContent).not.toContain('Yeni duyuru')
    old.unmount()
    render(<InvitationRenderer templateKey="zamansiz-dugun" rendererVersion={2} model={model} previewContext="public" />)
    expect(screen.getByText('Yeni duyuru')).toBeTruthy()
    expect(screen.getByRole('link', { name: /WhatsApp/ }).getAttribute('href')).toBe('https://wa.me/905551234567')
  })

  it.each(templateRendererRegistrations.map(item => [item.templateKey, item.rendererVersion] as const))(
    'renders %s@%s as a semantic, deterministic visual fixture',
    (templateKey, rendererVersion) => {
      const model = getTemplateDemoModel(templateKey)!
      const { container } = render(<InvitationRenderer
        templateKey={templateKey}
        rendererVersion={rendererVersion}
        model={model}
        previewContext="demo"
      />)

      expect(screen.getByRole('article', { name: model.headline })).toBeTruthy()
      expect(container.firstElementChild?.getAttribute('data-renderer')).toBe(`${templateKey}@${rendererVersion}`)
      expect(container.querySelector('script')).toBeNull()
      expect(container.querySelector('[dangerouslysetinnerhtml]')).toBeNull()
      expect(container.innerHTML).toMatchSnapshot()
    },
  )

  it('uses the identical renderer and normalized model for demo and Creator preview', () => {
    const model = getTemplateDemoModel('zamansiz-dugun')!
    const demo = render(<InvitationRenderer templateKey="zamansiz-dugun" rendererVersion={1} model={model} previewContext="demo" />)
    const creator = render(<InvitationRenderer templateKey="zamansiz-dugun" rendererVersion={1} model={model} previewContext="creator" />)

    expect(demo.container.querySelector('article')?.outerHTML)
      .toBe(creator.container.querySelector('article')?.outerHTML)
  })

  it('fails safely when a pinned renderer is unavailable', () => {
    const model = getTemplateDemoModel('zamansiz-dugun')!
    render(<InvitationRenderer templateKey="zamansiz-dugun" rendererVersion={9} model={model} previewContext="creator" />)
    expect(screen.getByRole('status').textContent).toContain('kullanılamıyor')
  })
})
