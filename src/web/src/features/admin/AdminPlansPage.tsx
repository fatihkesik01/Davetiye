import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useLatestT } from '../../i18n/useLatestT'
import { useTranslation } from 'react-i18next'
import {
  ApiRequestError,
  DavetiyeApiClient,
  type AdminPlanItem,
  type AdminPlanBillingKind,
  type UpdateAdminPlanRequest,
} from '../../api/generated/client'

const entitlementFields = [
  { key: 'maxPublishDays', type: 'number' }, { key: 'maxActiveInvitations', type: 'number' },
  { key: 'maxImages', type: 'number' }, { key: 'maxVideos', type: 'number' },
  { key: 'maxImageSizeMb', type: 'number' }, { key: 'maxVideoSizeMb', type: 'number' },
  { key: 'maxVideoDurationSeconds', type: 'number' }, { key: 'maxGuestImages', type: 'number' },
  { key: 'maxGuestVideos', type: 'number' }, { key: 'maxGuestImageSizeMb', type: 'number' },
  { key: 'maxGuestVideoSizeMb', type: 'number' }, { key: 'maxGuestVideoDurationSeconds', type: 'number' },
  { key: 'maxRSVPResponses', type: 'number' }, { key: 'memoriesEnabled', type: 'boolean' },
  { key: 'giftRegistryEnabled', type: 'boolean' }, { key: 'premiumTemplatesEnabled', type: 'boolean' },
] as const

type EntitlementKey = typeof entitlementFields[number]['key']
type PlanDraft = Omit<Pick<UpdateAdminPlanRequest, 'displayName' | 'description' | 'billingKind' | 'entitlements' | 'priceAmount'>, 'priceAmount'> & { priceAmount: string }

function toDraft(plan: AdminPlanItem): PlanDraft {
  return {
    displayName: plan.displayName,
    description: plan.description ?? '',
    priceAmount: String(plan.priceAmount),
    billingKind: plan.billingKind,
    entitlements: plan.entitlements.map(item => ({ ...item })),
  }
}

