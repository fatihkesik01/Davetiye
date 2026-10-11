import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { InternalLink } from '../../../components/ui/InternalLink'
import { TemplateDemoRenderer } from '../rendering/InvitationRenderer'
import { ZarfAccentPicker } from '../rendering/ZarfAccentPicker'
import { defaultZarfAccent, type ZarfAccentKey } from '../rendering/zarfAccent'

/**
 * Public demo of a template as a full page, like a real invitation: no catalog frame, just the
 * invitation plus a small back link and a compact floating bar with the colour choice.
 */
export function FullPageTemplateDemo({ templateKey }: { templateKey: string }) {
  const { t } = useTranslation()
  const [accent, setAccent] = useState<ZarfAccentKey>(defaultZarfAccent)
  // In-memory only: a reload always starts with the envelope closed.
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const previous = document.title
    document.title = `${t('templateUi.demoTitle')} | Kutlio`
    return () => { document.title = previous }
  }, [t])

  return <div className="template-fullpage-demo">
    <main className="template-fullpage-demo__canvas">
      <TemplateDemoRenderer templateKey={templateKey} rendererVersion={1} accentColor={accent} open={open} onOpenChange={setOpen} hideInlineClose />
    </main>
    <InternalLink className="template-fullpage-demo__back" to="/sablonlar">← {t('templateUi.zarf.back')}</InternalLink>
    <div className="template-fullpage-demo__bar" role="region" aria-label={t('templateUi.zarf.controls')}>
      <ZarfAccentPicker value={accent} onChange={setAccent} compact />
      {open ? <button type="button" className="template-fullpage-demo__close" onClick={() => setOpen(false)}>{t('templateUi.zarf.close')}</button> : null}
      <InternalLink className="template-fullpage-demo__start" to={`/panel/davetiyeler/yeni?template=${encodeURIComponent(templateKey)}`}>{t('templateUi.zarf.start')}</InternalLink>
    </div>
  </div>
}
