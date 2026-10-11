import { getTemplateDemoModel } from './demoData'
import { templateRendersCoverInline, type NormalizedInvitationRenderModel } from './model'
import { resolveTemplateRenderer } from './registry'
import { zarfDemoPhotoUrl, type ZarfAccentKey } from './zarfAccent'

export type PreviewContext = 'demo' | 'creator' | 'public'

interface InvitationRendererProps {
  templateKey: string
  rendererVersion: number
  model: NormalizedInvitationRenderModel
  previewContext: PreviewContext
  onOpenChange?: (open: boolean) => void
  initialOpen?: boolean
}

export function InvitationRenderer({
  templateKey,
  rendererVersion,
  model,
  previewContext,
  onOpenChange,
  initialOpen,
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
  ><Renderer model={shown} onOpenChange={onOpenChange} initialOpen={initialOpen} /></div>
}

export function TemplateDemoRenderer({
  templateKey,
  rendererVersion,
  accentColor,
}: Pick<InvitationRendererProps, 'templateKey' | 'rendererVersion'> & { accentColor?: ZarfAccentKey }) {
  const demoModel = getTemplateDemoModel(templateKey)
  const model = demoModel && accentColor ? { ...demoModel, accentColor } : demoModel
  if (!model) return <section className="renderer-unavailable" role="status">Demo içeriği bulunamadı.</section>
  return <InvitationRenderer
    templateKey={templateKey}
    rendererVersion={rendererVersion}
    model={model}
    previewContext="demo"
  />
}
