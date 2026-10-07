import { useEffect, useRef, useState } from 'react'

import { ApiRequestError, DavetiyeApiClient, type InvitationPublicationStatus, type PublicationExpected } from '../../api/generated/client'
import { navigate } from '../../routes/navigation'
import { formatPublicationTime } from './publicationPresentation'

interface TrashInvitationControlProps {
  api: DavetiyeApiClient
  status: InvitationPublicationStatus | null
  disabled: boolean
  busy: boolean
  onBusy: (busy: boolean) => void
  onRefresh: () => void
}

export function TrashInvitationControl({ api, status, disabled, busy, onBusy, onRefresh }: TrashInvitationControlProps) {
  const [confirmation, setConfirmation] = useState<{ expected: PublicationExpected; days: number } | null>(null)
  const [message, setMessage] = useState('')
  const headingRef = useRef<HTMLHeadingElement>(null)
  const errorRef = useRef<HTMLParagraphElement>(null)

  useEffect(() => { if (confirmation) headingRef.current?.focus() }, [confirmation])
  useEffect(() => { if (message) errorRef.current?.focus() }, [message])

  if (!status) return null
  const days = status.trashRetentionDays
  const estimatedDeadline = typeof days === 'number' ? new Date(Date.parse(status.serverNowUtc) + days * 86_400_000) : null
  const policyAvailable = typeof days === 'number' && Number.isInteger(days) && days >= 0 && estimatedDeadline && !Number.isNaN(estimatedDeadline.valueOf())
  const validConfirmation = confirmation && confirmation.days === days && JSON.stringify(confirmation.expected) === JSON.stringify(status.expected)
  const cancelsFutureSchedule = status.effectiveState === 'Scheduled' && status.currentWindow && Date.parse(status.currentWindow.startsAtUtc) > Date.parse(status.serverNowUtc)

  const trash = async () => {
    if (!validConfirmation || !confirmation || disabled || busy) return
    onBusy(true)
    setMessage('')
    try {
      const token = await api.getAntiforgeryToken()
      await api.trashInvitation(status.invitationId, { expected: confirmation.expected, expectedRetentionDays: confirmation.days }, token)
      navigate('/panel/cop-kutusu')
    } catch (error) {
      setConfirmation(null)
      setMessage(error instanceof ApiRequestError && error.status === 409
        ? 'Davetiye veya silme bilgileri değişti. Güncel durumu yenileyip yeniden onaylayın.'
        : error instanceof ApiRequestError && error.status === 404
          ? 'Davetiye bulunamadı veya zaten silinmiş olabilir. Davetiye listesini kontrol edin.'
          : 'Davetiye silinemedi. Bağlantınızı ve hesabınızı kontrol edip yeniden deneyin.')
      onRefresh()
    } finally {
      onBusy(false)
    }
  }

  return <section className="trash-management" aria-labelledby="trash-management-heading">
    <h3 id="trash-management-heading">Davetiye silme</h3>
    <p>Silinen davetiyenin public erişimi hemen kapanır. Geri yükleme, yayın erişimini otomatik açmaz.</p>
    {!policyAvailable ? <p className="inline-alert" role="status">Güncel geri yükleme süresi doğrulanamadı. Silme işlemi için yayın durumunu yenileyin.</p> : null}
    {!confirmation ? <button className="button button--secondary" type="button" disabled={disabled || busy || !policyAvailable} onClick={() => {
      if (days === null || days === undefined) return
      setConfirmation({ expected: { ...status.expected }, days })
      setMessage('')
    }}>Çöp kutusuna taşı</button> : null}
    {validConfirmation ? <section className="publication-confirmation" aria-labelledby="trash-confirm-heading">
      <h4 id="trash-confirm-heading" ref={headingRef} tabIndex={-1}>Davetiyeyi silmek istiyor musunuz?</h4>
      <p>Public erişim hemen kapanır; davetiye ve ona bağlı içerikler kalıcı silme sürecine girer.</p>
      {cancelsFutureSchedule ? <p>Henüz başlamamış yayın planı iptal edilir ve kullanılmamış yayın hakkı serbest bırakılır. Geri yükleme yalnızca taslağı getirir; eski planlama açılmaz. Yeniden yayınlamak için ayrıca yeni bir yayın işlemini açıkça onaylamanız gerekir.</p> : null}
      {days === 0 ? <p>Güncel ayarda geri yükleme süresi yoktur. Davetiye hemen kalıcı silme sürecine alınır ve geri yüklenemez.</p> : <>
        <p>Güncel geri yükleme süresi {days} gündür. Bu süre içinde çöp kutusundan yalnızca taslak olarak geri yükleyebilirsiniz.</p>
        <p>Tahmini son tarih: {estimatedDeadline ? formatPublicationTime(estimatedDeadline.toISOString(), status.timeZoneId) : ''} ({status.timeZoneId}). Kesin son tarih, silme tamamlandıktan sonra çöp kutusunda gösterilir.</p>
      </>}
      <div className="button-row">
        <button className="button button--primary" type="button" disabled={disabled || busy || !policyAvailable} onClick={() => void trash()}>{busy ? 'Siliniyor…' : 'Onayla ve çöp kutusuna taşı'}</button>
        <button className="button button--secondary" type="button" disabled={busy} onClick={() => setConfirmation(null)}>Vazgeç</button>
      </div>
    </section> : null}
    {confirmation && !validConfirmation ? <p role="status">Silme bilgileri değişti. Yayın durumunu yenileyip işlemi yeniden gözden geçirin.</p> : null}
    {confirmation && !validConfirmation ? <button className="button button--secondary" type="button" disabled={busy} onClick={() => setConfirmation(null)}>Onayı sıfırla</button> : null}
    {message ? <p className="inline-alert" role="alert" ref={errorRef} tabIndex={-1}>{message}</p> : null}
  </section>
}
