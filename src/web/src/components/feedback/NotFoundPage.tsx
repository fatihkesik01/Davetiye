import { useTranslation } from 'react-i18next'

export function NotFoundPage() {
  const { t } = useTranslation()

  return (
    <main id="main-content" className="page-container" tabIndex={-1}>
      <section className="feedback-state feedback-state--stacked">
        <h1>{t('notFound.title')}</h1>
        <p>{t('notFound.description')}</p>
      </section>
    </main>
  )
}

