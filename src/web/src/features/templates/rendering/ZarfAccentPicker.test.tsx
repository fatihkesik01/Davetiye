import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ZarfAccentPicker } from './ZarfAccentPicker'
import { zarfAccentKeys } from './zarfAccent'

describe('ZarfAccentPicker', () => {
  it('offers every curated colour with an accessible name and reports the chosen key', () => {
    const onChange = vi.fn()
    render(<ZarfAccentPicker value={undefined} onChange={onChange} />)

    expect(screen.getAllByRole('radio')).toHaveLength(zarfAccentKeys.length)
    expect((screen.getByRole('radio', { name: 'Adaçayı' }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Lavanta' }))
    expect(onChange).toHaveBeenCalledWith('lavanta')
  })

  it('treats an unknown stored value as the default colour', () => {
    render(<ZarfAccentPicker value="#ff0000" onChange={() => undefined} />)
    expect((screen.getByRole('radio', { name: 'Adaçayı' }) as HTMLInputElement).checked).toBe(true)
  })
})
