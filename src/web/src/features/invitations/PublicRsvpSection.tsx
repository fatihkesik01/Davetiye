import { useEffect, useState } from 'react'

import { type DavetiyeApiClient, type PublicRsvpConfiguration } from '../../api/generated/client'
import { PublicRsvpForm } from './PublicRsvpForm'

interface Props {
  api: DavetiyeApiClient
  publicCode: string
  templateKey: string
}

export function PublicRsvpSection({ api, publicCode, templateKey }: Props) {
  const [configuration, setConfiguration] = useState<PublicRsvpConfiguration | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    void api.listTemplates(controller.signal).then(async templates => {
      if (controller.signal.aborted) return
      const template = templates.find(item => item.key === templateKey)
      if (!template?.supportedModules.includes('rsvp')) return
      const result = await api.getPublicRsvpConfiguration(publicCode, controller.signal)
      if (!controller.signal.aborted && result.status === 'available') setConfiguration(result)
    }).catch(() => {
      // RSVP is optional public content. A catalog or configuration failure hides this section.
    })
    return () => controller.abort()
  }, [api, publicCode, templateKey])

  return configuration
    ? <PublicRsvpForm key={publicCode} api={api} publicCode={publicCode} configuration={configuration} />
    : null
}
