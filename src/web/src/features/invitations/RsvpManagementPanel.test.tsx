import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { InvitationRsvpConfiguration, DavetiyeApiClient } from '../../api/generated/client'
import { RsvpManagementPanel } from './RsvpManagementPanel'

const initial: InvitationRsvpConfiguration = {
  invitationId: 'invitation-1', enabled: false, revision: 0, effectiveState: 'Draft', questions: [],
  inputLimits: {
    maxActiveQuestionsPerInvitation: 5,
    maxQuestionPromptCharacters: 87,
    maxOptionLabelCharacters: 44,
    maxDefinedOptionsPerChoiceQuestion: 3,
    maxShortTextAnswerCharacters: 61,
    maxLongTextAnswerCharacters: 401,
    minimumParticipantCount: 2,
    maximumParticipantCount: 9,
    maxMultipleChoiceSelections: 2,
  },
}

function mockApi(configuration = initial) {
  return {
    getInvitationRsvp: vi.fn().mockResolvedValue(configuration),
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf'),
    setInvitationRsvp: vi.fn().mockImplementation(async (_id, request) => ({
      ...configuration, enabled: request.isEnabled, revision: configuration.revision + 1,
      questions: request.isEnabled ? starterQuestions : [],
    })),
    addInvitationRsvpQuestion: vi.fn(),
    updateInvitationRsvpQuestion: vi.fn(),
    deleteInvitationRsvpQuestion: vi.fn(),
    reorderInvitationRsvpQuestions: vi.fn(),
  } as unknown as DavetiyeApiClient
}

const starterQuestions = [
  { id: 'name', prompt: 'Adınız', type: 'ShortText' as const, isRequired: true, sortOrder: 0, semanticRole: null, options: [] },
  { id: 'attending', prompt: 'Katılacak mısınız?', type: 'YesNo' as const, isRequired: true, sortOrder: 1, semanticRole: null, options: [] },
  { id: 'count', prompt: 'Kaç kişi katılacaksınız?', type: 'Number' as const, isRequired: true, sortOrder: 2, semanticRole: 'ParticipantCount' as const, options: [] },
  { id: 'message', prompt: 'Notunuz / mesajınız', type: 'LongText' as const, isRequired: false, sortOrder: 3, semanticRole: null, options: [] },
]

const baseProps = {
  invitationId: 'invitation-1', effectiveState: 'Draft', publicationReady: true,
  templateSupportsRsvp: true, onManagePublication: vi.fn(),
}

