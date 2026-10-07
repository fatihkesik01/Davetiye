import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminPlanItem,
  type AdminPlanBillingKind,
  type UpdateAdminPlanRequest,
} from '../../api/generated/client'

const entitlementFields = [
  { key: 'maxPublishDays', label: 'En uzun yayın süresi (gün)', type: 'number' },
  { key: 'maxActiveInvitations', label: 'Aynı anda aktif davetiye', type: 'number' },
  { key: 'maxImages', label: 'Creator fotoğraf kotası', type: 'number' },
  { key: 'maxVideos', label: 'Creator video kotası', type: 'number' },
  { key: 'maxImageSizeMb', label: 'Creator fotoğraf boyutu (MB)', type: 'number' },
  { key: 'maxVideoSizeMb', label: 'Creator video boyutu (MB)', type: 'number' },
  { key: 'maxVideoDurationSeconds', label: 'Creator video süresi (saniye)', type: 'number' },
  { key: 'maxGuestImages', label: 'Misafir fotoğraf kotası', type: 'number' },
  { key: 'maxGuestVideos', label: 'Misafir video kotası', type: 'number' },
  { key: 'maxGuestImageSizeMb', label: 'Misafir fotoğraf boyutu (MB)', type: 'number' },
  { key: 'maxGuestVideoSizeMb', label: 'Misafir video boyutu (MB)', type: 'number' },
  { key: 'maxGuestVideoDurationSeconds', label: 'Misafir video süresi (saniye)', type: 'number' },
  { key: 'maxRSVPResponses', label: 'Davetiye başına RSVP yanıtı', type: 'number' },
  { key: 'memoriesEnabled', label: 'Anı modülü', type: 'boolean' },
  { key: 'giftRegistryEnabled', label: 'Hediye listesi', type: 'boolean' },
  { key: 'premiumTemplatesEnabled', label: 'Premium şablonlar', type: 'boolean' },
] as const

type EntitlementKey = typeof entitlementFields[number]['key']
type PlanDraft = Omit<Pick<UpdateAdminPlanRequest, 'displayName' | 'description' | 'billingKind' | 'entitlements' | 'priceAmount'>, 'priceAmount'> & { priceAmount: string }

const billingLabels: Record<AdminPlanBillingKind, string> = {
  Free: 'Ücretsiz',
  OneTime: 'Tek seferlik',
  Monthly: 'Aylık',
}

function toDraft(plan: AdminPlanItem): PlanDraft {
  return {
    displayName: plan.displayName,
    description: plan.description ?? '',
    priceAmount: String(plan.priceAmount),
    billingKind: plan.billingKind,
    entitlements: plan.entitlements.map(item => ({ ...item })),
  }
}

function priceLabel(plan: AdminPlanItem) {
  const amount = new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 4 }).format(plan.priceAmount)
  const period = plan.billingKind === 'Monthly' ? ' / aylık' : plan.billingKind === 'OneTime' ? ' / tek sefer' : ' / ücretsiz'
  return `${amount} ${plan.currency}${period}`
}

function parsePriceAmount(value: string): number | null {
  const normalized = value.trim().replace(',', '.')
  if (!/^\d+(?:\.\d{0,4})?$/.test(normalized)) return null
  const amount = Number(normalized)
  return Number.isFinite(amount) ? amount : null
}

function draftIsComplete(draft: PlanDraft) {
  if (draft.entitlements.length !== entitlementFields.length) return false
  return entitlementFields.every(field => {
    const item = draft.entitlements.find(value => value.key === field.key)
    return field.type === 'number'
      ? item?.numericValue !== null && item?.numericValue !== undefined && Number.isInteger(item.numericValue) && item.numericValue >= 0 && item.booleanValue === null
      : item?.booleanValue !== null && item?.booleanValue !== undefined && item.numericValue === null
  })
}