function priceLabel(plan: AdminPlanItem, locale: string, t: (key: string) => string) {
  const amount = new Intl.NumberFormat(locale, { minimumFractionDigits: 0, maximumFractionDigits: 4 }).format(plan.priceAmount)
  const period = plan.billingKind === 'Monthly' ? ` / ${t('adminUi.plans.periodMonthly')}` : plan.billingKind === 'OneTime' ? ` / ${t('adminUi.plans.periodOneTime')}` : ` / ${t('adminUi.plans.periodFree')}`
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
  const { t, i18n } = useTranslation()
  const tRef = useLatestT()
  const translate = (key: string) => t(key)
  const locale = i18n.resolvedLanguage === 'en' ? 'en-US' : 'tr-TR'
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
          setActionError(tRef.current('adminUi.plans.authRequired'))
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [api, reloadVersion, tRef])

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
    const currentPrice = priceLabel(selected, locale, translate)
    const newPrice = `${new Intl.NumberFormat(locale, { minimumFractionDigits: 0, maximumFractionDigits: 4 }).format(draftPrice!)} ${selected.currency}${draft.billingKind === 'Monthly' ? ` / ${t('adminUi.plans.periodMonthly')}` : draft.billingKind === 'OneTime' ? ` / ${t('adminUi.plans.periodOneTime')}` : ` / ${t('adminUi.plans.periodFree')}`}`
    const message = t('adminUi.plans.confirmSave', { name: selected.displayName, oldPrice: currentPrice, newPrice })
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
      setMessage(t('adminUi.plans.saved', { name: updated.displayName }))
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 409) {
        staleRef.current = true
        setStale(true)
        setActionError(t('adminUi.plans.stale'))
      } else if (error instanceof ApiRequestError && error.status === 400) {
        setValidationErrors([t('adminUi.plans.invalidServer')])
      } else if (error instanceof ApiRequestError && (error.status === 401 || error.status === 403)) {
        setActionError(t('adminUi.plans.saveAuthRequired'))
      } else {
        setActionError(t('adminUi.plans.saveError'))
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
          <h2 id="admin-plans-title">{t('adminUi.plans.title')}</h2>
          <p>{t('adminUi.plans.description')}</p>
        </div>
      </header>

      {actionError ? <p className="admin-banned__message is-error" role="alert">{actionError}</p> : null}
      {message ? <p className="admin-banned__message" role="status">{message}</p> : null}
      {validationErrors.length ? <ul className="admin-banned__message is-error" role="alert">{validationErrors.map(error => <li key={error}>{error}</li>)}</ul> : null}
      {loading ? <p className="admin-operational__status" role="status">{t('adminUi.plans.loading')}</p> : null}
      {!loading && loadError ? (
        <div className="admin-overview__error" role="alert">
          <h3>{t('adminUi.plans.loadFailed')}</h3>
          <p>{actionError || t('adminUi.plans.loadError')}</p>
          <button className="button button--secondary" type="button" onClick={retryLoad}>{t('adminUi.plans.retry')}</button>
        </div>
      ) : null}
      {!loading && !loadError && plans?.length === 0 ? (
        <div className="admin-operational__empty" role="status">
          <h3>{t('adminUi.plans.emptyTitle')}</h3>
          <p>{t('adminUi.plans.emptyBody')}</p>
        </div>
      ) : null}
      {!loading && !loadError && plans && plans.length > 0 ? (
        <div className="admin-plans__layout">
          <nav className="admin-plans__list" aria-label={t('adminUi.plans.planList')}>
            {plans.map(plan => (
              <button key={plan.id} type="button"
                className={`admin-plans__choice${plan.id === selectedId ? ' is-selected' : ''}`}
                aria-current={plan.id === selectedId ? 'true' : undefined}
                disabled={saving}
                onClick={() => selectPlan(plan)}>
                <span className="admin-plans__choice-name">{plan.displayName}</span>
                <span className="admin-plans__choice-meta">{plan.key} · {t(`adminUi.plans.${plan.billingKind === 'OneTime' ? 'oneTime' : plan.billingKind === 'Monthly' ? 'monthly' : 'free'}`)}</span>
                <span className="admin-plans__choice-price">{priceLabel(plan, locale, translate)}</span>
              </button>
            ))}
          </nav>
          {selected && draft ? (
            <form className="admin-plans__editor" onSubmit={event => void save(event)} aria-labelledby="admin-plan-editor-title">
              <div className="admin-plans__editor-heading">
                <div><p className="eyebrow">{selected.key}</p><h3 id="admin-plan-editor-title">{t('adminUi.plans.planDetails')}</h3></div>
                <span className="admin-templates__revision">{t('adminUi.plans.revision', { revision: selected.revision })}</span>
              </div>
              <label className="admin-templates__field" htmlFor="admin-plan-name"><span>{t('adminUi.plans.planName')}</span>
                <input id="admin-plan-name" required maxLength={200} value={draft.displayName}
                  onChange={event => setDraft(current => current ? { ...current, displayName: event.target.value } : current)} />
              </label>
              <label className="admin-templates__field" htmlFor="admin-plan-description">
                <span>{t('adminUi.plans.descriptionLabel')} <span className="admin-templates__optional">{t('adminUi.plans.optional')}</span></span>
                <textarea id="admin-plan-description" rows={3} maxLength={2000} value={draft.description ?? ''}
                  onChange={event => setDraft(current => current ? { ...current, description: event.target.value } : current)} />
                <span className="admin-templates__hint">{t('adminUi.plans.maxChars')}</span>
              </label>
              <label className="admin-templates__field" htmlFor="admin-plan-price">
                <span>{t('adminUi.plans.amount', { currency: selected.currency })}</span>
                <input id="admin-plan-price" type="number" min={0} step="0.0001" required inputMode="decimal"
                  value={draft.priceAmount} onChange={event => setDraft(current => current ? { ...current, priceAmount: event.target.value } : current)} />
                <span className="admin-templates__hint">{t('adminUi.plans.priceHint')}</span>
              </label>
              <label className="admin-templates__field" htmlFor="admin-plan-billing">
                <span>{t('adminUi.plans.billing')}</span>
                <select id="admin-plan-billing" value={draft.billingKind}
                  onChange={event => setDraft(current => current ? { ...current, billingKind: event.target.value as AdminPlanBillingKind } : current)}>
                  <option value="Free">{t('adminUi.plans.free')}</option><option value="OneTime">{t('adminUi.plans.oneTime')}</option><option value="Monthly">{t('adminUi.plans.monthly')}</option>
                </select>
              </label>
              {draft.billingKind === 'Free' && draftPrice !== 0 ? <p className="admin-plans__notice" role="note">{t('adminUi.plans.freeMustBeZero')}</p> : null}
              {draft.billingKind !== 'Free' && draftPrice === 0 ? <p className="admin-plans__notice" role="note">{t('adminUi.plans.paidMustBePositive')}</p> : null}
              <fieldset className="admin-plans__entitlements">
                <legend>{t('adminUi.plans.entitlements')}</legend>
                <p>{t('adminUi.plans.entitlementHint')}</p>
                <div className="admin-plans__entitlement-grid">
                  {entitlementFields.map(field => {
                    const item = draft.entitlements.find(value => value.key === field.key)
                    if (!item) return <p key={field.key} className="admin-plans__notice" role="alert">{t('adminUi.plans.missingEntitlement', { name: t(`adminUi.plans.fields.${field.key}`) })}</p>
                    return field.type === 'number' ? (
                      <label className="admin-templates__field" htmlFor={`plan-entitlement-${field.key}`} key={field.key}>
                        <span>{t(`adminUi.plans.fields.${field.key}`)}</span>
                        <input id={`plan-entitlement-${field.key}`} type="number" min={0} step={1} required
                          value={item.numericValue ?? ''} onChange={event => updateEntitlement(field.key, event.target.value === '' ? null : Number(event.target.value))} />
                      </label>
                    ) : (
                      <label className="admin-templates__visibility" htmlFor={`plan-entitlement-${field.key}`} key={field.key}>
                        <input id={`plan-entitlement-${field.key}`} type="checkbox" checked={item.booleanValue === true}
                          onChange={event => updateEntitlement(field.key, event.target.checked)} />
                        <span><strong>{t(`adminUi.plans.fields.${field.key}`)}</strong><small>{t(item.booleanValue ? 'adminUi.plans.enabled' : 'adminUi.plans.disabled')}</small></span>
                      </label>
                    )
                  })}
                </div>
              </fieldset>
              {stale ? <div className="admin-templates__conflict" role="group" aria-label={t('adminUi.plans.currentRevision')}>
                <p>{t('adminUi.plans.refreshConflict')}</p>
                <button className="button button--secondary" type="button" onClick={retryLoad} disabled={loading}>{t('adminUi.plans.refresh')}</button>
              </div> : null}
              <div className="admin-templates__actions">
                <button className="button button--primary" type="submit" disabled={!changed || !valid || saving || stale}>
                  {saving ? t('adminUi.plans.saving') : t('adminUi.plans.saveChanges')}
                </button>
              </div>
            </form>
          ) : null}
        </div>
      ) : null}
    </section>
  )
}
