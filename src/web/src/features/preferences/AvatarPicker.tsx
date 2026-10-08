import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { Avatar } from './avatars'
import { PreferenceStatus } from './PreferenceStatus'
import { AVATAR_KEYS, type AvatarKey, useAccountPreferences } from './preferencesContext'

/**
 * Single-choice avatar group built on native radio inputs (arrow keys, roving tab stop and
 * selected-state semantics come from the browser). The selected tile also shows a check badge and a
 * thick ring so selection is not conveyed by colour alone. "Remove" sets the choice back to none.
 */
export function AvatarPicker({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation()
  const { preferences, ready, save } = useAccountPreferences()
  const name = useId()
  const choose = (avatar: AvatarKey | null) => { if (avatar !== preferences.avatar) void save({ ...preferences, avatar }) }
  return <fieldset className={`avatar-picker${compact ? ' avatar-picker--compact' : ''}`} disabled={!ready}>
    <legend>{t('avatar.legend')}</legend>
    <div className="avatar-picker__grid">
      {AVATAR_KEYS.map(key => <label key={key} className="avatar-picker__option">
        <input type="radio" name={name} value={key} checked={preferences.avatar === key} onChange={() => choose(key)} />
        <span className="avatar-picker__tile"><Avatar avatarKey={key} size={48} /><span className="avatar-picker__check" aria-hidden="true">✓</span></span>
        <span className="avatar-picker__name">{t(`avatar.names.${key}`)}</span>
      </label>)}
    </div>
    <button type="button" className="avatar-picker__remove" disabled={preferences.avatar === null} onClick={() => choose(null)}>{t('avatar.remove')}</button>
  </fieldset>
}

/** Settings-page section: the picker plus its own live region. */
export function AvatarPreferenceSection() {
  const { t } = useTranslation()
  return <section aria-labelledby="avatar-preferences-title" className="avatar-section">
    <h3 id="avatar-preferences-title">{t('avatar.title')}</h3>
    <p>{t('avatar.description')}</p>
    <AvatarPicker />
    <PreferenceStatus />
  </section>
}
