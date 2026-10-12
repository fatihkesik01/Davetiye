import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ZarfAccent2Picker, ZarfAmbiancePicker, ZarfGardenPicker, ZarfLookPickers } from './ZarfLookPickers'
import { zarfAccent2Keys } from './zarfAccent'
import { zarfAmbianceKeys, zarfThemePickerKeys } from './zarfTheme'

describe('zarf look pickers', () => {
  it('offers all eight ambiances with accessible names, defaults to the morning garden and reports the key', () => {
    const onChange = vi.fn()
    render(<ZarfAmbiancePicker value={undefined} onChange={onChange} />)
    expect(zarfAmbianceKeys).toHaveLength(8)
    expect(screen.getAllByRole('radio')).toHaveLength(8)
    expect((screen.getByRole('radio', { name: 'Sabah bahçesi' }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Lila sis' }))
    expect(onChange).toHaveBeenCalledWith('lila')
  })

  it('offers the second colour (seal and lettering), defaulting to gold', () => {
    const onChange = vi.fn()
    render(<ZarfAccent2Picker value="#fff" onChange={onChange} />)
    expect(screen.getAllByRole('radio')).toHaveLength(zarfAccent2Keys.length)
    expect((screen.getByRole('radio', { name: 'Altın' }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Gümüş' }))
    expect(onChange).toHaveBeenCalledWith('gumus')
  })

  it('offers the picker themes, treats unknown garden values as the rose garden and reports the key', () => {
    const onChange = vi.fn()
    render(<ZarfGardenPicker value="<b>" onChange={onChange} />)
    expect(screen.getAllByRole('radio')).toHaveLength(zarfThemePickerKeys.length)
    expect((screen.getByRole('radio', { name: 'Gül bahçesi' }) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: 'Papatya ve kelebek' }))
    expect(onChange).toHaveBeenCalledWith('papatya-kelebek')
  })

  it('stacks the four pickers (envelope colour, second colour, ambiance, garden) for the editor', () => {
    const calls: string[] = []
    render(<ZarfLookPickers values={{}} onAccent={key => calls.push(`accent:${key}`)} onAccent2={key => calls.push(`accent2:${key}`)} onAmbiance={key => calls.push(`amb:${key}`)} onGarden={key => calls.push(`garden:${key}`)} />)
    for (const name of ['Zarf rengi', 'Mühür, yazı ve astar', 'Arka plan havası', 'Dekor teması']) expect(screen.getByRole('group', { name })).toBeTruthy()
    fireEvent.click(within(screen.getByRole('group', { name: 'Zarf rengi' })).getByRole('radio', { name: 'Bordo' }))
    fireEvent.click(screen.getByRole('radio', { name: 'Bakır' }))
    fireEvent.click(screen.getByRole('radio', { name: 'Yıldızlı gece' }))
    fireEvent.click(screen.getByRole('radio', { name: 'Lale ve uğur böceği' }))
    expect(calls).toEqual(['accent:bordo', 'accent2:bakir', 'amb:gece', 'garden:lale-ugurbocegi'])
  })
})