describe('RsvpManagementPanel', () => {
  it('loads config separately, seeds starter questions on enable, and exposes an accessible question list', async () => {
    const api = mockApi()
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    fireEvent.click(await screen.findByLabelText('RSVP bölümünü aç'))

    expect(await screen.findByText('Adınız')).toBeTruthy()
    expect(screen.getByText('Katılacak mısınız?')).toBeTruthy()
    expect(screen.getByText('Kaç kişi katılacaksınız?')).toBeTruthy()
    expect(screen.getByText('Notunuz / mesajınız')).toBeTruthy()
    expect(screen.getByRole('list', { name: 'RSVP soruları' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'RSVP yanıtları' }).getAttribute('href')).toBe('/panel/davetiyeler/invitation-1/rsvp-yanitlari')
    expect(api.setInvitationRsvp).toHaveBeenCalledWith('invitation-1', { expectedRevision: 0, isEnabled: true }, 'csrf')
  })

  it('keeps saved RSVP results accessible after RSVP is disabled', async () => {
    const api = mockApi({ ...initial, questions: starterQuestions })
    render(<RsvpManagementPanel {...baseProps} api={api} />)
    await screen.findByLabelText('RSVP bölümünü aç')
    expect(screen.getByRole('link', { name: 'RSVP yanıtları' }).getAttribute('href')).toBe('/panel/davetiyeler/invitation-1/rsvp-yanitlari')
  })

  it('makes Active configuration read-only and offers the pause-first route', async () => {
    const config: InvitationRsvpConfiguration = { ...initial, enabled: true, effectiveState: 'Active', questions: starterQuestions }
    const api = mockApi(config)
    render(<RsvpManagementPanel {...baseProps} effectiveState="Active" api={api} />)

    const toggle = await screen.findByLabelText('RSVP bölümünü aç') as HTMLInputElement
    expect(toggle.disabled).toBe(true)
    expect(screen.getByText(/önce davetiyeyi duraklatın/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Soru ekle' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Yayını duraklat' }))
    expect(baseProps.onManagePublication).toHaveBeenCalled()
    expect(api.setInvitationRsvp).not.toHaveBeenCalled()
  })

  it('reorders questions with buttons, retains focus and announces the new position', async () => {
    const config = { ...initial, enabled: true, questions: starterQuestions }
    const api = mockApi(config)
    ;(api.reorderInvitationRsvpQuestions as ReturnType<typeof vi.fn>).mockImplementation(async (_id, request) => ({
      ...config, revision: request.expectedRevision + 1,
      questions: [...config.questions].reverse().map((question, sortOrder) => ({ ...question, sortOrder })),
    }))
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    const moveDown = await screen.findByRole('button', { name: 'Adınız sorusunu aşağı taşı' })
    moveDown.focus()
    fireEvent.click(moveDown)

    await waitFor(() => expect(document.activeElement).toBe(moveDown))
    expect(api.reorderInvitationRsvpQuestions).toHaveBeenCalledWith('invitation-1', {
      expectedRevision: 0, questionIds: ['attending', 'name', 'count', 'message'],
    }, 'csrf')
    expect(screen.getByText(/sıra 2 \/ 4/i)).toBeTruthy()
  })

  it('asks before removing the semantic participant-count question', async () => {
    const config = { ...initial, enabled: true, questions: starterQuestions }
    const api = mockApi(config)
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Kaç kişi katılacaksınız? sorusunu kaldır' }))
    expect(screen.getByRole('alertdialog')).toBeTruthy()
    expect(screen.getByText(/katılımcı toplamlarını etkileyebilir/)).toBeTruthy()
    expect(api.deleteInvitationRsvpQuestion).not.toHaveBeenCalled()
  })

  it.each([
    ['the prompt', () => fireEvent.change(screen.getByLabelText('Soru metni'), { target: { value: 'Yeni katılımcı sayısı sorusu' } })],
    ['the required setting', () => fireEvent.click(screen.getByLabelText('Zorunlu'))],
  ])('asks before changing %s on the semantic participant-count question', async (_changeName, change) => {
    const config: InvitationRsvpConfiguration = { ...initial, enabled: true, questions: starterQuestions }
    const api = mockApi(config)
    ;(api.updateInvitationRsvpQuestion as ReturnType<typeof vi.fn>).mockResolvedValue({
      ...config, revision: 1,
    })
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Kaç kişi katılacaksınız? sorusunu düzenle' }))
    change()
    fireEvent.click(screen.getByRole('button', { name: 'Soruyu kaydet' }))

    expect(screen.getByRole('alertdialog')).toBeTruthy()
    expect(api.updateInvitationRsvpQuestion).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Değişikliği onayla' }))
    await waitFor(() => expect(api.updateInvitationRsvpQuestion).toHaveBeenCalledTimes(1))
  })

  it('does not show the impact warning when the semantic participant-count question is saved unchanged', async () => {
    const config: InvitationRsvpConfiguration = { ...initial, enabled: true, questions: starterQuestions }
    const api = mockApi(config)
    ;(api.updateInvitationRsvpQuestion as ReturnType<typeof vi.fn>).mockResolvedValue(config)
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Kaç kişi katılacaksınız? sorusunu düzenle' }))
    fireEvent.click(screen.getByRole('button', { name: 'Soruyu kaydet' }))

    expect(screen.queryByRole('alertdialog')).toBeNull()
    await waitFor(() => expect(api.updateInvitationRsvpQuestion).toHaveBeenCalledTimes(1))
  })

  it('shows the short-text limit and enforces the prompt character limit accessibly', async () => {
    const config = { ...initial, enabled: true, questions: [starterQuestions[0]!] }
    const api = mockApi(config)
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Adınız sorusunu düzenle' }))
    expect(screen.getByText('Soru metni en fazla 87 karakter olabilir.')).toBeTruthy()
    expect(screen.getByText('Davetli kısa metin yanıtında en fazla 61 karakter girebilir.')).toBeTruthy()
    expect((screen.getByLabelText('Soru metni') as HTMLInputElement).maxLength).toBe(87)
    expect(screen.getByRole('combobox', { name: 'Soru türü' }).getAttribute('aria-describedby')).toBe('rsvp-answer-limit')
    fireEvent.change(screen.getByRole('combobox', { name: 'Soru türü' }), { target: { value: 'LongText' } })
    expect(screen.getByText('Davetli uzun metin yanıtında en fazla 401 karakter girebilir.')).toBeTruthy()
  })

  it('prevents adding more than 20 active questions or choice options', async () => {
    const cappedQuestions = Array.from({ length: initial.inputLimits.maxActiveQuestionsPerInvitation }, (_, index) => ({
      id: `question-${index}`, prompt: `Question ${index + 1}`, type: 'ShortText' as const,
      isRequired: false, sortOrder: index, semanticRole: null, options: [],
    }))
    const api = mockApi({ ...initial, enabled: true, questions: cappedQuestions })
    const { rerender } = render(<RsvpManagementPanel {...baseProps} api={api} />)

    expect(await screen.findByText('5 / 5 aktif soru')).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Soru ekle' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('En fazla 5 aktif soru ekleyebilirsiniz.')).toBeTruthy()

    const options = Array.from({ length: initial.inputLimits.maxDefinedOptionsPerChoiceQuestion }, (_, index) => ({ id: `option-${index}`, label: `Option ${index + 1}`, sortOrder: index }))
    const choiceQuestion = { id: 'choice', prompt: 'Meal', type: 'MultipleChoice' as const, isRequired: false, sortOrder: 0, semanticRole: null, options }
    const choiceApi = mockApi({ ...initial, enabled: true, questions: [choiceQuestion] })
    rerender(<RsvpManagementPanel {...baseProps} api={choiceApi} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Meal sorusunu düzenle' }))

    expect((screen.getByRole('button', { name: 'Seçenek ekle' }) as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText('En fazla 3 seçenek tanımlayabilirsiniz.')).toBeTruthy()
    expect(screen.getByText('Davetli en fazla 2 seçenek işaretleyebilir.')).toBeTruthy()
    expect(screen.getByText(/Her seçenek etiketi en fazla 44 karakter olabilir/)).toBeTruthy()
    expect((screen.getByLabelText('Seçenek 1') as HTMLInputElement).maxLength).toBe(44)
  })

  it('explains that the participant-count answer is an integer from 0 through 20', async () => {
    const config = { ...initial, enabled: true, questions: [starterQuestions[2]!] }
    const api = mockApi(config)
    render(<RsvpManagementPanel {...baseProps} api={api} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Kaç kişi katılacaksınız? sorusunu düzenle' }))
    expect(screen.getByText('Katılımcı sayısı yanıtı tam sayı olmalı ve 2 ile 9 arasında olmalıdır.')).toBeTruthy()
  })

  it('keeps Scheduled and Expired configurations read-only even if they were loaded as a Draft earlier', async () => {
    const api = mockApi({ ...initial, enabled: true, questions: starterQuestions })
    const { rerender } = render(<RsvpManagementPanel {...baseProps} api={api} />)
    expect(await screen.findByRole('button', { name: 'Soru ekle' })).toBeTruthy()
    rerender(<RsvpManagementPanel {...baseProps} effectiveState="Scheduled" api={api} />)
    expect((screen.getByLabelText('RSVP bölümünü aç') as HTMLInputElement).disabled).toBe(true)
    expect(screen.getByText('Bu yayın durumunda RSVP ayarları düzenlenemez.')).toBeTruthy()
    rerender(<RsvpManagementPanel {...baseProps} effectiveState="Expired" api={api} />)
    expect((screen.getByLabelText('RSVP bölümünü aç') as HTMLInputElement).disabled).toBe(true)
  })
})
