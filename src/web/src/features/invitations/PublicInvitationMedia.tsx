import { useEffect, useMemo, useState } from 'react'

import {
  DavetiyeApiClient,
  type PublicInvitationMedia as PublicMediaSnapshotItem,
  type PublicMediaDeliverySession,
} from '../../api/generated/client'

interface Props {
  api: DavetiyeApiClient
  publicCode: string
  templateKey: string
  media: PublicMediaSnapshotItem[]
  role: 'Cover' | 'Gallery'
}

export function PublicInvitationMedia({ api, publicCode, templateKey, media, role }: Props) {
  const [attempt, setAttempt] = useState(0)
  const [sessions, setSessions] = useState<Record<string, PublicMediaDeliverySession>>({})
  const roleMedia = useMemo(() => media.filter(item => item.role === role).sort((a, b) => a.sortOrder - b.sortOrder), [media, role])
  const mediaKey = roleMedia.map(item => item.assetId).join(',')

  useEffect(() => {
    if (!roleMedia.length) return

    const controller = new AbortController()
    let expiryTimer: number | undefined
    void api.listTemplates(controller.signal).then(async templates => {
      if (controller.signal.aborted) return
      const template = templates.find(item => item.key === templateKey)
      const requiredModule = role === 'Cover' ? 'hero' : 'gallery'
      if (!template?.supportedModules.includes(requiredModule)) return

      const resolved = await Promise.all(roleMedia.map(async item => {
        try {
          const session = await api.createPublicMediaDeliverySession(publicCode, item.assetId, controller.signal)
          if (controller.signal.aborted || !validSessionFor(item, session)) return null
          return { item, session }
        } catch {
          return null
        }
      }))
      if (controller.signal.aborted) return

      const next: Record<string, PublicMediaDeliverySession> = {}
      let nextExpiry = Number.POSITIVE_INFINITY
      for (const result of resolved) {
        if (!result) continue
        next[result.item.assetId] = result.session
        nextExpiry = Math.min(nextExpiry, Date.parse(result.session.expiresAt))
      }
      setSessions(next)
      if (Number.isFinite(nextExpiry)) {
        const refreshDelay = Math.max(1000, nextExpiry - Date.now() - 5000)
        expiryTimer = window.setTimeout(() => setAttempt(current => current + 1), refreshDelay)
      }
    }).catch(() => {
      // Media is optional public content. A catalog/session failure fails closed for this slot.
    })

    return () => {
      controller.abort()
      if (expiryTimer !== undefined) window.clearTimeout(expiryTimer)
    }
  }, [api, attempt, mediaKey, publicCode, role, roleMedia, templateKey])

  const visible = roleMedia.filter(item => sessions[item.assetId])
  if (!visible.length) return null

  return <section className={`public-invitation-media public-invitation-media--${role.toLowerCase()}`} aria-label={role === 'Cover' ? 'Davetiye kapağı' : 'Fotoğraf galerisi'}>
    {visible.map((item, index) => {
      const session = sessions[item.assetId]!
      if (item.kind === 'Image' && session.kind === 'image') {
        return <img className="public-invitation-media__image" key={item.assetId} src={session.url}
          alt={role === 'Cover' ? 'Davetiye kapak fotoğrafı' : `Davetiye galerisi fotoğrafı ${index + 1}`} loading="lazy" />
      }
      if (item.kind === 'Video' && session.kind === 'video') {
        return <iframe className="public-invitation-media__video" key={item.assetId} src={session.url}
          title={role === 'Cover' ? 'Davetiye kapak videosu' : `Davetiye galerisi videosu ${index + 1}`}
          referrerPolicy="no-referrer" sandbox="allow-scripts allow-same-origin allow-presentation"
          allow="accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture" allowFullScreen loading="lazy" />
      }
      return null
    })}
  </section>
}

function validSessionFor(item: PublicMediaSnapshotItem, value: PublicMediaDeliverySession): boolean {
  if (value.kind !== (item.kind === 'Image' ? 'image' : 'video') || typeof value.url !== 'string' ||
    typeof value.expiresAt !== 'string' || Date.parse(value.expiresAt) <= Date.now()) return false
  try {
    const url = new URL(value.url)
    return url.protocol === 'https:' && !url.username && !url.password && !url.hash
  } catch {
    return false
  }
}
