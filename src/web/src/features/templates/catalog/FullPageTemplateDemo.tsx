import { useEffect, useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'

import { SiteHeader } from '../../../components/layout/SiteHeader'
import { InternalLink } from '../../../components/ui/InternalLink'
import { TemplateDemoRenderer } from '../rendering/InvitationRenderer'
import { DantelLookPickers, ZarfLookPickers } from '../rendering/ZarfLookPickers'
import { defaultZarfAccent, defaultZarfAccent2, resolveZarfAccent, type ZarfAccent2Key, type ZarfAccentKey } from '../rendering/zarfAccent'
import { defaultZarfAmbiance, defaultZarfGarden, type ZarfAmbianceKey, type ZarfGardenKey } from '../rendering/zarfTheme'
import { templatePublicSlug } from './templateRouting'

/** Visible label that shortens on phones (CSS swaps in data-short); the control keeps its full aria-label. */
function BarLabel({ long, short }: { long: string; short: string }) {
  return <span className="template-fullpage-demo__label" data-short={short}>{long}</span>
}

/**
 * Public demo of a template as a full page, like a real invitation: no catalog frame, just the
 * invitation plus the shared site header and a compact floating bar. The bar keeps "close" and "start" in
 * view and opens a small panel with the look pickers (envelope colour, ambiance, flowers).
 */
export function FullPageTemplateDemo({ templateKey }: { templateKey: string }) {
  const { t } = useTranslation()
  const isDantel = templateKey === 'dantel-davetiye'
  const [accent, setAccent] = useState<ZarfAccentKey>(isDantel ? 'pudra-pembe' : defaultZarfAccent)
  const [accent2, setAccent2] = useState<ZarfAccent2Key>(isDantel ? 'altin' : defaultZarfAccent2)
  const [ambiance, setAmbiance] = useState<ZarfAmbianceKey>(defaultZarfAmbiance)
  const [garden, setGarden] = useState<ZarfGardenKey>(isDantel ? 'saray-kapisi' : defaultZarfGarden)
  // In-memory only: a reload always starts with the invitation cover closed.
  const [open, setOpen] = useState(false)
  const [panelOpen, setPanelOpen] = useState(false)
  const panelId = useId()
  const panelRef = useRef<HTMLDivElement>(null)
  const toggleRef = useRef<HTMLButtonElement>(null)
  const envelopeToggleRef = useRef<HTMLButtonElement>(null)
  const focusEnvelopeToggle = useRef(false)

  useEffect(() => {
    const previous = document.title
    document.title = `${isDantel ? t('templateUi.gate.name') : t('templateUi.demoTitle')} | Kutlio`
    const publicSlug = templatePublicSlug(templateKey)
    const canonicalPath = `/sablonlar/${encodeURIComponent(publicSlug)}`
    if (window.location.pathname !== canonicalPath && publicSlug !== templateKey) {
      window.history.replaceState(window.history.state, '', `${canonicalPath}${window.location.search}${window.location.hash}`)
    }
    return () => { document.title = previous }
  }, [isDantel, t, templateKey])

  useEffect(() => {
    if (!focusEnvelopeToggle.current) return
    focusEnvelopeToggle.current = false
    envelopeToggleRef.current?.focus({ preventScroll: true })
  }, [open])

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
    <SiteHeader zone="public" />
    <main className="template-fullpage-demo__canvas">
      <TemplateDemoRenderer templateKey={templateKey} rendererVersion={1} accentColor={accent} accentColor2={accent2} ambiance={ambiance} garden={garden} open={open} onOpenChange={setOpen} hideInlineControls />
    </main>
    <div className="template-fullpage-demo__bar" role="region" aria-label={t(isDantel ? 'templateUi.dantel.controls' : 'templateUi.zarf.controls')}>
      {panelOpen ? <div
        id={panelId}
        ref={panelRef}
        className="template-fullpage-demo__panel"
        role="dialog"
        aria-label={t(isDantel ? 'templateUi.dantel.controls' : 'templateUi.zarf.controls')}
        onKeyDown={event => { if (event.key === 'Escape') { event.stopPropagation(); closePanel() } }}
      >
        {isDantel ? <DantelLookPickers
          values={{ accentColor: accent, accentColor2: accent2, ambiance, garden }}
          showThemeNames
          onAccent={key => setAccent(resolveZarfAccent(key))}
          onAccent2={setAccent2}
          onAmbiance={setAmbiance}
          onGarden={setGarden}
        /> : <ZarfLookPickers
          values={{ accentColor: accent, accentColor2: accent2, ambiance, garden }}
          showThemeNames
          onAccent={key => setAccent(resolveZarfAccent(key))}
          onAccent2={setAccent2}
          onAmbiance={setAmbiance}
          onGarden={setGarden}
        />}
      </div> : null}
      <button type="button" ref={toggleRef} className="template-fullpage-demo__custom" aria-expanded={panelOpen} aria-controls={panelOpen ? panelId : undefined} aria-haspopup="dialog" onClick={() => setPanelOpen(value => !value)}>{t('templateUi.zarf.customize')}</button>
      {/* One control: "open" while closed, "close" only while open (never both). */}
      {open
        ? <button type="button" ref={envelopeToggleRef} className="template-fullpage-demo__toggle template-fullpage-demo__close" aria-label={t(isDantel ? 'templateUi.dantel.close' : 'templateUi.zarf.close')} onClick={() => { focusEnvelopeToggle.current = true; setOpen(false) }}><BarLabel long={t(isDantel ? 'templateUi.dantel.close' : 'templateUi.zarf.close')} short={t('templateUi.zarf.closeShort')} /></button>
        : <button type="button" ref={envelopeToggleRef} className="template-fullpage-demo__toggle template-fullpage-demo__open" aria-label={t(isDantel ? 'templateUi.dantel.openControl' : 'templateUi.zarf.openControl')} onClick={() => { focusEnvelopeToggle.current = true; setOpen(true) }}><BarLabel long={t(isDantel ? 'templateUi.dantel.openControl' : 'templateUi.zarf.openControl')} short={t('templateUi.zarf.openShort')} /></button>}
      <InternalLink className="template-fullpage-demo__start" to={`/panel/davetiyeler/yeni?template=${encodeURIComponent(templatePublicSlug(templateKey))}`} aria-label={t('templateUi.zarf.start')}><BarLabel long={t('templateUi.zarf.start')} short={t('templateUi.zarf.startShort')} /></InternalLink>
    </div>
  </div>
}