export function AdminPlansPage() {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [plans, setPlans] = useState<AdminPlanItem[] | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [draft, setDraft] = useState<PlanDraft | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState(false)
  const [reloadVersion, setReloadVersion] = useState(0)
  const [saving, setSaving] = useState(false)
  const [stale, setStale] = useState(false)
  const [message, setMessage] = useState('')
  const [actionError, setActionError] = useState('')
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const staleRef = useRef(false)
  const selectedIdRef = useRef<string | null>(null)
  const selected = plans?.find(plan => plan.id === selectedId) ?? null

  useEffect(() => {
    const controller = new AbortController()
    void api.getAdminPlans(controller.signal)
      .then(items => {
        if (controller.signal.aborted) return
        setPlans(items)
        const currentId = selectedIdRef.current
        const next = items.find(item => item.id === currentId) ?? items[0] ?? null
        selectedIdRef.current = next?.id ?? null
        setSelectedId(next?.id ?? null)
        if (next && (staleRef.current || !currentId)) setDraft(toDraft(next))
        staleRef.current = false
        setStale(false)
        setLoadError(false)
        setActionError('')
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        setLoadError(true)
        if (error instanceof ApiRequestError && (error.status === 401 || error.status === 403)) {
          setActionError('Bu görünüm için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.')
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [api, reloadVersion])

  const changed = Boolean(selected && draft && (
    draft.displayName.trim() !== selected.displayName ||
    (draft.description?.trim() || null) !== selected.description ||
    parsePriceAmount(draft.priceAmount) !== selected.priceAmount ||
    draft.billingKind !== selected.billingKind ||
    draft.entitlements.some(item => {
      const original = selected.entitlements.find(value => value.key === item.key)
      return !original || original.numericValue !== item.numericValue || original.booleanValue !== item.booleanValue
    })
  ))
  const draftPrice = draft ? parsePriceAmount(draft.priceAmount) : null
  const validPrice = draftPrice !== null && (draft?.billingKind === 'Free' ? draftPrice === 0 : draftPrice > 0)
  const valid = Boolean(draft && draft.displayName.trim() && draft.displayName.trim().length <= 200 && (draft.description?.trim().length ?? 0) <= 2000 && validPrice && draftIsComplete(draft))

  function selectPlan(plan: AdminPlanItem) {
    selectedIdRef.current = plan.id
    setSelectedId(plan.id)
    setDraft(toDraft(plan))
    setStale(false)
    staleRef.current = false
    setMessage('')
    setActionError('')
    setValidationErrors([])
  }

  function retryLoad() {
    setLoading(true)
    setLoadError(false)
    setActionError('')
    setReloadVersion(value => value + 1)
  }

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!selected || !draft || !changed || !valid || saving || stale) return
    const request: UpdateAdminPlanRequest = {
      expectedRevision: selected.revision,
      displayName: draft.displayName.trim(),
      description: draft.description?.trim() || null,
      priceAmount: draftPrice!,
      billingKind: draft.billingKind,
      entitlements: [...draft.entitlements].sort((a, b) => a.key.localeCompare(b.key)),
    }
    const currentPrice = priceLabel(selected)
    const newPrice = `${new Intl.NumberFormat('tr-TR', { minimumFractionDigits: 0, maximumFractionDigits: 4 }).format(draftPrice!)} ${selected.currency}${draft.billingKind === 'Monthly' ? ' / aylık' : draft.billingKind === 'OneTime' ? ' / tek sefer' : ' / ücretsiz'}`
    const message = `“${selected.displayName}” planı ${currentPrice} → ${newPrice} olarak güncellenecek. Fiyat ve faturalandırma değişikliği yalnızca yeni alımlara uygulanır; mevcut yayın hakları ve aboneliklerin fiyatı korunur. Hak limitleri düşerse mevcut veriler silinmez. Devam edilsin mi?`
    if (!window.confirm(message)) return

    setSaving(true)
    setActionError('')
    setMessage('')
    setValidationErrors([])
    try {
      const csrfToken = await api.getAntiforgeryToken()
      const updated = await api.updateAdminPlan(selected.id, request, csrfToken)
      setPlans(current => current?.map(item => item.id === updated.id ? updated : item) ?? [updated])
      setDraft(toDraft(updated))
      staleRef.current = false
      setMessage(`${updated.displayName} planı kaydedildi.`)
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        staleRef.current = true
        setStale(true)
        setActionError('Plan siz düzenlerken başka bir yönetici tarafından değiştirildi. Yerel düzenlemeleriniz korunuyor; kaydetmeden önce güncel sürümü yükleyin.')
      } else if (error instanceof ApiRequestError && error.status === 400) {
        setValidationErrors(['Sunucu plan değerlerini veya desteklenen sınırları kabul etmedi. Değerleri gözden geçirip yeniden deneyin.'])
      } else if (error instanceof ApiRequestError && (error.status === 401 || error.status === 403)) {
        setActionError('Kaydetmek için MFA doğrulaması tamamlanmış yönetici oturumu gerekiyor.')
      } else {
        setActionError('Plan kaydedilemedi. Bağlantınızı kontrol edip yeniden deneyin.')
      }
    } finally {
      setSaving(false)
    }
  }

  function updateEntitlement(key: EntitlementKey, value: number | boolean | null) {
    setDraft(current => current ? {
      ...current,
      entitlements: current.entitlements.map(item => item.key !== key ? item : typeof value === 'boolean'
        ? { ...item, numericValue: null, booleanValue: value }
        : { ...item, numericValue: value, booleanValue: null }),
    } : current)
    setValidationErrors([])
  }

  return (
    <section className="admin-plans" aria-labelledby="admin-plans-title" aria-busy={loading || saving}>
      <header className="admin-overview__heading">
        <div>
          <h2 id="admin-plans-title">Planlar ve haklar</h2>
          <p>Plan adını, açıklamasını, fiyatı, faturalandırma türünü ve desteklenen hak değerlerini yönetin.</p>
        </div>
      </header>

      {actionError ? <p className="admin-banned__message is-error" role="alert">{actionError}</p> : null}
      {message ? <p className="admin-banned__message" role="status">{message}</p> : null}
      {validationErrors.length ? <ul className="admin-banned__message is-error" role="alert">{validationErrors.map(error => <li key={error}>{error}</li>)}</ul> : null}
      {loading ? <p className="admin-operational__status" role="status">Planlar yükleniyor…</p> : null}
      {!loading && loadError ? (
        <div className="admin-overview__error" role="alert">
          <h3>Planlar yüklenemedi</h3>
          <p>{actionError || 'Plan listesi şu anda alınamadı. Biraz sonra yeniden deneyin.'}</p>
          <button className="button button--secondary" type="button" onClick={retryLoad}>Yeniden dene</button>
        </div>
      ) : null}
      {!loading && !loadError && plans?.length === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>Yönetilecek plan yok</h3>
          <p>Plan tanımları eklendiğinde burada görünür.</p>
        </div>
      ) : null}
      {!loading && !loadError && plans && plans.length > 0 ? (
        <div className="admin-plans__layout">
          <nav className="admin-plans__list" aria-label="Düzenlenecek plan">
            {plans.map(plan => (
              <button key={plan.id} type="button"
                className={`admin-plans__choice${plan.id === selectedId ? ' is-selected' : ''}`}
                aria-current={plan.id === selectedId ? 'true' : undefined}
                disabled={saving}
                onClick={() => selectPlan(plan)}>
                <span className="admin-plans__choice-name">{plan.displayName}</span>
                <span className="admin-plans__choice-meta">{plan.key} · {billingLabels[plan.billingKind]}</span>
                <span className="admin-plans__choice-price">{priceLabel(plan)}</span>
              </button>
            ))}
          </nav>
          {selected && draft ? (
            <form className="admin-plans__editor" onSubmit={event => void save(event)} aria-labelledby="admin-plan-editor-title">
              <div className="admin-plans__editor-heading">
                <div><p className="eyebrow">{selected.key}</p><h3 id="admin-plan-editor-title">Plan bilgileri</h3></div>
                <span className="admin-templates__revision">Sürüm {selected.revision}</span>
              </div>
              <label className="admin-templates__field" htmlFor="admin-plan-name"><span>Plan adı</span>
                <input id="admin-plan-name" required maxLength={200} value={draft.displayName}
                  onChange={event => setDraft(current => current ? { ...current, displayName: event.target.value } : current)} />
              </label>
              <label className="admin-templates__field" htmlFor="admin-plan-description">
                <span>Açıklama <span className="admin-templates__optional">(isteğe bağlı)</span></span>
                <textarea id="admin-plan-description" rows={3} maxLength={2000} value={draft.description ?? ''}
                  onChange={event => setDraft(current => current ? { ...current, description: event.target.value } : current)} />
                <span className="admin-templates__hint">En çok 2.000 karakter.</span>
              </label>
              <label className="admin-templates__field" htmlFor="admin-plan-price">
                <span>Plan tutarı ({selected.currency})</span>
                <input id="admin-plan-price" type="number" min={0} step="0.0001" required inputMode="decimal"
                  value={draft.priceAmount} onChange={event => setDraft(current => current ? { ...current, priceAmount: event.target.value } : current)} />
                <span className="admin-templates__hint">En fazla 4 ondalık basamak. Free plan 0; Tek seferlik ve Aylık planlar 0’dan büyük tutar kullanır. Para birimi salt okunurdur. Fiyat değişikliği yalnızca yeni alımlara uygulanır; mevcut yayın hakları ve abonelikler eski fiyatını korur.</span>
              </label>
              <label className="admin-templates__field" htmlFor="admin-plan-billing">
                <span>Faturalandırma türü</span>
                <select id="admin-plan-billing" value={draft.billingKind}
                  onChange={event => setDraft(current => current ? { ...current, billingKind: event.target.value as AdminPlanBillingKind } : current)}>
                  <option value="Free">Ücretsiz</option><option value="OneTime">Tek seferlik</option><option value="Monthly">Aylık</option>
                </select>
              </label>
              {draft.billingKind === 'Free' && draftPrice !== 0 ? <p className="admin-plans__notice" role="note">Ücretsiz planın tutarı sıfır olmalıdır. Tutarı 0 yapın veya başka bir faturalandırma türü seçin.</p> : null}
              {draft.billingKind !== 'Free' && draftPrice === 0 ? <p className="admin-plans__notice" role="note">Ücretli planların tutarı sıfırdan büyük olmalıdır. Pozitif bir tutar girin.</p> : null}
              <fieldset className="admin-plans__entitlements">
                <legend>Plan hakları</legend>
                <p>Sayısal alanlar tam sayı kabul eder. Desteklenen üst sınırlar sunucuda doğrulanır; buradaki değerler mevcut kullanım verisini silmez.</p>
                <div className="admin-plans__entitlement-grid">
                  {entitlementFields.map(field => {
                    const item = draft.entitlements.find(value => value.key === field.key)
                    if (!item) return <p key={field.key} className="admin-plans__notice" role="alert">{field.label} verisi alınamadı.</p>
                    return field.type === 'number' ? (
                      <label className="admin-templates__field" htmlFor={`plan-entitlement-${field.key}`} key={field.key}>
                        <span>{field.label}</span>
                        <input id={`plan-entitlement-${field.key}`} type="number" min={0} step={1} required
                          value={item.numericValue ?? ''} onChange={event => updateEntitlement(field.key, event.target.value === '' ? null : Number(event.target.value))} />
                      </label>
                    ) : (
                      <label className="admin-templates__visibility" htmlFor={`plan-entitlement-${field.key}`} key={field.key}>
                        <input id={`plan-entitlement-${field.key}`} type="checkbox" checked={item.booleanValue === true}
                          onChange={event => updateEntitlement(field.key, event.target.checked)} />
                        <span><strong>{field.label}</strong><small>{item.booleanValue ? 'Açık' : 'Kapalı'}</small></span>
                      </label>
                    )
                  })}
                </div>
              </fieldset>
              {stale ? <div className="admin-templates__conflict" role="group" aria-label="Güncel plan sürümü">
                <p>Sunucudaki güncel değerleri yükleyin. Yerel düzenlemeler bu işlem yapılana kadar korunur.</p>
                <button className="button button--secondary" type="button" onClick={retryLoad} disabled={loading}>Güncel sürümü yükle</button>
              </div> : null}
              <div className="admin-templates__actions">
                <button className="button button--primary" type="submit" disabled={!changed || !valid || saving || stale}>
                  {saving ? 'Kaydediliyor…' : 'Değişiklikleri kaydet'}
                </button>
              </div>
            </form>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
