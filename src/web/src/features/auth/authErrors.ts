import { ApiRequestError } from '../../api/generated/client'

export type AuthOperation = 'register' | 'login' | 'mfa-login' | 'mfa-setup' | 'confirm-email' | 'request-reset' | 'reset-password' | 'google-link' | 'logout'

export function authErrorMessage(error: unknown, operation: AuthOperation): string {
  if (!(error instanceof ApiRequestError)) {
    return 'Şu anda isteğinizi tamamlayamadık. Bağlantınızı kontrol edip tekrar deneyin.'
  }

  if (error.status === 429) return 'Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin.'
  if (operation === 'register' && error.status === 409) return 'Bu e-posta adresiyle daha önce hesap açılmış. Giriş yapmayı deneyin.'

  if (operation === 'register' && error.status === 400) {
    if (error.problem?.title === 'Invalid password.') {
      return 'Şifreniz güvenlik kurallarını karşılamıyor. Daha uzun; büyük-küçük harf, rakam ve simge içeren benzersiz bir şifre seçin.'
    }
    return 'Hesap oluşturma bilgilerinizi kontrol edip tekrar deneyin.'
  }

  if (operation === 'login') {
    if (error.status === 401) return 'E-posta adresi veya şifre hatalı.'
    if (error.status === 403) return 'Giriş yapmadan önce e-posta adresinizi doğrulayın.'
    if (error.status === 423) return 'Çok sayıda hatalı deneme nedeniyle hesap geçici olarak kilitlendi.'
  }

  if ((operation === 'mfa-login' || operation === 'mfa-setup') && error.status === 400) return 'Kod geçersiz veya süresi dolmuş. Yeni bir kod girip tekrar deneyin.'
  if ((operation === 'mfa-login' || operation === 'mfa-setup') && error.status === 423) return 'Çok sayıda hatalı deneme nedeniyle MFA doğrulaması geçici olarak kilitlendi.'

  if (operation === 'confirm-email' && error.status === 400) return 'Bu doğrulama bağlantısı geçersiz veya süresi dolmuş.'
  if (operation === 'reset-password' && error.status === 400) {
    if (error.problem?.title === 'Invalid password.') {
      return 'Yeni şifreniz güvenlik kurallarını karşılamıyor. Daha uzun; büyük-küçük harf, rakam ve simge içeren benzersiz bir şifre seçin.'
    }
    return 'Bağlantı geçersiz veya süresi dolmuş olabilir. Yeni bir şifre sıfırlama bağlantısı isteyin.'
  }

  if (operation === 'google-link') {
    if (error.status === 401) return 'E-posta adresi veya şifre hatalı.'
    if (error.status === 403) return 'Bu hesap için Google ile giriş bağlanamıyor.'
    if (error.status === 409) return 'Google hesabı bu hesapla eşleşmiyor veya başka bir hesaba bağlı.'
    if (error.status === 400) return 'Google bağlantısı tamamlanamadı. Google ile giriş işlemini yeniden başlatın.'
  }

  if (operation === 'request-reset') return 'İsteği şu anda tamamlayamadık. Biraz sonra tekrar deneyin.'
  return 'Şu anda isteğinizi tamamlayamadık. Lütfen tekrar deneyin.'
}
