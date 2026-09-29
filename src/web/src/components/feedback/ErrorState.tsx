import { useTranslation } from 'react-i18next'

type ErrorStateProps = {
  title?: string
  description?: string
}

export function ErrorState({ title, description }: ErrorStateProps) {
  const { t } = useTranslation()

  return (
    <section className="feedback-state feedback-state--stacked" role="alert">
      <h1>{title ?? t('error.title')}</h1>
      <p>{description ?? t('error.description')}</p>
    </section>
  )
}

