import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { InternalLink } from '../../../components/ui/InternalLink'
import { TemplateDemoRenderer } from '../rendering/InvitationRenderer'
import { ZarfLookPickers } from '../rendering/ZarfLookPickers'
import { defaultZarfAccent, resolveZarfAccent, type ZarfAccentKey } from '../rendering/zarfAccent'
import { defaultZarfAmbiance, defaultZarfFlora, type ZarfAmbianceKey, type ZarfFloraKey } from '../rendering/zarfTheme'

/**
 * Public demo of a template as a full page, like a real invitation: no catalog frame, just the
 * invitation plus a small back link and a compact floating bar. The bar keeps "close" and "start" in
 * view and opens a small panel with the look pickers (envelope colour, ambiance, flowers).
 */
export function FullPageTemplateDemo({ templateKey }: { templateKey: string }) {
  const { t } = useTranslation()
  const [accent, setAccent] = useState<ZarfAccentKey>(defaultZarfAccent)
  const [ambiance, setAmbiance] = useState<ZarfAmbianceKey>(defaultZarfAmbiance)
  const [flora, setFlora] = useState<ZarfFloraKey>(defaultZarfFlora)
  // In-memory only: a reload always starts with the envelope closed.
  const [open, setOpen] = useState(false)
  const [panelOpen, setPanelOpen] = useState(false)
  const panelId = useId()
  const panelRef = useRef<HTMLDivElement>(null)
  const toggleRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const previous = document.title
    document.title = `${t('templateUi.demoTitle')} | Kutlio`
    return () => { document.title = previous }
  }, [t])

  // Focus moves into the panel when it opens; a press outside it closes it.
  useEffect(() => {
    if (!panelOpen) return
    panelRef.current?.querySelector<HTMLInputElement>('input:checked')?.focus()
    const outside = (event: PointerEvent) => {
      const target = event.target as Node
      if (!panelRef.current?.contains(target) && !toggleRef.current?.contains(target)) setPanelOpen(false)
    }
    document.addEventListener('pointerdown', outside)
    return () => document.removeEventListener('pointerdown', outside)
  }, [panelOpen])

  const closePanel = () => {
    setPanelOpen(false)
    toggleRef.current?.focus()
  }

  return <div className="template-fullpage-demo">
    <main className="template-fullpage-demo__canvas">
      <TemplateDemoRenderer templateKey={templateKey} rendererVersion={1} accentColor={accent} ambiance={ambiance} flora={flora} open={open} onOpenChange={setOpen} hideInlineClose />
    </main>
    <InternalLink className="template-fullpage-demo__back" to="/sablonlar">← {t('templateUi.zarf.back')}</InternalLink>
    <div className="template-fullpage-demo__bar" role="region" aria-label={t('templateUi.zarf.controls')}>
      {panelOpen ? <div
        id={panelId}
        ref={panelRef}
        className="template-fullpage-demo__panel"
        role="dialog"
        aria-label={t('templateUi.zarf.controls')}
        onKeyDown={event => { if (event.key === 'Escape') { event.stopPropagation(); closePanel() } }}
      >
        <ZarfLookPickers
          values={{ accentColor: accent, ambiance, flora }}
          onAccent={key => setAccent(resolveZarfAccent(key))}
          onAmbiance={setAmbiance}
          onFlora={setFlora}
        />
      </div> : null}
      <button type="button" ref={toggleRef} className="template-fullpage-demo__custom" aria-expanded={panelOpen} aria-controls={panelOpen ? panelId : undefined} aria-haspopup="dialog" onClick={() => setPanelOpen(value => !value)}>{t('templateUi.zarf.customize')}</button>
      {open ? <button type="button" className="template-fullpage-demo__close" aria-label={t('templateUi.zarf.close')} onClick={() => setOpen(false)}><span className="template-fullpage-demo__long">{t('templateUi.zarf.close')}</span><span className="template-fullpage-demo__short" aria-hidden="true">{t('templateUi.zarf.closeShort')}</span></button> : null}
      <InternalLink className="template-fullpage-demo__start" to={`/panel/davetiyeler/yeni?template=${encodeURIComponent(templateKey)}`} aria-label={t('templateUi.zarf.start')}><span className="template-fullpage-demo__long">{t('templateUi.zarf.start')}</span><span className="template-fullpage-demo__short" aria-hidden="true">{t('templateUi.zarf.startShort')}</span></InternalLink>
    </div>
  </div>
}
