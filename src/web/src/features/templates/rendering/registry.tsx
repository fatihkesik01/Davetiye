import type { ComponentType } from 'react'

import rendererManifest from './registry.manifest.json'
import { InvitationOptionalSections } from './InvitationOptionalSections'
import {
  ColorfulBirthdayRenderer,
  HennaNightRenderer,
  JoyfulCircumcisionRenderer,
  MinimalOpeningRenderer,
  ModernGraduationRenderer,
  PastelBabyShowerRenderer,
  RomanticEngagementRenderer,
  TimelessWeddingRenderer,
  type TemplateRendererProps,
} from './renderers'

export interface TemplateRendererRegistration {
  templateKey: string
  rendererVersion: number
  Renderer: ComponentType<TemplateRendererProps>
}

const rendererComponents: Record<string, ComponentType<TemplateRendererProps>> = {
  'zamansiz-dugun@1': TimelessWeddingRenderer,
  'romantik-nisan@1': RomanticEngagementRenderer,
  'gece-kina@1': HennaNightRenderer,
  'neseli-sunnet@1': JoyfulCircumcisionRenderer,
  'renkli-dogum-gunu@1': ColorfulBirthdayRenderer,
  'pastel-baby-shower@1': PastelBabyShowerRenderer,
  'modern-mezuniyet@1': ModernGraduationRenderer,
  'minimal-acilis@1': MinimalOpeningRenderer,
}

for (const [id, Original] of Object.entries({ ...rendererComponents })) {
  rendererComponents[id.replace('@1', '@2')] = function ExtendedRenderer({ model }: TemplateRendererProps) {
    return <><Original model={model} /><InvitationOptionalSections model={model} /></>
  }
}

const manifestIds = rendererManifest.supportedRenderers.map(
  ({ templateKey, rendererVersion }) => `${templateKey}@${rendererVersion}`,
)
const componentIds = Object.keys(rendererComponents)
if (manifestIds.length !== componentIds.length || manifestIds.some(id => !rendererComponents[id])) {
  throw new Error('Template renderer manifest and compiled React renderer registry are out of sync.')
}

const registrations: TemplateRendererRegistration[] = rendererManifest.supportedRenderers.map(
  ({ templateKey, rendererVersion }) => ({
    templateKey,
    rendererVersion,
    Renderer: rendererComponents[`${templateKey}@${rendererVersion}`]!,
  }),
)

const registry = new Map(registrations.map(registration => [
  `${registration.templateKey}@${registration.rendererVersion}`,
  registration,
]))

export const templateRendererRegistrations: readonly TemplateRendererRegistration[] = registrations

export function resolveTemplateRenderer(templateKey: string, rendererVersion: number) {
  return registry.get(`${templateKey}@${rendererVersion}`) ?? null
}
