import {
  ApiRequestError,
  type DavetiyeApiClient,
  type IndividualPurchasePlan as ApiIndividualPurchasePlan,
  type PaymentCheckoutResponse,
} from '../../api/generated/client'

export type IndividualPurchasePlan = ApiIndividualPurchasePlan
export type InvitationCheckout = PaymentCheckoutResponse
type CheckoutApi = Pick<DavetiyeApiClient, 'listPaymentPlans' | 'startInvitationCheckout'>

export class CheckoutApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message)
    this.name = 'CheckoutApiError'
  }
}

export async function listIndividualPurchasePlans(api: CheckoutApi, signal?: AbortSignal): Promise<IndividualPurchasePlan[]> {
  try {
    const plans: unknown = await api.listPaymentPlans(signal)
    if (!Array.isArray(plans)) throw new CheckoutApiError(502, 'Paket bilgileri doğrulanamadı.')
    return plans.filter(isIndividualPurchasePlan)
  } catch (error) {
    throw asCheckoutApiError(error)
  }
}

export async function createInvitationCheckout(
  api: CheckoutApi,
  invitationId: string,
  planKey: IndividualPurchasePlan['key'],
  idempotencyKey: string,
  csrfToken: string,
  signal?: AbortSignal,
): Promise<InvitationCheckout> {
  try {
    const checkout: unknown = await api.startInvitationCheckout(invitationId, planKey, idempotencyKey, csrfToken, signal)
    if (!isInvitationCheckout(checkout)) throw new CheckoutApiError(502, 'Ödeme yanıtı doğrulanamadı.')
    return checkout
  } catch (error) {
    throw asCheckoutApiError(error)
  }
}

function asCheckoutApiError(error: unknown): CheckoutApiError {
  if (error instanceof CheckoutApiError) return error
  if (error instanceof ApiRequestError) {
    return new CheckoutApiError(error.status, error.problem?.title ?? error.problem?.detail ?? error.message)
  }
  return new CheckoutApiError(0, error instanceof Error ? error.message : 'İstek tamamlanamadı.')
}

function isIndividualPurchasePlan(value: unknown): value is IndividualPurchasePlan {
  if (typeof value !== 'object' || value === null) return false
  const item = value as Record<string, unknown>
  return (item.key === 'standard' || item.key === 'premium') &&
    typeof item.displayName === 'string' && item.displayName.length > 0 &&
    typeof item.amount === 'number' && Number.isFinite(item.amount) && item.amount >= 0 &&
    item.currency === 'TRY' && item.billingPeriod === 'one-time'
}

function isInvitationCheckout(value: unknown): value is InvitationCheckout {
  if (typeof value !== 'object' || value === null) return false
  const item = value as Record<string, unknown>
  return typeof item.attemptId === 'string' && typeof item.reference === 'string' &&
    (item.status === 'Pending' || item.status === 'Unknown' || item.status === 'Failed' ||
      item.status === 'Canceled' || item.status === 'Succeeded') &&
    (item.planKey === 'standard' || item.planKey === 'premium') &&
    typeof item.amount === 'number' && Number.isFinite(item.amount) && item.currency === 'TRY' &&
    item.billingPeriod === 'one-time' &&
    (typeof item.checkoutUrl === 'string' || item.checkoutUrl === null)
}
