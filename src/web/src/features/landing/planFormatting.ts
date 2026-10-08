import type { PublicPlanCatalogItem } from '../../api/generated/client'

const countFormat = new Intl.NumberFormat('tr-TR')
const amountFormat = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 2 })

/**
 * PRODUCT.md §19: a price is shown as amount + billing period (e.g. `1.199 TRY / tek sefer`), with
 * no tax-included/excluded claim. A free plan reads `Ücretsiz`.
 */
export function formatPlanPrice(plan: Pick<PublicPlanCatalogItem, 'priceAmount' | 'currency' | 'billingPeriod'>): string {
  if (plan.billingPeriod === 'free' || plan.priceAmount === 0) return 'Ücretsiz'
  const period = plan.billingPeriod === 'monthly' ? 'ay' : 'tek sefer'
  return `${amountFormat.format(plan.priceAmount)} ${plan.currency} / ${period}`
}

export interface PlanLimitRow {
  label: string
  value: string
}

/**
 * Plain-Turkish summary of the live plan limits. Image/video limits are intentionally omitted while
 * the production media provider is not enabled (PRODUCT.md §30a: media upload is not advertised).
 */
export function describePlanLimits(plan: PublicPlanCatalogItem): PlanLimitRow[] {
  const included = (value: boolean) => value ? 'Dahil' : 'Dahil değil'
  return [
    { label: 'Yayın süresi', value: `${countFormat.format(plan.maxPublishDays)} gün` },
    { label: 'Aynı anda aktif davetiye', value: countFormat.format(plan.maxActiveInvitations) },
    { label: 'Davetiye başına katılım yanıtı (RSVP)', value: countFormat.format(plan.maxRSVPResponses) },
    { label: 'Anılarımız', value: included(plan.memoriesEnabled) },
    { label: 'Hediye / çeyiz listesi', value: included(plan.giftRegistryEnabled) },
    { label: 'Premium şablonlar', value: included(plan.premiumTemplatesEnabled) },
  ]
}
