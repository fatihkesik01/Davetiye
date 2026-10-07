import { useEffect, useState } from 'react'

import {
  ApiRequestError,
  type CreatorMemoryConfiguration,
  type DavetiyeApiClient,
  type MemoryVisibility,
} from '../../api/generated/client'
import { CreatorMemoriesModeration } from './CreatorMemoriesModeration'

interface Props {
  api: DavetiyeApiClient
  invitationId: string
}

export function MemoriesManagementPanel({ api, invitationId }: Props) {
  const [configuration, setConfiguration] = useState<CreatorMemoryConfiguration | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const [loadAttempt, setLoadAttempt] = useState(0)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [isEnabled, setIsEnabled] = useState(false)
  const [visibility, setVisibility] = useState<MemoryVisibility>('CreatorOnly')

  useEffect(() => {
    const controller = new AbortController()
    void Promise.resolve().then(() => api.getInvitationMemoriesConfiguration(invitationId, controller.signal)).then(result => {
      if (controller.signal.aborted) return
      adopt(result)
      setLoading(false)
    }).catch(error => {
      if (error instanceof DOMException && error.name === 'AbortError') return
      setFailed(true)
      setLoading(false)
    })
    return () => controller.abort()
  }, [api, invitationId, loadAttempt])

  function adopt(result: CreatorMemoryConfiguration) {
    setConfiguration(result)
    setIsEnabled(result.isEnabled)
    setVisibility(result.visibility === 'Public' ? 'Public' : 'CreatorOnly')
  }

  const dirty = Boolean(configuration) && (isEnabled !== configuration!.isEnabled || visibility !== configuration!.visibility)

  const save = async () => {
    if (!configuration || busy || !dirty) return
    setBusy(true)
    setMessage('Kaydediliyor…')
    try {
      const csrf = await api.getAntiforgeryToken()
      // The revision in the response is authoritative: it can advance by more than one.
      const updated = await api.setInvitationMemoriesConfiguration(invitationId, {
        expectedRevision: configuration.revision, isEnabled, visibility,
      }, csrf)
      adopt(updated)
      setMessage('Anılar ayarları kaydedildi.')
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        try {
          adopt(await api.getInvitationMemoriesConfiguration(invitationId))
          setMessage('Anılar ayarları başka bir sekmede değişti. Güncel ayarlar yüklendi; değişikliğinizi yeniden uygulayın.')
        } catch {
          setMessage('Güncel anılar ayarları yüklenemedi. Tekrar deneyin.')
        }
      } else if (error instanceof ApiRequestError && error.status === 400) setMessage('Bu değişiklik doğrulanamadı. Seçimlerinizi kontrol edip tekrar deneyin.')
      else if (error instanceof ApiRequestError && error.status === 404) setMessage('Davetiyeye erişilemiyor. Sayfayı yenileyip tekrar deneyin.')
      else if (error instanceof ApiRequestError && error.status === 429) setMessage('Kısa sürede çok fazla deneme yapıldı. Biraz bekleyip tekrar deneyin.')
      else setMessage('Anılar ayarları kaydedilemedi. Tekrar deneyin.')
    } finally {
      setBusy(false)
    }
  }

  const titleId = `memories-panel-title-${invitationId}`
  const limits = configuration?.inputLimits

  return <section className="rsvp-panel memories-panel" aria-labelledby={titleId}>
    <div className="rsvp-panel__heading"><div><h3 id={titleId}>Anılar</h3><p>Davetlilerin davetiyeye kısa bir not ve emoji bırakmasına izin verin.</p></div></div>
    {loading ? <p role="status">Anılar ayarları yükleniyor…</p> : null}
    {failed ? <div className="inline-alert" role="alert"><p>Anılar ayarları yüklenemedi.</p>
      <button type="button" className="button button--secondary" onClick={() => { setLoading(true); setFailed(false); setLoadAttempt(value => value + 1) }}>Tekrar dene</button></div> : null}
    {configuration ? <>
      <label className="rsvp-panel__toggle"><input type="checkbox" checked={isEnabled} disabled={busy} onChange={event => setIsEnabled(event.target.checked)} /> Anılar bölümünü aç</label>
      <fieldset className="memories-panel__visibility">
        <legend>Anıları kimler görebilir?</legend>
        <label className="memories-panel__option">
          <input type="radio" name={`memories-visibility-${invitationId}`} value="CreatorOnly" checked={visibility === 'CreatorOnly'} disabled={busy} onChange={() => setVisibility('CreatorOnly')} />
          <span><strong>Sadece Creator</strong><span className="memories-panel__hint">Anıları yalnızca siz görürsünüz. Davetliler anı bırakabilir ama diğer davetlilerin anılarını göremez.</span></span>
        </label>
        <label className="memories-panel__option">
          <input type="radio" name={`memories-visibility-${invitationId}`} value="Public" checked={visibility === 'Public'} disabled={busy} onChange={() => setVisibility('Public')} />
          <span><strong>Public</strong><span className="memories-panel__hint">Davetlilerin yazdığı yazı ve emoji, herkese açık davetiye sayfasında hemen görünür.</span></span>
        </label>
        <p className="rsvp-panel__privacy">Public seçiliyken sonradan Sadece Creator’a dönerseniz tüm anılar herkese kapalı hale gelir. Anılar silinmez, saklanır; tekrar Public yaparsanız yeniden görünür.</p>
      </fieldset>
      <p className="rsvp-panel__privacy">Anılar bölümü, yalnızca planınız bu özelliği içeriyorsa ve davetiye yayında (Aktif) olduğunda davetlilere ulaşır. Ayarı şimdi kaydedebilirsiniz; bu koşullar sağlanana kadar davetli sayfasında görünmez.</p>
      {limits ? <p className="rsvp-panel__privacy">Davetliler en fazla {limits.maxDisplayNameCharacters} birimlik isim, {limits.maxTextCharacters} birimlik not ve tek bir emoji bırakabilir. Davetiye başına en fazla {limits.maxMemoriesPerInvitation} anı kabul edilir. Emojiler birden fazla birim sayılır.</p> : null}
      <div className="button-row"><button type="button" className="button button--primary" disabled={busy || !dirty} onClick={() => void save()}>Kaydet</button></div>
      <CreatorMemoriesModeration api={api} invitationId={invitationId} />
    </> : null}
    <p role="status" aria-live="polite">{message}</p>
  </section>
}
