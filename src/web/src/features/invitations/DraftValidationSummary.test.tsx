import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { InvitationDraftValidationReport } from '../../api/generated/client'
import { DraftValidationSummary } from './DraftValidationSummary'

const report: InvitationDraftValidationReport = {
  invitationId: '11111111-1111-1111-1111-111111111111',
  templateKey: 'zamansiz-dugun',
  rendererVersion: 1,
  invitationRevision: 2,
  contentRevision: 3,
  templateSelected: true,
  templateAvailable: true,
  requiredFields: [
    { field: 'headline', isRecognized: true, isPresent: false },
    { field: 'startsAt', isRecognized: true, isPresent: true },
  ],
  recommendedFields: [
    { field: 'venue.name', isRecognized: true, isPresent: false },
  ],
}

describe('DraftValidationSummary', () => {
  it('separates required blockers from recommended warnings', () => {
    render(<DraftValidationSummary
      report={report}
      loading={false}
      failed={false}
      hasUnsavedChanges={false}
      onRetry={() => undefined}
    />)

    expect(screen.getByRole('heading', { name: 'Gerekli alanlar' })).toBeTruthy()
    expect(screen.getByText('Davetiye başlığı')).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Önerilen alanlar' })).toBeTruthy()
    expect(screen.getByText('Mekân adı')).toBeTruthy()
    expect(screen.getByText(/Yayınla adımında yayın hakkınız/i)).toBeTruthy()
  })

  it('fails closed when the selected template is unavailable', () => {
    render(<DraftValidationSummary
      report={{ ...report, templateAvailable: false, requiredFields: [] }}
      loading={false}
      failed={false}
      hasUnsavedChanges={false}
      onRetry={() => undefined}
    />)

    expect(screen.getByRole('heading', { name: 'Şablon doğrulanamadı' })).toBeTruthy()
    expect(screen.getByText(/rapor tamamlanmış sayılmaz/i)).toBeTruthy()
  })

  it('offers an explicit retry when the report cannot be loaded', () => {
    const retry = vi.fn()
    render(<DraftValidationSummary
      report={null}
      loading={false}
      failed
      hasUnsavedChanges={false}
      onRetry={retry}
    />)

    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(retry).toHaveBeenCalledOnce()
  })
})
