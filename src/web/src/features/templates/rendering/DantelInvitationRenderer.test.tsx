import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { getTemplateDemoModel } from './demoData'
import { InvitationRenderer } from './InvitationRenderer'

const renderGate = (props: Partial<Parameters<typeof InvitationRenderer>[0]> = {}) => render(<InvitationRenderer
  templateKey="dantel-davetiye"
  rendererVersion={1}
  model={{ ...getTemplateDemoModel('dantel-davetiye')!, garden: 'saray-kapisi' }}
  previewContext="public"
  {...props}
/>)

describe('dantel (palace gate) renderer', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('renders no invitation content before the doors are opened and draws the palace gate scene', () => {
    const { container } = renderGate()
    expect(container.querySelector('.dantel__content')).toBeNull()
    expect(container.querySelector('.dantel-gate__scene')).not.toBeNull()
    expect(container.querySelectorAll('.dantel-gate__leaf')).toHaveLength(2)
    expect(container.querySelector('.dantel__medallion')?.textContent).toContain('HF')
  })

  it('opens from the medallion, mounts the content, and fades it out fast on closing while the doors keep closing', async () => {
    const { container } = renderGate()
    fireEvent.click(container.querySelector('.dantel__medallion')!)
    expect(container.querySelector('.dantel--open')).not.toBeNull()
    await waitFor(() => expect(container.querySelector('.dantel__content')).not.toBeNull(), { timeout: 800 })
    await waitFor(() => expect(container.querySelector('.dantel--expanded')).not.toBeNull(), { timeout: 1200 })

    fireEvent.click(screen.getByRole('button', { name: 'Kapıları kapat' }))
    expect(container.querySelector('.dantel--open')).toBeNull()
    expect(container.querySelector('.dantel__content--leaving')).not.toBeNull()
    await waitFor(() => expect(container.querySelector('.dantel__content')).toBeNull(), { timeout: 600 })
    expect(container.querySelector('.dantel--expanded')).toBeNull()
  })

  it('is instant under reduced motion, and a controlled host can open and close it without inline buttons', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const changes: boolean[] = []
    const view = renderGate({ open: false, hideInlineControls: true, onOpenChange: value => changes.push(value) })
    expect(screen.queryByRole('button', { name: 'Kapıları aç' })).toBeNull()
    view.rerender(<InvitationRenderer templateKey="dantel-davetiye" rendererVersion={1} model={getTemplateDemoModel('dantel-davetiye')!} previewContext="public" open hideInlineControls onOpenChange={value => changes.push(value)} />)
    expect(view.container.querySelector('.dantel__content')).not.toBeNull()
    act(() => { view.rerender(<InvitationRenderer templateKey="dantel-davetiye" rendererVersion={1} model={getTemplateDemoModel('dantel-davetiye')!} previewContext="public" open={false} hideInlineControls onOpenChange={value => changes.push(value)} />) })
    expect(view.container.querySelector('.dantel__content')).toBeNull()
    expect(changes).toEqual([true, false])
  })
})
