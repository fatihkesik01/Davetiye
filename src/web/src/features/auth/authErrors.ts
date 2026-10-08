import { ApiRequestError } from '../../api/generated/client'

const localized = (turkish: string, english: string) => document.documentElement.lang === 'en' ? english : turkish

export type AuthOperation = 'register' | 'login' | 'mfa-login' | 'mfa-setup' | 'confirm-email' | 'request-reset' | 'reset-password' | 'google-link' | 'logout'

export function authErrorMessage(error: unknown, operation: AuthOperation): string {
  if (!(error instanceof ApiRequestError)) {
    return localized('Şu anda isteğinizi tamamlayamadık. Bağlantınızı kontrol edip tekrar deneyin.', 'We could not complete your request. Check your connection and try again.')
  }

  if (error.status === 429) return localized('Çok fazla deneme yapıldı. Lütfen biraz bekleyip tekrar deneyin.', 'There have been too many attempts. Wait a little and try again.')
  if (operation === 'register' && error.status === 409) return localized('Bu e-posta adresiyle daha önce hesap açılmış. Giriş yapmayı deneyin.', 'An account already exists with this email address. Try signing in.')

  if (operation === 'register' && error.status === 400) {
    if (error.problem?.title === 'Invalid password.') {
      return localized('Şifreniz güvenlik kurallarını karşılamıyor. Daha uzun; büyük-küçük harf, rakam ve simge içeren benzersiz bir şifre seçin.', 'Your password does not meet the security requirements. Choose a longer, unique password with uppercase and lowercase letters, a number, and a symbol.')
    }
    return localized('Hesap oluşturma bilgilerinizi kontrol edip tekrar deneyin.', 'Check your account details and try again.')
  }

  if (operation === 'login') {
    if (error.status === 401) return localized('E-posta adresi veya şifre hatalı.', 'The email address or password is incorrect.')
    if (error.status === 403) return localized('Giriş yapmadan önce e-posta adresinizi doğrulayın.', 'Verify your email address before signing in.')
    if (error.status === 423) return localized('Çok sayıda hatalı deneme nedeniyle hesap geçici olarak kilitlendi.', 'The account is temporarily locked after too many failed attempts.')
  }

  if ((operation === 'mfa-login' || operation === 'mfa-setup') && error.status === 400) return localized('Kod geçersiz veya süresi dolmuş. Yeni bir kod girip tekrar deneyin.', 'The code is invalid or expired. Enter a new code and try again.')
  if ((operation === 'mfa-login' || operation === 'mfa-setup') && error.status === 423) return localized('Çok sayıda hatalı deneme nedeniyle MFA doğrulaması geçici olarak kilitlendi.', 'MFA verification is temporarily locked after too many failed attempts.')

  if (operation === 'confirm-email' && error.status === 400) return localized('Bu doğrulama bağlantısı geçersiz veya süresi dolmuş.', 'This verification link is invalid or has expired.')
  if (operation === 'reset-password' && error.status === 400) {
    if (error.problem?.title === 'Invalid password.') {
      return localized('Yeni şifreniz güvenlik kurallarını karşılamıyor. Daha uzun; büyük-küçük harf, rakam ve simge içeren benzersiz bir şifre seçin.', 'Your new password does not meet the security requirements. Choose a longer, unique password with uppercase and lowercase letters, a number, and a symbol.')
    }
    return localized('Bağlantı geçersiz veya süresi dolmuş olabilir. Yeni bir şifre sıfırlama bağlantısı isteyin.', 'The link may be invalid or expired. Request a new password reset link.')
  }

  if (operation === 'google-link') {
    if (error.status === 401) return localized('E-posta adresi veya şifre hatalı.', 'The email address or password is incorrect.')
    if (error.status === 403) return localized('Bu hesap için Google ile giriş bağlanamıyor.', 'Google sign-in cannot be linked to this account.')
    if (error.status === 409) return localized('Google hesabı bu hesapla eşleşmiyor veya başka bir hesaba bağlı.', 'This Google account does not match this account or is linked to another account.')
    if (error.status === 400) return localized('Google bağlantısı tamamlanamadı. Google ile giriş işlemini yeniden başlatın.', 'The Google link could not be completed. Restart the Google sign-in flow.')
  }

  if (operation === 'request-reset') return localized('İsteği şu anda tamamlayamadık. Biraz sonra tekrar deneyin.', 'We could not complete the request right now. Please try again shortly.')
  return localized('Şu anda isteğinizi tamamlayamadık. Lütfen tekrar deneyin.', 'We could not complete your request right now. Please try again.')
}
