import { ApiRequestError, type InvitationPublicationState } from '../../api/generated/client'

export const publicationStateLabels: Record<InvitationPublicationState, string> = {
  Draft: 'Taslak', Scheduled: 'Planlandı', Active: 'Yayında', Paused: 'Geçici olarak durduruldu', Expired: 'Yayın süresi bitti',
}

export function formatPublicationTime(instant: string, timeZoneId: string): string {
  try {
    return new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short', timeZone: timeZoneId }).format(new Date(instant))
  } catch {
    return 'Tarih gösterilemiyor'
  }
}

export function publicationLocalTime(instant: string, timeZoneId: string): string {
  try {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: timeZoneId, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    }).formatToParts(new Date(instant))
    const part = (name: string) => parts.find(value => value.type === name)?.value ?? ''
    return `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}`
  } catch {
    return ''
  }
}

export function publicationErrorMessage(error: unknown): string {
  if (!(error instanceof ApiRequestError)) return 'İşlem tamamlanamadı. Bağlantınızı kontrol edip tekrar deneyin.'
  const code = error.problem?.code ?? ''
  const localTimeErrors = [
    ...(error.problem?.errors?.startsAtLocal ?? []),
    ...(error.problem?.errors?.endsAtLocal ?? []),
  ].join(' ')
  if (code === 'InvalidRequest' && /ambiguous/i.test(localTimeErrors)) return 'Bu saat, seçtiğiniz saat diliminde iki kez yaşanıyor. Lütfen başka bir saat seçin.'
  if (code === 'InvalidRequest' && /does not exist/i.test(localTimeErrors)) return 'Bu saat, seçtiğiniz saat diliminde yaşanmıyor. Lütfen başka bir saat seçin.'
  if (/ambiguous/i.test(code)) return 'Bu saat, seçtiğiniz saat diliminde iki kez yaşanıyor. Lütfen başka bir saat seçin.'
  if (/nonexistent|invalid_local|invalidlocal|invalid_time|invalidtime|timezone/i.test(code)) return 'Tarih, saat veya saat dilimi geçerli değil. Saat değişimine denk gelen geçersiz bir saat seçmiş olabilirsiniz.'
  if (/quota/i.test(code)) return 'Seçtiğiniz yayın aralığında davetiye kotanız dolu. Başka bir aralık seçin.'
  if (/premium/i.test(code)) return 'Bu şablonu yayımlamak için uygun Premium hakkınız yok. Ücretsiz bir şablon seçebilirsiniz.'
  if (/duration/i.test(code)) return 'Seçtiğiniz bitiş, yayın hakkınızın izin verdiği süreyi aşıyor. Bitiş tarihini değiştirin.'
  if (/grant|entitlement/i.test(code)) return 'Bu işlem için uygun yayın hakkı bulunamadı. Güncel yayın haklarınızı kontrol edin.'
  if (/required/i.test(code)) return 'Yayın için gerekli alanları tamamlayın.'
  if (/template/i.test(code)) return 'Seçili şablon yayımlanmaya uygun değil. Şablon seçiminizi kontrol edin.'
  if (error.status === 404) return 'Davetiye bulunamadı veya erişim izniniz yok.'
  if (error.status === 403) return 'Bu işlem için hesabınızın doğrulanmış ve kullanıma açık olması gerekir.'
  if (error.status === 409) return 'Davetiye veya yayın hakkınız değişti. Güncel durumu yenileyip işlemi yeniden onaylayın.'
  return 'Tarih, saat ve yayın bilgilerini kontrol edip yeniden deneyin.'
}
