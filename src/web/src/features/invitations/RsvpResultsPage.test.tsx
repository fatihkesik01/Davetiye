import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type {
  CreatorRsvpSubmissionDetail,
  CreatorRsvpSubmissionPage,
  DavetiyeApiClient,
  InvitationRsvpConfiguration,
} from '../../api/generated/client'
import { RsvpResultsPage } from './RsvpResultsPage'

const config: InvitationRsvpConfiguration = {
  invitationId: 'invitation-1', enabled: true, revision: 1, effectiveState: 'Active',
  inputLimits: {
    maxActiveQuestionsPerInvitation: 20, maxQuestionPromptCharacters: 200,
    maxOptionLabelCharacters: 100, maxDefinedOptionsPerChoiceQuestion: 20,
    maxShortTextAnswerCharacters: 200, maxLongTextAnswerCharacters: 2000,
    minimumParticipantCount: 0, maximumParticipantCount: 20, maxMultipleChoiceSelections: 10,
  },
  questions: [
    { id: 'attendance', prompt: 'Katılacak mısınız?', type: 'YesNo', isRequired: true, sortOrder: 0, semanticRole: null, options: [] },
    { id: 'meal', prompt: 'Yeni yemek sorusu', type: 'SingleChoice', isRequired: false, sortOrder: 1, semanticRole: null, options: [{ id: 'vegetarian', label: 'Şu anki seçenek adı', sortOrder: 0 }] },
  ],
}

const submission: CreatorRsvpSubmissionPage = {
  invitationId: 'invitation-1', page: 1, pageSize: 25, totalCount: 1,
  summary: {
    responseCount: 1,
    questions: [
      { questionId: 'attendance', prompt: 'Katılacak mısınız?', type: 'YesNo', answeredCount: 1, valueCounts: [{ value: 'true', label: null, count: 1 }] },
      { questionId: 'meal', prompt: 'Yemek tercihi', type: 'SingleChoice', answeredCount: 1, valueCounts: [{ value: 'vegetarian', label: 'Gönderim anındaki seçenek adı', count: 1 }] },
    ],
    totalParticipants: null, participantCountAvailable: false,
  },
  submissions: [{ submissionId: 'response-1', submittedAt: '2026-10-01T10:00:00Z', updatedAt: '2026-10-01T11:00:00Z' }],
}

const detail: CreatorRsvpSubmissionDetail = {
  submissionId: 'response-1', submittedAt: '2026-10-01T10:00:00Z', updatedAt: '2026-10-01T11:00:00Z',
  answers: [
    { questionId: 'attendance', prompt: 'Katılacak mısınız?', type: 'YesNo', semanticRole: null, textValue: null, numberValue: null, booleanValue: true, selectedOptions: [] },
    { questionId: 'meal', prompt: 'Yemek tercihi', type: 'SingleChoice', semanticRole: null, textValue: null, numberValue: null, booleanValue: null, selectedOptions: [{ optionId: 'vegetarian', label: 'Vejetaryen' }] },
  ],
}

function mockApi() {
  let deleted = false
  return {
    getCreatorRsvpSubmissions: vi.fn().mockImplementation(async (_id, page, pageSize) => deleted
      ? { ...submission, page, pageSize, totalCount: 0, summary: { ...submission.summary, responseCount: 0, questions: [] }, submissions: [] }
      : { ...submission, page, pageSize }),
    getInvitationRsvp: vi.fn().mockResolvedValue(config),
    getCreatorRsvpSubmission: vi.fn().mockResolvedValue(detail),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf-token'),
    deleteCreatorRsvpSubmission: vi.fn().mockImplementation(async () => { deleted = true }),
  } as unknown as DavetiyeApiClient
}

