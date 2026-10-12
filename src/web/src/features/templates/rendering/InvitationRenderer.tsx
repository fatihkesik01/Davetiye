import { getTemplateDemoModel } from './demoData'
import { templateRendersCoverInline, type NormalizedInvitationRenderModel } from './model'
import { resolveTemplateRenderer } from './registry'
import { zarfDemoPhotoUrl, type ZarfAccent2Key, type ZarfAccentKey } from './zarfAccent'
import type { ZarfAmbianceKey, ZarfGardenKey } from './zarfTheme'

export type PreviewContext = 'demo' | 'creator' | 'public'

interface InvitationRendererProps {
  templateKey: string
  rendererVersion: number
  model: NormalizedInvitationRenderModel
  previewContext: PreviewContext
  onOpenChange?: (open: boolean) => void
  initialOpen?: boolean
  open?: boolean
  hideInlineControls?: boolean
}

export function InvitationRenderer({
  templateKey,
  rendererVersion,
  model,
  previewContext,
  onOpenChange,
  initialOpen,
  open,
  hideInlineControls,
}: InvitationRendererProps) {
  const registration = resolveTemplateRenderer(templateKey, rendererVersion)
  if (!registration) {
    return <section className="renderer-unavailable" role="status">
      Bu şablon sürümü bu uygulama sürümünde kullanılamıyor.
    </section>
  }

  const { Renderer } = registration
  // Demo and Creator surfaces fall back to the bundled stand-in photo; a published public page never
  // shows it (a guest without a real Cover simply sees the placeholder).
  const shown: NormalizedInvitationRenderModel = previewContext !== 'public' && templateRendersCoverInline(templateKey) && !model.coverImageUrl
    ? { ...model, coverImageUrl: zarfDemoPhotoUrl }
    : model
  return <div
    className="invitation-renderer"
    data-preview-context={previewContext}
    data-renderer={`${templateKey}@${rendererVersion}`}
  ><Renderer model={shown} onOpenChange={onOpenChange} initialOpen={initialOpen} open={open} hideInlineControls={hideInlineControls} /></div>
}

export function TemplateDemoRenderer({
  templateKey,
  rendererVersion,
  accentColor,
  accentColor2,
  ambiance,
  garden,
  open,
  onOpenChange,
  hideInlineControls,
}: Pick<InvitationRendererProps, 'templateKey' | 'rendererVersion' | 'open' | 'onOpenChange' | 'hideInlineControls'> & { accentColor?: ZarfAccentKey; accentColor2?: ZarfAccent2Key; ambiance?: ZarfAmbianceKey; garden?: ZarfGardenKey }) {
  const demoModel = getTemplateDemoModel(templateKey)
  const model = demoModel ? { ...demoModel, ...(accentColor ? { accentColor } : {}), ...(accentColor2 ? { accentColor2 } : {}), ...(ambiance ? { ambiance } : {}), ...(garden ? { garden } : {}) } : demoModel
  if (!model) return <section className="renderer-unavailable" role="status">Demo içeriği bulunamadı.</section>
  return <InvitationRenderer
    templateKey={templateKey}
    rendererVersion={rendererVersion}
    model={model}
    previewContext="demo"
    open={open}
    onOpenChange={onOpenChange}
    hideInlineControls={hideInlineControls}
  />
}
