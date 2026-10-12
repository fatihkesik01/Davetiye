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

  it('keeps the invitation out of the accessibility tree and layout before opening, and draws the gate scene in two layers', () => {
    const { container } = renderGate()
    expect(container.querySelector('.dantel__inside')?.getAttribute('aria-hidden')).toBe('true')
    expect(container.querySelector('.dantel--shown')).toBeNull()
    expect(container.querySelectorAll('.dantel__layer')).toHaveLength(2)
    expect(container.querySelector('.dantel__layer--front .dantel-gate__scene')).not.toBeNull()
    expect(container.querySelectorAll('.dantel-gate__leaf')).toHaveLength(2)
    expect(container.querySelector('.dantel__medallion')?.textContent).toContain('HF')
  })

  it('opens with a single class flip, rolls the paper back up on closing and removes it from layout afterwards', async () => {
    const { container } = renderGate()
    fireEvent.click(container.querySelector('.dantel__medallion')!)
    expect(container.querySelector('.dantel--open.dantel--shown')).not.toBeNull()
    expect(container.querySelector('.dantel__inside')?.getAttribute('aria-hidden')).toBe('false')
    expect(container.querySelector('.dantel')?.hasAttribute('data-animating')).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Kapıları kapat' }))
    expect(container.querySelector('.dantel--open')).toBeNull()
    expect(container.querySelector('.dantel--closing.dantel--shown')).not.toBeNull()
    await waitFor(() => expect(container.querySelector('.dantel--shown')).toBeNull(), { timeout: 1500 })
  })

  it('is instant under reduced motion, and a controlled host can open and close it without inline buttons', () => {
    vi.stubGlobal('matchMedia', () => ({ matches: true }))
    const changes: boolean[] = []
    const props = { templateKey: 'dantel-davetiye', rendererVersion: 1, model: getTemplateDemoModel('dantel-davetiye')!, previewContext: 'public' as const, hideInlineControls: true, onOpenChange: (value: boolean) => { changes.push(value) } }
    const view = renderGate({ open: false, hideInlineControls: true, onOpenChange: props.onOpenChange })
    expect(screen.queryByRole('button', { name: 'Kapıları aç' })).toBeNull()
    view.rerender(<InvitationRenderer {...props} open />)
    expect(view.container.querySelector('.dantel--open')).not.toBeNull()
    act(() => { view.rerender(<InvitationRenderer {...props} open={false} />) })
    expect(view.container.querySelector('.dantel--shown')).toBeNull()
    expect(changes).toEqual([true, false])
  })
})
