import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiRequestError, type DavetiyeApiClient, type PublicRsvpConfiguration } from '../../api/generated/client'
import { PublicRsvpForm } from './PublicRsvpForm'

const code = 'a'.repeat(64)
const locatorKey = `davetiye:rsvp-submission:${code}`
const configuration: PublicRsvpConfiguration = {
  status: 'available',
  answerLimits: {
    maxShortTextAnswerCharacters: 200,
    maxLongTextAnswerCharacters: 2000,
    minimumParticipantCount: 0,
    maximumParticipantCount: 20,
    maxMultipleChoiceSelections: 10,
  },
  questions: [
    { id: 'name-id', prompt: 'Adınız', type: 'ShortText', isRequired: true, sortOrder: 0, options: [], minimumNumberValue: null, maximumNumberValue: null },
    { id: 'count-id', prompt: 'Kaç kişi katılacak?', type: 'Number', isRequired: true, sortOrder: 1, options: [], minimumNumberValue: 0, maximumNumberValue: 20 },
  ],
}

describe('PublicRsvpForm', () => {
  afterEach(() => {
    window.localStorage.clear()
  })

  it('validates required fields and explicit participant count bounds before submitting', async () => {
    const api = createApi()
    render(<PublicRsvpForm api={api} publicCode={code} configuration={configuration} />)

    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect((await screen.findByRole('alert')).textContent).toContain('Göndermeden önce işaretli alanları kontrol edin.')
    expect(api.createPublicRsvpSubmission).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Adınız *'), { target: { value: 'Ada' } })
    fireEvent.change(screen.getByLabelText('Kaç kişi katılacak? *'), { target: { value: '21' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(await screen.findByText('Değer 0 ile 20 arasında olmalıdır.')).toBeTruthy()
    expect(api.createPublicRsvpSubmission).not.toHaveBeenCalled()

    fireEvent.change(screen.getByLabelText('Kaç kişi katılacak? *'), { target: { value: '20.0000000000000001' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(await screen.findByText('Tam sayı girin.')).toBeTruthy()
    expect(api.createPublicRsvpSubmission).not.toHaveBeenCalled()
  })

  it('stores only the non-secret locator and reloads own answers through the capability API', async () => {
    const api = createApi()
    const { unmount } = render(<PublicRsvpForm api={api} publicCode={code} configuration={configuration} />)
    fireEvent.change(screen.getByLabelText('Adınız *'), { target: { value: 'Ada' } })
    fireEvent.change(screen.getByLabelText('Kaç kişi katılacak? *'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))

    expect((await screen.findByRole('status')).textContent).toContain('Yanıtınız kaydedildi.')
    await waitFor(() => expect(window.localStorage.getItem(locatorKey)).toBe('submission-1'))
    expect(window.localStorage.length).toBe(1)
    expect(window.localStorage.getItem(locatorKey)).toBe('submission-1')
    expect(api.createPublicRsvpSubmission).toHaveBeenCalledWith(code, { answers: [
      { questionId: 'name-id', textValue: 'Ada' },
      { questionId: 'count-id', numberValue: '2' },
    ] }, 'csrf-token')

    unmount()
    api.getPublicRsvpSubmission = vi.fn().mockResolvedValue({
      submissionId: 'submission-1', updatedAt: '2026-01-01T00:00:00Z',
      answers: [{ questionId: 'name-id', textValue: 'Ada' }, { questionId: 'count-id', numberValue: '2' }],
    })
    render(<PublicRsvpForm api={api} publicCode={code} configuration={configuration} />)
    expect(await screen.findByText('Daha önce yanıt verdiniz.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtımı Güncelle' }))
    expect((screen.getByLabelText('Adınız *') as HTMLInputElement).value).toBe('Ada')
    fireEvent.change(screen.getByLabelText('Adınız *'), { target: { value: 'Ada Lovelace' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtımı kaydet' }))
    await waitFor(() => expect(api.updatePublicRsvpSubmission).toHaveBeenCalledWith(code, 'submission-1', { answers: [
      { questionId: 'name-id', textValue: 'Ada Lovelace' },
      { questionId: 'count-id', numberValue: '2' },
    ] }, 'csrf-token'))
  })

  it('clears a stale local locator on generic not-found and offers a fresh response', async () => {
    window.localStorage.setItem(locatorKey, 'stale-id')
    const api = createApi()
    api.getPublicRsvpSubmission = vi.fn().mockRejectedValue(new ApiRequestError(404, null))
    render(<PublicRsvpForm api={api} publicCode={code} configuration={configuration} />)

    expect(await screen.findByText(/Önceki yanıt bu tarayıcıda artık düzenlenemiyor/)).toBeTruthy()
    expect(window.localStorage.getItem(locatorKey)).toBeNull()
    expect(screen.getByRole('button', { name: 'Yanıtı gönder' })).toBeTruthy()
  })

  it('clears a stale locator when an update is no longer available', async () => {
    window.localStorage.setItem(locatorKey, 'submission-1')
    const api = createApi()
    api.getPublicRsvpSubmission = vi.fn().mockResolvedValue({
      submissionId: 'submission-1', updatedAt: '2026-01-01T00:00:00Z', answers: [{ questionId: 'name-id', textValue: 'Ada' }],
    })
    api.updatePublicRsvpSubmission = vi.fn().mockRejectedValue(new ApiRequestError(404, null))
    render(<PublicRsvpForm api={api} publicCode={code} configuration={configuration} />)

    fireEvent.click(await screen.findByRole('button', { name: 'Yanıtımı Güncelle' }))
    fireEvent.change(screen.getByLabelText('Kaç kişi katılacak? *'), { target: { value: '2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtımı kaydet' }))
    expect(await screen.findByRole('button', { name: 'Yanıtı gönder' })).toBeTruthy()
    expect((screen.getByLabelText('Adınız *') as HTMLInputElement).value).toBe('')
    expect(window.localStorage.getItem(locatorKey)).toBeNull()
  })

  it('preserves a high-precision decimal through form state, own-answer prefill, and update payload', async () => {
    const exactDecimal = '0.1234567890123456789012345678'
    const numberQuestion = { ...configuration.questions[1]!, prompt: 'Ondalık değer', isRequired: true, minimumNumberValue: null, maximumNumberValue: null }
    const decimalConfiguration: PublicRsvpConfiguration = { ...configuration, questions: [numberQuestion] }
    const api = createApi()
    const { unmount } = render(<PublicRsvpForm api={api} publicCode={code} configuration={decimalConfiguration} />)
    fireEvent.change(screen.getByLabelText('Ondalık değer *'), { target: { value: exactDecimal } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(await screen.findByText(/Yanıtınız kaydedildi/)).toBeTruthy()
    expect(api.createPublicRsvpSubmission).toHaveBeenCalledWith(code, { answers: [{ questionId: 'count-id', numberValue: exactDecimal }] }, 'csrf-token')

    unmount()
    api.getPublicRsvpSubmission = vi.fn().mockResolvedValue({
      submissionId: 'submission-1', updatedAt: '2026-01-01T00:00:00Z',
      answers: [{ questionId: 'count-id', textValue: null, numberValue: exactDecimal, booleanValue: null, selectedOptionIds: [] }],
    })
    render(<PublicRsvpForm api={api} publicCode={code} configuration={decimalConfiguration} />)
    fireEvent.click(await screen.findByRole('button', { name: 'Yanıtımı Güncelle' }))
    expect((screen.getByLabelText('Ondalık değer *') as HTMLInputElement).value).toBe(exactDecimal)
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtımı kaydet' }))
    await waitFor(() => expect(api.updatePublicRsvpSubmission).toHaveBeenCalledWith(code, 'submission-1', {
      answers: [{ questionId: 'count-id', numberValue: exactDecimal }],
    }, 'csrf-token'))
  })

  it('rejects values outside .NET decimal range and accepts decimal.MinValue and decimal.MaxValue exactly', async () => {
    const numberQuestion = { ...configuration.questions[1]!, prompt: 'Sayı', isRequired: true, minimumNumberValue: null, maximumNumberValue: null }
    const decimalConfiguration: PublicRsvpConfiguration = { ...configuration, questions: [numberQuestion] }
    const api = createApi()
    const first = render(<PublicRsvpForm api={api} publicCode={code} configuration={decimalConfiguration} />)
    fireEvent.change(screen.getByLabelText('Sayı *'), { target: { value: '1e29' } })
    fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
    expect(await screen.findByText('Bu sayı desteklenen aralığın dışında.')).toBeTruthy()
    expect(api.createPublicRsvpSubmission).not.toHaveBeenCalled()
    first.unmount()

    for (const [index, value] of ['79228162514264337593543950335', '-79228162514264337593543950335'].entries()) {
      window.localStorage.clear()
      const page = render(<PublicRsvpForm api={api} publicCode={`${code}-${index}`} configuration={decimalConfiguration} />)
      fireEvent.change(screen.getByLabelText('Sayı *'), { target: { value } })
      fireEvent.click(screen.getByRole('button', { name: 'Yanıtı gönder' }))
      expect(await screen.findByText(/Yanıtınız kaydedildi/)).toBeTruthy()
      expect(api.createPublicRsvpSubmission).toHaveBeenLastCalledWith(`${code}-${index}`, { answers: [{ questionId: 'count-id', numberValue: value }] }, 'csrf-token')
      page.unmount()
    }
  })
})

function createApi(): DavetiyeApiClient {
  return {
    getAntiforgeryToken: vi.fn().mockResolvedValue('csrf-token'),
    getPublicRsvpSubmission: vi.fn(),
    createPublicRsvpSubmission: vi.fn().mockResolvedValue({ submissionId: 'submission-1', submittedAt: '2026-01-01T00:00:00Z' }),
    updatePublicRsvpSubmission: vi.fn().mockResolvedValue({ submissionId: 'submission-1', updatedAt: '2026-01-01T00:00:00Z' }),
  } as unknown as DavetiyeApiClient
}
