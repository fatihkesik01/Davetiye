import { useTranslation } from 'react-i18next'

type LoadingStateProps = {
  label?: string
}

export function LoadingState({ label }: LoadingStateProps) {
  const { t } = useTranslation()

  return (
    <div className="feedback-state" role="status" aria-live="polite">
      <span className="loading-indicator" aria-hidden="true" />
      <span>{label ?? t('common.loading')}</span>
    </div>
  )
}

