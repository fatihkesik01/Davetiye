import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { InvitationPublicationStatus, InvitationStatistics } from '../../api/generated/client'
import { CreatorSharePanel } from './CreatorSharePanel'

const statistics: InvitationStatistics = {
  totalPageViews: 1240,
  rsvpResponseCount: 18,
  participantCountTotal: 43,
  memoryCount: 7,
  readyMediaCount: 12,
  activeGiftReservationCount: 3,
}

const publication = {
  invitationId: '11111111-1111-4111-8111-111111111111',
  publicCode: 'a'.repeat(64),
  effectiveState: 'Active',
  published: {},
} as InvitationPublicationStatus

function stubFetch(statisticsReply: () => Promise<InvitationStatistics> | InvitationStatistics) {
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
    const url = String(input)
    if (url.endsWith('/api/v1/public/capabilities')) {
      return Promise.resolve(new Response(JSON.stringify({
        canonicalBaseUrl: 'https://davet.example', mapEmbedEnabled: false,
        mapEmbedOrigin: 'https://www.google.com', mapEmbedPath: '/maps/embed/v1/place',
      })))
    }
    if (url.endsWith('/api/v1/invitations/11111111-1111-4111-8111-111111111111/statistics')) {
      return Promise.resolve(statisticsReply()).then(body => new Response(JSON.stringify(body)))
    }
    return Promise.reject(new Error(`Unexpected request: ${url}`))
  }))
}

describe('CreatorSharePanel aggregate statistics', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows all six aggregate values with descriptive labels', async () => {
    stubFetch(() => statistics)
    render(<CreatorSharePanel status={publication} />)

    const stats = await screen.findByRole('region', { name: 'Davetiye istatistikleri' })
    expect(stats).toBeTruthy()
    expect(screen.getByText('1.240')).toBeTruthy()
    expect(screen.getByText('18')).toBeTruthy()
    expect(screen.getByText('43')).toBeTruthy()
    expect(screen.getByText('7')).toBeTruthy()
    expect(screen.getByText('12')).toBeTruthy()
    expect(screen.getByText('3')).toBeTruthy()
  })

  it('announces loading until the aggregate response arrives', async () => {
    let resolveStatistics: ((response: Response) => void) | undefined
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.endsWith('/api/v1/public/capabilities')) return Promise.resolve(new Response(JSON.stringify({
        canonicalBaseUrl: 'https://davet.example', mapEmbedEnabled: false,
        mapEmbedOrigin: 'https://www.google.com', mapEmbedPath: '/maps/embed/v1/place',
      })))
      if (url.endsWith('/api/v1/invitations/11111111-1111-4111-8111-111111111111/statistics')) {
        return new Promise<Response>(resolve => { resolveStatistics = resolve })
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    }))
    render(<CreatorSharePanel status={publication} />)

    expect(screen.getByText('İstatistikler yükleniyor…')).toBeTruthy()
    resolveStatistics?.(new Response(JSON.stringify(statistics)))
    expect(await screen.findByText('RSVP yanıtı sayısı')).toBeTruthy()
    expect(screen.queryByText('İstatistikler yükleniyor…')).toBeNull()
  })

  it('announces loading and offers a retry after a request error', async () => {
    const fetch = vi.fn()
    let attempts = 0
    vi.stubGlobal('fetch', fetch.mockImplementation((input: RequestInfo | URL) => {
      const url = String(input)
      if (url.endsWith('/api/v1/public/capabilities')) return Promise.resolve(new Response(JSON.stringify({
        canonicalBaseUrl: 'https://davet.example', mapEmbedEnabled: false,
        mapEmbedOrigin: 'https://www.google.com', mapEmbedPath: '/maps/embed/v1/place',
      })))
      if (url.endsWith('/api/v1/invitations/11111111-1111-4111-8111-111111111111/statistics')) {
        attempts++
        return attempts === 1
          ? Promise.resolve(new Response('{}', { status: 503 }))
          : Promise.resolve(new Response(JSON.stringify(statistics)))
      }
      return Promise.reject(new Error(`Unexpected request: ${url}`))
    }))
    render(<CreatorSharePanel status={publication} />)

    expect(await screen.findByRole('alert')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(screen.getByText('43')).toBeTruthy())
    expect(attempts).toBe(2)
  })
})
