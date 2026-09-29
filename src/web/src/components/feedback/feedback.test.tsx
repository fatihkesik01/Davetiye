import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AppErrorBoundary } from './AppErrorBoundary'
import { LoadingState } from './LoadingState'
import { NotFoundPage } from './NotFoundPage'

function BrokenComponent(): never {
  throw new Error('Expected test failure')
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('global feedback states', () => {
  it('announces loading in Turkish', () => {
    render(<LoadingState />)

    expect(screen.getByRole('status').textContent).toContain('Yükleniyor…')
  })

  it('renders an accessible not-found state', () => {
    render(<NotFoundPage />)

    expect(
      screen.getByRole('heading', { name: 'Sayfa bulunamadı' }),
    ).toBeTruthy()
    expect(screen.getByRole('main').getAttribute('tabindex')).toBe('-1')
  })

  it('contains rendering failures in the application boundary', () => {
    vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const onError = vi.fn()

    render(
      <AppErrorBoundary onError={onError}>
        <BrokenComponent />
      </AppErrorBoundary>,
    )

    expect(screen.getByRole('alert').textContent).toContain('Bir sorun oluştu')
    expect(onError).toHaveBeenCalledOnce()
  })
})
