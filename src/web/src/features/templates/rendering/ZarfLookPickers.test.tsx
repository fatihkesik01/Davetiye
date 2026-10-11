import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ZarfAmbiancePicker, ZarfFloraPicker, ZarfLookPickers } from './ZarfLookPickers'
import { zarfAmbianceKeys, zarfFloraKeys } from './zarfTheme'

describe('zarf look pickers', () => {
  it('offers every ambiance with an accessible name, defaults to the morning garden and reports the key', () => {
    const onChange = vi.fn()
    render(<ZarfAmbiancePicker value={undefined} onChange={onChange} />)
    expect(screen.getAllByRole('radio')).toHaveLength(zarfAmbianceKeys.length)
    expect((screen.getByRole('radio', { name: 'Sabah bahçesi' }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Pembe şafak' }))
    expect(onChange).toHaveBeenCalledWith('safak')
  })

  it('offers every flora, treats unknown values as the rose and reports the key', () => {
    const onChange = vi.fn()
    render(<ZarfFloraPicker value="<b>" onChange={onChange} />)
    expect(screen.getAllByRole('radio')).toHaveLength(zarfFloraKeys.length)
    expect((screen.getByRole('radio', { name: 'Gül' }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Kiraz çiçeği' }))
    expect(onChange).toHaveBeenCalledWith('kiraz')
  })

  it('stacks the three pickers (colour, ambiance, flowers) for the editor', () => {
    const calls: string[] = []
    render(<ZarfLookPickers values={{}} onAccent={key => calls.push(`accent:${key}`)} onAmbiance={key => calls.push(`amb:${key}`)} onFlora={key => calls.push(`flora:${key}`)} />)
    expect(screen.getByRole('group', { name: 'Zarf rengi' })).toBeTruthy()
    expect(screen.getByRole('group', { name: 'Ortam' })).toBeTruthy()
    expect(screen.getByRole('group', { name: 'Çiçek' })).toBeTruthy()
    fireEvent.click(screen.getByRole('radio', { name: 'Bordo' }))
    fireEvent.click(screen.getByRole('radio', { name: 'Yıldızlı gece' }))
    fireEvent.click(screen.getByRole('radio', { name: 'Lale' }))
    expect(calls).toEqual(['accent:bordo', 'amb:gece', 'flora:lale'])
  })
})
