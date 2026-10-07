import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { PrivacyInformationPage, TermsInformationPage } from './PrivacyInformationPages'

describe('privacy and terms information pages', () => {
  it('marks the privacy notice as a legal-review draft and describes separate opt-in/no optional tracking', () => {
    render(<PrivacyInformationPage />)
    expect(screen.getByRole('heading', { name: /hizmet bildirimi ve gizlilik/i })).toBeTruthy()
    expect(screen.getByText(/Phase 11’de yetkin hukuk incelemesinden geçirilecek/i)).toBeTruthy()
    expect(screen.getByText(/pazarlama izninden ayrıdır/i)).toBeTruthy()
    expect(screen.getByText(/isteğe bağlı analiz veya reklam takibi etkinleştirilmez/i)).toBeTruthy()
  })

  it('marks the terms page as a draft and links back to privacy information', () => {
    render(<TermsInformationPage />)
    expect(screen.getByRole('heading', { name: 'Kullanım koşulları' })).toBeTruthy()
    expect(screen.getByText(/tamamlanmış bir sözleşme değildir/i)).toBeTruthy()
    expect(screen.getByRole('link', { name: /hizmet bildirimi ve gizlilik bilgisi/i })).toBeTruthy()
  })
})
