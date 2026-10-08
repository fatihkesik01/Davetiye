import { useState } from 'react'
import { useTranslation } from 'react-i18next'

import type { DraftContentInput } from '../../api/generated/client'
import { InvitationRenderer } from '../templates/rendering/InvitationRenderer'
import { normalizeDraftContent } from '../templates/rendering/model'

type PreviewDevice = 'phone' | 'tablet' | 'desktop'

interface DevicePreviewProps {
  invitationId: string
  templateKey: string | null
  rendererVersion: number | null
  content: DraftContentInput
}

export function DevicePreview({ invitationId, templateKey, rendererVersion, content }: DevicePreviewProps) {
  const { t } = useTranslation()
  const [device, setDevice] = useState<PreviewDevice>('phone')
  const devices: Array<{ value: PreviewDevice; label: string }> = [
    { value: 'phone', label: t('creatorUi.preview.phone') },
    { value: 'tablet', label: t('creatorUi.preview.tablet') },
    { value: 'desktop', label: t('creatorUi.preview.desktop') },
  ]

  if (!templateKey || !rendererVersion) {
    return <section className="preview-empty" role="status">
      <h3>{t('creatorUi.preview.chooseTitle')}</h3>
      <p>{t('creatorUi.preview.chooseBody')}</p>
    </section>
  }

  return <section className="device-preview" aria-labelledby="preview-heading">
    <div className="device-preview__toolbar">
      <div>
        <h3 id="preview-heading">{t('creatorUi.preview.title')}</h3>
        <p>{t('creatorUi.preview.deviceNote')}</p>
      </div>
      <div className="segmented-control" role="group" aria-label={t('creatorUi.preview.devices')}>
        {devices.map(option => <button
          key={option.value}
          type="button"
          aria-pressed={device === option.value}
          onClick={() => setDevice(option.value)}
        >{option.label}</button>)}
      </div>
      <a
        className="button button--secondary"
        href={`/panel/davetiyeler/${encodeURIComponent(invitationId)}/onizleme`}
        target="_blank"
        rel="noopener noreferrer"
      >{t('creatorUi.preview.open')}</a>
    </div>
    <div className={`device-preview__stage device-preview__stage--${device}`}>
      <InvitationRenderer
        templateKey={templateKey}
        rendererVersion={rendererVersion}
        model={normalizeDraftContent(content)}
        previewContext="creator"
      />
    </div>
    <p className="device-preview__note">{t('creatorUi.preview.draftNote')}</p>
  </section>
}
