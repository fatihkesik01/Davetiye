import { describe, expect, it, vi } from 'vitest'

import { ApiRequestError, DavetiyeApiClient, normalizeRsvpNumberLexeme } from './client'

describe('DavetiyeApiClient', () => {
  it('reads only the typed Admin system-settings contract and updates with revision plus antiforgery', async () => {
    const setting = {
      key: 'deletedInvitationRetentionDays', displayName: 'Deleted invitations', description: 'Retention for newly trashed invitations.',
      value: 3, minimum: 0, maximum: 365, revision: 4,
    }
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ items: [setting] })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...setting, value: 7, revision: 5 })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getAdminSettings()).resolves.toEqual({ items: [setting] })
    await expect(client.updateAdminSetting('deletedInvitationRetentionDays', { expectedRevision: 4, value: 7 }, 'csrf-token'))
      .resolves.toMatchObject({ value: 7, revision: 5 })

    expect(fetch).toHaveBeenNthCalledWith(1, '/api/v1/admin/settings', expect.objectContaining({
      method: 'GET', credentials: 'include', cache: 'no-store',
    }))
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/v1/admin/settings/deletedInvitationRetentionDays', expect.objectContaining({
      method: 'PUT', credentials: 'include', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
      body: JSON.stringify({ expectedRevision: 4, value: 7 }),
    }))
  })

  it('represents the authenticated first-factor-only Super Admin access state', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ authenticated: true, access: 'mfa-setup-required-super-admin' })))
    const client = new DavetiyeApiClient({ fetch })
    await expect(client.getSessionAccess()).resolves.toEqual({ authenticated: true, access: 'mfa-setup-required-super-admin' })
  })

  it('uses antiforgery for enrollment and verification, but not the intermediate login challenge', async () => {
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ sharedKey: 'JBSWY3DPEHPK3PXP', authenticatorUri: 'otpauth://totp/Davetiye:admin' })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ recoveryCodes: ['one-use-code'] })))
      .mockResolvedValueOnce(new Response(null))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.enrollAdminMfa('csrf')).resolves.toMatchObject({ sharedKey: 'JBSWY3DPEHPK3PXP' })
    await expect(client.verifyAdminMfa('123456', 'csrf')).resolves.toEqual({ recoveryCodes: ['one-use-code'] })
    await client.completeAdminMfaLogin('recovery', true)

    expect(fetch).toHaveBeenNthCalledWith(1, '/api/v1/admin/mfa/enroll', expect.objectContaining({ method: 'POST', body: undefined, headers: { 'X-CSRF-TOKEN': 'csrf' } }))
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/v1/admin/mfa/verify', expect.objectContaining({ method: 'POST', body: JSON.stringify({ code: '123456' }), headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf' } }))
    expect(fetch).toHaveBeenNthCalledWith(3, '/api/v1/admin/mfa/login/complete', expect.objectContaining({ method: 'POST', body: JSON.stringify({ code: 'recovery', isRecoveryCode: true }), headers: { 'Content-Type': 'application/json' } }))
  })

  it('lists editable Admin template metadata and updates approved fields with revision and antiforgery', async () => {
    const item = { id: 'template-id', key: 'zamansiz-dugun', name: 'Zamansız Düğün', description: null, isActive: true, revision: 3 }
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([item])))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...item, name: 'Yeni Ad', revision: 4 })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getAdminTemplates()).resolves.toEqual([item])
    await expect(client.updateAdminTemplate('template/id', {
      expectedRevision: 3, name: 'Yeni Ad', description: 'Açıklama', isActive: false,
    }, 'csrf-token')).resolves.toMatchObject({ name: 'Yeni Ad', revision: 4 })

    expect(fetch).toHaveBeenNthCalledWith(1, '/api/v1/admin/templates', expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }))
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/v1/admin/templates/template%2Fid', expect.objectContaining({
      method: 'PUT', credentials: 'include', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
      body: JSON.stringify({ expectedRevision: 3, name: 'Yeni Ad', description: 'Açıklama', isActive: false }),
    }))
  })

  it('lists Admin plans and updates typed entitlements with revision and antiforgery while omitting price fields', async () => {
    const plan = {
      id: 'plan-id', key: 'standard', displayName: 'Standard', description: null, priceAmount: 699,
      currency: 'TRY', billingKind: 'OneTime', entitlements: [
        { key: 'maxImages', numericValue: 50, booleanValue: null },
        { key: 'memoriesEnabled', numericValue: null, booleanValue: true },
      ], revision: 7,
    }
    const request = {
      expectedRevision: 7, displayName: 'Standard Plus', description: 'Açıklama', priceAmount: 749.125, billingKind: 'Monthly' as const,
      entitlements: [
        { key: 'maxImages', numericValue: 70, booleanValue: null },
        { key: 'memoriesEnabled', numericValue: null, booleanValue: true },
      ],
    }
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify([plan])))
      .mockResolvedValueOnce(new Response(JSON.stringify({ ...plan, ...request, revision: 8 })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getAdminPlans()).resolves.toEqual([plan])
    await expect(client.updateAdminPlan('plan/id', request, 'csrf-token')).resolves.toMatchObject({ displayName: 'Standard Plus', revision: 8 })

    expect(fetch).toHaveBeenNthCalledWith(1, '/api/v1/admin/plans', expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }))
    expect(fetch).toHaveBeenNthCalledWith(2, '/api/v1/admin/plans/plan%2Fid', expect.objectContaining({
      method: 'PUT', credentials: 'include', cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
      body: JSON.stringify(request),
    }))
    expect(request.priceAmount).toBe(749.125)
    expect(JSON.stringify(request)).not.toContain('currency')
  })

  it('sends an MFA admin ban command with antiforgery and the minimized request body', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const client = new DavetiyeApiClient({ fetch })
    const request = { reason: 'Terms violation', internalNote: null }

    await client.banAccount('11111111-1111-1111-1111-111111111111', request, 'csrf-token')

    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/accounts/11111111-1111-1111-1111-111111111111/ban', expect.objectContaining({
      method: 'POST',
      credentials: 'include',
      cache: 'no-store',
      headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
      body: JSON.stringify(request),
    }))
  })

  it('sends an MFA admin unban command with antiforgery and no request body', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const client = new DavetiyeApiClient({ fetch })

    await client.unbanAccount('11111111-1111-1111-1111-111111111111', 'csrf-token')

    expect(fetch).toHaveBeenCalledWith('/api/v1/admin/accounts/11111111-1111-1111-1111-111111111111/unban', expect.objectContaining({
      method: 'POST',
      credentials: 'include',
      cache: 'no-store',
      headers: { 'X-CSRF-TOKEN': 'csrf-token' },
      body: undefined,
    }))
  })

  it('searches the paginated banned-account projection in a CSRF-protected POST body', async () => {
    const page = { items: [], page: 2, pageSize: 25, totalCount: 0 }
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(page)))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.searchAdminBannedAccounts(2, 25, 'ada+admin@example.test', 'csrf-token')).resolves.toEqual(page)
    expect(fetch).toHaveBeenCalledWith(
      '/api/v1/admin/accounts/banned/search',
      expect.objectContaining({
        method: 'POST', credentials: 'include', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
        body: JSON.stringify({ page: 2, pageSize: 25, emailPrefix: 'ada+admin@example.test' }),
      }),
    )
  })

  it('omits the optional banned-account email prefix from the POST body when absent', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0 })))
    const client = new DavetiyeApiClient({ fetch })

    await client.searchAdminBannedAccounts(1, 50, undefined, 'csrf-token')

    expect(fetch).toHaveBeenCalledWith(
      '/api/v1/admin/accounts/banned/search',
      expect.objectContaining({
        method: 'POST', credentials: 'include', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' },
        body: JSON.stringify({ page: 1, pageSize: 50 }),
      }),
    )
  })

  it('uses owner-scoped RSVP routes, optimistic revisions and antiforgery on every mutation', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      invitationId: 'invitation-1', enabled: true, revision: 8, effectiveState: 'Draft', questions: [],
    }))))
    const client = new DavetiyeApiClient({ fetch })
    const base = '/api/v1/invitations/invitation-1/rsvp'

    await client.getInvitationRsvp('invitation-1')
    await client.setInvitationRsvp('invitation-1', { expectedRevision: 1, isEnabled: true }, 'csrf')
    await client.addInvitationRsvpQuestion('invitation-1', {
      expectedRevision: 2, prompt: 'Meal', type: 'SingleChoice', isRequired: true,
      options: [{ label: 'Veg', sortOrder: 0 }],
    }, 'csrf')
    await client.updateInvitationRsvpQuestion('invitation-1', 'question-1', {
      expectedRevision: 3, prompt: 'Meal choice', type: 'SingleChoice', isRequired: false,
      options: [{ label: 'Veg', sortOrder: 0 }],
    }, 'csrf')
    await client.deleteInvitationRsvpQuestion('invitation-1', 'question-1', { expectedRevision: 4 }, 'csrf')
    await client.reorderInvitationRsvpQuestions('invitation-1', { expectedRevision: 5, questionIds: ['question-1'] }, 'csrf')

    expect(fetch.mock.calls.map(call => [call[0], (call[1] as RequestInit).method])).toEqual([
      [base, 'GET'], [base, 'PUT'], [`${base}/questions`, 'POST'],
      [`${base}/questions/question-1`, 'PUT'], [`${base}/questions/question-1`, 'DELETE'],
      [`${base}/questions/order`, 'PUT'],
    ])
    for (const [, init] of fetch.mock.calls.slice(1) as Array<[string, RequestInit]>) {
      expect(init.headers).toMatchObject({ 'X-CSRF-TOKEN': 'csrf', 'Content-Type': 'application/json' })
      expect(JSON.parse(String(init.body)).expectedRevision).toBeGreaterThan(0)
    }
    expect(fetch.mock.calls[4]?.[1]).toMatchObject({ method: 'DELETE', body: JSON.stringify({ expectedRevision: 4 }) })
  })

  it('loads the public RSVP configuration and its answer limits without sending Creator cookies', async () => {
    const configuration = {
      status: 'available', questions: [{ id: 'q1', prompt: 'Guests', type: 'Number', isRequired: true, sortOrder: 0, options: [], minimumNumberValue: 0, maximumNumberValue: 20 }], answerLimits: {
        maxShortTextAnswerCharacters: 61, maxLongTextAnswerCharacters: 401,
        minimumParticipantCount: 2, maximumParticipantCount: 9, maxMultipleChoiceSelections: 2,
      },
    }
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(configuration)))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getPublicRsvpConfiguration('public-code')).resolves.toEqual(configuration)
    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/rsvp', expect.objectContaining({
      method: 'GET', credentials: 'omit', cache: 'no-store',
    }))
  })

  it('uses the HttpOnly guest capability cookie for own-response reads and mutations', async () => {
    const exactDecimal = '0.1234567890123456789012345678'
    const fetch = vi.fn().mockImplementation((_url: string, init: RequestInit) => Promise.resolve(new Response(JSON.stringify(
      init.method === 'POST' ? { submissionId: 'submission-1', submittedAt: '2026-10-05T10:00:00Z' }
        : init.method === 'PUT' ? { submissionId: 'submission-1', updatedAt: '2026-10-05T10:01:00Z' }
          : { submissionId: 'submission-1', updatedAt: '2026-10-05T10:00:00Z', answers: [{ questionId: 'question-1', textValue: null, numberValue: exactDecimal, booleanValue: null, selectedOptionIds: [] }] },
    ))))
    const client = new DavetiyeApiClient({ fetch })
    const answerRequest = { answers: [{ questionId: 'question-1', numberValue: exactDecimal }] }

    await client.createPublicRsvpSubmission('public-code', answerRequest, 'csrf')
    const ownSubmission = await client.getPublicRsvpSubmission('public-code', 'submission-1')
    await client.updatePublicRsvpSubmission('public-code', 'submission-1', answerRequest, 'csrf')
    expect(ownSubmission.answers[0]?.numberValue).toBe(exactDecimal)

    expect(fetch.mock.calls.map(([url, init]) => [url, (init as RequestInit).method])).toEqual([
      ['/api/v1/public/invitations/public-code/rsvp/submissions', 'POST'],
      ['/api/v1/public/invitations/public-code/rsvp/submissions/submission-1', 'GET'],
      ['/api/v1/public/invitations/public-code/rsvp/submissions/submission-1', 'PUT'],
    ])
    for (const [, init] of fetch.mock.calls as Array<[string, RequestInit]>) {
      expect(init.credentials).toBe('include')
      expect(init.cache).toBe('no-store')
    }
    const expectedBody = `{"answers":[{"questionId":"question-1","numberValue":${exactDecimal}}]}`
    expect(fetch.mock.calls[0]?.[1]).toMatchObject({ method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf' }, body: expectedBody })
    expect(fetch.mock.calls[2]?.[1]).toMatchObject({ method: 'PUT', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf' }, body: expectedBody })
  })

  it('normalizes valid number-input lexemes and rejects incomplete decimal strings', async () => {
    expect(normalizeRsvpNumberLexeme('.5')).toBe('0.5')
    expect(normalizeRsvpNumberLexeme('001.20')).toBe('1.20')
    expect(normalizeRsvpNumberLexeme('1.')).toBe('1')
    expect(normalizeRsvpNumberLexeme('1e-20')).toBe('1e-20')
    expect(normalizeRsvpNumberLexeme('1e')).toBeNull()
    expect(normalizeRsvpNumberLexeme('-')).toBeNull()

    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ submissionId: 'submission-1', submittedAt: '2026-10-05T10:00:00Z' }), { status: 201 })))
    const client = new DavetiyeApiClient({ fetch })
    for (const [input, expected] of [['.5', '0.5'], ['001.20', '1.20'], ['1.', '1']] as const) {
      await client.createPublicRsvpSubmission('public-code', { answers: [{ questionId: 'question-1', numberValue: input }] }, 'csrf')
      expect((fetch.mock.calls.at(-1)?.[1] as RequestInit).body).toBe(`{"answers":[{"questionId":"question-1","numberValue":${expected}}]}`)
    }
    await expect(client.createPublicRsvpSubmission('public-code', { answers: [{ questionId: 'question-1', numberValue: '1e' }] }, 'csrf')).rejects.toThrow('valid decimal')
    expect(fetch).toHaveBeenCalledTimes(3)
  })

  it('keeps Creator media mutations antiforgery-protected and public delivery cookie-free', async () => {
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (url.endsWith('/intents')) return Promise.resolve(new Response(JSON.stringify({
        intentId: 'intent-1', assetId: 'asset-1', expiresAt: '2026-10-04T12:00:00Z', replayed: false,
        ingressUri: 'https://upload.example.test/opaque', capabilityExpiresAt: '2026-10-04T12:00:00Z',
        ingressHeaders: { 'X-Media-Capability': 'opaque-secret' },
      })))
      if (url.endsWith('/finalize')) return Promise.resolve(new Response(JSON.stringify({ state: 'processing' }), { status: 202 }))
      if (url.endsWith('/delivery')) return Promise.resolve(new Response(JSON.stringify({ kind: 'image', url: 'https://media.example.test/short', expiresAt: '2026-10-04T12:00:00Z' })))
      return Promise.resolve(new Response(null, { status: 204 }))
    })
    const client = new DavetiyeApiClient({ fetch })

    await client.createCreatorMediaIntent('invitation-1', { kind: 'Image', presentationRole: 'Cover', declaredByteLength: 1200 }, 'idempotency-key', 'csrf')
    expect(fetch).toHaveBeenLastCalledWith('/api/v1/invitations/invitation-1/media/intents', expect.objectContaining({
      method: 'POST', credentials: 'include', headers: expect.objectContaining({ 'Idempotency-Key': 'idempotency-key', 'X-CSRF-TOKEN': 'csrf' }),
    }))
    await client.finalizeCreatorMedia('invitation-1', 'asset-1', 'csrf')
    await client.getCreatorMediaLibrary('invitation-1')
    await client.setCreatorMediaPlacement('invitation-1', 'asset-1', 'Gallery', 0, 'csrf')
    await client.deleteCreatorMedia('invitation-1', 'asset-1', 'csrf')
    await client.createPublicMediaDeliverySession('public-code', 'asset-1')

    expect(fetch).toHaveBeenCalledWith('/api/v1/creator/invitations/invitation-1/media', expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/creator/invitations/invitation-1/media/asset-1/placement', expect.objectContaining({
      method: 'PUT', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf' }), body: JSON.stringify({ role: 'Gallery', sortOrder: 0 }),
    }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/creator/invitations/invitation-1/media/asset-1', expect.objectContaining({ method: 'DELETE', headers: { 'X-CSRF-TOKEN': 'csrf' } }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/media/asset-1/delivery', expect.objectContaining({ method: 'POST', credentials: 'omit', cache: 'no-store' }))
  })

  it('keeps guest-memory upload capabilities in the HttpOnly API cookie and omits credentials to the provider', async () => {
    const fetch = vi.fn().mockImplementation((url: string) => {
      if (url.endsWith('/with-media')) return Promise.resolve(new Response(JSON.stringify({
        memoryId: 'memory-1', createdAt: '2026-10-05T12:00:00Z', uploadExpiresAt: '2026-10-05T12:10:00Z',
        limits: { maxMediaItems: 3, maxImageBytes: 1024, maxVideoBytes: 4096, maxVideoDurationSeconds: 60 },
      }), { status: 201 }))
      if (url.endsWith('/media/intents')) return Promise.resolve(new Response(JSON.stringify({
        assetId: 'asset-1', kind: 'Image', uploadExpiresAt: '2026-10-05T12:10:00Z', replayed: false,
        ingressUri: 'https://upload.example.test/opaque', capabilityExpiresAt: '2026-10-05T12:10:00Z',
        ingressHeaders: { 'X-Media-Capability': 'provider-secret' },
      }), { status: 201 }))
      if (url.endsWith('/delivery')) return Promise.resolve(new Response(JSON.stringify({
        kind: 'image', url: 'https://media.example.test/short', expiresAt: '2026-10-05T12:01:00Z',
      })))
      return Promise.resolve(new Response(JSON.stringify({ state: 'processing', uploadExpiresAt: '2026-10-05T12:10:00Z', media: [] })))
    })
    const client = new DavetiyeApiClient({ fetch })
    await client.createPublicMemoryWithMedia('public-code', { displayName: null, text: 'Anı', emoji: null }, 'csrf')
    await client.createPublicMemoryMediaIntent('public-code', 'memory-1', {
      kind: 'Image', declaredByteLength: 10,
    }, 'stable-idempotency-key', 'csrf')
    await client.getPublicMemoryUploadStatus('public-code', 'memory-1')
    await client.finalizePublicMemoryUpload('public-code', 'memory-1', 'csrf')
    await client.createPublicMemoryMediaDelivery('public-code', 'memory-1', 'asset-1', 'csrf')

    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/memories/with-media', expect.objectContaining({
      method: 'POST', credentials: 'include', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf' }),
    }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/memories/memory-1/media/intents', expect.objectContaining({
      method: 'POST', credentials: 'include', headers: expect.objectContaining({ 'Idempotency-Key': 'stable-idempotency-key', 'X-CSRF-TOKEN': 'csrf' }),
    }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/memories/memory-1/status', expect.objectContaining({ method: 'GET', credentials: 'include' }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/memories/memory-1/finalize', expect.objectContaining({ method: 'POST', credentials: 'include' }))
    expect(fetch).toHaveBeenCalledWith('/api/v1/public/invitations/public-code/memories/memory-1/media/asset-1/delivery', expect.objectContaining({
      method: 'POST', credentials: 'include', headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf' }),
    }))

    let uploadCredentials = true
    let sentBody: unknown
    const uploadHeaders: Record<string, string> = {}
    let uploadMethod = ''
    let uploadUrl = ''
    let uploadProgress: ((event: ProgressEvent) => void) | null = null
    let uploadLoad: (() => void) | null = null
    class FakeXmlHttpRequest {
      upload = { set onprogress(handler: ((event: ProgressEvent) => void) | null) { uploadProgress = handler } }
      status = 201
      withCredentials = true
      set onload(handler: (() => void) | null) { uploadLoad = handler }
      set onerror(_handler: (() => void) | null) { /* not used */ }
      set onabort(_handler: (() => void) | null) { /* not used */ }
      open(method: string, url: string) { uploadMethod = method; uploadUrl = url }
      setRequestHeader(name: string, value: string) { uploadHeaders[name] = value }
      send(body: unknown) {
        uploadCredentials = this.withCredentials
        sentBody = body
        uploadProgress?.({ lengthComputable: true, loaded: 10, total: 10 } as ProgressEvent)
        uploadLoad?.()
      }
    }
    vi.stubGlobal('XMLHttpRequest', FakeXmlHttpRequest)
    const intent = {
      assetId: 'asset-1', kind: 'Image' as const, uploadExpiresAt: '2026-10-05T12:10:00Z', replayed: false,
      ingressUri: 'https://upload.example.test/opaque', capabilityExpiresAt: '2026-10-05T12:10:00Z',
      ingressHeaders: { 'X-Media-Capability': 'provider-secret' },
    }
    const file = new File(['image bytes'], 'photo.jpg', { type: 'image/jpeg' })
    const progress = vi.fn()
    await client.uploadPublicMemoryMedia(intent, file, progress)
    expect(uploadMethod).toBe('PUT')
    expect(uploadUrl).toBe(intent.ingressUri)
    expect(uploadCredentials).toBe(false)
    expect(sentBody).toBe(file)
    expect(uploadHeaders).toEqual({ 'X-Media-Capability': 'provider-secret' })
    expect(progress).toHaveBeenCalledWith(10, 10)
  })

  it('includes only same-origin sessions for counting so Creator visits can be excluded', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const client = new DavetiyeApiClient({ fetch })
    const code = 'a'.repeat(64)
    await client.recordPublicInvitationView(code)
    expect(fetch).toHaveBeenCalledWith(`/api/v1/public/invitations/${code}/views`, expect.objectContaining({ method: 'POST', credentials: 'same-origin', cache: 'no-store', headers: { 'X-Invitation-Render': '1' }, body: undefined }))
  })
  it('uses the owner trash list and sends antiforgery plus the complete expected tuple for restore', async () => {
    const fetch = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ items: [] }))))
    const client = new DavetiyeApiClient({ fetch })
    const expected = { invitationRevision: 4, workingContentRevision: 5, publishedContentRevision: 2,
      windowId: '22222222-2222-2222-2222-222222222222', windowRevision: 3 }
    await client.listInvitationTrash(2, 20)
    expect(fetch).toHaveBeenCalledWith('/api/v1/invitations/trash?page=2&pageSize=20', expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }))
    await client.restoreInvitation('11111111-1111-1111-1111-111111111111', { expected }, 'csrf-token')
    expect(fetch).toHaveBeenCalledWith('/api/v1/invitations/11111111-1111-1111-1111-111111111111/restore', expect.objectContaining({
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': 'csrf-token' }, body: JSON.stringify({ expected }),
    }))
  })
  it('loads public Published content without credentials, cache or management headers', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ status: 'unavailable' })))
    const client = new DavetiyeApiClient({ fetch })
    const code = 'a'.repeat(64)

    await expect(client.getPublicInvitation(code)).resolves.toEqual({ status: 'unavailable' })
    expect(fetch).toHaveBeenCalledWith(`/api/v1/public/invitations/${code}`, expect.objectContaining({
      method: 'GET', credentials: 'omit', cache: 'no-store', body: undefined, headers: {},
    }))
  })
  it('preserves the browser receiver when using the native global fetch', async () => {
    const strictFetch = vi.fn(function (this: typeof globalThis) {
      expect(this).toBe(globalThis)
      return Promise.resolve(new Response(JSON.stringify({ service: 'Davetiye.Api', apiVersion: 'v1' })))
    })
    vi.stubGlobal('fetch', strictFetch)
    const client = new DavetiyeApiClient()

    await expect(client.getSystemInfo()).resolves.toEqual({ service: 'Davetiye.Api', apiVersion: 'v1' })
    vi.unstubAllGlobals()
  })

  it('uses the versioned API route and sends cookies without storing secrets', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ service: 'Davetiye.Api', apiVersion: 'v1' }),
    ))
    const client = new DavetiyeApiClient({ baseUrl: 'https://api.example.test/', fetch })

    await expect(client.getSystemInfo()).resolves.toEqual({ service: 'Davetiye.Api', apiVersion: 'v1' })
    expect(fetch).toHaveBeenCalledWith('https://api.example.test/api/v1/system/info', {
      credentials: 'include',
      signal: undefined,
    })
  })

  it('gets only the session access classification without browser persistence', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      authenticated: true,
      access: 'creator',
    })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getSessionAccess()).resolves.toEqual({ authenticated: true, access: 'creator' })
    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/session', {
      credentials: 'include',
      cache: 'no-store',
      signal: undefined,
    })
  })

  it('gets the server-owned Google sign-in capability without assuming the challenge route exists', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ googleSignInEnabled: false })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getAuthenticationCapabilities()).resolves.toEqual({ googleSignInEnabled: false })
    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/capabilities', expect.objectContaining({
      method: 'GET',
      credentials: 'include',
      cache: 'no-store',
    }))
  })

  it('registers an account with the explicit account type, sending no credential storage side effects', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 200 }))
    const client = new DavetiyeApiClient({ fetch })

    await client.register({
      email: 'creator@example.test',
      password: 'Sup3rSecret!',
      displayName: 'Ada',
      accountType: 'Organization',
      serviceNoticeAcknowledged: true,
    })

    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/register', expect.objectContaining({
      method: 'POST',
      credentials: 'include',
      body: JSON.stringify({
        email: 'creator@example.test',
        password: 'Sup3rSecret!',
        displayName: 'Ada',
        accountType: 'Organization',
        serviceNoticeAcknowledged: true,
      }),
      headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
    }))
  })

  it('surfaces a 409 register conflict as a typed ApiRequestError with the hoisted extension code', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ title: 'Email already registered.', status: 409, detail: 'x' }),
      { status: 409 },
    ))
    const client = new DavetiyeApiClient({ fetch })

    const error = await client.register({
      email: 'dup@example.test',
      password: 'Sup3rSecret!',
      displayName: 'Ada',
      accountType: 'Individual',
      serviceNoticeAcknowledged: true,
    }).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiRequestError)
    expect((error as ApiRequestError).status).toBe(409)
    expect((error as ApiRequestError).problem?.title).toBe('Email already registered.')
  })

  it('sends current-password proof and antiforgery when confirming a Google account link', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(
      null,
      { status: 200 },
    ))
    const client = new DavetiyeApiClient({ fetch })

    await client.confirmGoogleLink({ password: 'Sup3rSecret!' }, 'csrf-token')

    expect(fetch).toHaveBeenCalledWith('/api/v1/auth/google/link/confirm', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ password: 'Sup3rSecret!' }),
      headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
    }))
  })

  it('reports a login that requires a second factor without treating it as an error', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ requiresTwoFactor: true })))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.login({ email: 'admin@example.test', password: 'x' }))
      .resolves.toEqual({ requiresTwoFactor: true })
  })

  it('treats a bodyless 200 login response as a completed sign-in', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 200 }))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.login({ email: 'creator@example.test', password: 'x' }))
      .resolves.toEqual({ requiresTwoFactor: false })
  })

  it('sends the antiforgery header, never a body, on logout', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 200 }))
    const client = new DavetiyeApiClient({ fetch })

    await client.logout('csrf-token')

    const [, init] = fetch.mock.calls[0] as [string, RequestInit]
    expect(init.body).toBeUndefined()
    expect(init.headers).toMatchObject({ 'X-CSRF-TOKEN': 'csrf-token' })
  })

  it('resolves password-reset requests identically on success regardless of account existence', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 200 }))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.requestPasswordReset({ email: 'anyone@example.test' })).resolves.toBeUndefined()
  })

  it('reads the public template catalog anonymously, without credentials', async () => {
    const catalog = [{
      key: 'zamansiz-dugun', name: 'Zamansız Düğün', description: null, category: 'Düğün', isPremium: false,
      rendererVersion: 1, previewImageUrl: null, supportedModules: [], requiredFields: [], recommendedFields: [],
    }]
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(catalog)))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.listTemplates()).resolves.toEqual(catalog)
    expect(fetch).toHaveBeenCalledWith('/api/v1/templates', expect.objectContaining({
      method: 'GET', credentials: 'omit', cache: 'no-store',
    }))
  })

  it('sends antiforgery and optimistic revision when autosaving a draft', async () => {
    const response = {
      id: 'd3b39a91-208b-4c38-9448-8477818b77ef', templateKey: null, rendererVersion: null,
      createdAt: '2026-09-30T10:00:00Z', updatedAt: '2026-09-30T10:01:00Z',
      invitationRevision: 0, contentRevision: 4, contentSchemaVersion: 1, content: { headline: 'Ada & Efe' },
    }
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(response)))
    const client = new DavetiyeApiClient({ fetch })

    await client.autosaveInvitationDraft(
      response.id,
      { contentSchemaVersion: 1, content: { headline: 'Ada & Efe' }, expectedContentRevision: 3 },
      'csrf-token',
    )

    expect(fetch).toHaveBeenCalledWith(
      `/api/v1/invitations/${response.id}/draft`,
      expect.objectContaining({
        method: 'PUT',
        credentials: 'include',
        headers: expect.objectContaining({ 'X-CSRF-TOKEN': 'csrf-token' }),
        body: JSON.stringify({
          contentSchemaVersion: 1,
          content: { headline: 'Ada & Efe' },
          expectedContentRevision: 3,
        }),
      }),
    )
  })

  it('preserves server revisions on an autosave conflict so the editor can resolve it', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      title: 'The draft was updated elsewhere.',
      status: 409,
      currentInvitationRevision: 2,
      currentContentRevision: 7,
    }), { status: 409 }))
    const client = new DavetiyeApiClient({ fetch })

    const error = await client.autosaveInvitationDraft(
      'd3b39a91-208b-4c38-9448-8477818b77ef',
      { contentSchemaVersion: 1, content: {}, expectedContentRevision: 6 },
      'csrf-token',
    ).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiRequestError)
    expect((error as ApiRequestError).problem).toMatchObject({
      currentInvitationRevision: 2,
      currentContentRevision: 7,
    })
  })

  it('reads the owner-scoped draft validation report without an unsafe request body', async () => {
    const report = {
      invitationId: 'd3b39a91-208b-4c38-9448-8477818b77ef',
      templateKey: 'zamansiz-dugun',
      rendererVersion: 1,
      invitationRevision: 2,
      contentRevision: 4,
      templateSelected: true,
      templateAvailable: true,
      requiredFields: [{ field: 'headline', isRecognized: true, isPresent: true }],
      recommendedFields: [{ field: 'message', isRecognized: true, isPresent: false }],
    }
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify(report)))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getInvitationDraftValidation(report.invitationId)).resolves.toEqual(report)
    expect(fetch).toHaveBeenCalledWith(
      `/api/v1/invitations/${report.invitationId}/validation`,
      expect.objectContaining({
        method: 'GET',
        body: undefined,
        credentials: 'include',
        cache: 'no-store',
      }),
    )
  })

  it('reads private Creator RSVP pages and details with invitation-scoped routes', async () => {
    const page = { invitationId: 'invitation-1', page: 1, pageSize: 25, totalCount: 0, summary: {
      responseCount: 0, questions: [], totalParticipants: null, participantCountAvailable: false,
    }, submissions: [] }
    const details = { submissionId: 'response-1', submittedAt: '2026-10-01T10:00:00Z', updatedAt: '2026-10-01T10:00:00Z', answers: [] }
    const fetch = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify(page)))
      .mockResolvedValueOnce(new Response(JSON.stringify(details)))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.getCreatorRsvpSubmissions('invitation-1')).resolves.toEqual(page)
    await expect(client.getCreatorRsvpSubmission('invitation-1', 'response-1')).resolves.toEqual(details)
    expect(fetch).toHaveBeenNthCalledWith(1,
      '/api/v1/invitations/invitation-1/rsvp/submissions?page=1&pageSize=25',
      expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }),
    )
    expect(fetch).toHaveBeenNthCalledWith(2,
      '/api/v1/invitations/invitation-1/rsvp/submissions/response-1',
      expect.objectContaining({ method: 'GET', credentials: 'include', cache: 'no-store' }),
    )
  })

  it('sends antiforgery for permanent Creator RSVP deletion and handles 204', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const client = new DavetiyeApiClient({ fetch })

    await expect(client.deleteCreatorRsvpSubmission('invitation-1', 'response-1', 'csrf-token')).resolves.toBeUndefined()
    expect(fetch).toHaveBeenCalledWith(
      '/api/v1/invitations/invitation-1/rsvp/submissions/response-1',
      expect.objectContaining({
        method: 'DELETE',
        credentials: 'include',
        cache: 'no-store',
        headers: { 'X-CSRF-TOKEN': 'csrf-token' },
      }),
    )
  })
})
