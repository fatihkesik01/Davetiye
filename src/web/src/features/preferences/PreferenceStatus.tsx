import { useTranslation } from 'react-i18next'
import { useAccountPreferences } from './preferencesContext'

/** The shared polite live region for preference saves. */
export function PreferenceStatus() {
  const { t } = useTranslation()
  const { ready, saveState } = useAccountPreferences()
  return <p className="preference-controls__status" role="status" aria-live="polite">{!ready ? t('common.loading') : saveState === 'saving' ? t('preferences.saving') : saveState === 'saved' ? t('preferences.saved') : saveState === 'local' ? t('preferences.local') : saveState === 'error' ? t('error.title') : ''}</p>
}
