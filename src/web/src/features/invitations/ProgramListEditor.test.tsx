import { fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'

import { programLimits, type EditableProgramItem } from './draftModel'
import { ProgramListEditor } from './ProgramListEditor'

function Harness({ initial = [] as EditableProgramItem[], onChange }: { initial?: EditableProgramItem[]; onChange?: (items: EditableProgramItem[]) => void }) {
  const [items, setItems] = useState(initial)
  return <ProgramListEditor items={items} onChange={next => { setItems(next); onChange?.(next) }} />
}

const item = (title: string, startsAtLocal = ''): EditableProgramItem => ({ title, description: '', startsAtLocal })

describe('ProgramListEditor', () => {
  it('adds, edits and removes lines with a visible counter and per-item accessible names', () => {
    let latest: EditableProgramItem[] = []
    render(<Harness onChange={items => { latest = items }} />)
    expect(screen.getByRole('status', { name: '' }).textContent).toContain('0 / 20')
    fireEvent.click(screen.getByRole('button', { name: 'Program satırı ekle' }))
    fireEvent.click(screen.getByRole('button', { name: 'Program satırı ekle' }))
    fireEvent.change(screen.getByLabelText('Program 2: başlık'), { target: { value: 'Yemek' } })
    fireEvent.change(screen.getByLabelText('Program 2: saat'), { target: { value: '2027-06-19T19:30' } })
    fireEvent.change(screen.getByLabelText('Program 2: açıklama'), { target: { value: 'Bahçede servis' } })
    expect(latest[1]).toEqual({ title: 'Yemek', description: 'Bahçede servis', startsAtLocal: '2027-06-19T19:30' })
    expect(screen.getByText('2 / 20 program satırı')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Program 1: kaldır' }))
    expect(latest).toHaveLength(1)
    expect(latest[0]!.title).toBe('Yemek')
    expect(screen.getByText('1 / 20 program satırı')).toBeTruthy()
  })

  it('reorders with keyboard-reachable buttons and disables the ends', () => {
    let latest: EditableProgramItem[] = []
    render(<Harness initial={[item('A'), item('B'), item('C')]} onChange={items => { latest = items }} />)
    expect((screen.getByRole('button', { name: 'Program 1: yukarı taşı' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'Program 3: aşağı taşı' }) as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(screen.getByRole('button', { name: 'Program 3: yukarı taşı' }))
    expect(latest.map(entry => entry.title)).toEqual(['A', 'C', 'B'])
    fireEvent.click(screen.getByRole('button', { name: 'Program 1: aşağı taşı' }))
    expect(latest.map(entry => entry.title)).toEqual(['C', 'A', 'B'])
    expect((screen.getByLabelText('Program 1: başlık') as HTMLInputElement).value).toBe('C')
  })

  it('keeps the creator order, hints when times are out of order and sorts on request', () => {
    let latest: EditableProgramItem[] = []
    render(<Harness initial={[item('Yemek', '2027-06-19T19:00'), item('Karşılama', '2027-06-19T17:00')]} onChange={items => { latest = items }} />)
    expect(screen.getByText(/Saatler sıralı değil/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Saate göre sırala' }))
    expect(latest.map(entry => entry.title)).toEqual(['Karşılama', 'Yemek'])
    expect(screen.queryByText(/Saatler sıralı değil/)).toBeNull()
  })

  it('stops at the backend maximum of 20 lines with a clear message', () => {
    render(<Harness initial={Array.from({ length: programLimits.maxItems }, (_, index) => item(`Satır ${index + 1}`))} />)
    expect((screen.getByRole('button', { name: 'Program satırı ekle' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('En fazla 20 program satırı ekleyebilirsiniz.')).toBeTruthy()
    expect(screen.getByText('20 / 20 program satırı')).toBeTruthy()
  })

  it('shows the backend validation messages for over-long and control-character text', () => {
    render(<Harness initial={[{ title: 'x'.repeat(201), description: 'y'.repeat(1001), startsAtLocal: '' }, { title: 'iki\nsatır', description: '', startsAtLocal: '' }]} />)
    expect(screen.getByText(/Başlık en fazla 200 karakter/)).toBeTruthy()
    expect(screen.getByText(/Açıklama en fazla 1000 karakter/)).toBeTruthy()
    expect(screen.getByText(/özel karakterler kullanılamaz/)).toBeTruthy()
    expect(screen.getByLabelText('Program 1: başlık').getAttribute('aria-invalid')).toBe('true')
  })
})
