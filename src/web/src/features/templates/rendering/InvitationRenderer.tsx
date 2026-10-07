import { getTemplateDemoModel } from './demoData'
import type { NormalizedInvitationRenderModel } from './model'
import { resolveTemplateRenderer } from './registry'

export type PreviewContext = 'demo' | 'creator' | 'public'

interface InvitationRendererProps {
  templateKey: string
  rendererVersion: number
  model: NormalizedInvitationRenderModel
  previewContext: PreviewContext
}

export function InvitationRenderer({
  templateKey,
  rendererVersion,
  model,
  previewContext,
}: InvitationRendererProps) {
  const registration = resolveTemplateRenderer(templateKey, rendererVersion)
  if (!registration) {
    return <section className="renderer-unavailable" role="status">
      Bu şablon sürümü bu uygulama sürümünde kullanılamıyor.
    </section>
  }

  const { Renderer } = registration
  return <div
    className="invitation-renderer"
    data-preview-context={previewContext}
    data-renderer={`${templateKey}@${rendererVersion}`}
  ><Renderer model={model} /></div>
}

export function TemplateDemoRenderer({
  templateKey,
  rendererVersion,
}: Pick<InvitationRendererProps, 'templateKey' | 'rendererVersion'>) {
  const model = getTemplateDemoModel(templateKey)
  if (!model) return <section className="renderer-unavailable" role="status">Demo içeriği bulunamadı.</section>
  return <InvitationRenderer
    templateKey={templateKey}
    rendererVersion={rendererVersion}
    model={model}
    previewContext="demo"
  />
}
