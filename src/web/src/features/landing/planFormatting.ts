import type { PublicPlanCatalogItem } from '../../api/generated/client'

type Locale = 'tr' | 'en'
const localeTag = (locale: Locale) => locale === 'en' ? 'en-US' : 'tr-TR'

/**
 * PRODUCT.md §19: a price is shown as amount + billing period (e.g. `1.199 TRY / tek sefer`), with
 * no tax-included/excluded claim. A free plan reads `Ücretsiz`.
 */
export function formatPlanPrice(plan: Pick<PublicPlanCatalogItem, 'priceAmount' | 'currency' | 'billingPeriod'>, locale: Locale = 'tr'): string {
  if (plan.billingPeriod === 'free' || plan.priceAmount === 0) return locale === 'en' ? 'Free' : 'Ücretsiz'
  const period = plan.billingPeriod === 'monthly' ? (locale === 'en' ? 'month' : 'ay') : (locale === 'en' ? 'one time' : 'tek sefer')
  return `${new Intl.NumberFormat(localeTag(locale), { maximumFractionDigits: 2 }).format(plan.priceAmount)} ${plan.currency} / ${period}`
}

export interface PlanLimitRow {
  label: string
  value: string
}

/**
 * Plain-Turkish summary of the live plan limits. Image/video limits are intentionally omitted while
 * the production media provider is not enabled (PRODUCT.md §30a: media upload is not advertised).
 */
export function describePlanLimits(plan: PublicPlanCatalogItem, locale: Locale = 'tr'): PlanLimitRow[] {
  const countFormat = new Intl.NumberFormat(localeTag(locale))
  const included = (value: boolean) => value ? (locale === 'en' ? 'Included' : 'Dahil') : (locale === 'en' ? 'Not included' : 'Dahil değil')
  const labels = locale === 'en'
    ? { days: 'Publication period', active: 'Active invitations at a time', rsvp: 'RSVP responses per invitation', memories: 'Shared memories', gifts: 'Gift list', premium: 'Premium templates' }
    : { days: 'Yayın süresi', active: 'Aynı anda aktif davetiye', rsvp: 'Davetiye başına katılım yanıtı (RSVP)', memories: 'Anılarımız', gifts: 'Hediye / çeyiz listesi', premium: 'Premium şablonlar' }
  return [
    { label: labels.days, value: `${countFormat.format(plan.maxPublishDays)} ${locale === 'en' ? 'days' : 'gün'}` },
    { label: labels.active, value: countFormat.format(plan.maxActiveInvitations) },
    { label: labels.rsvp, value: countFormat.format(plan.maxRSVPResponses) },
    { label: labels.memories, value: included(plan.memoriesEnabled) },
    { label: labels.gifts, value: included(plan.giftRegistryEnabled) },
    { label: labels.premium, value: included(plan.premiumTemplatesEnabled) },
  ]
}
