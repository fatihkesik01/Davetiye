import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

describe('test environment', () => {
  it('renders test content', () => {
    render(<p>Cleanup doğrulama işareti</p>)

    expect(screen.getByText('Cleanup doğrulama işareti')).toBeTruthy()
  })

  it('starts the next test with an empty document body', () => {
    expect(screen.queryByText('Cleanup doğrulama işareti')).toBeNull()
  })
})