describe('RsvpResultsPage', () => {
  it('shows private question aggregates, maps option IDs to labels, and discloses an answer on request', async () => {
    const api = mockApi()
    render(<RsvpResultsPage invitationId="invitation-1" api={api} />)

    expect(await screen.findByText('RSVP yanıtları')).toBeTruthy()
    expect(screen.queryByText('Toplam katılımcı')).toBeNull()
    expect(screen.getByText(/Etkin bir katılımcı sayısı sorusu bulunmadığı/)).toBeTruthy()
    expect(screen.getByText('Evet')).toBeTruthy()
    expect(screen.getByText('Gönderim anındaki seçenek adı')).toBeTruthy()
    expect(screen.queryByText('Şu anki seçenek adı')).toBeNull()
    expect(screen.getByText('Yemek tercihi')).toBeTruthy()
    expect(screen.getByText('Yanıt biçimi: Tek seçim')).toBeTruthy()
    expect(screen.queryByText('Yanıt verilmedi')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı görüntüle' }))
    expect(await screen.findByText('Son güncelleme: 1 Eki 2026 14:00')).toBeTruthy()
    expect(await screen.findByText('Evet', { selector: 'dd' })).toBeTruthy()
    expect(api.getCreatorRsvpSubmission).toHaveBeenCalledWith('invitation-1', 'response-1')
  })

  it('renders historical question variants and archived choice labels as distinct snapshots', async () => {
    const api = mockApi()
    const historical = {
      ...submission,
      summary: {
        ...submission.summary,
        participantCountAvailable: true,
        totalParticipants: 0,
        questions: [
          { questionId: 'meal', prompt: 'Önceki yemek sorusu', type: 'SingleChoice' as const, answeredCount: 2, valueCounts: [{ value: 'archived-option', label: 'Arşivlenmiş seçenek', count: 2 }] },
          { questionId: 'meal', prompt: 'Güncel soru', type: 'MultipleChoice' as const, answeredCount: 1, valueCounts: [{ value: 'vegetarian', label: 'Yanıttaki güncel ad', count: 1 }] },
        ],
      },
    }
    ;(api.getCreatorRsvpSubmissions as ReturnType<typeof vi.fn>).mockResolvedValue(historical)
    render(<RsvpResultsPage invitationId="invitation-1" api={api} />)

    expect(await screen.findByText('Önceki yemek sorusu')).toBeTruthy()
    expect(screen.getByText('Güncel soru')).toBeTruthy()
    expect(screen.getByText('Arşivlenmiş seçenek')).toBeTruthy()
    expect(screen.getByText('Yanıttaki güncel ad')).toBeTruthy()
    expect(screen.getByText('Toplam katılımcı')).toBeTruthy()
    expect(screen.getByText('0')).toBeTruthy()
  })

  it('asks for permanent deletion, supports Escape with focus restoration, and refreshes counts after delete', async () => {
    const api = mockApi()
    render(<RsvpResultsPage invitationId="invitation-1" api={api} />)
    const deleteButton = await screen.findByRole('button', { name: /Yanıt 1.*kalıcı olarak sil/ })
    deleteButton.focus()
    fireEvent.click(deleteButton)
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    expect(screen.getByText(/ona ait yanıt güncelleme yetkisi kalıcı olarak silinir/)).toBeTruthy()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Vazgeç' }))

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('alertdialog')).toBeNull())
    await waitFor(() => expect(document.activeElement).toBe(deleteButton))

    fireEvent.click(deleteButton)
    fireEvent.click(screen.getByRole('button', { name: 'Kalıcı olarak sil' }))
    await waitFor(() => expect(api.deleteCreatorRsvpSubmission).toHaveBeenCalledWith('invitation-1', 'response-1', 'csrf-token'))
    expect(await screen.findByText('Henüz yanıt yok')).toBeTruthy()
    expect(await screen.findByText('Yanıt kalıcı olarak silindi. Toplamlar güncellendi.')).toBeTruthy()
  })

  it('shows a retry state when result access fails', async () => {
    const api = mockApi()
    ;(api.getCreatorRsvpSubmissions as ReturnType<typeof vi.fn>).mockRejectedValueOnce(new Error('network'))
    render(<RsvpResultsPage invitationId="invitation-1" api={api} />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByText('Yanıt 1')).toBeTruthy()
  })
})
